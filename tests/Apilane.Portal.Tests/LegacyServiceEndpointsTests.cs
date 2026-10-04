using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// The three Portal endpoints that other services call: /Info/GetApplication and
    /// /Info/UserOwnsApplication (every API server, see PortalInfoService in Apilane.Api.Core) and
    /// /Authenticate/InRole (external apps). They are MVC actions outside /api/v1 and must keep
    /// their address, their status codes and the names in their JSON when the Razor pages are
    /// removed: this class has to pass before that removal and, unchanged, after it. That is why
    /// it signs in through the API and never through a Razor page.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class LegacyServiceEndpointsTests
    {
        private readonly PortalFactory _portal;

        public LegacyServiceEndpointsTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- /Info/GetApplication ----------

        [Fact]
        public async Task GetApplication_Should_Return_The_Application_As_The_Api_Server_Reads_It()
        {
            var scene = await CreateSceneAsync();

            var response = await SendAsync($"/Info/GetApplication?appToken={scene.Token}", await InstallationKeyAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

            // Read exactly as PortalInfoService does: default options, so the names are case-sensitive.
            // A camelCase answer would give an application with nothing in it.
            var application = JsonSerializer.Deserialize<DBWS_Application>(await response.Content.ReadAsStringAsync())
                ?? throw new InvalidOperationException("No application.");

            Assert.Equal(scene.Token, application.Token);
            Assert.Equal(scene.Name, application.Name);
            Assert.Equal((int)DatabaseType.SQLServer, application.DatabaseType);
            Assert.False(string.IsNullOrWhiteSpace(application.EncryptionKey));
            Assert.Equal(scene.ServerUrl, application.Server.ServerUrl);

            var entity = Assert.Single(application.Entities);
            Assert.Equal("Orders", entity.Name);
            Assert.Equal(new[] { "ID", "Code" }, entity.Properties.OrderBy(x => x.ID).Select(x => x.Name));
            Assert.Equal((int)PropertyType.String, entity.Properties.Single(x => x.Name == "Code").TypeID);

            var endpoint = Assert.Single(application.CustomEndpoints);
            Assert.Equal("OpenOrders", endpoint.Name);
            Assert.Equal("select 1", endpoint.Query);

            Assert.Equal(scene.CollaboratorEmail, Assert.Single(application.Collaborates).UserEmail);
        }

        [Fact]
        public async Task GetApplication_Unknown_Token_Should_Return_200_With_Null()
        {
            var response = await SendAsync($"/Info/GetApplication?appToken={Guid.NewGuid()}", await InstallationKeyAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("null", await response.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-the-installation-key")]
        public async Task GetApplication_Without_The_Installation_Key_Should_Return_401(string? key)
        {
            var scene = await CreateSceneAsync();

            var response = await SendAsync($"/Info/GetApplication?appToken={scene.Token}", key);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.DoesNotContain(scene.Token, await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task GetApplication_With_The_Key_In_The_Address_Should_Return_401()
        {
            var scene = await CreateSceneAsync();

            var response = await SendAsync($"/Info/GetApplication?appToken={scene.Token}&installationKey={await InstallationKeyAsync()}", null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        // ---------- /Info/UserOwnsApplication ----------

        [Fact]
        public async Task UserOwnsApplication_Should_Be_True_For_The_Owner_A_Collaborator_And_An_Administrator()
        {
            var scene = await CreateSceneAsync();
            var (adminEmail, adminPassword) = await _portal.CreateAdminUserAsync();

            Assert.True(await UserOwnsApplicationAsync(scene.Token, await SignInAndGetTokenAsync(scene.OwnerEmail, scene.OwnerPassword)));
            Assert.True(await UserOwnsApplicationAsync(scene.Token, await SignInAndGetTokenAsync(scene.CollaboratorEmail, scene.CollaboratorPassword)));
            Assert.True(await UserOwnsApplicationAsync(scene.Token, await SignInAndGetTokenAsync(adminEmail, adminPassword)));
        }

        [Fact]
        public async Task UserOwnsApplication_Should_Be_False_For_Another_User_And_For_An_Unknown_Token()
        {
            var scene = await CreateSceneAsync();
            var (strangerEmail, strangerPassword) = await _portal.CreateUserAsync();
            var strangerToken = await SignInAndGetTokenAsync(strangerEmail, strangerPassword);

            Assert.False(await UserOwnsApplicationAsync(scene.Token, strangerToken));

            // Asked as an ordinary user: an administrator gets true for any token.
            Assert.False(await UserOwnsApplicationAsync(Guid.NewGuid().ToString(), await SignInAndGetTokenAsync(scene.OwnerEmail, scene.OwnerPassword)));
        }

        [Fact]
        public async Task UserOwnsApplication_Should_Be_False_Without_A_User_Token_And_With_One_That_Was_Replaced()
        {
            var scene = await CreateSceneAsync();

            Assert.False(await UserOwnsApplicationAsync(scene.Token, null));
            Assert.False(await UserOwnsApplicationAsync(scene.Token, Guid.NewGuid().ToString()));

            // Signing in again gives the user a new token; the first one is no longer anybody's.
            var first = await SignInAndGetTokenAsync(scene.OwnerEmail, scene.OwnerPassword);
            var second = await SignInAndGetTokenAsync(scene.OwnerEmail, scene.OwnerPassword);

            Assert.NotEqual(first, second);
            Assert.False(await UserOwnsApplicationAsync(scene.Token, first));
            Assert.True(await UserOwnsApplicationAsync(scene.Token, second));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("not-the-installation-key")]
        public async Task UserOwnsApplication_Without_The_Installation_Key_Should_Return_401(string? key)
        {
            var scene = await CreateSceneAsync();
            var ownerToken = await SignInAndGetTokenAsync(scene.OwnerEmail, scene.OwnerPassword);

            var response = await SendAsync($"/Info/UserOwnsApplication?appToken={scene.Token}", key, ownerToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }

        // ---------- /Authenticate/InRole ----------

        [Fact]
        public async Task InRole_Anonymous_Should_Return_401_Without_A_Redirect()
        {
            var response = await _portal.CreateAnonymousClient().GetAsync($"/Authenticate/InRole?role={Globals.AdminRoleName}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }

        [Fact]
        public async Task InRole_Should_Return_200_For_An_Administrator_And_401_For_Anyone_Else()
        {
            var (adminEmail, adminPassword) = await _portal.CreateAdminUserAsync();
            var (userEmail, userPassword) = await _portal.CreateUserAsync();

            var admin = (await _portal.SignInWithApiAsync(adminEmail, adminPassword)).Client;
            var user = (await _portal.SignInWithApiAsync(userEmail, userPassword)).Client;

            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/Authenticate/InRole?role={Globals.AdminRoleName}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync($"/Authenticate/InRole?role={Globals.AdminRoleName}")).StatusCode);

            // No role asked for, or one the administrator does not have.
            Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/Authenticate/InRole")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/Authenticate/InRole?role=")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/Authenticate/InRole?role=Nothing")).StatusCode);
        }

        // ---------- Helpers ----------

        private record Scene(string Token, string Name, string ServerUrl, string OwnerEmail, string OwnerPassword, string CollaboratorEmail, string CollaboratorPassword);

        /// <summary>
        /// An application of a new user, shared with another new user, with one entity (two
        /// properties) and one custom endpoint.
        /// </summary>
        private async Task<Scene> CreateSceneAsync()
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();

            var server = await _portal.CreateServerAsync();
            var seeded = await _portal.CreateApplicationAsync(server.ID, ownerEmail, $"service-endpoints-{Guid.NewGuid():N}", collaboratorEmail);
            var appId = seeded.Application.ID;

            await _portal.WithDbContextAsync(db =>
            {
                db.Entities.Add(EntityScene.Entity(appId, "Orders", false,
                    EntityScene.Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true),
                    EntityScene.Property("Code", PropertyType.String)));

                db.CustomEndpoints.Add(new DBWS_CustomEndpoint { AppID = appId, Name = "OpenOrders", Query = "select 1", DateModified = DateTime.UtcNow });

                return db.SaveChangesAsync();
            });

            return new Scene(seeded.Application.Token, seeded.Application.Name, server.ServerUrl, ownerEmail, ownerPassword, collaboratorEmail, collaboratorPassword);
        }

        /// <summary>
        /// The key as the Portal checks it: the stored one, read when it is needed, because the
        /// settings tests change it.
        /// </summary>
        private Task<string> InstallationKeyAsync()
        {
            return _portal.WithDbContextAsync(async db => (await db.GlobalSettings.AsNoTracking().SingleAsync()).InstallationKey);
        }

        /// <summary>
        /// Signs the user in through the API and returns the token the Portal then sends to an API
        /// server for that user (Authorization: Bearer), which the API server sends back here.
        /// </summary>
        private async Task<string> SignInAndGetTokenAsync(string email, string password)
        {
            await _portal.SignInWithApiAsync(email, password);

            var token = await _portal.WithDbContextAsync(db => db.Users
                .AsNoTracking()
                .Where(x => x.Email == email)
                .Select(x => x.AdminAuthToken)
                .SingleAsync());

            return token ?? throw new InvalidOperationException("Signing in stored no token.");
        }

        private async Task<bool> UserOwnsApplicationAsync(string appToken, string? userToken)
        {
            var response = await SendAsync($"/Info/UserOwnsApplication?appToken={appToken}", await InstallationKeyAsync(), userToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // Read as PortalInfoService does.
            return JsonSerializer.Deserialize<bool>(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        /// A call as an API server makes it: no cookie, the installation key and the user's token in headers.
        /// </summary>
        private async Task<HttpResponseMessage> SendAsync(string url, string? installationKey, string? userToken = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (installationKey is not null)
            {
                request.Headers.Add(Globals.InstallationKeyHeaderName, installationKey);
            }

            if (userToken is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            }

            return await _portal.CreateCookielessClient().SendAsync(request);
        }
    }
}
