using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// The instance has one settings row, shared by every test class. Each test here starts from
    /// known values and the row is put back afterwards.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class AdminSettingsApiTests : IAsyncLifetime
    {
        private const string Url = "/api/v1/admin/settings";
        private const string StoredKey = "stored-installation-key-0123456789";
        private const string StoredMailPassword = "stored-mail-password";

        private readonly PortalFactory _portal;
        private GlobalSettings? _original;

        public AdminSettingsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        public async Task InitializeAsync()
        {
            _original = await ReadStoredAsync();

            await WriteStoredAsync(new GlobalSettings
            {
                InstanceTitle = "Before",
                InstallationKey = StoredKey,
                AllowRegisterToPortal = true,
                MailServer = "smtp.before.test",
                MailServerPort = 25,
                MailUserName = "before-user",
                MailPassword = StoredMailPassword,
                MailFromAddress = "before@portal.test",
                MailFromDisplayName = "Before"
            });
        }

        public async Task DisposeAsync()
        {
            if (_original is not null)
            {
                await WriteStoredAsync(_original);
            }
        }

        // ---------- Get ----------

        [Fact]
        public async Task Get_Should_Return_The_Settings_Without_The_Secrets()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.GetAsync(Url);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(StoredKey, body);
            Assert.DoesNotContain(StoredMailPassword, body);

            var settings = await response.ReadJsonAsync<InstanceSettingsResponse>();
            Assert.Equal("Before", settings.InstanceTitle);
            Assert.True(settings.AllowRegisterToPortal);
            Assert.True(settings.HasInstallationKey);
            Assert.Equal("smtp.before.test", settings.MailServer);
            Assert.Equal(25, settings.MailServerPort);
            Assert.Equal("before-user", settings.MailUserName);
            Assert.True(settings.HasMailPassword);
            Assert.Equal("before@portal.test", settings.MailFromAddress);
            Assert.Equal("Before", settings.MailFromDisplayName);
            Assert.True(settings.IsMailSetup);
        }

        [Fact]
        public async Task Get_Without_Mail_Settings_Should_Say_So()
        {
            var stored = await ReadStoredAsync();
            stored.MailServer = null;
            stored.MailServerPort = null;
            stored.MailPassword = null;
            await WriteStoredAsync(stored);

            var client = await _portal.CreateAdminClientAsync();

            var settings = await (await client.GetAsync(Url)).ReadJsonAsync<InstanceSettingsResponse>();

            Assert.Null(settings.MailServer);
            Assert.Null(settings.MailServerPort);
            Assert.False(settings.HasMailPassword);
            Assert.False(settings.IsMailSetup);
        }

        [Fact]
        public async Task Get_Anonymous_Should_Return_401()
        {
            var response = await _portal.CreateAnonymousClient().GetAsync(Url);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        [Fact]
        public async Task Get_User_Without_Admin_Role_Should_Return_403()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.GetAsync(Url);

            await AssertNotAdminAsync(response);
        }

        // ---------- Update ----------

        [Fact]
        public async Task Update_Should_Store_Every_Value_And_Add_An_Audit_Row()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.PutAsync(Url, new InstanceSettingsRequest
            {
                InstanceTitle = "After",
                AllowRegisterToPortal = false,
                InstallationKey = "new-installation-key-0123456789abc",
                MailServer = "smtp.after.test",
                MailServerPort = 587,
                MailUserName = "after-user",
                MailPassword = "new-mail-password",
                MailFromAddress = "after@portal.test",
                MailFromDisplayName = "After"
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("new-installation-key", body);
            Assert.DoesNotContain("new-mail-password", body);

            var settings = await response.ReadJsonAsync<InstanceSettingsResponse>();
            Assert.Equal("After", settings.InstanceTitle);
            Assert.False(settings.AllowRegisterToPortal);
            Assert.True(settings.HasInstallationKey);
            Assert.Equal("smtp.after.test", settings.MailServer);
            Assert.Equal(587, settings.MailServerPort);
            Assert.True(settings.HasMailPassword);
            Assert.True(settings.IsMailSetup);

            var stored = await ReadStoredAsync();
            Assert.Equal("After", stored.InstanceTitle);
            Assert.False(stored.AllowRegisterToPortal);
            Assert.Equal("new-installation-key-0123456789abc", stored.InstallationKey);
            Assert.Equal("smtp.after.test", stored.MailServer);
            Assert.Equal(587, stored.MailServerPort);
            Assert.Equal("after-user", stored.MailUserName);
            Assert.Equal("new-mail-password", stored.MailPassword);
            Assert.Equal("after@portal.test", stored.MailFromAddress);
            Assert.Equal("After", stored.MailFromDisplayName);

            var audit = await _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .SingleAsync(x => x.EntityType == "Global Settings" && x.UserEmail == email));
            Assert.Equal("Modified", audit.Action);
            Assert.Equal("Instance Settings", audit.EntityIdentifier);
            Assert.Null(audit.AppID);
            Assert.Contains("smtp.after.test", audit.Changes);
            // The audit log never holds the secrets.
            Assert.DoesNotContain("new-installation-key", audit.Changes);
            Assert.DoesNotContain("new-mail-password", audit.Changes);
        }

        [Fact]
        public async Task Update_Should_Change_The_Title_Of_The_Session()
        {
            var client = await _portal.CreateAdminClientAsync();

            await client.PutAsync(Url, Valid(x => x.InstanceTitle = "Renamed").ToJsonContent());

            var session = await (await client.GetAsync("/api/v1/session")).ReadJsonAsync<SessionResponse>();
            Assert.Equal("Renamed", session.InstanceTitle);
        }

        [Theory]
        [InlineData("{\"InstanceTitle\":\"After\",\"AllowRegisterToPortal\":true}")]
        [InlineData("{\"InstanceTitle\":\"After\",\"AllowRegisterToPortal\":true,\"InstallationKey\":null,\"MailPassword\":null}")]
        public async Task Update_Without_The_Secrets_Should_Keep_Them(string body)
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(Url, Json(body));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var settings = await response.ReadJsonAsync<InstanceSettingsResponse>();
            Assert.True(settings.HasInstallationKey);
            Assert.True(settings.HasMailPassword);

            var stored = await ReadStoredAsync();
            Assert.Equal("After", stored.InstanceTitle);
            Assert.Equal(StoredKey, stored.InstallationKey);
            Assert.Equal(StoredMailPassword, stored.MailPassword);
        }

        [Fact]
        public async Task Update_Without_The_Mail_Settings_Should_Clear_Them()
        {
            // Unlike the secrets, a mail setting that is left out is written as empty.
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(Url, Json("{\"InstanceTitle\":\"After\",\"AllowRegisterToPortal\":true}"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await response.ReadJsonAsync<InstanceSettingsResponse>()).IsMailSetup);

            var stored = await ReadStoredAsync();
            Assert.Null(stored.MailServer);
            Assert.Null(stored.MailServerPort);
            Assert.Null(stored.MailUserName);
            Assert.Null(stored.MailFromAddress);
            Assert.Null(stored.MailFromDisplayName);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Update_With_Empty_Text_Should_Store_Null(string empty)
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(Url, new InstanceSettingsRequest
            {
                InstanceTitle = "After",
                AllowRegisterToPortal = true,
                MailServer = empty,
                MailUserName = empty,
                MailPassword = empty,
                MailFromAddress = empty,
                MailFromDisplayName = empty
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var settings = await response.ReadJsonAsync<InstanceSettingsResponse>();
            Assert.Null(settings.MailServer);
            Assert.False(settings.HasMailPassword);

            var stored = await ReadStoredAsync();
            Assert.Null(stored.MailServer);
            Assert.Null(stored.MailUserName);
            Assert.Null(stored.MailPassword);
            Assert.Null(stored.MailFromAddress);
            Assert.Null(stored.MailFromDisplayName);
            // The installation key was not sent, so it is kept.
            Assert.Equal(StoredKey, stored.InstallationKey);
        }

        [Fact]
        public async Task Update_With_The_Same_Values_Should_Succeed_Without_An_Audit_Row()
        {
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.PutAsync(Url, Valid().ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.UserEmail == email)));
        }

        [Fact]
        public async Task Update_Only_The_Installation_Key_Should_Add_An_Audit_Row_Without_The_Key()
        {
            const string newKey = "rotated-installation-key-0123456789";
            var (email, password) = await _portal.CreateAdminUserAsync();
            var client = await _portal.CreateSignedInClientAsync(email, password);

            var response = await client.PutAsync(Url, Valid(x => x.InstallationKey = newKey).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(newKey, (await ReadStoredAsync()).InstallationKey);

            var audit = await _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .SingleAsync(x => x.EntityType == "Global Settings" && x.UserEmail == email));
            Assert.Equal("Modified", audit.Action);
            Assert.Contains("\"Property\":\"InstallationKey\"", audit.Changes);
            Assert.Contains("***", audit.Changes);
            Assert.DoesNotContain(StoredKey, audit.Changes);
            Assert.DoesNotContain(newKey, audit.Changes);
        }

        [Theory]
        [InlineData("{\"AllowRegisterToPortal\":true}", "InstanceTitle", "Required")]
        [InlineData("{\"InstanceTitle\":null,\"AllowRegisterToPortal\":true}", "InstanceTitle", "Required")]
        [InlineData("{\"InstanceTitle\":\"\",\"AllowRegisterToPortal\":true}", "InstanceTitle", "Required")]
        [InlineData("{\"InstanceTitle\":\"    \",\"AllowRegisterToPortal\":true}", "InstanceTitle", "Required")]
        [InlineData("{\"InstanceTitle\":\"ab\",\"AllowRegisterToPortal\":true}", "InstanceTitle", "Must be 3 to 16 characters")]
        [InlineData("{\"InstanceTitle\":\"12345678901234567\",\"AllowRegisterToPortal\":true}", "InstanceTitle", "Must be 3 to 16 characters")]
        [InlineData("{\"InstanceTitle\":\"After\"}", "AllowRegisterToPortal", "Required")]
        [InlineData("{\"InstanceTitle\":\"After\",\"AllowRegisterToPortal\":null}", "AllowRegisterToPortal", "Required")]
        // The installation key cannot be cleared.
        [InlineData("{\"InstanceTitle\":\"After\",\"AllowRegisterToPortal\":true,\"InstallationKey\":\"\"}", "InstallationKey", "Must be 30 to 100 characters")]
        [InlineData("{\"InstanceTitle\":\"After\",\"AllowRegisterToPortal\":true,\"InstallationKey\":\"12345678901234567890123456789\"}", "InstallationKey", "Must be 30 to 100 characters")]
        [InlineData("{\"InstanceTitle\":\"After\",\"AllowRegisterToPortal\":true,\"InstallationKey\":\"12345678901234567890 1234567890\"}", "InstallationKey", "Must not contain whitespace")]
        public async Task Update_Invalid_Should_Return_400_With_The_Property_And_Change_Nothing(string body, string property, string message)
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(Url, Json(body));

            await AssertValidationAsync(response, property, message);
            await AssertUnchangedAsync();
        }

        [Fact]
        public async Task Update_Installation_Key_Too_Long_Should_Return_400()
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(Url, Valid(x => x.InstallationKey = new string('k', 101)).ToJsonContent());

            await AssertValidationAsync(response, "InstallationKey", "Must be 30 to 100 characters");
            await AssertUnchangedAsync();
        }

        [Theory]
        [InlineData(3, 30)]
        [InlineData(16, 100)]
        public async Task Update_Shortest_And_Longest_Values_Should_Be_Accepted(int titleLength, int keyLength)
        {
            var client = await _portal.CreateAdminClientAsync();
            var title = new string('t', titleLength);
            var key = new string('k', keyLength);

            var response = await client.PutAsync(Url, Valid(x =>
            {
                x.InstanceTitle = title;
                x.InstallationKey = key;
            }).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var stored = await ReadStoredAsync();
            Assert.Equal(title, stored.InstanceTitle);
            Assert.Equal(key, stored.InstallationKey);
        }

        [Theory]
        [InlineData("\"abc\"")]
        [InlineData("25.5")]
        public async Task Update_Port_That_Is_Not_A_Whole_Number_Should_Return_400_And_Change_Nothing(string port)
        {
            var client = await _portal.CreateAdminClientAsync();

            var response = await client.PutAsync(Url, Json($"{{\"InstanceTitle\":\"After\",\"AllowRegisterToPortal\":true,\"MailServerPort\":{port}}}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            // Named like every other property error, not by its JSON path ($.MailServerPort).
            // The only entry, and without the parser's own text, which names .NET types.
            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal("MailServerPort", detail.Property);
            Assert.Equal("Invalid value.", detail.Message);

            await AssertUnchangedAsync();
        }

        [Fact]
        public async Task Update_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            var client = await _portal.CreateAdminClientAsync();
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await client.PutAsync(Url, Valid(x => x.InstanceTitle = "After").ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);

            await AssertUnchangedAsync();
        }

        [Fact]
        public async Task Update_Anonymous_Should_Return_401_And_Change_Nothing()
        {
            var response = await _portal.CreateAnonymousClient().PutAsync(Url, Valid(x => x.InstanceTitle = "After").ToJsonContent());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            await AssertUnchangedAsync();
        }

        [Fact]
        public async Task Update_User_Without_Admin_Role_Should_Return_403_And_Change_Nothing()
        {
            var client = await _portal.CreateUserClientAsync();

            var response = await client.PutAsync(Url, Valid(x => x.InstanceTitle = "After").ToJsonContent());

            await AssertNotAdminAsync(response);
            await AssertUnchangedAsync();
        }

        // ---------- Helpers ----------

        // A request that writes back what InitializeAsync stored; the secrets are left out, so they are kept.
        private static InstanceSettingsRequest Valid(Action<InstanceSettingsRequest>? change = null)
        {
            var request = new InstanceSettingsRequest
            {
                InstanceTitle = "Before",
                AllowRegisterToPortal = true,
                MailServer = "smtp.before.test",
                MailServerPort = 25,
                MailUserName = "before-user",
                MailFromAddress = "before@portal.test",
                MailFromDisplayName = "Before"
            };

            change?.Invoke(request);

            return request;
        }

        private static HttpContent Json(string body)
        {
            return new StringContent(body, Encoding.UTF8, "application/json");
        }

        private Task<GlobalSettings> ReadStoredAsync()
        {
            return _portal.WithDbContextAsync(db => db.GlobalSettings.AsNoTracking().SingleAsync());
        }

        private Task<int> WriteStoredAsync(GlobalSettings values)
        {
            return _portal.WithDbContextAsync(async db =>
            {
                var stored = await db.GlobalSettings.SingleAsync();

                stored.InstanceTitle = values.InstanceTitle;
                stored.InstallationKey = values.InstallationKey;
                stored.AllowRegisterToPortal = values.AllowRegisterToPortal;
                stored.MailServer = values.MailServer;
                stored.MailServerPort = values.MailServerPort;
                stored.MailUserName = values.MailUserName;
                stored.MailPassword = values.MailPassword;
                stored.MailFromAddress = values.MailFromAddress;
                stored.MailFromDisplayName = values.MailFromDisplayName;

                return await db.SaveChangesAsync();
            });
        }

        private async Task AssertUnchangedAsync()
        {
            var stored = await ReadStoredAsync();

            Assert.Equal("Before", stored.InstanceTitle);
            Assert.True(stored.AllowRegisterToPortal);
            Assert.Equal(StoredKey, stored.InstallationKey);
            Assert.Equal("smtp.before.test", stored.MailServer);
            Assert.Equal(25, stored.MailServerPort);
            Assert.Equal(StoredMailPassword, stored.MailPassword);
        }

        private static async Task AssertValidationAsync(HttpResponseMessage response, string property, string message)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var detail = Assert.Single(error.Errors ?? new List<ErrorDetail>());
            Assert.Equal(property, detail.Property);
            Assert.Equal(message, detail.Message);
        }

        // A 403 for the missing role, not for the CSRF header.
        private static async Task AssertNotAdminAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.DoesNotContain(PortalCsrfFilter.HeaderName, error.Message);
        }
    }
}
