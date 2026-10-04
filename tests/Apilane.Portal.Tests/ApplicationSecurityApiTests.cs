using Apilane.Common.Enums;
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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// GET /applications/{appToken}/security, PUT .../security/settings and PUT .../security/rules.
    /// The application is the one of <see cref="EntityScene"/> plus the custom endpoints Beta and Alpha.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class ApplicationSecurityApiTests
    {
        private const string UserRolesJson = "[{\"Roles\":\"Editors\"},{\"Roles\":\"Viewers,Admins\"},{\"Roles\":null},{\"Roles\":\" \"},{\"Roles\":\"Editors\"}]";

        // A stored rule list as the Portal writes it, plus rules older versions and other writers
        // left behind. The comments say what the API makes of each.
        private const string StoredWithLegacyRules = "["
            // Read as is.
            + "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Code,Amount\",\"RateLimit\":null},"
            // Upper-case action, unknown record and rate window, names that are not offered for put.
            + "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"Editors\",\"Action\":\"PUT\",\"Record\":5,\"Properties\":\"Amount,Nope,ID, Code,Amount\",\"RateLimit\":{\"MaxRequests\":3,\"TimeWindowType\":9}},"
            // Only the four values a rule cannot do without.
            + "{\"Name\":\"Customers\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\"},"
            + "{\"Name\":\"Alpha\",\"TypeID\":1,\"RoleID\":\"Gone role\",\"Action\":\"get\",\"Record\":1,\"Properties\":null,\"RateLimit\":{\"MaxRequests\":10,\"TimeWindowType\":2,\"TimeWindow\":\"00:01:00\"}},"
            + "{\"Name\":\"Schema\",\"TypeID\":2,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null},"
            // Left out: a second rule for the same cell, an entity that is gone (its role still gets
            // a row), an unknown type, an unknown action, Schema with post, an
            // empty role, actions the item does not offer (Users post, a custom endpoint put),
            // something that is not a rule and a rule that cannot be read.
            + "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"GET\",\"Record\":1,\"Properties\":\"Code\",\"RateLimit\":null},"
            + "{\"Name\":\"Deleted\",\"TypeID\":0,\"RoleID\":\"Ghost\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"\",\"RateLimit\":null},"
            + "{\"Name\":\"Orders\",\"TypeID\":99,\"RoleID\":\"Editors\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"\",\"RateLimit\":null},"
            + "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"Editors\",\"Action\":\"patch\",\"Record\":0,\"Properties\":\"\",\"RateLimit\":null},"
            + "{\"Name\":\"Schema\",\"TypeID\":2,\"RoleID\":\"ANONYMOUS\",\"Action\":\"post\",\"Record\":0,\"Properties\":null,\"RateLimit\":null},"
            + "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"\",\"Action\":\"delete\",\"Record\":0,\"Properties\":\"\",\"RateLimit\":null},"
            + "{\"Name\":\"Users\",\"TypeID\":0,\"RoleID\":\"Editors\",\"Action\":\"post\",\"Record\":0,\"Properties\":\"Email\",\"RateLimit\":null},"
            + "{\"Name\":\"Alpha\",\"TypeID\":1,\"RoleID\":\"Editors\",\"Action\":\"put\",\"Record\":0,\"Properties\":null,\"RateLimit\":null},"
            + "42,"
            + "{\"Name\":\"Orders\",\"TypeID\":\"zero\",\"RoleID\":\"Unread\",\"Action\":\"get\"}"
            + "]";

        // What ValidRules() is stored as.
        private const string ValidRulesAsStored =
            "[{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":\"Code,Amount\",\"RateLimit\":null},"
            + "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"Editors\",\"Action\":\"put\",\"Record\":1,\"Properties\":\"Amount\",\"RateLimit\":{\"MaxRequests\":30,\"TimeWindowType\":2,\"TimeWindow\":\"00:01:00\"}},"
            + "{\"Name\":\"Orders\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"delete\",\"Record\":0,\"Properties\":\"\",\"RateLimit\":null},"
            + "{\"Name\":\"Files\",\"TypeID\":0,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"post\",\"Record\":0,\"Properties\":\"\",\"RateLimit\":{\"MaxRequests\":5,\"TimeWindowType\":1,\"TimeWindow\":\"00:00:01\"}},"
            + "{\"Name\":\"Alpha\",\"TypeID\":1,\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":{\"MaxRequests\":100,\"TimeWindowType\":3,\"TimeWindow\":\"01:00:00\"}},"
            + "{\"Name\":\"Schema\",\"TypeID\":2,\"RoleID\":\"AUTHENTICATED\",\"Action\":\"get\",\"Record\":0,\"Properties\":null,\"RateLimit\":null},"
            + "{\"Name\":\"Users\",\"TypeID\":0,\"RoleID\":\"Editors\",\"Action\":\"get\",\"Record\":1,\"Properties\":\"Nickname,Email\",\"RateLimit\":null}]";

        private static readonly string[] _legacyRulesAsRead =
        {
            "Entity Orders ANONYMOUS get All [Code,Amount] -",
            "Entity Orders Editors put All [Amount] -",
            "Entity Customers AUTHENTICATED get All [] -",
            "CustomEndpoint Alpha Gone role get Owned [] 10/Per_Minute",
            "Schema Schema AUTHENTICATED get All [] -"
        };

        private readonly PortalFactory _portal;

        public ApplicationSecurityApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Read ----------

        [Fact]
        public async Task Get_Should_Return_Settings_Links_Roles_Items_And_The_Rules_That_Apply()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x =>
            {
                x.Security = StoredWithLegacyRules;
                x.ClientIPsLogic = 1;
                x.ClientIPsValue = " 10.0.0.1, ,10.0.0.2";
                x.ForceSingleLogin = true;
                x.AllowUserRegister = true;
            });

            // Upper-case letters in a path segment, so the lower-casing of the links shows (Uri lower-cases the host anyway).
            var serverUrl = $"{scene.ServerUrl}/Apilane";
            await _portal.WithDbContextAsync(async db =>
            {
                (await db.Servers.SingleAsync(x => x.ServerUrl == scene.ServerUrl)).ServerUrl = serverUrl;
                return await db.SaveChangesAsync();
            });
            _portal.ApiServer.Respond($"/Apilane{FakeApiServer.StatsDistinctPath}", HttpStatusCode.OK, UserRolesJson);

            var response = await scene.Owner.GetAsync(Url(scene));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var security = await response.ReadJsonAsync<SecurityResponse>();

            Assert.Equal(60, security.Settings.AuthTokenExpireMinutes);
            Assert.True(security.Settings.ForceSingleLogin);
            Assert.False(security.Settings.AllowLoginUnconfirmedEmail);
            Assert.True(security.Settings.AllowUserRegister);
            Assert.Equal(1024, security.Settings.MaxAllowedFileSizeInKB);
            Assert.Equal("Allow", security.Settings.ClientIPsLogic);
            Assert.Equal(new[] { "10.0.0.1", "10.0.0.2" }, security.Settings.ClientIPs);

            // Built from the lower-cased server URL; the token keeps its case.
            var lowerServerUrl = serverUrl.ToLowerInvariant();
            Assert.NotEqual(serverUrl, lowerServerUrl);
            Assert.Equal($"{lowerServerUrl}/App/{scene.Token}/Account/Manage/ForgotPassword", security.ForgotPasswordLinks.PageUrl);
            Assert.Equal($"{lowerServerUrl}/api/Email/ForgotPassword?AppToken={scene.Token}&Email={{Email}}", security.ForgotPasswordLinks.ApiUrl);

            Assert.True(security.RolesAvailable);
            Assert.Equal(
                new[]
                {
                    "ANONYMOUS | Anonymous users | Anonymous | False",
                    "AUTHENTICATED | Authenticated users | Authenticated | False",
                    "Editors | Editors | Role | False",
                    "Gone role | Gone role | Role | True",
                    "Ghost | Ghost | Role | True",
                    "Viewers | Viewers | Role | False",
                    "Admins | Admins | Role | False"
                },
                security.Roles.Select(x => $"{x.RoleID} | {x.DisplayName} | {x.Kind} | {x.Orphaned}"));

            Assert.Equal(
                new[]
                {
                    "Schema Schema post:False put:False delete:False owner:False get:[] postput:[]",
                    "Entity Files post:True put:False delete:True owner:False get:[Name] postput:[]",
                    "Entity Users post:False put:True delete:True owner:False get:[Email,Nickname] postput:[Email,Nickname]",
                    "Entity Customers post:True put:True delete:True owner:False get:[Name] postput:[Name]",
                    "Entity Invoices post:True put:True delete:True owner:False get:[] postput:[]",
                    "Entity Orders post:True put:True delete:True owner:True get:[Owner,Created,Customer_ID,Agent_ID,Amount,Code,Secret,Paid] postput:[Customer_ID,Agent_ID,Amount,Code,Secret,Paid]",
                    "CustomEndpoint Alpha post:False put:False delete:False owner:False get:[] postput:[]",
                    "CustomEndpoint Beta post:False put:False delete:False owner:False get:[] postput:[]"
                },
                security.Items.Select(Describe));

            Assert.Equal(new[] { "Files", "Users" }, security.Items.Where(x => x.IsSystem).Select(x => x.Name));
            Assert.All(security.Items, x => Assert.Null(x.DifferentiationProperty));

            Assert.Equal(_legacyRulesAsRead, security.Rules.Select(Describe));

            // The roles are asked for as the caller, on the server URL as stored.
            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"{serverUrl}/api/Stats/Distinct?Entity=Users&Property=Roles", request.Url);
            await scene.AssertPortalHeadersAsync(request, scene.OwnerEmail);

            // A read changes nothing.
            Assert.Equal(StoredWithLegacyRules, (await LoadAsync(scene.AppId)).Security);
        }

        [Fact]
        public async Task Get_Without_Stored_Rules_Should_Return_Only_The_Built_In_And_User_Roles()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer("[]");

            var security = await (await scene.Owner.GetAsync(Url(scene))).ReadJsonAsync<SecurityResponse>();

            Assert.Empty(security.Rules);
            Assert.True(security.RolesAvailable);
            Assert.Equal(new[] { "ANONYMOUS", "AUTHENTICATED" }, security.Roles.Select(x => x.RoleID));
            Assert.Equal("Block", security.Settings.ClientIPsLogic);
            Assert.Empty(security.Settings.ClientIPs);
        }

        [Theory]
        [InlineData("this is not json")]
        [InlineData("null")]
        [InlineData("{}")]
        public async Task Get_And_Replace_With_A_Stored_Value_That_Is_Not_A_Rule_List_Should_Work(string stored)
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = stored);
            ScriptApiServer("[]");

            var get = await scene.Owner.GetAsync(Url(scene));
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);

            var security = await get.ReadJsonAsync<SecurityResponse>();
            Assert.Empty(security.Rules);
            Assert.Equal(new[] { "ANONYMOUS", "AUTHENTICATED" }, security.Roles.Select(x => x.RoleID));

            var put = await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = ValidRules() }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.StartsWith("[{\"Name\":\"Orders\"", (await LoadAsync(scene.AppId)).Security);
        }

        [Fact]
        public async Task Get_With_A_Stored_Client_IPs_Logic_That_Is_Not_Allow_Should_Read_Block()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.ClientIPsLogic = 2);
            ScriptApiServer();

            var security = await (await scene.Owner.GetAsync(Url(scene))).ReadJsonAsync<SecurityResponse>();

            Assert.Equal("Block", security.Settings.ClientIPsLogic);
        }

        [Fact]
        public async Task Get_Should_Name_The_Differentiation_Property_Of_The_Entities_That_Have_One()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.DifferentiationEntity = "Customers");
            await _portal.WithDbContextAsync(async db =>
            {
                (await db.Entities.SingleAsync(x => x.AppID == scene.AppId && x.Name == "Orders")).HasDifferentiationProperty = true;
                return await db.SaveChangesAsync();
            });
            ScriptApiServer();

            var security = await (await scene.Owner.GetAsync(Url(scene))).ReadJsonAsync<SecurityResponse>();

            Assert.Equal(
                new[] { "Schema -", "Files -", "Users -", "Customers -", "Invoices -", "Orders Customers_ID", "Alpha -", "Beta -" },
                security.Items.Select(x => $"{x.Name} {x.DifferentiationProperty ?? "-"}"));
        }

        [Fact]
        public async Task Get_Should_Skip_Role_Values_That_Are_Not_Text_And_Keep_The_Rest_As_Written()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer("[{\"Roles\":5},{\"Roles\":\"ANONYMOUS,Viewers\"},{\"Roles\":\"Editors, Admins\"}]");

            var security = await (await scene.Owner.GetAsync(Url(scene))).ReadJsonAsync<SecurityResponse>();

            // ANONYMOUS is not listed twice; ' Admins' is not trimmed.
            Assert.True(security.RolesAvailable);
            Assert.Equal(new[] { "ANONYMOUS", "AUTHENTICATED", "Viewers", "Editors", " Admins" }, security.Roles.Select(x => x.RoleID));
        }

        [Theory]
        [InlineData("[1,2]")]
        [InlineData("not json")]
        [InlineData("{\"Roles\":\"Editors\"}")]
        [InlineData("[null]")]
        public async Task Get_With_An_Unreadable_Roles_Answer_Should_Still_Answer_Without_User_Roles(string body)
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);
            _portal.ApiServer.Respond(FakeApiServer.StatsDistinctPath, HttpStatusCode.OK, body);

            await AssertDegradedAsync(scene);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        public async Task Get_When_The_Api_Server_Fails_Should_Still_Answer_Without_User_Roles(HttpStatusCode status)
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);
            _portal.ApiServer.Respond(FakeApiServer.StatsDistinctPath, status, EntityScene.ApiError("Down"));

            await AssertDegradedAsync(scene);
        }

        [Fact]
        public async Task Get_When_The_Api_Server_Cannot_Be_Reached_Should_Still_Answer_Without_User_Roles()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);
            _portal.ApiServer.Unreachable(FakeApiServer.StatsDistinctPath);

            await AssertDegradedAsync(scene);
        }

        // ---------- Settings ----------

        [Fact]
        public async Task Update_Settings_Should_Save_Every_Value_And_Reset_The_Cache_Once()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);
            ScriptApiServer();

            var body = SettingsBody();
            body["ClientIPs"] = new[] { " 10.0.0.1 ", "", "  ", null, "192.168.1.255" };

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(Services.ApiServerCacheReset.WarningHeaderName));

            var settings = await response.ReadJsonAsync<SecuritySettingsResponse>();
            AssertSettingsBody(settings);
            Assert.Equal(new[] { "10.0.0.1", "192.168.1.255" }, settings.ClientIPs);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal(30, stored.AuthTokenExpireMinutes);
            Assert.True(stored.ForceSingleLogin);
            Assert.True(stored.AllowLoginUnconfirmedEmail);
            Assert.True(stored.AllowUserRegister);
            Assert.Equal(25600, stored.MaxAllowedFileSizeInKB);
            Assert.Equal(1, stored.ClientIPsLogic);
            Assert.Equal("10.0.0.1,192.168.1.255", stored.ClientIPsValue);

            // The rules are not touched, not even the ones the API leaves out when it reads them.
            Assert.Equal(StoredWithLegacyRules, stored.Security);

            var read = await (await scene.Owner.GetAsync(Url(scene))).ReadJsonAsync<SecurityResponse>();
            AssertSettingsBody(read.Settings);
            Assert.Equal(new[] { "10.0.0.1", "192.168.1.255" }, read.Settings.ClientIPs);

            var request = Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            Assert.Equal($"{scene.ServerUrl}/api/Application/ClearCache", request.Url);
            await scene.AssertPortalHeadersAsync(request, scene.OwnerEmail);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Update_Settings_Without_Addresses_Should_Store_An_Empty_List(bool sendNull)
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.ClientIPsValue = "10.0.0.1");
            ScriptApiServer();

            var body = SettingsBody();
            body["ClientIPsLogic"] = "Block";

            if (sendNull)
            {
                body["ClientIPs"] = null;
            }
            else
            {
                body.Remove("ClientIPs");
            }

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var settings = await response.ReadJsonAsync<SecuritySettingsResponse>();
            Assert.Empty(settings.ClientIPs);
            Assert.Equal("Block", settings.ClientIPsLogic);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal(string.Empty, stored.ClientIPsValue);
            Assert.Equal(0, stored.ClientIPsLogic);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(int.MaxValue, 25600)]
        public async Task Update_Settings_With_The_Edge_Values_Should_Be_Saved(int minutes, int kilobytes)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = SettingsBody();
            body["AuthTokenExpireMinutes"] = minutes;
            body["MaxAllowedFileSizeInKB"] = kilobytes;

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal(minutes, stored.AuthTokenExpireMinutes);
            Assert.Equal(kilobytes, stored.MaxAllowedFileSizeInKB);
        }

        [Fact]
        public async Task Update_Settings_With_Bad_Addresses_Should_Name_Each_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();

            var body = SettingsBody();
            // The last four would pass a lenient parser, but the API server compares addresses as text, so they could never match.
            body["ClientIPs"] = new[] { "10.0.0.1", "300.1.1.1", "", "abc", "1.2.3", "::1", "10.0.0.1,10.0.0.2", " 192.168.0.1 ", "010.0.0.1", "+10.0.0.1", "10. 0.0.1", "10.0.0.-0" };

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), body.ToJsonContent());

            await EntityScene.AssertValidationAsync(
                response,
                "ClientIPs[1]: '300.1.1.1' is not a valid IPv4 address",
                "ClientIPs[3]: 'abc' is not a valid IPv4 address",
                "ClientIPs[4]: '1.2.3' is not a valid IPv4 address",
                "ClientIPs[5]: '::1' is not a valid IPv4 address",
                "ClientIPs[6]: '10.0.0.1,10.0.0.2' is not a valid IPv4 address",
                "ClientIPs[8]: '010.0.0.1' is not a valid IPv4 address",
                "ClientIPs[9]: '+10.0.0.1' is not a valid IPv4 address",
                "ClientIPs[10]: '10. 0.0.1' is not a valid IPv4 address",
                "ClientIPs[11]: '10.0.0.-0' is not a valid IPv4 address");

            await AssertUnchangedAsync(scene);
        }

        [Theory]
        [InlineData("AuthTokenExpireMinutes", 0, "AuthTokenExpireMinutes: Must be 1 or more")]
        [InlineData("AuthTokenExpireMinutes", -5, "AuthTokenExpireMinutes: Must be 1 or more")]
        [InlineData("MaxAllowedFileSizeInKB", 0, "MaxAllowedFileSizeInKB: Must be between 1 and 25600 (1 KB to 25 MB)")]
        [InlineData("MaxAllowedFileSizeInKB", 25601, "MaxAllowedFileSizeInKB: Must be between 1 and 25600 (1 KB to 25 MB)")]
        [InlineData("ClientIPsLogic", "Deny", "ClientIPsLogic: Must be Block or Allow")]
        [InlineData("ClientIPsLogic", "allow", "ClientIPsLogic: Must be Block or Allow")]
        [InlineData("ClientIPsLogic", "1", "ClientIPsLogic: Must be Block or Allow")]
        [InlineData("ClientIPsLogic", null, "ClientIPsLogic: Required")]
        public async Task Update_Settings_With_A_Bad_Value_Should_Return_400_And_Change_Nothing(string property, object? value, string expected)
        {
            var scene = await CreateSceneAsync();

            var body = SettingsBody();
            body[property] = value;

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), body.ToJsonContent());

            await EntityScene.AssertValidationAsync(response, expected);
            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Update_Settings_With_A_Missing_Number_Should_Return_400()
        {
            var scene = await CreateSceneAsync();

            var body = SettingsBody();
            body.Remove("AuthTokenExpireMinutes");
            body.Remove("MaxAllowedFileSizeInKB");

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), body.ToJsonContent());

            await EntityScene.AssertValidationAsync(
                response,
                "AuthTokenExpireMinutes: Required",
                "MaxAllowedFileSizeInKB: Required");
            await AssertUnchangedAsync(scene);
        }

        [Theory]
        [InlineData("ForceSingleLogin")]
        [InlineData("AllowLoginUnconfirmedEmail")]
        [InlineData("AllowUserRegister")]
        public async Task Update_Settings_With_A_Missing_Switch_Should_Return_400_And_Change_Nothing(string property)
        {
            var scene = await CreateSceneAsync();

            // Left out must not mean false: false for AllowUserRegister turns off sign-up.
            var body = SettingsBody();
            body.Remove(property);

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), body.ToJsonContent());

            await EntityScene.AssertValidationAsync(response, $"{property}: Required");
            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Update_Settings_Should_Audit_The_Changed_Values()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.ClientIPsValue = string.Empty);
            var before = await LoadAsync(scene.AppId);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), SettingsBody().ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var row = Assert.Single(await scene.AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal($"Application | {before.Name} | Modified | {scene.AppId}", Shape(row));
            Assert.Equal(
                new[]
                {
                    "AllowLoginUnconfirmedEmail: False -> True",
                    "AllowUserRegister: False -> True",
                    "AuthTokenExpireMinutes: 60 -> 30",
                    "ClientIPsLogic: 0 -> 1",
                    "ClientIPsValue:  -> 10.0.0.1",
                    "ForceSingleLogin: False -> True",
                    "MaxAllowedFileSizeInKB: 1024 -> 25600"
                },
                Changes(row));
        }

        // ---------- Rules ----------

        [Fact]
        public async Task Replace_Rules_Should_Store_Them_And_Reset_The_Cache_Once()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = ValidRules() }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(Services.ApiServerCacheReset.WarningHeaderName));

            var expected = new[]
            {
                "Entity Orders ANONYMOUS get All [Code,Amount] -",
                "Entity Orders Editors put Owned [Amount] 30/Per_Minute",
                "Entity Orders AUTHENTICATED delete All [] -",
                "Entity Files AUTHENTICATED post All [] 5/Per_Second",
                "CustomEndpoint Alpha ANONYMOUS get All [] 100/Per_Hour",
                "Schema Schema AUTHENTICATED get All [] -",
                "Entity Users Editors get Owned [Nickname,Email] -"
            };

            Assert.Equal(expected, (await response.ReadJsonAsync<SecurityRulesResponse>()).Rules.Select(Describe));

            // Full replacement: nothing of the old list is left.
            var read = await (await scene.Owner.GetAsync(Url(scene))).ReadJsonAsync<SecurityResponse>();
            Assert.Equal(expected, read.Rules.Select(Describe));

            Assert.Equal(ValidRulesAsStored, (await LoadAsync(scene.AppId)).Security);

            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Fact]
        public async Task Replace_Rules_Should_Audit_The_Stored_Text_Before_And_After()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = ValidRules() }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var stored = await LoadAsync(scene.AppId);
            var row = Assert.Single(await scene.AuditRowsAsync(scene.OwnerEmail));

            Assert.Equal($"Application | {stored.Name} | Modified | {scene.AppId}", Shape(row));

            // Only the Security column changes, from nothing to the stored text.
            Assert.Equal(new[] { $"Security:  -> {ValidRulesAsStored}" }, Changes(row));
        }

        [Fact]
        public async Task Replace_Rules_Should_Store_The_Action_In_Lower_Case()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var rules = new[] { Rule("Entity", "Customers", "ANONYMOUS", "GET", "All", new[] { "Name" }), Rule("Schema", "Schema", "ANONYMOUS", "Get", "All") };

            var response = await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = rules }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[] { "Entity Customers ANONYMOUS get All [Name] -", "Schema Schema ANONYMOUS get All [] -" },
                (await response.ReadJsonAsync<SecurityRulesResponse>()).Rules.Select(Describe));

            var stored = (await LoadAsync(scene.AppId)).Security ?? string.Empty;
            Assert.Contains("\"Action\":\"get\"", stored);
            Assert.DoesNotContain("\"Action\":\"GET\"", stored);
            Assert.DoesNotContain("\"Action\":\"Get\"", stored);
        }

        [Fact]
        public async Task Replace_Rules_With_An_Empty_List_Should_Remove_Them_All()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = Array.Empty<object>() }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty((await response.ReadJsonAsync<SecurityRulesResponse>()).Rules);
            Assert.Equal("[]", (await LoadAsync(scene.AppId)).Security);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Fact]
        public async Task Replace_Rules_With_The_Rules_Read_Should_Keep_Them()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);
            ScriptApiServer();

            // What a GET returns can always be sent back: the legacy rules come back as they were read.
            var read = await (await scene.Owner.GetAsync(Url(scene))).ReadJsonAsync<SecurityResponse>();

            var response = await scene.Owner.PutAsync(RulesUrl(scene), new { read.Rules }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(_legacyRulesAsRead, (await response.ReadJsonAsync<SecurityRulesResponse>()).Rules.Select(Describe));
        }

        [Theory]
        [InlineData("{\"Type\":\"Table\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].Type: Must be Entity, CustomEndpoint or Schema")]
        [InlineData("{\"Type\":\"entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].Type: Must be Entity, CustomEndpoint or Schema")]
        [InlineData("{\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].Type: Must be Entity, CustomEndpoint or Schema")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].Name: The application has no entity 'orders'")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Alpha\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].Name: The application has no entity 'Alpha'")]
        [InlineData("{\"Type\":\"CustomEndpoint\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].Name: The application has no custom endpoint 'Orders'")]
        [InlineData("{\"Type\":\"Schema\",\"Name\":\"schema\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].Name: Must be Schema")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\" \",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].Name: Required")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].RoleID: Required")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].RoleID: Required")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"patch\",\"Record\":\"All\"}", "Rules[3].Action: Must be get, post, put or delete")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Record\":\"All\"}", "Rules[3].Action: Must be get, post, put or delete")]
        [InlineData("{\"Type\":\"Schema\",\"Name\":\"Schema\",\"RoleID\":\"X\",\"Action\":\"post\",\"Record\":\"All\"}", "Rules[3].Action: Schema rules allow only get")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"Mine\"}", "Rules[3].Record: Must be All or Owned")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"owned\"}", "Rules[3].Record: Must be All or Owned")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\"}", "Rules[3].Record: Must be All or Owned")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":[\"Code\",\"Nope\"]}", "Rules[3].Properties: 'Nope' is not a property a get rule of Orders can list")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":[\"ID\"]}", "Rules[3].Properties: 'ID' is not a property a get rule of Orders can list")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":[\" Code\"]}", "Rules[3].Properties: ' Code' is not a property a get rule of Orders can list")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"put\",\"Record\":\"All\",\"Properties\":[\"Owner\"]}", "Rules[3].Properties: 'Owner' is not a property a put rule of Orders can list")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"delete\",\"Record\":\"All\",\"Properties\":[\"Code\"]}", "Rules[3].Properties: 'Code' is not a property a delete rule of Orders can list")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Users\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":[\"Code\"]}", "Rules[3].Properties: 'Code' is not a property a get rule of Users can list")]
        [InlineData("{\"Type\":\"CustomEndpoint\",\"Name\":\"Alpha\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":[\"Code\"]}", "Rules[3].Properties: Only entity rules have properties")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].RateLimit.TimeWindow: Must be Per_Second, Per_Minute or Per_Hour")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"RateLimit\":{\"MaxRequests\":5}}", "Rules[3].RateLimit.TimeWindow: Must be Per_Second, Per_Minute or Per_Hour")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"RateLimit\":{\"MaxRequests\":0,\"TimeWindow\":\"Per_Second\"}}", "Rules[3].RateLimit.MaxRequests: Must be 1 or more")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"RateLimit\":{\"TimeWindow\":\"Per_Hour\"}}", "Rules[3].RateLimit.MaxRequests: Must be 1 or more")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"ANONYMOUS\",\"Action\":\"GET\",\"Record\":\"Owned\"}", "Rules[3]: Same type, name, role and action as Rules[0]")]
        [InlineData("null", "Rules[3]: Required")]
        // Actions the item does not offer.
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Users\",\"RoleID\":\"X\",\"Action\":\"post\",\"Record\":\"All\"}", "Rules[3].Action: Users does not allow post")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Files\",\"RoleID\":\"X\",\"Action\":\"put\",\"Record\":\"All\"}", "Rules[3].Action: Files does not allow put")]
        [InlineData("{\"Type\":\"CustomEndpoint\",\"Name\":\"Alpha\",\"RoleID\":\"X\",\"Action\":\"post\",\"Record\":\"All\"}", "Rules[3].Action: Alpha does not allow post")]
        [InlineData("{\"Type\":\"CustomEndpoint\",\"Name\":\"Alpha\",\"RoleID\":\"X\",\"Action\":\"DELETE\",\"Record\":\"All\"}", "Rules[3].Action: Alpha does not allow delete")]
        // Files offers no properties for its post, although it has one to read.
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Files\",\"RoleID\":\"X\",\"Action\":\"post\",\"Record\":\"All\",\"Properties\":[\"Name\"]}", "Rules[3].Properties: 'Name' is not a property a post rule of Files can list")]
        [InlineData("{\"Type\":\"Schema\",\"Name\":\"Schema\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":[\"Code\"]}", "Rules[3].Properties: Only entity rules have properties")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\" \",\"Action\":\"get\",\"Record\":\"All\"}", "Rules[3].RoleID: Required")]
        // The values are checked in the order the contract lists them: each row fixes one more value than the one before.
        [InlineData("{\"Type\":\"Table\",\"Name\":\" \",\"RoleID\":\"\",\"Action\":\"patch\",\"Record\":\"Mine\",\"Properties\":[\"Nope\"],\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].Type: Must be Entity, CustomEndpoint or Schema")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Nope\",\"RoleID\":\"\",\"Action\":\"patch\",\"Record\":\"Mine\",\"Properties\":[\"Nope\"],\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].Name: The application has no entity 'Nope'")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Users\",\"RoleID\":\"\",\"Action\":\"patch\",\"Record\":\"Mine\",\"Properties\":[\"Nope\"],\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].RoleID: Required")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Users\",\"RoleID\":\"X\",\"Action\":\"patch\",\"Record\":\"Mine\",\"Properties\":[\"Nope\"],\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].Action: Must be get, post, put or delete")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Users\",\"RoleID\":\"X\",\"Action\":\"post\",\"Record\":\"Mine\",\"Properties\":[\"Nope\"],\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].Action: Users does not allow post")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Users\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"Mine\",\"Properties\":[\"Nope\"],\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].Record: Must be All or Owned")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Users\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":[\"Nope\"],\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].Properties: 'Nope' is not a property a get rule of Users can list")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Users\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":[\"Email\"],\"RateLimit\":{\"MaxRequests\":5,\"TimeWindow\":\"None\"}}", "Rules[3].RateLimit.TimeWindow: Must be Per_Second, Per_Minute or Per_Hour")]
        // The values are checked before the duplicate: a copy of Rules[0] with a bad Record.
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"ANONYMOUS\",\"Action\":\"get\",\"Record\":\"Mine\"}", "Rules[3].Record: Must be All or Owned")]
        // Values of the wrong JSON type, as in the stored shape.
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":0}", "Rules[3].Record: Invalid value.")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"Properties\":\"Code,Amount\"}", "Rules[3].Properties: Invalid value.")]
        [InlineData("{\"Type\":\"Entity\",\"Name\":\"Orders\",\"RoleID\":\"X\",\"Action\":\"get\",\"Record\":\"All\",\"RateLimit\":{\"MaxRequests\":\"abc\",\"TimeWindow\":\"Per_Hour\"}}", "Rules[3].RateLimit.MaxRequests: Invalid value.")]
        public async Task Replace_Rules_With_A_Bad_Rule_Should_Name_It_And_Change_Nothing(string badRule, string expected)
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);

            var rules = ValidRules().Take(3).Select(x => (object?)x).ToList();
            rules.Add(JsonNode.Parse(badRule));

            var response = await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = rules }.ToJsonContent());

            await EntityScene.AssertValidationAsync(response, expected);
            await AssertRulesUnchangedAsync(scene);
        }

        [Fact]
        public async Task Replace_Rules_Should_Stop_At_The_First_Bad_Rule()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);

            var rules = new[]
            {
                Rule("Entity", "Orders", "ANONYMOUS", "get", "All"),
                Rule("Entity", "Orders", "ANONYMOUS", "fetch", "All"),
                Rule("Table", "Orders", "ANONYMOUS", "get", "All")
            };

            var response = await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = rules }.ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "Rules[1].Action: Must be get, post, put or delete");
            await AssertRulesUnchangedAsync(scene);
        }

        [Fact]
        public async Task Replace_Rules_Without_A_List_Should_Return_400_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            await SetApplicationAsync(scene.AppId, x => x.Security = StoredWithLegacyRules);

            await EntityScene.AssertValidationAsync(await scene.Owner.PutAsync(RulesUrl(scene), new { }.ToJsonContent()), "Rules: Required");
            await EntityScene.AssertValidationAsync(await scene.Owner.PutAsync(RulesUrl(scene), EntityScene.Json("{\"Rules\":null}")), "Rules: Required");

            await AssertRulesUnchangedAsync(scene);
        }

        [Fact]
        public async Task Replace_Rules_When_The_Cache_Reset_Fails_Should_Save_And_Warn()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.InternalServerError, EntityScene.ApiError("Down"));

            var response = await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = ValidRules() }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            EntityScene.AssertWarning(response);
            Assert.StartsWith("[{\"Name\":\"Orders\"", (await LoadAsync(scene.AppId)).Security);
        }

        [Fact]
        public async Task Update_Settings_When_The_Cache_Reset_Fails_Should_Save_And_Warn()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);

            var response = await scene.Owner.PutAsync(SettingsUrl(scene), SettingsBody().ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            EntityScene.AssertWarning(response);
            Assert.Equal(30, (await LoadAsync(scene.AppId)).AuthTokenExpireMinutes);
        }

        // ---------- Access ----------

        [Fact]
        public async Task Collaborator_Should_Read_And_Save_Both()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var read = await scene.Collaborator.GetAsync(Url(scene));
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            var settings = await scene.Collaborator.PutAsync(SettingsUrl(scene), SettingsBody().ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

            var rules = await scene.Collaborator.PutAsync(RulesUrl(scene), new { Rules = ValidRules() }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, rules.StatusCode);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal(30, stored.AuthTokenExpireMinutes);
            Assert.StartsWith("[{\"Name\":\"Orders\"", stored.Security);

            Assert.Equal(2, (await scene.AuditRowsAsync(scene.CollaboratorEmail)).Count(x => x.AppID == scene.AppId));
            Assert.Equal(2, _portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath).Count);
        }

        [Fact]
        public async Task Stranger_Admin_And_Unknown_Token_Should_Get_404_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            var admin = await _portal.CreateAdminClientAsync();

            // The Admin role gives nothing here.
            foreach (var client in new[] { scene.Stranger, admin })
            {
                await EntityScene.AssertNotFoundAsync(await client.GetAsync(Url(scene)), "Application");
                await EntityScene.AssertNotFoundAsync(await client.PutAsync(SettingsUrl(scene), SettingsBody().ToJsonContent()), "Application");
                await EntityScene.AssertNotFoundAsync(await client.PutAsync(RulesUrl(scene), new { Rules = ValidRules() }.ToJsonContent()), "Application");
            }

            var unknown = $"/api/v1/applications/{Guid.NewGuid()}/security";
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(unknown), "Application");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.PutAsync($"{unknown}/settings", SettingsBody().ToJsonContent()), "Application");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.PutAsync($"{unknown}/rules", new { Rules = ValidRules() }.ToJsonContent()), "Application");

            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Anonymous_Should_Get_401_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            var anonymous = _portal.CreateAnonymousClient();

            await EntityScene.AssertErrorAsync(await anonymous.GetAsync(Url(scene)), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            await EntityScene.AssertErrorAsync(await anonymous.PutAsync(SettingsUrl(scene), SettingsBody().ToJsonContent()), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            await EntityScene.AssertErrorAsync(await anonymous.PutAsync(RulesUrl(scene), new { Rules = ValidRules() }.ToJsonContent()), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);

            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Writes_Without_The_Csrf_Header_Should_Get_403_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            foreach (var response in new[]
            {
                await scene.Owner.PutAsync(SettingsUrl(scene), SettingsBody().ToJsonContent()),
                await scene.Owner.PutAsync(RulesUrl(scene), new { Rules = ValidRules() }.ToJsonContent())
            })
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Forbidden, error.Code);
                Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
            }

            await AssertUnchangedAsync(scene);
        }

        // ---------- Helpers ----------

        private static string Url(EntityScene scene) => $"{scene.AppUrl}/security";

        private static string SettingsUrl(EntityScene scene) => $"{Url(scene)}/settings";

        private static string RulesUrl(EntityScene scene) => $"{Url(scene)}/rules";

        private async Task<EntityScene> CreateSceneAsync()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await AddCustomEndpointsAsync(scene.AppId);

            // An editable property on Files, so the rule that its post lists no properties can show.
            await _portal.WithDbContextAsync(async db =>
            {
                var files = await db.Entities.SingleAsync(x => x.AppID == scene.AppId && x.Name == "Files");
                var name = EntityScene.Property("Name", PropertyType.String);
                name.EntityID = files.ID;
                db.EntityProperties.Add(name);

                return await db.SaveChangesAsync();
            });

            return scene;
        }

        private Task<int> AddCustomEndpointsAsync(long appId)
        {
            return _portal.WithDbContextAsync(db =>
            {
                foreach (var name in new[] { "Beta", "Alpha" })
                {
                    db.CustomEndpoints.Add(new DBWS_CustomEndpoint { AppID = appId, Name = name, Query = "SELECT 1", DateModified = DateTime.UtcNow });
                }

                return db.SaveChangesAsync();
            });
        }

        private void ScriptApiServer(string userRoles = UserRolesJson)
        {
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
            _portal.ApiServer.Respond(FakeApiServer.StatsDistinctPath, HttpStatusCode.OK, userRoles);
        }

        private async Task AssertDegradedAsync(EntityScene scene)
        {
            var response = await scene.Owner.GetAsync(Url(scene));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var security = await response.ReadJsonAsync<SecurityResponse>();

            Assert.False(security.RolesAvailable);
            Assert.Equal(
                new[]
                {
                    "ANONYMOUS | Anonymous | False",
                    "AUTHENTICATED | Authenticated | False",
                    "Editors | Role | True",
                    "Gone role | Role | True",
                    "Ghost | Role | True"
                },
                security.Roles.Select(x => $"{x.RoleID} | {x.Kind} | {x.Orphaned}"));

            // Everything else is there.
            Assert.Equal(_legacyRulesAsRead, security.Rules.Select(Describe));
            Assert.Equal(8, security.Items.Count);
            Assert.Equal(60, security.Settings.AuthTokenExpireMinutes);
        }

        private static Dictionary<string, object?> SettingsBody()
        {
            return new Dictionary<string, object?>
            {
                ["AuthTokenExpireMinutes"] = 30,
                ["ForceSingleLogin"] = true,
                ["AllowLoginUnconfirmedEmail"] = true,
                ["AllowUserRegister"] = true,
                ["MaxAllowedFileSizeInKB"] = 25600,
                ["ClientIPsLogic"] = "Allow",
                ["ClientIPs"] = new[] { "10.0.0.1" }
            };
        }

        private static void AssertSettingsBody(SecuritySettingsResponse settings)
        {
            Assert.Equal(30, settings.AuthTokenExpireMinutes);
            Assert.True(settings.ForceSingleLogin);
            Assert.True(settings.AllowLoginUnconfirmedEmail);
            Assert.True(settings.AllowUserRegister);
            Assert.Equal(25600, settings.MaxAllowedFileSizeInKB);
            Assert.Equal("Allow", settings.ClientIPsLogic);
        }

        /// <summary>
        /// One rule of each kind the rule grid can produce.
        /// </summary>
        private static List<Dictionary<string, object?>> ValidRules()
        {
            return new List<Dictionary<string, object?>>
            {
                Rule("Entity", "Orders", "ANONYMOUS", "get", "All", new[] { "Code", "Amount" }),
                Rule("Entity", "Orders", "Editors", "put", "Owned", new[] { "Amount" }, 30, "Per_Minute"),
                Rule("Entity", "Orders", "AUTHENTICATED", "delete", "All"),
                Rule("Entity", "Files", "AUTHENTICATED", "post", "All", null, 5, "Per_Second"),
                Rule("CustomEndpoint", "Alpha", "ANONYMOUS", "get", "All", null, 100, "Per_Hour"),
                Rule("Schema", "Schema", "AUTHENTICATED", "get", "All"),
                Rule("Entity", "Users", "Editors", "get", "Owned", new[] { "Nickname", "Email" })
            };
        }

        private static Dictionary<string, object?> Rule(string type, string name, string roleId, string action, string record, string[]? properties = null, int? maxRequests = null, string? timeWindow = null)
        {
            return new Dictionary<string, object?>
            {
                ["Type"] = type,
                ["Name"] = name,
                ["RoleID"] = roleId,
                ["Action"] = action,
                ["Record"] = record,
                ["Properties"] = properties ?? Array.Empty<string>(),
                ["RateLimit"] = maxRequests is null ? null : new Dictionary<string, object?> { ["MaxRequests"] = maxRequests, ["TimeWindow"] = timeWindow }
            };
        }

        private static string Describe(SecurityItemResponse item)
        {
            return $"{item.Type} {item.Name} post:{item.AllowPost} put:{item.AllowPut} delete:{item.AllowDelete} owner:{item.HasOwner}"
                + $" get:[{string.Join(",", item.PropertiesGet)}] postput:[{string.Join(",", item.PropertiesPostPut)}]";
        }

        private static string Describe(SecurityRuleResponse rule)
        {
            var rateLimit = rule.RateLimit is null ? "-" : $"{rule.RateLimit.MaxRequests}/{rule.RateLimit.TimeWindow}";

            return $"{rule.Type} {rule.Name} {rule.RoleID} {rule.Action} {rule.Record} [{string.Join(",", rule.Properties)}] {rateLimit}";
        }

        private async Task<DBWS_Application> LoadAsync(long appId)
        {
            return await _portal.WithDbContextAsync(db => db.Applications.AsNoTracking().SingleAsync(x => x.ID == appId));
        }

        private Task<int> SetApplicationAsync(long appId, Action<DBWS_Application> change)
        {
            return _portal.WithDbContextAsync(async db =>
            {
                change(await db.Applications.SingleAsync(x => x.ID == appId));

                return await db.SaveChangesAsync();
            });
        }

        /// <summary>
        /// The seeded settings, no rules, no audit row and no request to the API server.
        /// </summary>
        private async Task AssertUnchangedAsync(EntityScene scene)
        {
            var stored = await LoadAsync(scene.AppId);
            Assert.Equal(60, stored.AuthTokenExpireMinutes);
            Assert.Equal(1024, stored.MaxAllowedFileSizeInKB);
            Assert.Equal(0, stored.ClientIPsLogic);
            Assert.Null(stored.ClientIPsValue);
            Assert.False(stored.ForceSingleLogin);
            Assert.False(stored.AllowUserRegister);
            Assert.False(stored.AllowLoginUnconfirmedEmail);
            Assert.Null(stored.Security);

            await AssertNoAuditAndNoCallAsync(scene);
        }

        private async Task AssertRulesUnchangedAsync(EntityScene scene)
        {
            Assert.Equal(StoredWithLegacyRules, (await LoadAsync(scene.AppId)).Security);

            await AssertNoAuditAndNoCallAsync(scene);
        }

        private async Task AssertNoAuditAndNoCallAsync(EntityScene scene)
        {
            var appId = scene.AppId;
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.AppID == appId)));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        private static string Shape(PortalAuditLog row)
        {
            return $"{row.EntityType} | {row.EntityIdentifier} | {row.Action} | {row.AppID}";
        }

        /// <summary>
        /// The changes of an audit row as 'Property: old -> new', by property name. DateModified is
        /// left out: the audit compares it to the second, so whether it shows depends on the clock.
        /// </summary>
        private static List<string> Changes(PortalAuditLog row)
        {
            using var document = JsonDocument.Parse(row.Changes ?? "[]");

            return document.RootElement.EnumerateArray()
                .Where(x => x.GetProperty("Property").GetString() != nameof(DBWS_Application.DateModified))
                .Select(x => $"{x.GetProperty("Property").GetString()}: {x.GetProperty("OldValue").GetString()} -> {x.GetProperty("NewValue").GetString()}")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }
    }
}
