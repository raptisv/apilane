using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// GET and PUT /applications/{appToken}/email-settings.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class ApplicationEmailSettingsApiTests
    {
        private const string StoredPassword = "stored-mail-password";

        private readonly PortalFactory _portal;

        public ApplicationEmailSettingsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Read ----------

        [Fact]
        public async Task Get_Should_Return_The_Settings_Without_The_Password()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.GetAsync(scene.Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(StoredPassword, text);
            Assert.DoesNotContain("\"MailPassword\"", text);

            var settings = await response.ReadJsonAsync<EmailSettingsResponse>();
            Assert.Equal("smtp.example.test", settings.MailServer);
            Assert.Equal(587, settings.MailServerPort);
            Assert.Equal("noreply@example.test", settings.MailFromAddress);
            Assert.Equal("Example", settings.MailFromDisplayName);
            Assert.Equal("mailer", settings.MailUserName);
            Assert.True(settings.HasMailPassword);
            Assert.Equal("https://example.test/confirmed", settings.EmailConfirmationRedirectUrl);
            Assert.True(settings.IsMailSetup);

            // A read changes nothing and calls nothing.
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Get_Application_Without_Mail_Settings_Should_Return_Nulls_And_Not_Set_Up()
        {
            var scene = await CreateSceneAsync(withMailSettings: false);

            var settings = await (await scene.Owner.GetAsync(scene.Url)).ReadJsonAsync<EmailSettingsResponse>();

            Assert.Null(settings.MailServer);
            Assert.Null(settings.MailServerPort);
            Assert.Null(settings.MailFromAddress);
            Assert.Null(settings.MailFromDisplayName);
            Assert.Null(settings.MailUserName);
            Assert.False(settings.HasMailPassword);
            Assert.Null(settings.EmailConfirmationRedirectUrl);
            Assert.False(settings.IsMailSetup);
        }

        // ---------- Update ----------

        [Fact]
        public async Task Update_Should_Save_Every_Value_Together_And_Reset_The_Cache_Once()
        {
            var scene = await CreateSceneAsync(withMailSettings: false);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.Url, FullBody("new-password").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(Services.ApiServerCacheReset.WarningHeaderName));

            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("new-password", text);

            var settings = await response.ReadJsonAsync<EmailSettingsResponse>();
            AssertFullBody(settings);
            Assert.True(settings.HasMailPassword);
            Assert.True(settings.IsMailSetup);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal("smtp.new.test", stored.MailServer);
            Assert.Equal(465, stored.MailServerPort);
            Assert.Equal("sender@new.test", stored.MailFromAddress);
            Assert.Equal("New sender", stored.MailFromDisplayName);
            Assert.Equal("new-user", stored.MailUserName);
            Assert.Equal("new-password", stored.MailPassword);
            Assert.Equal("https://new.test/welcome", stored.EmailConfirmationRedirectUrl);

            // A read afterwards gives the same answer.
            var read = await (await scene.Owner.GetAsync(scene.Url)).ReadJsonAsync<EmailSettingsResponse>();
            AssertFullBody(read);
            Assert.True(read.HasMailPassword);

            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"{scene.Server.ServerUrl}/api/Application/ClearCache", request.Url);
            await AssertPortalHeadersAsync(request, scene.Token, scene.OwnerEmail);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Update_Without_A_Password_Should_Keep_The_Stored_One(bool sendNull)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = FullBody(null);

            if (!sendNull)
            {
                body.Remove("MailPassword");
            }

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var settings = await response.ReadJsonAsync<EmailSettingsResponse>();
            Assert.True(settings.HasMailPassword);
            Assert.True(settings.IsMailSetup);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal(StoredPassword, stored.MailPassword);
            Assert.Equal("smtp.new.test", stored.MailServer);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Update_With_An_Empty_Password_Should_Clear_It(string password)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.Url, FullBody(password).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var settings = await response.ReadJsonAsync<EmailSettingsResponse>();
            Assert.False(settings.HasMailPassword);
            Assert.False(settings.IsMailSetup);
            Assert.Null((await LoadAsync(scene.AppId)).MailPassword);
        }

        [Fact]
        public async Task Update_With_A_Password_Should_Replace_It()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.Url, FullBody(" replaced password ").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((await response.ReadJsonAsync<EmailSettingsResponse>()).HasMailPassword);

            // Stored as sent: not trimmed.
            Assert.Equal(" replaced password ", (await LoadAsync(scene.AppId)).MailPassword);
        }

        [Fact]
        public async Task Update_With_Empty_Values_Should_Clear_Them_And_Store_Null()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = new Dictionary<string, object?>
            {
                ["MailServer"] = "",
                ["MailServerPort"] = null,
                ["MailFromAddress"] = "  ",
                ["MailFromDisplayName"] = "",
                ["MailUserName"] = "",
                ["MailPassword"] = "",
                ["EmailConfirmationRedirectUrl"] = ""
            };

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var settings = await response.ReadJsonAsync<EmailSettingsResponse>();
            Assert.False(settings.IsMailSetup);
            Assert.False(settings.HasMailPassword);

            var stored = await LoadAsync(scene.AppId);
            Assert.Null(stored.MailServer);
            Assert.Null(stored.MailServerPort);
            Assert.Null(stored.MailFromAddress);
            Assert.Null(stored.MailFromDisplayName);
            Assert.Null(stored.MailUserName);
            Assert.Null(stored.MailPassword);
            Assert.Null(stored.EmailConfirmationRedirectUrl);
        }

        [Fact]
        public async Task Update_With_An_Empty_Body_Should_Clear_Everything_But_The_Password()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.Url, new { }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var settings = await response.ReadJsonAsync<EmailSettingsResponse>();
            Assert.True(settings.HasMailPassword);
            Assert.False(settings.IsMailSetup);

            var stored = await LoadAsync(scene.AppId);
            Assert.Null(stored.MailServer);
            Assert.Null(stored.MailServerPort);
            Assert.Null(stored.MailFromAddress);
            Assert.Null(stored.MailFromDisplayName);
            Assert.Null(stored.MailUserName);
            Assert.Null(stored.EmailConfirmationRedirectUrl);
            Assert.Equal(StoredPassword, stored.MailPassword);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(65535)]
        public async Task Update_Port_Of_1_To_65535_Should_Be_Saved(int port)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = FullBody(null);
            body["MailServerPort"] = port;

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(port, (await response.ReadJsonAsync<EmailSettingsResponse>()).MailServerPort);
            Assert.Equal(port, (await LoadAsync(scene.AppId)).MailServerPort);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(65536)]
        public async Task Update_Port_Out_Of_Range_Should_Return_400_And_Change_Nothing(int port)
        {
            var scene = await CreateSceneAsync();

            var body = FullBody("not-saved");
            body["MailServerPort"] = port;

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "MailServerPort: Must be between 1 and 65535");
            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Update_Port_That_Is_Not_A_Number_Should_Return_400_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PutAsync(scene.Url, EntityScene.Json("{\"MailServer\":\"smtp.new.test\",\"MailServerPort\":\"abc\"}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(PortalErrorCode.Validation, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Update_Redirect_Url_Of_10000_Characters_Should_Be_Saved_And_Longer_Rejected()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var longest = "https://example.test/" + new string('a', 10000 - 21);
            var body = FullBody(null);
            body["EmailConfirmationRedirectUrl"] = longest;

            var saved = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(longest, (await LoadAsync(scene.AppId)).EmailConfirmationRedirectUrl);

            body["EmailConfirmationRedirectUrl"] = longest + "a";
            body["MailServer"] = "not-saved.test";

            var rejected = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            await EntityScene.AssertValidationAsync(rejected, "EmailConfirmationRedirectUrl: Must be 10000 characters or fewer");

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal(longest, stored.EmailConfirmationRedirectUrl);
            Assert.Equal("smtp.new.test", stored.MailServer);
        }

        [Fact]
        public async Task Update_Redirect_Url_Should_Not_Be_Checked_To_Be_A_Url()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = FullBody(null);
            body["EmailConfirmationRedirectUrl"] = "not a url";
            body["MailFromAddress"] = "not an address";

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal("not a url", stored.EmailConfirmationRedirectUrl);
            Assert.Equal("not an address", stored.MailFromAddress);
        }

        [Theory]
        [InlineData("MailServer")]
        [InlineData("MailServerPort")]
        [InlineData("MailFromAddress")]
        [InlineData("MailFromDisplayName")]
        [InlineData("MailUserName")]
        [InlineData("MailPassword")]
        public async Task IsMailSetup_Should_Be_False_When_One_Smtp_Value_Is_Missing(string missing)
        {
            var scene = await CreateSceneAsync(withMailSettings: false);
            ScriptApiServer();

            var body = FullBody("a-password");

            // An empty password clears it; null would keep the (absent) stored one.
            body[missing] = missing == "MailPassword" ? "" : null;

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await response.ReadJsonAsync<EmailSettingsResponse>()).IsMailSetup);
            Assert.False((await (await scene.Owner.GetAsync(scene.Url)).ReadJsonAsync<EmailSettingsResponse>()).IsMailSetup);
        }

        [Fact]
        public async Task IsMailSetup_Should_Not_Need_The_Redirect_Url()
        {
            var scene = await CreateSceneAsync(withMailSettings: false);
            ScriptApiServer();

            var body = FullBody("a-password");
            body["EmailConfirmationRedirectUrl"] = null;

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            Assert.True((await response.ReadJsonAsync<EmailSettingsResponse>()).IsMailSetup);
        }

        [Fact]
        public async Task Update_When_The_Cache_Reset_Fails_Should_Save_And_Warn()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.InternalServerError, EntityScene.ApiError("Down"));

            var response = await scene.Owner.PutAsync(scene.Url, FullBody(null).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            EntityScene.AssertWarning(response);
            Assert.Equal("smtp.new.test", (await LoadAsync(scene.AppId)).MailServer);
        }

        // ---------- Audit ----------

        [Fact]
        public async Task Update_Of_The_Password_Only_Should_Audit_The_Change_With_Both_Values_Masked()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // Every field as it is stored, and a new password.
            var body = new Dictionary<string, object?>
            {
                ["MailServer"] = "smtp.example.test",
                ["MailServerPort"] = 587,
                ["MailFromAddress"] = "noreply@example.test",
                ["MailFromDisplayName"] = "Example",
                ["MailUserName"] = "mailer",
                ["MailPassword"] = "a-new-password",
                ["EmailConfirmationRedirectUrl"] = "https://example.test/confirmed"
            };

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));

            var row = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));

            Assert.Equal($"Application | {scene.Name} | Modified | {scene.AppId}", Shape(row));

            // Compared before masking, so the change is recorded; the values never are.
            Assert.Equal(new[] { "MailPassword: *** -> ***" }, Changes(row));
            Assert.DoesNotContain("a-new-password", row.Changes);
            Assert.DoesNotContain(StoredPassword, row.Changes);
        }

        [Fact]
        public async Task Update_Should_Audit_Every_Changed_Value_With_Old_And_New()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.Url, FullBody("").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var row = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal(scene.AppId, row.AppID);
            Assert.Equal(
                new[]
                {
                    "EmailConfirmationRedirectUrl: https://example.test/confirmed -> https://new.test/welcome",
                    "MailFromAddress: noreply@example.test -> sender@new.test",
                    "MailFromDisplayName: Example -> New sender",
                    "MailPassword: *** -> ",
                    "MailServer: smtp.example.test -> smtp.new.test",
                    "MailServerPort: 587 -> 465",
                    "MailUserName: mailer -> new-user"
                },
                Changes(row));
        }

        [Fact]
        public async Task Update_With_The_Same_Values_Should_Write_No_Audit_Row_But_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = new Dictionary<string, object?>
            {
                ["MailServer"] = "smtp.example.test",
                ["MailServerPort"] = 587,
                ["MailFromAddress"] = "noreply@example.test",
                ["MailFromDisplayName"] = "Example",
                ["MailUserName"] = "mailer",
                ["MailPassword"] = null,
                ["EmailConfirmationRedirectUrl"] = "https://example.test/confirmed"
            };

            var response = await scene.Owner.PutAsync(scene.Url, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        // ---------- Access ----------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Agent_With_Full_Grants_Should_Read_But_Not_Change_Mail_Settings(bool legacyCookie)
        {
            var scene = await CreateSceneAsync();
            using var agent = await CreateAgentWithFullGrantsAsync(scene, legacyCookie);
            var before = await LoadAsync(scene.AppId);

            // Even a stored policy from before email-setting writes were retired cannot let an
            // agent move SMTP credentials or account emails to another destination. Supplying
            // a replacement password does not make changing the transport safe either.
            foreach (var password in new string?[] { null, "", "agent-supplied-password" })
            {
                var response = await agent.PutAsync(scene.Url, FullBody(password).ToJsonContent());

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Forbidden, error.Code);
                Assert.Equal(PortalAgent.RefusedMessage, error.Message);
                await AssertUnchangedAsync(scene);
                Assert.Equal(before.DateModified, (await LoadAsync(scene.AppId)).DateModified);
            }

            // Reads remain available under the saved read grant, with the password still hidden.
            var read = await agent.GetAsync(scene.Url);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var settings = await read.ReadJsonAsync<EmailSettingsResponse>();
            Assert.Equal("smtp.example.test", settings.MailServer);
            Assert.Equal("https://example.test/confirmed", settings.EmailConfirmationRedirectUrl);
            Assert.True(settings.HasMailPassword);
            var text = await read.Content.ReadAsStringAsync();
            Assert.DoesNotContain(StoredPassword, text);
            Assert.DoesNotContain("\"MailPassword\"", text);
            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Collaborator_Should_Read_And_Update()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var read = await scene.Collaborator.GetAsync(scene.Url);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.True((await read.ReadJsonAsync<EmailSettingsResponse>()).IsMailSetup);

            var updated = await scene.Collaborator.PutAsync(scene.Url, FullBody(null).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal("smtp.new.test", (await LoadAsync(scene.AppId)).MailServer);

            var row = Assert.Single(await AuditRowsAsync(scene.CollaboratorEmail));
            Assert.Equal(scene.AppId, row.AppID);
        }

        [Fact]
        public async Task Stranger_And_Unknown_Token_Should_Get_404_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            var admin = await _portal.CreateAdminClientAsync();

            // The Admin role gives nothing here.
            foreach (var client in new[] { scene.Stranger, admin })
            {
                await EntityScene.AssertNotFoundAsync(await client.GetAsync(scene.Url), "Application");
                await EntityScene.AssertNotFoundAsync(await client.PutAsync(scene.Url, FullBody("nope").ToJsonContent()), "Application");
            }

            var unknownUrl = $"/api/v1/applications/{Guid.NewGuid()}/email-settings";
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(unknownUrl), "Application");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.PutAsync(unknownUrl, FullBody("nope").ToJsonContent()), "Application");

            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Anonymous_Should_Get_401_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            var anonymous = _portal.CreateAnonymousClient();

            await EntityScene.AssertErrorAsync(await anonymous.GetAsync(scene.Url), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            await EntityScene.AssertErrorAsync(await anonymous.PutAsync(scene.Url, FullBody("nope").ToJsonContent()), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);

            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Update_Without_The_Csrf_Header_Should_Get_403_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await scene.Owner.PutAsync(scene.Url, FullBody("nope").ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);

            await AssertUnchangedAsync(scene);
        }

        // ---------- Contract ----------

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Warning_Header_Of_The_Update()
        {
            var client = await _portal.CreateUserClientAsync();
            var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")) ?? throw new InvalidOperationException("No document.");

            const string path = "/api/v1/applications/{appToken}/email-settings";

            Assert.NotNull(document["paths"]?[path]?["put"]?["responses"]?["200"]?["headers"]?["Warning"]);
            Assert.Null(document["paths"]?[path]?["get"]?["responses"]?["200"]?["headers"]);
        }

        // ---------- Helpers ----------

        private record Scene(
            string OwnerEmail,
            HttpClient Owner,
            string CollaboratorEmail,
            HttpClient Collaborator,
            HttpClient Stranger,
            DBWS_Server Server,
            string Token,
            long AppId,
            string Name)
        {
            public string Url => $"/api/v1/applications/{Token}/email-settings";
        }

        /// <summary>
        /// Three new users and an application of the owner, shared with the collaborator, with
        /// complete mail settings unless <paramref name="withMailSettings"/> is false.
        /// </summary>
        private async Task<Scene> CreateSceneAsync(bool withMailSettings = true)
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await _portal.CreateUserAsync();

            var server = await _portal.CreateServerAsync();
            var name = $"email-{Guid.NewGuid():N}";
            var seeded = await _portal.CreateApplicationAsync(server.ID, ownerEmail, name, collaboratorEmail);

            if (withMailSettings)
            {
                await SetStoredAsync(seeded.Application.ID, SetMailSettings);
            }

            return new Scene(
                ownerEmail,
                await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword),
                collaboratorEmail,
                await _portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword),
                await _portal.CreateSignedInClientAsync(strangerEmail, strangerPassword),
                server,
                seeded.Application.Token,
                seeded.Application.ID,
                name);
        }

        private static void SetMailSettings(DBWS_Application application)
        {
            application.MailServer = "smtp.example.test";
            application.MailServerPort = 587;
            application.MailFromAddress = "noreply@example.test";
            application.MailFromDisplayName = "Example";
            application.MailUserName = "mailer";
            application.MailPassword = StoredPassword;
            application.EmailConfirmationRedirectUrl = "https://example.test/confirmed";
        }

        private async Task<HttpClient> CreateAgentWithFullGrantsAsync(Scene scene, bool legacyCookie)
        {
            string email;
            HttpClient client;
            if (legacyCookie)
            {
                // Old agent accounts may retain both a password and an administrator role.
                // The restriction must follow the account, not only Bearer authentication.
                var (oldEmail, password) = await _portal.CreateAdminUserAsync();
                email = $"legacy-mail-{Guid.NewGuid():N}@agent.local";
                await _portal.WithDbContextAsync(async db =>
                {
                    var user = await db.Users.SingleAsync(x => x.Email == oldEmail);
                    user.Email = email;
                    user.UserName = email;
                    user.NormalizedEmail = email.ToUpperInvariant();
                    user.NormalizedUserName = email.ToUpperInvariant();
                    return await db.SaveChangesAsync();
                });
                client = await _portal.CreateSignedInClientAsync(email, password);
            }
            else
            {
                using var admin = await _portal.CreateAdminClientAsync();
                var response = await admin.PostAsync("/api/v1/admin/agents", new { Name = $"mail-{Guid.NewGuid():N}" }.ToJsonContent());
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var created = await response.ReadJsonAsync<AgentCreatedResponse>();
                email = created.Email;
                client = _portal.CreateAnonymousClient();
                client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Key);
            }

            var permissions = await (await scene.Owner.GetAsync($"/api/v1/applications/{scene.Token}/permissions"))
                .ReadJsonAsync<ApplicationPermissionsResponse>();
            var grants = permissions.Resources.Select(resource => new AgentPermissionGrant
            {
                Resource = resource.Resource,
                Read = resource.CanRead,
                Write = resource.CanWrite || resource.Resource == "email-settings",
                Delete = resource.CanDelete
            }).ToList();

            // Seed the retired write bit directly: new policies correctly reject it, but saved
            // grants must never bypass the endpoint's permanent restriction after an upgrade.
            await _portal.WithDbContextAsync(async db =>
            {
                var collaboration = new DBWS_Collaborate
                {
                    AppID = scene.AppId,
                    UserEmail = email,
                    DateModified = DateTime.UtcNow
                };
                db.Collaborations.Add(collaboration);
                db.AgentPermissions.Add(new PortalAgentPermission
                {
                    Collaboration = collaboration,
                    PermissionsJson = JsonSerializer.Serialize(grants)
                });
                return await db.SaveChangesAsync();
            });

            return client;
        }

        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        /// <summary>
        /// A complete body with new values for everything; the password as given (null keeps it).
        /// </summary>
        private static Dictionary<string, object?> FullBody(string? password)
        {
            return new Dictionary<string, object?>
            {
                ["MailServer"] = "smtp.new.test",
                ["MailServerPort"] = 465,
                ["MailFromAddress"] = "sender@new.test",
                ["MailFromDisplayName"] = "New sender",
                ["MailUserName"] = "new-user",
                ["MailPassword"] = password,
                ["EmailConfirmationRedirectUrl"] = "https://new.test/welcome"
            };
        }

        private static void AssertFullBody(EmailSettingsResponse settings)
        {
            Assert.Equal("smtp.new.test", settings.MailServer);
            Assert.Equal(465, settings.MailServerPort);
            Assert.Equal("sender@new.test", settings.MailFromAddress);
            Assert.Equal("New sender", settings.MailFromDisplayName);
            Assert.Equal("new-user", settings.MailUserName);
            Assert.Equal("https://new.test/welcome", settings.EmailConfirmationRedirectUrl);
        }

        private async Task<DBWS_Application> LoadAsync(long appId)
        {
            return await _portal.WithDbContextAsync(db => db.Applications.AsNoTracking().SingleAsync(x => x.ID == appId));
        }

        private Task<int> SetStoredAsync(long appId, Action<DBWS_Application> change)
        {
            return _portal.WithDbContextAsync(async db =>
            {
                change(await db.Applications.SingleAsync(x => x.ID == appId));

                return await db.SaveChangesAsync();
            });
        }

        /// <summary>
        /// The stored mail settings are those of <see cref="SetMailSettings"/>, nobody caused an
        /// audit row and the API server got no request.
        /// </summary>
        private async Task AssertUnchangedAsync(Scene scene)
        {
            var stored = await LoadAsync(scene.AppId);
            Assert.Equal("smtp.example.test", stored.MailServer);
            Assert.Equal(587, stored.MailServerPort);
            Assert.Equal("noreply@example.test", stored.MailFromAddress);
            Assert.Equal("Example", stored.MailFromDisplayName);
            Assert.Equal("mailer", stored.MailUserName);
            Assert.Equal(StoredPassword, stored.MailPassword);
            Assert.Equal("https://example.test/confirmed", stored.EmailConfirmationRedirectUrl);

            var appId = scene.AppId;
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.AppID == appId)));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        private Task<List<PortalAuditLog>> AuditRowsAsync(string userEmail)
        {
            return _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .Where(x => x.UserEmail == userEmail)
                .OrderBy(x => x.ID)
                .ToListAsync());
        }

        private static string Shape(PortalAuditLog row)
        {
            return $"{row.EntityType} | {row.EntityIdentifier} | {row.Action} | {row.AppID}";
        }

        /// <summary>
        /// The changes of an audit row as 'Property: old -> new', by property name.
        /// </summary>
        private static List<string> Changes(PortalAuditLog row)
        {
            using var document = JsonDocument.Parse(row.Changes ?? "[]");

            return document.RootElement.EnumerateArray()
                .Select(x => $"{x.GetProperty("Property").GetString()}: {x.GetProperty("OldValue").GetString()} -> {x.GetProperty("NewValue").GetString()}")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The headers every Portal call carries: the application token, the caller's own API
        /// token, the client name and Accept. Nothing else: no installation key.
        /// </summary>
        private async Task AssertPortalHeadersAsync(ApiServerRequest request, string appToken, string callerEmail)
        {
            var token = await _portal.WithDbContextAsync(db => db.Users
                .AsNoTracking()
                .Where(x => x.Email == callerEmail)
                .Select(x => x.AdminAuthToken)
                .SingleAsync());

            Assert.Equal(appToken, request.Headers["x-application-token"]);
            Assert.Equal("portal", request.Headers["x-client-id"]);
            Assert.Equal($"Bearer {token}", request.Headers["Authorization"]);
            Assert.Equal(new[] { "Accept", "Authorization", "x-application-token", "x-client-id" }, request.Headers.Keys.OrderBy(x => x));
        }
    }
}
