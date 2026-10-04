using Apilane.Common.Enums;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Services;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class ApplicationsApiTests
    {
        private const string Url = "/api/v1/applications";
        private const string ServersUrl = "/api/v1/servers";
        private const string ApiTokenUrl = "/api/v1/session/api-token";

        private readonly PortalFactory _portal;

        public ApplicationsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Should_Return_Exactly_The_Callers_Applications_Owned_First_Then_By_Name()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.OwnerClient.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var list = await response.ReadJsonAsync<ListResponse<ApplicationResponse>>();

            // The owner is a new user, so the whole list is what this test created: the two owned
            // applications by name, then the one shared with the owner. Not the stranger's.
            Assert.Equal(
                new[] { scene.OwnedA.Application.Token, scene.OwnedB.Application.Token, scene.SharedWithOwner.Application.Token },
                list.Data.Select(x => x.Token));
            Assert.Equal(3, list.Total);
        }

        [Fact]
        public async Task List_Should_Describe_An_Owned_Application()
        {
            var scene = await CreateSceneAsync();

            var list = await (await scene.OwnerClient.GetAsync(Url)).ReadJsonAsync<ListResponse<ApplicationResponse>>();
            var application = Assert.Single(list.Data, x => x.Token == scene.OwnedB.Application.Token);

            Assert.Equal(scene.OwnedB.Application.Name, application.Name);
            Assert.True(application.Online);
            Assert.Equal("SQLServer", application.DatabaseType);
            Assert.Null(application.DifferentiationEntity);
            Assert.Equal(1024, application.MaxAllowedFileSizeInKB);
            Assert.True(application.IsOwner);
            Assert.Equal(scene.OwnerEmail, application.OwnerEmail);
            Assert.Equal(2, application.CollaboratorCount);
            Assert.Equal(scene.SecondServerId, application.Server.ID);
            Assert.Equal(scene.SecondServerName, application.Server.Name);
            Assert.Equal(scene.SecondServerUrl, application.Server.ServerUrl);
        }

        [Fact]
        public async Task List_Collaborator_Should_See_The_Shared_Application_As_Not_Owned()
        {
            var scene = await CreateSceneAsync();

            var list = await (await scene.CollaboratorClient.GetAsync(Url)).ReadJsonAsync<ListResponse<ApplicationResponse>>();

            var application = Assert.Single(list.Data);
            Assert.Equal(scene.OwnedB.Application.Token, application.Token);
            Assert.False(application.IsOwner);
            Assert.Equal(scene.OwnerEmail, application.OwnerEmail);
            Assert.Null(application.CollaboratorCount);
        }

        [Fact]
        public async Task List_Should_Sort_Names_Lower_Case_Before_Upper_Case()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();

            var zeta = await _portal.CreateApplicationAsync(server.ID, email, "Zeta");
            var alpha = await _portal.CreateApplicationAsync(server.ID, email, "alpha");
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var list = await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<ApplicationResponse>>();

            Assert.Equal(new[] { alpha.Application.Token, zeta.Application.Token }, list.Data.Select(x => x.Token));
        }

        [Fact]
        public async Task List_Should_Map_Online_DatabaseType_And_DifferentiationEntity()
        {
            var scene = await CreateSceneAsync();

            await _portal.WithDbContextAsync(async db =>
            {
                var blank = await db.Applications.SingleAsync(x => x.Token == scene.OwnedA.Application.Token);
                blank.Online = false;
                blank.DatabaseType = (int)DatabaseType.MySQL;
                blank.DifferentiationEntity = "   ";

                var named = await db.Applications.SingleAsync(x => x.Token == scene.OwnedB.Application.Token);
                named.DifferentiationEntity = "Company";

                return await db.SaveChangesAsync();
            });

            var list = await (await scene.OwnerClient.GetAsync(Url)).ReadJsonAsync<ListResponse<ApplicationResponse>>();

            var blankApplication = Assert.Single(list.Data, x => x.Token == scene.OwnedA.Application.Token);
            Assert.False(blankApplication.Online);
            Assert.Equal("MySQL", blankApplication.DatabaseType);
            Assert.Null(blankApplication.DifferentiationEntity);

            var namedApplication = Assert.Single(list.Data, x => x.Token == scene.OwnedB.Application.Token);
            Assert.Equal("Company", namedApplication.DifferentiationEntity);
        }

        [Fact]
        public async Task Collaborator_Shared_With_The_Address_In_Another_Case_Should_Not_See_The_Application()
        {
            var (ownerEmail, _) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();

            // The share row must match the account e-mail exactly.
            var shared = await _portal.CreateApplicationAsync(server.ID, ownerEmail, "shared-other-case", collaboratorEmail.ToUpperInvariant());
            var client = await _portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword);

            var list = await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<ApplicationResponse>>();

            Assert.Empty(list.Data);
            await AssertNotFoundAsync(await client.GetAsync(ConnectionInfoUrl(shared)));
        }

        [Fact]
        public async Task List_Admin_Who_Is_Not_A_Member_Should_Get_An_Empty_List()
        {
            await CreateSceneAsync();
            var admin = await _portal.CreateAdminClientAsync();

            var list = await (await admin.GetAsync(Url)).ReadJsonAsync<ListResponse<ApplicationResponse>>();

            Assert.Empty(list.Data);
            Assert.Equal(0, list.Total);
        }

        [Fact]
        public async Task List_Should_Not_Contain_Any_Secret()
        {
            var scene = await CreateSceneAsync();

            var json = await scene.OwnerClient.GetStringAsync(Url);

            foreach (var seeded in new[] { scene.OwnedA, scene.OwnedB, scene.SharedWithOwner })
            {
                Assert.Contains(seeded.Application.Token, json);
                Assert.DoesNotContain(seeded.EncryptionKey, json);
                Assert.DoesNotContain(seeded.Application.EncryptionKey, json);
                Assert.DoesNotContain(seeded.Application.ConnectionString ?? "no connection string", json);
                Assert.DoesNotContain(seeded.Application.UserID, json);
            }

            Assert.DoesNotContain("EncryptionKey", json);
            // Only the flag that says one is stored.
            Assert.DoesNotContain("\"ConnectionString\"", json);
            Assert.Contains("\"HasConnectionString\":true", json);
        }

        [Fact]
        public async Task List_Anonymous_Should_Return_401()
        {
            await AssertUnauthorizedAsync(await _portal.CreateAnonymousClient().GetAsync(Url));
        }

        // ---------- Servers ----------

        [Fact]
        public async Task Servers_User_Without_Admin_Role_Should_Get_Them_By_Name()
        {
            var later = await _portal.CreateServerAsync("srv-b");
            var earlier = await _portal.CreateServerAsync("srv-a");
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync(ServersUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var servers = (await response.ReadJsonAsync<ListResponse<ServerSummaryResponse>>()).Data;
            var earlierIndex = servers.FindIndex(x => x.ID == earlier.ID);
            var laterIndex = servers.FindIndex(x => x.ID == later.ID);

            Assert.True(earlierIndex >= 0 && earlierIndex < laterIndex, "Servers are not listed by name.");
            Assert.Equal(earlier.Name, servers[earlierIndex].Name);
            Assert.Equal(earlier.ServerUrl, servers[earlierIndex].ServerUrl);
        }

        [Fact]
        public async Task Servers_Anonymous_Should_Return_401()
        {
            await AssertUnauthorizedAsync(await _portal.CreateAnonymousClient().GetAsync(ServersUrl));
        }

        // ---------- Connection info ----------

        [Fact]
        public async Task ConnectionInfo_Owner_Should_Get_The_Decrypted_Key()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.OwnerClient.GetAsync(ConnectionInfoUrl(scene.OwnedB));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var info = await response.ReadJsonAsync<ConnectionInfoResponse>();
            Assert.Equal(scene.OwnedB.Application.Token, info.Token);
            Assert.Equal(scene.SecondServerUrl, info.ServerUrl);
            Assert.Equal(scene.OwnedB.EncryptionKey, info.EncryptionKey);
            Assert.NotEqual(scene.OwnedB.Application.EncryptionKey, info.EncryptionKey);
        }

        [Fact]
        public async Task ConnectionInfo_Collaborator_Should_Get_The_Decrypted_Key()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.CollaboratorClient.GetAsync(ConnectionInfoUrl(scene.OwnedB));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal(scene.OwnedB.EncryptionKey, (await response.ReadJsonAsync<ConnectionInfoResponse>()).EncryptionKey);
        }

        [Fact]
        public async Task ConnectionInfo_Stranger_Should_Return_404()
        {
            var scene = await CreateSceneAsync();

            await AssertNotFoundAsync(await scene.StrangerClient.GetAsync(ConnectionInfoUrl(scene.OwnedB)));
        }

        [Fact]
        public async Task ConnectionInfo_Admin_Who_Is_Not_A_Member_Should_Return_404()
        {
            var scene = await CreateSceneAsync();
            var admin = await _portal.CreateAdminClientAsync();

            await AssertNotFoundAsync(await admin.GetAsync(ConnectionInfoUrl(scene.OwnedB)));
        }

        [Fact]
        public async Task ConnectionInfo_Unknown_Token_Should_Return_404()
        {
            var client = await _portal.CreateUserClientAsync();

            await AssertNotFoundAsync(await client.GetAsync($"{Url}/{Guid.NewGuid()}/connection-info"));
        }

        [Fact]
        public async Task ConnectionInfo_Token_In_Another_Case_Should_Return_404()
        {
            var scene = await CreateSceneAsync();
            var token = scene.OwnedB.Application.Token.ToUpperInvariant();

            await AssertNotFoundAsync(await scene.OwnerClient.GetAsync($"{Url}/{token}/connection-info"));
        }

        [Fact]
        public async Task ConnectionInfo_Anonymous_Should_Return_401()
        {
            var scene = await CreateSceneAsync();

            await AssertUnauthorizedAsync(await _portal.CreateAnonymousClient().GetAsync(ConnectionInfoUrl(scene.OwnedB)));
        }

        // ---------- API token ----------

        [Fact]
        public async Task ApiToken_Should_Be_The_Token_Of_The_Session_And_Change_At_The_Next_Sign_In()
        {
            var (email, password) = await _portal.CreateUserAsync();

            var first = await _portal.CreateSignedInClientAsync(email, password);
            var response = await first.GetAsync(ApiTokenUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);

            var firstToken = (await response.ReadJsonAsync<ApiTokenResponse>()).Token;
            Assert.False(string.IsNullOrWhiteSpace(firstToken));
            Assert.Equal(await StoredTokenAsync(email), firstToken);

            var second = await _portal.CreateSignedInClientAsync(email, password);
            var secondToken = (await (await second.GetAsync(ApiTokenUrl)).ReadJsonAsync<ApiTokenResponse>()).Token;

            Assert.NotEqual(firstToken, secondToken);
            Assert.Equal(await StoredTokenAsync(email), secondToken);

            // The first session ended with the second sign-in.
            await AssertUnauthorizedAsync(await first.GetAsync(ApiTokenUrl));
        }

        [Fact]
        public async Task ApiToken_Should_Not_Be_Part_Of_The_Session_Response()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var session = await client.GetStringAsync("/api/v1/session");

            Assert.DoesNotContain(await StoredTokenAsync(email), session);
        }

        [Fact]
        public async Task ApiToken_Anonymous_Should_Return_401()
        {
            await AssertUnauthorizedAsync(await _portal.CreateAnonymousClient().GetAsync(ApiTokenUrl));
        }

        // ---------- Get one ----------

        [Fact]
        public async Task Get_Owner_And_Collaborator_Should_Get_The_Application_As_The_List_Shows_It()
        {
            var scene = await CreateSceneAsync();
            var url = $"{Url}/{scene.OwnedB.Application.Token}";

            var response = await scene.OwnerClient.GetAsync(url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var application = await response.ReadJsonAsync<ApplicationResponse>();
            Assert.Equal(scene.OwnedB.Application.Token, application.Token);
            Assert.Equal(scene.OwnedB.Application.Name, application.Name);
            Assert.True(application.IsOwner);
            Assert.Equal(2, application.CollaboratorCount);
            Assert.Equal(scene.SecondServerId, application.Server.ID);

            var shared = await (await scene.CollaboratorClient.GetAsync(url)).ReadJsonAsync<ApplicationResponse>();
            Assert.False(shared.IsOwner);
            Assert.Null(shared.CollaboratorCount);
        }

        [Fact]
        public async Task Get_Stranger_Admin_And_Unknown_Token_Should_Return_404()
        {
            var scene = await CreateSceneAsync();
            var admin = await _portal.CreateAdminClientAsync();

            await AssertNotFoundAsync(await scene.StrangerClient.GetAsync($"{Url}/{scene.OwnedB.Application.Token}"));
            await AssertNotFoundAsync(await admin.GetAsync($"{Url}/{scene.OwnedB.Application.Token}"));
            await AssertNotFoundAsync(await scene.OwnerClient.GetAsync($"{Url}/{Guid.NewGuid()}"));
        }

        [Fact]
        public async Task Get_Anonymous_Should_Return_401()
        {
            var scene = await CreateSceneAsync();

            await AssertUnauthorizedAsync(await _portal.CreateAnonymousClient().GetAsync($"{Url}/{scene.OwnedB.Application.Token}"));
        }

        // ---------- Cache reset ----------

        [Fact]
        public async Task CacheReset_Owner_Should_Call_The_Api_Server_Once()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await AssertClearCacheCallAsync(scene, scene.OwnerEmail);
        }

        [Fact]
        public async Task CacheReset_Collaborator_Should_Call_The_Api_Server_Once()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");

            var response = await scene.CollaboratorClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await AssertClearCacheCallAsync(scene, scene.CollaboratorEmail);
        }

        [Fact]
        public async Task CacheReset_Admin_Who_Is_Not_A_Member_Should_Call_The_Api_Server_Once()
        {
            var scene = await CreateSceneAsync();
            var (email, password) = await _portal.CreateAdminUserAsync();
            var admin = await _portal.CreateSignedInClientAsync(email, password);
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");

            var response = await admin.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await AssertClearCacheCallAsync(scene, email);
        }

        [Fact]
        public async Task CacheReset_Should_Trim_The_Trailing_Slash_Of_The_Server_Url()
        {
            var scene = await CreateSceneAsync();

            await _portal.WithDbContextAsync(async db =>
            {
                var server = await db.Servers.SingleAsync(x => x.ID == scene.SecondServerId);
                server.ServerUrl = scene.SecondServerUrl + "/";
                return await db.SaveChangesAsync();
            });

            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            await AssertClearCacheCallAsync(scene, scene.OwnerEmail);
        }

        [Fact]
        public async Task CacheReset_Stranger_Should_Return_404_And_Not_Call_The_Api_Server()
        {
            var scene = await CreateSceneAsync();

            await AssertNotFoundAsync(await scene.StrangerClient.PostAsync(CacheResetUrl(scene.OwnedB), null));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task CacheReset_Admin_Unknown_Token_Should_Return_404()
        {
            var admin = await _portal.CreateAdminClientAsync();

            await AssertNotFoundAsync(await admin.PostAsync($"{Url}/{Guid.NewGuid()}/cache-reset", null));
        }

        [Fact]
        public async Task CacheReset_When_The_Api_Server_Answers_400_Should_Return_400_With_Its_Message_And_Property()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(
                FakeApiServer.ClearCachePath,
                HttpStatusCode.BadRequest,
                "{\"Code\":\"ERROR\",\"Message\":\"The API server said no.\",\"Property\":\"Token\",\"Entity\":null}");

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("The API server said no.", error.Message);
            Assert.Equal("Token", error.Property);

            var detail = Assert.Single(error.Errors ?? throw new InvalidOperationException("No Errors."));
            Assert.Equal("Token", detail.Property);
            Assert.Equal("The API server said no.", detail.Message);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task CacheReset_When_The_Api_Server_Answers_Another_Error_Should_Return_502_With_Its_Message(HttpStatusCode upstream)
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, upstream, "{\"Code\":\"UNAUTHORIZED\",\"Message\":\"The API server said no.\"}");

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.UpstreamError, error.Code);
            Assert.Equal("The API server said no.", error.Message);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>", PortalErrorCode.UpstreamError)]
        [InlineData(HttpStatusCode.BadGateway, "", PortalErrorCode.UpstreamError)]
        [InlineData(HttpStatusCode.BadRequest, "<html><body>400 Bad Request</body></html>", PortalErrorCode.Validation)]
        public async Task CacheReset_When_The_Failure_Text_Is_Not_A_Message_Should_Answer_With_A_Fixed_Message(HttpStatusCode upstream, string text, string code)
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, upstream, text, "text/html");

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(code, error.Code);
            Assert.Equal(ApiServerClient.UnusableAnswerMessage, error.Message);
        }

        [Fact]
        public async Task CacheReset_When_The_Api_Server_Answers_Plain_Text_Should_Return_502_With_That_Text()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.ServiceUnavailable, "Starting up, try again later.", "text/plain");

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("Starting up, try again later.", (await response.ReadJsonAsync<ErrorResponse>()).Message);
        }

        [Theory]
        [InlineData(300, false)]
        [InlineData(301, true)]
        public async Task CacheReset_When_The_Plain_Text_Is_Too_Long_Should_Answer_With_A_Fixed_Message(int length, bool replaced)
        {
            var scene = await CreateSceneAsync();
            var text = new string('x', length);
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.ServiceUnavailable, text, "text/plain");

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal(replaced ? ApiServerClient.UnusableAnswerMessage : text, (await response.ReadJsonAsync<ErrorResponse>()).Message);
        }

        [Fact]
        public async Task CacheReset_When_The_Api_Server_Times_Out_Should_Return_502()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.TimesOut(FakeApiServer.ClearCachePath);

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.UpstreamError, error.Code);
            Assert.Equal(ApiServerClient.UnusableAnswerMessage, error.Message);
        }

        [Fact]
        public async Task CacheReset_When_The_Api_Server_Cannot_Be_Reached_Should_Return_502()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.UpstreamError, error.Code);
            Assert.Equal(ApiServerClient.UnusableAnswerMessage, error.Message);
        }

        [Fact]
        public async Task CacheReset_Without_The_Csrf_Header_Should_Return_403_And_Not_Call_The_Api_Server()
        {
            var scene = await CreateSceneAsync();
            scene.OwnerClient.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await scene.OwnerClient.PostAsync(CacheResetUrl(scene.OwnedB), null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task CacheReset_Anonymous_Should_Return_401()
        {
            var scene = await CreateSceneAsync();

            await AssertUnauthorizedAsync(await _portal.CreateAnonymousClient().PostAsync(CacheResetUrl(scene.OwnedB), null));
        }

        // ---------- Helpers ----------

        private record Scene(
            string OwnerEmail,
            HttpClient OwnerClient,
            string CollaboratorEmail,
            HttpClient CollaboratorClient,
            HttpClient StrangerClient,
            long SecondServerId,
            string SecondServerName,
            string SecondServerUrl,
            SeededApplication OwnedA,
            SeededApplication OwnedB,
            SeededApplication SharedWithOwner);

        /// <summary>
        /// Two servers and three new users. The owner has 'a-owned' on the first server and 'b-owned'
        /// on the second, shared with the collaborator and with an address that has no account.
        /// The stranger owns 'a-private', which nobody else sees, and '0-shared', shared with the
        /// owner: its name sorts first, so only "owned first" puts it last in the owner's list.
        /// </summary>
        private async Task<Scene> CreateSceneAsync()
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await _portal.CreateUserAsync();

            var firstServer = await _portal.CreateServerAsync();
            var secondServer = await _portal.CreateServerAsync();

            // Added in the opposite order of their names.
            var ownedB = await _portal.CreateApplicationAsync(secondServer.ID, ownerEmail, "b-owned", collaboratorEmail, "nobody@portal.test");
            var ownedA = await _portal.CreateApplicationAsync(firstServer.ID, ownerEmail, "a-owned");
            await _portal.CreateApplicationAsync(firstServer.ID, strangerEmail, "a-private");
            var sharedWithOwner = await _portal.CreateApplicationAsync(firstServer.ID, strangerEmail, "0-shared", ownerEmail);

            return new Scene(
                ownerEmail,
                await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword),
                collaboratorEmail,
                await _portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword),
                await _portal.CreateSignedInClientAsync(strangerEmail, strangerPassword),
                secondServer.ID,
                secondServer.Name,
                secondServer.ServerUrl,
                ownedA,
                ownedB,
                sharedWithOwner);
        }

        private static string ConnectionInfoUrl(SeededApplication seeded)
        {
            return $"{Url}/{seeded.Application.Token}/connection-info";
        }

        private static string CacheResetUrl(SeededApplication seeded)
        {
            return $"{Url}/{seeded.Application.Token}/cache-reset";
        }

        /// <summary>
        /// The one call the Portal makes for a cache reset of 'b-owned': the URL of its own
        /// server, the application token and the caller's own API token.
        /// </summary>
        private async Task AssertClearCacheCallAsync(Scene scene, string callerEmail)
        {
            var request = Assert.Single(_portal.ApiServer.Requests);

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"{scene.SecondServerUrl}/api/Application/ClearCache", request.Url);
            Assert.Equal(scene.OwnedB.Application.Token, request.Headers["x-application-token"]);
            Assert.Equal("portal", request.Headers["x-client-id"]);
            Assert.Equal($"Bearer {await StoredTokenAsync(callerEmail)}", request.Headers["Authorization"]);
            Assert.False(request.Headers.ContainsKey("x-installation-key"));
        }

        private async Task<string> StoredTokenAsync(string email)
        {
            var token = await _portal.WithDbContextAsync(db => db.Users
                .AsNoTracking()
                .Where(x => x.Email == email)
                .Select(x => x.AdminAuthToken)
                .SingleAsync());

            return token ?? throw new InvalidOperationException($"{email} has no stored token.");
        }

        private static async Task AssertNotFoundAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Equal("Application", error.Entity);
        }

        private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }
    }
}
