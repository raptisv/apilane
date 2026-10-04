using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class AdminApplicationsApiTests
    {
        private const string Url = "/api/v1/admin/applications";

        private readonly PortalFactory _portal;

        public AdminApplicationsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Admin_Should_Return_Applications_Of_Other_Users_With_The_Razor_Columns()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-list");
            var server = await LoadServerAsync(seeded.Application.ServerID);
            var admin = await _portal.CreateAdminClientAsync();

            var response = await admin.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var list = await response.ReadJsonAsync<ListResponse<AdminApplicationResponse>>();
            Assert.Equal(list.Data.Count, list.Total);

            var application = Assert.Single(list.Data, x => x.Token == seeded.Application.Token);
            Assert.Equal(seeded.Application.ID, application.ID);
            Assert.Equal("admin-list", application.Name);
            Assert.Equal(seeded.Application.AdminEmail, application.OwnerEmail);
            Assert.True(application.Online);
            Assert.Equal(server.ID, application.Server.ID);
            Assert.Equal(server.Name, application.Server.Name);
            Assert.Equal(server.ServerUrl, application.Server.ServerUrl);
            Assert.Equal(nameof(DatabaseType.SQLServer), application.DatabaseType);
            Assert.True(application.HasConnectionString);
            Assert.Equal(1024, application.MaxAllowedFileSizeInKB);
            Assert.Equal(60, application.AuthTokenExpireMinutes);
            Assert.Equal("https://app.example.test/confirmed", application.EmailConfirmationRedirectUrl);
            Assert.Equal("smtp.app.example.test", application.MailServer);
            Assert.Equal(2525, application.MailServerPort);
            Assert.Equal("noreply@app.example.test", application.MailFromAddress);
            Assert.Equal("app-mailer", application.MailUserName);
            Assert.True(application.HasMailPassword);
            Assert.Equal("App mail", application.MailFromDisplayName);

            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task List_Should_Be_In_The_Order_The_Applications_Were_Created()
        {
            // Names and servers in the opposite order of the IDs, so a sort by name or by server would fail.
            var (email, _) = await _portal.CreateUserAsync();
            var older = await _portal.CreateServerAsync();
            var newer = await _portal.CreateServerAsync();
            var first = await _portal.CreateApplicationAsync(newer.ID, email, "zzz-admin-order");
            var second = await _portal.CreateApplicationAsync(older.ID, email, "aaa-admin-order");
            var admin = await _portal.CreateAdminClientAsync();

            var list = (await (await admin.GetAsync(Url)).ReadJsonAsync<ListResponse<AdminApplicationResponse>>()).Data;

            var firstIndex = list.FindIndex(x => x.Token == first.Application.Token);
            var secondIndex = list.FindIndex(x => x.Token == second.Application.Token);

            Assert.True(firstIndex >= 0 && firstIndex < secondIndex, "Applications are not listed in the order they were created.");
            Assert.Equal(list.Select(x => x.ID).OrderBy(x => x), list.Select(x => x.ID));
        }

        [Fact]
        public async Task List_Should_Not_Return_Any_Secret_Value()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-list-secrets");
            var stored = await LoadStoredSecretsAsync(seeded);
            var admin = await _portal.CreateAdminClientAsync();

            var response = await admin.GetAsync(Url);
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(seeded.Application.Token, text);
            AssertHasNoSecret(text, seeded, stored);
        }

        [Fact]
        public async Task List_Should_Say_No_Secret_Is_Set_When_None_Is_Stored()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-list-no-secrets");
            var sqlServer = await CreateApplicationOfAnotherUserAsync("admin-list-nothing-stored");

            await _portal.WithDbContextAsync(async db =>
            {
                // An SQLite application with a stray connection string, and no mail password.
                var application = await db.Applications.SingleAsync(x => x.ID == seeded.Application.ID);
                application.DatabaseType = (int)DatabaseType.SQLLite;
                application.MailPassword = null;

                // An SQL Server application without a connection string, and a blank mail password.
                var second = await db.Applications.SingleAsync(x => x.ID == sqlServer.Application.ID);
                second.ConnectionString = null;
                second.MailPassword = "  ";

                return await db.SaveChangesAsync();
            });

            var admin = await _portal.CreateAdminClientAsync();

            var list = (await (await admin.GetAsync(Url)).ReadJsonAsync<ListResponse<AdminApplicationResponse>>()).Data;

            var application = Assert.Single(list, x => x.Token == seeded.Application.Token);
            Assert.Equal(nameof(DatabaseType.SQLLite), application.DatabaseType);
            Assert.False(application.HasConnectionString);
            Assert.False(application.HasMailPassword);

            var second = Assert.Single(list, x => x.Token == sqlServer.Application.Token);
            Assert.Equal(nameof(DatabaseType.SQLServer), second.DatabaseType);
            Assert.False(second.HasConnectionString);
            Assert.False(second.HasMailPassword);
        }

        [Fact]
        public async Task List_Application_Without_Owner_Email_And_Mail_Settings_Should_Return_Null_For_Each()
        {
            var server = await _portal.CreateServerAsync();

            // The bare row: no AdminEmail, no mail settings, offline.
            var seeded = await _portal.CreateApplicationAsync(server.ID);
            var admin = await _portal.CreateAdminClientAsync();

            var list = (await (await admin.GetAsync(Url)).ReadJsonAsync<ListResponse<AdminApplicationResponse>>()).Data;

            var application = Assert.Single(list, x => x.Token == seeded.Token);
            Assert.Null(application.OwnerEmail);
            Assert.False(application.Online);
            Assert.Null(application.EmailConfirmationRedirectUrl);
            Assert.Null(application.MailServer);
            Assert.Null(application.MailServerPort);
            Assert.Null(application.MailFromAddress);
            Assert.Null(application.MailUserName);
            Assert.False(application.HasMailPassword);
            Assert.Null(application.MailFromDisplayName);
        }

        [Fact]
        public async Task List_User_Without_Admin_Role_Should_Return_403_Even_For_Their_Own_Applications()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            await _portal.CreateApplicationAsync(server.ID, email, "own-app");
            var client = await _portal.CreateSignedInClientAsync(email, password);

            await AssertForbiddenAsync(await client.GetAsync(Url));
        }

        [Fact]
        public async Task List_Anonymous_Should_Return_401()
        {
            await AssertUnauthorizedAsync(await _portal.CreateAnonymousClient().GetAsync(Url));
        }

        // ---------- Get one ----------

        [Fact]
        public async Task Get_Admin_Should_Return_An_Application_Of_Another_User_With_Entities_And_Properties()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-detail");
            var server = await LoadServerAsync(seeded.Application.ServerID);
            var appId = seeded.Application.ID;
            await _portal.WithDbContextAsync(db =>
            {
                db.Entities.AddRange(EntityScene.SeedEntities(appId));
                return db.SaveChangesAsync();
            });

            var (adminEmail, adminPassword) = await _portal.CreateAdminUserAsync();
            var admin = await _portal.CreateSignedInClientAsync(adminEmail, adminPassword);

            var response = await admin.GetAsync($"{Url}/{seeded.Application.Token}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var detail = await response.ReadJsonAsync<AdminApplicationDetailResponse>();
            Assert.Equal(seeded.Application.Token, detail.Application.Token);
            Assert.Equal("admin-detail", detail.Application.Name);
            Assert.Equal(seeded.Application.AdminEmail, detail.Application.OwnerEmail);
            Assert.True(detail.Application.HasConnectionString);
            Assert.True(detail.Application.HasMailPassword);

            // What the data browser reads from it: where the records live and the upload limit.
            Assert.Equal(server.ServerUrl, detail.Application.Server.ServerUrl);
            Assert.Equal(1024, detail.Application.MaxAllowedFileSizeInKB);

            // Entities by name, as GET /applications/{appToken}/entities sends them.
            Assert.Equal(new[] { "Customers", "Files", "Invoices", "Orders", "Users" }, detail.Entities.Select(x => x.Name));

            // The primary key, then the custom properties by name, then the system ones by name.
            var orders = Assert.Single(detail.Entities, x => x.Name == "Orders");
            var properties = orders.Properties ?? throw new InvalidOperationException("No Properties.");
            Assert.Equal(
                new[] { "ID", "Agent_ID", "Amount", "Code", "Customer_ID", "Paid", "Secret", "Created", "Owner" },
                properties.Select(x => x.Name));
            Assert.Equal(Enumerable.Range(0, 9), properties.Select(x => x.Position).OrderBy(x => x));
            Assert.True(Assert.Single(properties, x => x.Name == "Secret").Encrypted);
            Assert.False(Assert.Single(properties, x => x.Name == "ID").AllowEdit);
            Assert.True(Assert.Single(properties, x => x.Name == "Code").AllowEdit);
            Assert.Equal(2, orders.Constraints.Count);
            Assert.True(orders.AllowPost);

            var users = Assert.Single(detail.Entities, x => x.Name == "Users");
            Assert.True(users.IsSystem);
            Assert.False(users.AllowPost);
            Assert.Equal(new[] { "ID", "Nickname", "Email" }, (users.Properties ?? throw new InvalidOperationException("No Properties.")).Select(x => x.Name));

            // A read: no audit row and no call to the API server.
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.UserEmail == adminEmail)));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Get_Entities_Should_Be_The_Entities_The_Owner_Gets_With_Their_Properties()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var appId = scene.AppId;

            // A lowercase name: where another sort would show.
            await _portal.WithDbContextAsync(db =>
            {
                db.Entities.Add(EntityScene.Entity(appId, "archive", false,
                    EntityScene.Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true)));

                return db.SaveChangesAsync();
            });

            var admin = await _portal.CreateAdminClientAsync();

            var owned = await (await scene.Owner.GetAsync($"{scene.AppUrl}/entities?IncludeProperties=true")).ReadJsonAsync<ListResponse<EntityResponse>>();
            var detail = await (await admin.GetAsync($"{Url}/{scene.Token}")).ReadJsonAsync<AdminApplicationDetailResponse>();

            Assert.Equal("archive", detail.Entities[0].Name);
            Assert.Equal(JsonSerializer.Serialize(owned.Data), JsonSerializer.Serialize(detail.Entities));
        }

        [Fact]
        public async Task Get_Should_Not_Return_Any_Secret_Value()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-detail-secrets");
            var appId = seeded.Application.ID;
            await _portal.WithDbContextAsync(db =>
            {
                db.Entities.AddRange(EntityScene.SeedEntities(appId));
                return db.SaveChangesAsync();
            });

            var stored = await LoadStoredSecretsAsync(seeded);
            var admin = await _portal.CreateAdminClientAsync();

            var response = await admin.GetAsync($"{Url}/{seeded.Application.Token}");
            var text = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Orders", text);
            AssertHasNoSecret(text, seeded, stored);
        }

        [Fact]
        public async Task Get_Application_Without_Entities_Should_Return_An_Empty_List()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-detail-empty");
            var admin = await _portal.CreateAdminClientAsync();

            var response = await admin.GetAsync($"{Url}/{seeded.Application.Token}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var detail = await response.ReadJsonAsync<AdminApplicationDetailResponse>();
            Assert.Equal(seeded.Application.Token, detail.Application.Token);
            Assert.Empty(detail.Entities);
        }

        [Fact]
        public async Task Get_Unknown_Or_Differently_Cased_Token_Should_Return_404()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-detail-404");
            var admin = await _portal.CreateAdminClientAsync();

            await AssertNotFoundAsync(await admin.GetAsync($"{Url}/{Guid.NewGuid()}"));
            await AssertNotFoundAsync(await admin.GetAsync($"{Url}/{seeded.Application.Token.ToUpperInvariant()}"));
        }

        [Fact]
        public async Task Get_User_Without_Admin_Role_Should_Return_403_Even_As_The_Owner()
        {
            var (email, password) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var seeded = await _portal.CreateApplicationAsync(server.ID, email, "own-app-detail");
            var client = await _portal.CreateSignedInClientAsync(email, password);

            await AssertForbiddenAsync(await client.GetAsync($"{Url}/{seeded.Application.Token}"));
            await AssertForbiddenAsync(await client.GetAsync($"{Url}/{Guid.NewGuid()}"));
        }

        [Fact]
        public async Task Get_Anonymous_Should_Return_401()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-detail-anonymous");

            await AssertUnauthorizedAsync(await _portal.CreateAnonymousClient().GetAsync($"{Url}/{seeded.Application.Token}"));
        }

        [Fact]
        public async Task Get_Admin_After_Signing_In_Elsewhere_Should_Return_401()
        {
            var seeded = await CreateApplicationOfAnotherUserAsync("admin-detail-revoked");
            var (email, password) = await _portal.CreateAdminUserAsync();

            var first = await _portal.CreateSignedInClientAsync(email, password);
            await _portal.CreateSignedInClientAsync(email, password);

            await AssertUnauthorizedAsync(await first.GetAsync($"{Url}/{seeded.Application.Token}"));
        }

        // ---------- Helpers ----------

        /// <summary>
        /// An application of a new user, who shares it with nobody, with every mail setting filled in.
        /// </summary>
        private async Task<SeededApplication> CreateApplicationOfAnotherUserAsync(string name)
        {
            var (email, _) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var seeded = await _portal.CreateApplicationAsync(server.ID, email, name);

            await _portal.WithDbContextAsync(async db =>
            {
                var application = await db.Applications.SingleAsync(x => x.ID == seeded.Application.ID);
                application.EmailConfirmationRedirectUrl = "https://app.example.test/confirmed";
                application.MailServer = "smtp.app.example.test";
                application.MailServerPort = 2525;
                application.MailFromAddress = "noreply@app.example.test";
                application.MailUserName = "app-mailer";
                application.MailPassword = $"mail-pw-{Guid.NewGuid():N}";
                application.MailFromDisplayName = "App mail";

                return await db.SaveChangesAsync();
            });

            return seeded;
        }

        private Task<DBWS_Server> LoadServerAsync(long serverId)
        {
            return _portal.WithDbContextAsync(db => db.Servers.AsNoTracking().SingleAsync(x => x.ID == serverId));
        }

        private async Task<(string ConnectionString, string MailPassword, string StoredEncryptionKey)> LoadStoredSecretsAsync(SeededApplication seeded)
        {
            var application = await _portal.WithDbContextAsync(db => db.Applications
                .AsNoTracking()
                .SingleAsync(x => x.ID == seeded.Application.ID));

            return (
                application.ConnectionString ?? throw new InvalidOperationException("No connection string seeded."),
                application.MailPassword ?? throw new InvalidOperationException("No mail password seeded."),
                application.EncryptionKey);
        }

        private static void AssertHasNoSecret(string text, SeededApplication seeded, (string ConnectionString, string MailPassword, string StoredEncryptionKey) stored)
        {
            Assert.DoesNotContain(stored.ConnectionString, text);
            Assert.DoesNotContain(stored.MailPassword, text);
            Assert.DoesNotContain(stored.StoredEncryptionKey, text);
            Assert.DoesNotContain(seeded.EncryptionKey, text);
            Assert.DoesNotContain("\"ConnectionString\"", text);
            Assert.DoesNotContain("\"MailPassword\"", text);
            Assert.DoesNotContain("\"EncryptionKey\"", text);
        }

        private static async Task AssertNotFoundAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Equal("Application", error.Entity);
        }

        private static async Task AssertForbiddenAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }
    }
}
