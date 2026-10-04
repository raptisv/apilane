using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class ApplicationProvisioningApiTests
    {
        private const string Url = "/api/v1/applications";
        private const string ImportUrl = "/api/v1/applications/import";
        private const string ApiServerRefusal = "Cannot open database requested by the login.";

        private readonly PortalFactory _portal;

        public ApplicationProvisioningApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Create ----------

        [Fact]
        public async Task Create_SQLite_Should_Return_201_And_Ignore_The_Connection_String()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLLite", "Server=ignored").ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var created = await response.ReadJsonAsync<ApplicationResponse>();
            Assert.Equal($"{Url}/{created.Token}", response.Headers.Location?.ToString());
            Assert.True(Guid.TryParse(created.Token, out _));
            Assert.Equal(name, created.Name);
            Assert.True(created.Online);
            Assert.Equal("SQLLite", created.DatabaseType);
            Assert.False(created.HasConnectionString);
            Assert.Null(created.DifferentiationEntity);
            Assert.Equal(100, created.MaxAllowedFileSizeInKB);
            Assert.True(created.IsOwner);
            Assert.Equal(user.Email, created.OwnerEmail);
            Assert.Equal(0, created.CollaboratorCount);
            Assert.Equal(server.ID, created.Server.ID);
            Assert.Equal(server.Name, created.Server.Name);

            var stored = await LoadAsync(created.Token);
            Assert.Null(stored.ConnectionString);
            Assert.Equal((int)DatabaseType.SQLLite, stored.DatabaseType);

            // The Location is a real address.
            var location = await user.Client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, location.StatusCode);
            Assert.Equal(created.Token, (await location.ReadJsonAsync<ApplicationResponse>()).Token);
        }

        [Theory]
        [InlineData("SQLServer", DatabaseType.SQLServer)]
        [InlineData("MySQL", DatabaseType.MySQL)]
        [InlineData("PostgreSQL", DatabaseType.PostgreSQL)]
        public async Task Create_Other_Database_Should_Store_The_Connection_String(string databaseType, DatabaseType expected)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var connectionString = $"Server=db-{Guid.NewGuid():N};Database=app";
            var response = await user.Client.PostAsync(Url, CreateBody(UniqueName(), server.ID, databaseType, connectionString).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(connectionString, json);
            // Only the flag that says one is stored.
            Assert.DoesNotContain("\"ConnectionString\"", json);
            Assert.DoesNotContain("EncryptionKey", json);

            var created = await response.ReadJsonAsync<ApplicationResponse>();
            Assert.Equal(databaseType, created.DatabaseType);
            Assert.True(created.HasConnectionString);

            var stored = await LoadAsync(created.Token);
            Assert.Equal((int)expected, stored.DatabaseType);
            Assert.Equal(connectionString, stored.ConnectionString);
        }

        [Theory]
        [InlineData("SQLServer", null)]
        [InlineData("MySQL", "")]
        [InlineData("PostgreSQL", "   ")]
        public async Task Create_Other_Database_Without_A_Connection_String_Should_Return_400(string databaseType, string? connectionString)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            var name = UniqueName();

            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, databaseType, connectionString).ToJsonContent());

            await AssertValidationAsync(response, "ConnectionString", "Required");
            await AssertNothingCreatedAsync(name);
        }

        [Theory]
        [InlineData("Name", null, "Required")]
        [InlineData("Name", "", "Required")]
        [InlineData("Name", "abc", null)]
        [InlineData("Name", "101", null)]
        [InlineData("DatabaseType", null, "Required")]
        [InlineData("DatabaseType", "Oracle", null)]
        [InlineData("DatabaseType", "sqlserver", null)]
        [InlineData("DatabaseType", "2", null)]
        [InlineData("DifferentiationEntity", "41", null)]
        [InlineData("ServerID", null, "Required")]
        public async Task Create_Invalid_Value_Should_Return_400_On_That_Property(string property, string? value, string? message)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();

            var body = CreateBody(UniqueName(), server.ID, "SQLServer", "Server=db;Database=app");

            // A number stands for a text of that length.
            body[property] = int.TryParse(value, out var length) && length > 2 ? new string('a', length) : value;

            var response = await user.Client.PostAsync(Url, body.ToJsonContent());

            await AssertValidationAsync(response, property, message);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Create_Should_Accept_The_Longest_Name_And_Differentiation_Entity()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var body = CreateBody(new string('n', 100), server.ID, "SQLLite", null);
            body["DifferentiationEntity"] = new string('d', 40);

            var response = await user.Client.PostAsync(Url, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Fact]
        public async Task Create_Without_A_ServerID_Should_Return_400_On_ServerID()
        {
            var user = await NewUserAsync();
            var name = UniqueName();

            var body = CreateBody(name, 0, "SQLLite", null);
            body.Remove("ServerID");

            var response = await user.Client.PostAsync(Url, body.ToJsonContent());

            await AssertValidationAsync(response, "ServerID", "Required");
            await AssertNothingCreatedAsync(name);
        }

        [Fact]
        public async Task Create_Should_Trim_The_Trailing_Slash_Of_The_Server_Url()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            // A server URL stored with a trailing slash.
            await _portal.WithDbContextAsync(async db =>
            {
                var stored = await db.Servers.SingleAsync(x => x.ID == server.ID);
                stored.ServerUrl = server.ServerUrl + "/";
                return await db.SaveChangesAsync();
            });

            var response = await user.Client.PostAsync(Url, CreateBody(UniqueName(), server.ID, "SQLLite", null).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(
                new[]
                {
                    $"{server.ServerUrl}/api/ApplicationNew/GetSystemEntities?differentiationEntity=",
                    $"{server.ServerUrl}/api/ApplicationNew/Generate",
                    $"{server.ServerUrl}/api/Application/ClearCache"
                },
                _portal.ApiServer.Requests.Select(x => x.Url));
        }

        [Fact]
        public async Task Create_When_The_Api_Server_Refuses_With_A_Long_Message_Should_Return_That_Message_In_Full()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            // A database driver's connection error is this long, and says what is wrong at its end.
            var refusal = new string('a', 330) + " (provider: TCP Provider)";
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.BadRequest, ApiError(refusal));

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLServer", "Server=nowhere").ToJsonContent());

            await AssertValidationAsync(response, "ConnectionString", refusal);
            await AssertNoRowAsync(name);
        }

        [Fact]
        public async Task Create_When_The_Api_Server_Refuses_With_A_Long_Text_That_Is_Not_Its_Error_Should_Return_A_Fixed_Message()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.BadRequest, new string('x', 301), "text/plain");

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLServer", "Server=nowhere").ToJsonContent());

            await AssertValidationAsync(response, "ConnectionString", ApiServerClient.UnusableAnswerMessage);
            await AssertNoRowAsync(name);
        }

        [Fact]
        public async Task Create_Unknown_Server_Should_Return_404_And_Not_Call_The_Api_Server()
        {
            var user = await NewUserAsync();
            var name = UniqueName();

            var response = await user.Client.PostAsync(Url, CreateBody(name, long.MaxValue, "SQLLite", null).ToJsonContent());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Equal("Server", error.Entity);
            await AssertNothingCreatedAsync(name);
        }

        [Fact]
        public async Task Create_When_The_Api_Server_Refuses_Should_Return_400_On_ConnectionString_With_Its_Message_And_Leave_No_Row()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.BadRequest, ApiError(ApiServerRefusal));

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLServer", "Server=nowhere").ToJsonContent());

            await AssertValidationAsync(response, "ConnectionString", ApiServerRefusal);
            await AssertNoRowAsync(name);
            Assert.Empty(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            Assert.Empty(await ListAsync(user.Client));
        }

        [Fact]
        public async Task Create_SQLite_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message_And_Leave_No_Row()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.BadRequest, ApiError(ApiServerRefusal));

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLLite", null).ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // There is no connection string to blame: the message is for the whole form.
            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(ApiServerRefusal, error.Message);
            Assert.Null(error.Errors);
            await AssertNoRowAsync(name);
        }

        [Fact]
        public async Task Create_When_The_Api_Server_Cannot_Be_Reached_Should_Return_502_And_Leave_No_Row()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();
            _portal.ApiServer.Unreachable(FakeApiServer.GeneratePath);

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLServer", "Server=db").ToJsonContent());

            await AssertUpstreamAsync(response, ApiServerClient.UnusableAnswerMessage);
            await AssertNoRowAsync(name);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task Create_When_Generate_Fails_Otherwise_Should_Return_502_With_The_Message_And_Leave_No_Row(HttpStatusCode upstream)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, upstream, ApiError("UNAUTHORIZED"));

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLServer", "Server=db").ToJsonContent());

            await AssertUpstreamAsync(response, "UNAUTHORIZED");
            await AssertNoRowAsync(name);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "{\"Message\":\"Not today.\"}", "Not today.")]
        [InlineData(HttpStatusCode.OK, "this is not json", "Invalid response from Api server")]
        [InlineData(HttpStatusCode.OK, "null", "Invalid response from Api server")]
        [InlineData(HttpStatusCode.OK, "[{\"Name\":\"Users\"}]", "Invalid response from Api server")]
        public async Task Create_When_The_System_Entities_Cannot_Be_Read_Should_Return_502_And_Not_Generate(HttpStatusCode upstream, string body, string message)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            _portal.ApiServer.Respond(FakeApiServer.GetSystemEntitiesPath, upstream, body);

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLLite", null).ToJsonContent());

            await AssertUpstreamAsync(response, message);
            Assert.Single(_portal.ApiServer.Requests);
            await AssertNoRowAsync(name);
        }

        [Fact]
        public async Task Create_Should_Send_Three_Requests_To_The_Api_Server()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var body = CreateBody(UniqueName(), server.ID, "MySQL", "Server=db;Database=app");
            body["DifferentiationEntity"] = "  Comp any  ";

            var response = await user.Client.PostAsync(Url, body.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<ApplicationResponse>();
            var stored = await LoadAsync(created.Token);
            var bearer = $"Bearer {await StoredTokenAsync(user.Email)}";
            var installationKey = _portal.Services.GetRequiredService<PortalConfiguration>().InstallationKey;

            var requests = _portal.ApiServer.Requests;
            Assert.Equal(3, requests.Count);

            // No header but these; the installation key goes with Generate only.
            Assert.Equal(
                new[]
                {
                    "Accept,Authorization,x-application-token,x-client-id",
                    "Accept,Authorization,x-application-token,x-client-id,x-installation-key",
                    "Accept,Authorization,x-application-token,x-client-id"
                },
                requests.Select(x => string.Join(",", x.Headers.Keys.OrderBy(k => k))));

            // 1. The system entities, before the application exists: the token header is empty. The value is trimmed and encoded.
            Assert.Equal(HttpMethod.Get, requests[0].Method);
            Assert.Equal($"{server.ServerUrl}/api/ApplicationNew/GetSystemEntities?differentiationEntity=Comp%20any", requests[0].Url);
            Assert.Equal(string.Empty, requests[0].Headers["x-application-token"]);
            Assert.Equal("portal", requests[0].Headers["x-client-id"]);
            Assert.Equal(bearer, requests[0].Headers["Authorization"]);
            Assert.False(requests[0].Headers.ContainsKey("x-installation-key"));

            // 2. Generate, with the installation key in a header and the whole application as the body.
            Assert.Equal(HttpMethod.Post, requests[1].Method);
            Assert.Equal($"{server.ServerUrl}/api/ApplicationNew/Generate", requests[1].Url);
            Assert.Equal(created.Token, requests[1].Headers["x-application-token"]);
            Assert.Equal("portal", requests[1].Headers["x-client-id"]);
            Assert.Equal(bearer, requests[1].Headers["Authorization"]);
            Assert.Equal(installationKey, requests[1].Headers["x-installation-key"]);
            Assert.DoesNotContain(installationKey, requests[1].Url);

            var sent = JsonSerializer.Deserialize<DBWS_Application>(requests[1].Body) ?? throw new InvalidOperationException("No Generate body.");
            Assert.Equal(created.Token, sent.Token);
            Assert.Equal(created.Name, sent.Name);
            Assert.Equal(stored.EncryptionKey, sent.EncryptionKey);
            Assert.Equal((int)DatabaseType.MySQL, sent.DatabaseType);
            Assert.Equal("Server=db;Database=app", sent.ConnectionString);
            Assert.Equal("Comp any", sent.DifferentiationEntity);
            Assert.Equal(server.ID, sent.ServerID);
            Assert.True(sent.Online);
            Assert.Equal(new[] { "Users", "Files" }, sent.Entities.Select(x => x.Name));
            Assert.All(sent.Entities, x => Assert.Equal(0, x.ID));
            Assert.All(sent.Entities.SelectMany(x => x.Properties), x => Assert.Equal(0, x.ID));
            Assert.Equal("User registrations per day", Assert.Single(sent.Reports).Title);

            // 3. The cache reset, after the row was saved.
            Assert.Equal(HttpMethod.Get, requests[2].Method);
            Assert.Equal($"{server.ServerUrl}/api/Application/ClearCache", requests[2].Url);
            Assert.Equal(created.Token, requests[2].Headers["x-application-token"]);
            Assert.Equal(bearer, requests[2].Headers["Authorization"]);
            Assert.False(requests[2].Headers.ContainsKey("x-installation-key"));

            Assert.Equal("Comp any", created.DifferentiationEntity);
        }

        [Fact]
        public async Task Create_Should_Store_The_Defaults_Of_A_New_Application()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var response = await user.Client.PostAsync(Url, CreateBody(UniqueName(), server.ID, "SQLLite", null).ToJsonContent());
            var created = await response.ReadJsonAsync<ApplicationResponse>();

            var stored = await LoadAsync(created.Token);
            var owner = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Email == user.Email));

            Assert.Equal(owner.Id, stored.UserID);
            Assert.Equal(user.Email, stored.AdminEmail);
            Assert.True(stored.Online);
            Assert.True(stored.AllowUserRegister);
            Assert.True(stored.AllowLoginUnconfirmedEmail);
            Assert.False(stored.ForceSingleLogin);
            Assert.Equal(60, stored.AuthTokenExpireMinutes);
            Assert.Equal(100, stored.MaxAllowedFileSizeInKB);
            Assert.Equal(string.Empty, stored.DifferentiationEntity);
            Assert.Null(stored.Security);
            Assert.Null(stored.MailServer);
            Assert.Empty(stored.CustomEndpoints);
            Assert.Empty(stored.Collaborates);

            // Eight characters of A-Z and 0-9, stored encrypted.
            Assert.Matches("^[A-Z0-9]{8}$", stored.EncryptionKey.Decrypt(Globals.EncryptionKey));

            Assert.Equal(new[] { "Users", "Files" }, stored.Entities.Select(x => x.Name));
            Assert.Equal(new[] { "ID", "Email" }, stored.Entities[0].Properties.Select(x => x.Name));
            Assert.All(stored.Entities, x => Assert.True(x.IsSystem));

            var report = Assert.Single(stored.Reports);
            Assert.Equal("User registrations per day", report.Title);
            Assert.Equal((int)ReportType.Line, report.TypeID);
            Assert.Equal(1000, report.MaxRecords);
            Assert.Equal((0, 0, 12, 4), (report.X, report.Y, report.W, report.H));

            var series = Assert.Single(report.Series);
            Assert.Equal("Registrations", series.Label);
            Assert.Equal("Users", series.Entity);
            Assert.Equal("Created.Year,Created.Month,Created.Day", series.GroupBy);
            Assert.Equal("ID.Count", series.Property);
            Assert.Null(series.Filter);
            Assert.Equal(0, series.Order);
        }

        [Fact]
        public async Task Create_Should_Write_The_Audit_Row_Of_A_New_Application()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLServer", "Server=audit-secret").ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var owner = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Email == user.Email));

            // The row of a new application names who created it and carries no application ID.
            var audit = await AuditRowAsync(name);
            Assert.Equal(user.Email, audit.UserEmail);
            Assert.Equal(owner.Id, audit.UserId);
            Assert.Null(audit.AppID);
            Assert.Contains(name, audit.Changes);
            Assert.DoesNotContain("audit-secret", audit.Changes);
        }

        [Fact]
        public async Task Create_Should_Ignore_A_Token_And_An_Encryption_Key_Sent_By_The_Client()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var sentToken = Guid.NewGuid().ToString();
            var body = CreateBody(UniqueName(), server.ID, "SQLLite", null);
            body["Token"] = sentToken;
            body["EncryptionKey"] = "CLIENTKEY";
            body["UserID"] = "someone-else";
            body["Online"] = "false";

            var response = await user.Client.PostAsync(Url, body.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<ApplicationResponse>();
            Assert.NotEqual(sentToken, created.Token);
            Assert.True(created.Online);
            Assert.True(created.IsOwner);

            var stored = await LoadAsync(created.Token);
            Assert.NotEqual("CLIENTKEY", stored.EncryptionKey.Decrypt(Globals.EncryptionKey));
            Assert.False(await _portal.WithDbContextAsync(db => db.Applications.AnyAsync(x => x.Token == sentToken)));
        }

        [Fact]
        public async Task Create_Should_Be_Listed_For_Its_Creator_And_Not_For_Another_User()
        {
            var user = await NewUserAsync();
            var other = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var response = await user.Client.PostAsync(Url, CreateBody(UniqueName(), server.ID, "SQLLite", null).ToJsonContent());
            var created = await response.ReadJsonAsync<ApplicationResponse>();

            var listed = Assert.Single(await ListAsync(user.Client));
            Assert.Equal(created.Token, listed.Token);
            Assert.True(listed.IsOwner);

            Assert.Empty(await ListAsync(other.Client));
            Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"{Url}/{created.Token}")).StatusCode);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Create_When_The_Cache_Reset_Fails_Should_Still_Return_201_With_A_Warning_Header(bool unreachable)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            if (unreachable)
            {
                _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);
            }
            else
            {
                _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.BadRequest, ApiError("No."));
            }

            var response = await user.Client.PostAsync(Url, CreateBody(UniqueName(), server.ID, "SQLLite", null).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("Saved, but the API server could not be refreshed.", Assert.Single(response.Headers.GetValues("Warning")));

            var created = await response.ReadJsonAsync<ApplicationResponse>();
            Assert.Single(await ListAsync(user.Client), x => x.Token == created.Token);
        }

        [Fact]
        public async Task Create_Without_The_Csrf_Header_Should_Return_403_And_Create_Nothing()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();
            user.Client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var name = UniqueName();
            var response = await user.Client.PostAsync(Url, CreateBody(name, server.ID, "SQLLite", null).ToJsonContent());

            await AssertForbiddenAsync(response);
            await AssertNothingCreatedAsync(name);
        }

        [Fact]
        public async Task Create_Anonymous_Should_Return_401()
        {
            var server = await _portal.CreateServerAsync();

            var response = await _portal.CreateAnonymousClient().PostAsync(Url, CreateBody(UniqueName(), server.ID, "SQLLite", null).ToJsonContent());

            await AssertUnauthorizedAsync(response);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        // ---------- Import ----------

        [Fact]
        public async Task Import_Valid_File_Should_Return_201_And_Keep_Token_And_Encryption_Key()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var export = Export();
            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLLite", "Server=ignored"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var imported = await response.ReadJsonAsync<ApplicationResponse>();
            Assert.Equal($"{Url}/{export.Token}", response.Headers.Location?.ToString());
            Assert.Equal(export.Token, imported.Token);
            Assert.Equal(export.Name, imported.Name);
            Assert.Equal("SQLLite", imported.DatabaseType);
            Assert.True(imported.IsOwner);
            Assert.Equal(user.Email, imported.OwnerEmail);
            Assert.Equal(0, imported.CollaboratorCount);
            Assert.Equal(server.ID, imported.Server.ID);

            var stored = await LoadAsync(export.Token);
            Assert.Equal(export.EncryptionKey, stored.EncryptionKey);
            Assert.Null(stored.ConnectionString);
            Assert.Equal((int)DatabaseType.SQLLite, stored.DatabaseType);

            // What the file says about the application itself is kept.
            Assert.False(stored.Online);
            Assert.Equal(export.Security, stored.Security);
            Assert.Equal(export.AuthTokenExpireMinutes, stored.AuthTokenExpireMinutes);
            Assert.Equal(export.MaxAllowedFileSizeInKB, stored.MaxAllowedFileSizeInKB);
            Assert.Equal(export.DifferentiationEntity, stored.DifferentiationEntity);

            Assert.Single(await ListAsync(user.Client), x => x.Token == export.Token);
            Assert.Equal(user.Email, (await AuditRowAsync(export.Name)).UserEmail);
        }

        [Fact]
        public async Task Import_Should_Replace_Owner_Server_Collaborators_And_IDs_Of_The_File()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            var otherServer = await _portal.CreateServerAsync();
            var existing = await _portal.CreateApplicationAsync(otherServer.ID);
            Assert.NotEqual(server.ServerUrl, otherServer.ServerUrl);
            ScriptApiServer();

            // The file claims IDs, an owner, a server and a collaborator of its own.
            var export = Export();
            export.ID = existing.ID;
            export.UserID = "someone-else";
            export.AdminEmail = "someone-else@portal.test";
            export.ServerID = otherServer.ID;
            export.Server = new DBWS_Server { ID = otherServer.ID, Name = "from the file", ServerUrl = "https://evil.example.test" };
            export.Collaborates = new List<DBWS_Collaborate> { new DBWS_Collaborate { ID = 1, AppID = existing.ID, UserEmail = "intruder@portal.test" } };
            export.ConnectionString = "Server=from-the-file";
            export.DatabaseType = (int)DatabaseType.MySQL;

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLLite", null));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var owner = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Email == user.Email));
            var stored = await LoadAsync(export.Token);

            Assert.NotEqual(existing.ID, stored.ID);
            Assert.Equal(owner.Id, stored.UserID);
            Assert.Equal(user.Email, stored.AdminEmail);
            Assert.Equal(server.ID, stored.ServerID);
            Assert.Equal((int)DatabaseType.SQLLite, stored.DatabaseType);
            Assert.Null(stored.ConnectionString);
            Assert.Empty(stored.Collaborates);

            // The server named in the file is untouched, and the existing application too.
            var untouched = await _portal.WithDbContextAsync(db => db.Servers.AsNoTracking().SingleAsync(x => x.ID == otherServer.ID));
            Assert.Equal(otherServer.Name, untouched.Name);
            Assert.Equal(otherServer.ServerUrl, untouched.ServerUrl);
            Assert.Equal(existing.Token, (await LoadAsync(existing.Token)).Token);

            // Entities and properties are added in the order of their old IDs, whatever their order in the file.
            Assert.Equal(new[] { "Users", "Files", "Orders" }, stored.Entities.Select(x => x.Name));
            Assert.Equal(new[] { "ID", "Email" }, stored.Entities[0].Properties.Select(x => x.Name));
            Assert.All(stored.Entities, x => Assert.Equal(stored.ID, x.AppID));

            Assert.Equal("GetOrders", Assert.Single(stored.CustomEndpoints).Name);
            Assert.Equal("Orders per day", Assert.Single(stored.Reports).Title);
            Assert.Equal("Orders", Assert.Single(stored.Reports[0].Series).Label);

            // One call: Generate, with the installation key. No system entities and no cache reset.
            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal(
                new[] { "Accept", "Authorization", "x-application-token", "x-client-id", "x-installation-key" },
                request.Headers.Keys.OrderBy(x => x));
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"{server.ServerUrl}/api/ApplicationNew/Generate", request.Url);
            Assert.Equal(export.Token, request.Headers["x-application-token"]);
            Assert.Equal("portal", request.Headers["x-client-id"]);
            Assert.Equal($"Bearer {await StoredTokenAsync(user.Email)}", request.Headers["Authorization"]);
            Assert.Equal(_portal.Services.GetRequiredService<PortalConfiguration>().InstallationKey, request.Headers["x-installation-key"]);

            var sent = JsonSerializer.Deserialize<DBWS_Application>(request.Body) ?? throw new InvalidOperationException("No Generate body.");
            Assert.Equal(0, sent.ID);
            Assert.Equal(export.Token, sent.Token);
            Assert.Equal(owner.Id, sent.UserID);
            Assert.Equal(server.ID, sent.ServerID);
            Assert.Equal(server.ServerUrl, sent.Server.ServerUrl);
            Assert.Null(sent.ConnectionString);
            Assert.Empty(sent.Collaborates);
            Assert.Contains("\"Collaborates\":[]", request.Body);
            Assert.Equal(new[] { "Users", "Files", "Orders" }, sent.Entities.Select(x => x.Name));
            Assert.All(sent.Entities, x => Assert.Equal(0, x.ID));
            Assert.All(sent.Entities.SelectMany(x => x.Properties), x => Assert.Equal(0, x.ID));
            Assert.All(sent.CustomEndpoints, x => Assert.Equal(0, x.ID));
            Assert.All(sent.Reports, x => Assert.Equal(0, x.ID));
        }

        [Fact]
        public async Task Import_Twice_With_The_Same_Report_Series_IDs_Should_Work()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            // Both files carry the same series ID, as two exports of one application would.
            var first = await user.Client.PostAsync(ImportUrl, ImportBody(Json(Export()), server.ID, "SQLLite", null));
            var second = await user.Client.PostAsync(ImportUrl, ImportBody(Json(Export()), server.ID, "SQLLite", null));

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        }

        [Theory]
        [InlineData("SQLServer", DatabaseType.SQLServer)]
        [InlineData("MySQL", DatabaseType.MySQL)]
        [InlineData("PostgreSQL", DatabaseType.PostgreSQL)]
        public async Task Import_Other_Database_Should_Store_The_Connection_String(string databaseType, DatabaseType expected)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var export = Export();
            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, databaseType, "Host=db;Database=app"));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.DoesNotContain("Host=db", await response.Content.ReadAsStringAsync());

            var stored = await LoadAsync(export.Token);
            Assert.Equal((int)expected, stored.DatabaseType);
            Assert.Equal("Host=db;Database=app", stored.ConnectionString);
        }

        [Theory]
        [InlineData("D")]
        [InlineData("N")]
        [InlineData("B")]
        public async Task Import_Should_Store_The_Token_In_The_Spelling_The_Portal_Issues(string format)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            var token = Guid.NewGuid();
            var export = Export();
            export.Token = token.ToString(format).ToUpperInvariant();

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLLite", null));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(token.ToString(), (await response.ReadJsonAsync<ApplicationResponse>()).Token);
            Assert.Equal(token.ToString(), Assert.Single(_portal.ApiServer.Requests).Headers["x-application-token"]);
        }

        [Theory]
        [InlineData("D", false)]
        [InlineData("D", true)]
        [InlineData("N", false)]
        [InlineData("B", true)]
        public async Task Import_Existing_Token_In_Any_Spelling_Should_Return_409_And_Not_Call_The_Api_Server(string format, bool upperCase)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();

            // An application of somebody else: the token is taken on the whole instance.
            var existing = await _portal.CreateApplicationAsync(server.ID);
            var spelling = Guid.Parse(existing.Token).ToString(format);

            var export = Export();
            export.Token = upperCase ? spelling.ToUpperInvariant() : spelling;

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLLite", null));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Conflict, error.Code);
            Assert.Equal($"Application token '{existing.Token}' already exists", error.Message);
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(1, await _portal.WithDbContextAsync(db => db.Applications.CountAsync(x => x.Name == existing.Name || x.Name == export.Name)));
        }

        [Fact]
        public async Task Import_Without_A_File_Should_Return_400_On_File()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(null, server.ID, "SQLLite", null));

            await AssertValidationAsync(response, "File", "Required");
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Theory]
        [InlineData("", "No file found to upload")]
        [InlineData("PK\u0003\u0004 this is a zip, not json", null)]
        [InlineData("{ \"Token\": ", null)]
        [InlineData("[]", null)]
        [InlineData("null", "Application json cannot be null")]
        [InlineData("{}", null)]
        public async Task Import_Bad_File_Should_Return_400_On_File(string content, string? message)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(content, server.ID, "SQLLite", null));

            await AssertValidationAsync(response, "File", message);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Theory]
        [InlineData("Token")]
        [InlineData("Name")]
        [InlineData("EncryptionKey")]
        [InlineData("Entities")]
        [InlineData("Properties")]
        [InlineData("NullEntity")]
        [InlineData("NullProperty")]
        [InlineData("NullCustomEndpoint")]
        [InlineData("NullReport")]
        [InlineData("NullSeries")]
        [InlineData("Entity.Name")]
        [InlineData("Property.Name")]
        [InlineData("CustomEndpoint.Name")]
        [InlineData("CustomEndpoint.Query")]
        [InlineData("Report.Title")]
        [InlineData("Series.Label")]
        [InlineData("Series.Entity")]
        [InlineData("Series.GroupBy")]
        [InlineData("Series.Property")]
        public async Task Import_File_Without_A_Needed_Value_Should_Return_400_On_File(string missing)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();

            var export = Export();
            var json = JsonSerializer.SerializeToNode(export)?.AsObject() ?? throw new InvalidOperationException("No JSON.");
            var entity = json["Entities"]?[0]?.AsObject() ?? throw new InvalidOperationException("No entity.");
            var report = json["Reports"]?[0]?.AsObject() ?? throw new InvalidOperationException("No report.");

            // 'Entity.Name' and the like: the first item of that kind, with the value set to null.
            var items = new Dictionary<string, JsonObject?>
            {
                ["Entity"] = entity,
                ["Property"] = entity["Properties"]?[0]?.AsObject(),
                ["CustomEndpoint"] = json["CustomEndpoints"]?[0]?.AsObject(),
                ["Report"] = report,
                ["Series"] = report["Series"]?[0]?.AsObject()
            };

            var parts = missing.Split('.');

            if (parts.Length == 2)
            {
                (items[parts[0]] ?? throw new InvalidOperationException($"No {parts[0]}."))[parts[1]] = null;
            }
            else if (missing == "Token")
            {
                json["Token"] = "not-a-guid";
            }
            else if (missing == "Properties")
            {
                entity.Remove("Properties");
            }
            else if (missing == "NullEntity")
            {
                json["Entities"] = new JsonArray((JsonNode?)null);
            }
            else if (missing == "NullProperty")
            {
                entity["Properties"] = new JsonArray((JsonNode?)null);
            }
            else if (missing == "NullCustomEndpoint")
            {
                json["CustomEndpoints"] = new JsonArray((JsonNode?)null);
            }
            else if (missing == "NullReport")
            {
                json["Reports"] = new JsonArray((JsonNode?)null);
            }
            else if (missing == "NullSeries")
            {
                report["Series"] = new JsonArray((JsonNode?)null);
            }
            else
            {
                json.Remove(missing);
            }

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(json.ToJsonString(), server.ID, "SQLLite", null));

            await AssertValidationAsync(response, "File", missing == "Token" ? "Application token is not valid" : null);
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNoRowAsync(export.Name);
        }

        [Theory]
        [InlineData("SQLServer", null, "ConnectionString", "Required")]
        [InlineData("PostgreSQL", "  ", "ConnectionString", "Required")]
        [InlineData("Oracle", "Server=db", "DatabaseType", null)]
        [InlineData("2", "Server=db", "DatabaseType", null)]
        [InlineData(null, "Server=db", "DatabaseType", "Required")]
        public async Task Import_Invalid_Value_Should_Return_400_On_That_Property(string? databaseType, string? connectionString, string property, string? message)
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            var export = Export();

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, databaseType, connectionString));

            await AssertValidationAsync(response, property, message);
            await AssertNothingCreatedAsync(export.Name);
        }

        [Fact]
        public async Task Import_Should_Read_Its_Values_From_The_Body_Not_From_The_Query_String()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            var export = Export();

            var response = await user.Client.PostAsync(
                $"{ImportUrl}?ServerID={server.ID}&DatabaseType=SQLLite",
                ImportBody(Json(export), server.ID, null, null));

            await AssertValidationAsync(response, "DatabaseType", "Required");
            await AssertNothingCreatedAsync(export.Name);
        }

        [Fact]
        public async Task Import_Without_A_ServerID_Should_Return_400_On_ServerID()
        {
            var user = await NewUserAsync();
            var export = Export();

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), null, "SQLLite", null));

            await AssertValidationAsync(response, "ServerID", "Required");
            await AssertNothingCreatedAsync(export.Name);
        }

        [Fact]
        public async Task Import_File_Without_Reports_Custom_Endpoints_Or_Series_Should_Return_201()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            // One file without the two lists, one whose report has no Series list.
            var bare = Export();
            var bareJson = JsonSerializer.SerializeToNode(bare)?.AsObject() ?? throw new InvalidOperationException("No JSON.");
            bareJson.Remove("Reports");
            bareJson.Remove("CustomEndpoints");

            var noSeries = Export();
            var noSeriesJson = JsonSerializer.SerializeToNode(noSeries)?.AsObject() ?? throw new InvalidOperationException("No JSON.");
            (noSeriesJson["Reports"]?[0]?.AsObject() ?? throw new InvalidOperationException("No report."))["Series"] = null;

            var bareResponse = await user.Client.PostAsync(ImportUrl, ImportBody(bareJson.ToJsonString(), server.ID, "SQLLite", null));
            var noSeriesResponse = await user.Client.PostAsync(ImportUrl, ImportBody(noSeriesJson.ToJsonString(), server.ID, "SQLLite", null));

            Assert.Equal(HttpStatusCode.Created, bareResponse.StatusCode);
            Assert.Equal(HttpStatusCode.Created, noSeriesResponse.StatusCode);
            Assert.Equal(2, _portal.ApiServer.RequestsTo(FakeApiServer.GeneratePath).Count);

            var stored = await LoadAsync(bare.Token);
            Assert.Empty(stored.Reports);
            Assert.Empty(stored.CustomEndpoints);
            Assert.Empty(Assert.Single((await LoadAsync(noSeries.Token)).Reports).Series);
        }

        [Fact]
        public async Task Import_Unknown_Server_Should_Return_404_And_Not_Call_The_Api_Server()
        {
            var user = await NewUserAsync();
            var export = Export();

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), long.MaxValue, "SQLLite", null));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Server", (await response.ReadJsonAsync<ErrorResponse>()).Entity);
            await AssertNothingCreatedAsync(export.Name);
        }

        [Fact]
        public async Task Import_When_The_Api_Server_Refuses_Should_Return_400_On_ConnectionString_With_Its_Message_And_Leave_No_Row()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.BadRequest, ApiError(ApiServerRefusal));

            var export = Export();
            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLServer", "Server=nowhere"));

            await AssertValidationAsync(response, "ConnectionString", ApiServerRefusal);
            await AssertNoRowAsync(export.Name);
        }

        [Fact]
        public async Task Import_When_The_Api_Server_Refuses_A_Property_It_Names_Should_Return_400_On_That_Property()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.BadRequest, ApiError("Property Email already exists", "Email"));

            var export = Export();
            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLServer", "Server=db"));

            // What is wrong is in the file, not in the connection string.
            await AssertValidationAsync(response, "Email", "Property Email already exists");
            await AssertNoRowAsync(export.Name);
        }

        [Fact]
        public async Task Import_When_The_Portal_Cannot_Save_After_Generate_Should_Return_500_That_Says_So()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();

            // Two entities of one name: a real API server refuses them, this one does not, and the
            // Portal database cannot store them.
            var export = Export();
            export.Entities[1].Name = export.Entities[0].Name;

            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLLite", null));

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Error, error.Code);
            Assert.Equal("The application was created on the API server but could not be saved in the Portal.", error.Message);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.GeneratePath));
            await AssertNoRowAsync(export.Name);
        }

        [Fact]
        public async Task Import_When_The_Api_Server_Cannot_Be_Reached_Should_Return_502_And_Leave_No_Row()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            _portal.ApiServer.Unreachable(FakeApiServer.GeneratePath);

            var export = Export();
            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLLite", null));

            await AssertUpstreamAsync(response, ApiServerClient.UnusableAnswerMessage);
            await AssertNoRowAsync(export.Name);

            // Nothing was kept, so the same file can be imported once the API server is back.
            ScriptApiServer();
            var retry = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLLite", null));
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        }

        [Fact]
        public async Task Import_Without_The_Csrf_Header_Should_Return_403_And_Create_Nothing()
        {
            var user = await NewUserAsync();
            var server = await _portal.CreateServerAsync();
            ScriptApiServer();
            user.Client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var export = Export();
            var response = await user.Client.PostAsync(ImportUrl, ImportBody(Json(export), server.ID, "SQLLite", null));

            await AssertForbiddenAsync(response);
            await AssertNothingCreatedAsync(export.Name);
        }

        [Fact]
        public async Task Import_Anonymous_Should_Return_401()
        {
            var server = await _portal.CreateServerAsync();

            var response = await _portal.CreateAnonymousClient().PostAsync(ImportUrl, ImportBody(Json(Export()), server.ID, "SQLLite", null));

            await AssertUnauthorizedAsync(response);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        // ---------- Contract ----------

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Multipart_Import_And_The_Warning_Header()
        {
            var client = await _portal.CreateUserClientAsync();
            var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")) ?? throw new InvalidOperationException("No document.");

            var schema = document["paths"]?[ImportUrl]?["post"]?["requestBody"]?["content"]?["multipart/form-data"]?["schema"]
                ?? throw new InvalidOperationException("The import is not described as multipart/form-data.");

            Assert.Equal("binary", schema["properties"]?["File"]?["format"]?.GetValue<string>());
            Assert.NotNull(schema["properties"]?["ServerID"]);
            Assert.NotNull(schema["properties"]?["DatabaseType"]);
            Assert.NotNull(schema["properties"]?["ConnectionString"]);
            Assert.Empty(document["paths"]?[ImportUrl]?["post"]?["parameters"]?.AsArray() ?? new JsonArray());

            Assert.NotNull(document["paths"]?[Url]?["post"]?["responses"]?["201"]?["headers"]?["Warning"]);
            Assert.Null(document["paths"]?[ImportUrl]?["post"]?["responses"]?["201"]?["headers"]);
        }

        // ---------- Helpers ----------

        private record User(string Email, HttpClient Client);

        private async Task<User> NewUserAsync()
        {
            var (email, password) = await _portal.CreateUserAsync();

            return new User(email, await _portal.CreateSignedInClientAsync(email, password));
        }

        private static string UniqueName()
        {
            return $"app-{Guid.NewGuid():N}";
        }

        private static Dictionary<string, object?> CreateBody(string name, long serverId, string databaseType, string? connectionString)
        {
            return new Dictionary<string, object?>
            {
                ["Name"] = name,
                ["ServerID"] = serverId,
                ["DatabaseType"] = databaseType,
                ["ConnectionString"] = connectionString
            };
        }

        /// <summary>
        /// The form the Portal UI posts. A null part is left out.
        /// </summary>
        private static MultipartFormDataContent ImportBody(string? fileContent, long? serverId, string? databaseType, string? connectionString)
        {
            var form = new MultipartFormDataContent();

            if (fileContent is not null)
            {
                var file = new ByteArrayContent(Encoding.UTF8.GetBytes(fileContent));
                file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                form.Add(file, "File", "application.json");
            }

            if (serverId is not null)
            {
                form.Add(new StringContent(serverId.Value.ToString()), "ServerID");
            }

            if (databaseType is not null)
            {
                form.Add(new StringContent(databaseType), "DatabaseType");
            }

            if (connectionString is not null)
            {
                form.Add(new StringContent(connectionString), "ConnectionString");
            }

            return form;
        }

        /// <summary>
        /// The API server answers the three calls of a create the way a healthy one does.
        /// </summary>
        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.GetSystemEntitiesPath, HttpStatusCode.OK, JsonSerializer.Serialize(SystemEntities()));
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.OK, "true");
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        // Numbered -1, as the API server returns them.
        private static List<DBWS_Entity> SystemEntities()
        {
            return new List<DBWS_Entity>
            {
                Entity(-1, "Users", isSystem: true, Property(-1, "ID", isPrimaryKey: true), Property(-1, "Email", isPrimaryKey: false)),
                Entity(-1, "Files", isSystem: true, Property(-1, "ID", isPrimaryKey: true))
            };
        }

        /// <summary>
        /// An application as an export holds it, with a new token and name. Entities and properties
        /// are listed out of the order of their IDs.
        /// </summary>
        private static DBWS_Application Export()
        {
            return new DBWS_Application
            {
                ID = 0,
                Token = Guid.NewGuid().ToString(),
                Name = UniqueName(),
                UserID = string.Empty,
                ServerID = 0,
                Server = new DBWS_Server(),
                EncryptionKey = "KEY12345".Encrypt(Globals.EncryptionKey),
                Online = false,
                AuthTokenExpireMinutes = 45,
                MaxAllowedFileSizeInKB = 2048,
                DatabaseType = 0,
                DifferentiationEntity = "Company",
                Security = "[]",
                Collaborates = new List<DBWS_Collaborate>(),
                Entities = new List<DBWS_Entity>
                {
                    Entity(30, "Orders", isSystem: false, Property(31, "ID", isPrimaryKey: true)),
                    Entity(20, "Files", isSystem: true, Property(21, "ID", isPrimaryKey: true)),
                    Entity(10, "Users", isSystem: true, Property(12, "Email", isPrimaryKey: false), Property(11, "ID", isPrimaryKey: true))
                },
                CustomEndpoints = new List<DBWS_CustomEndpoint>
                {
                    new DBWS_CustomEndpoint { ID = 7, AppID = 99, Name = "GetOrders", Query = "SELECT 1" }
                },
                Reports = new List<DBWS_ReportPanel>
                {
                    new DBWS_ReportPanel
                    {
                        ID = 5,
                        AppID = 99,
                        Title = "Orders per day",
                        MaxRecords = 100,
                        W = 6,
                        H = 4,
                        Series = new List<DBWS_ReportSeries>
                        {
                            new DBWS_ReportSeries { ID = 1, PanelID = 5, Label = "Orders", Entity = "Orders", GroupBy = "Created.Day", Property = "ID.Count" }
                        }
                    }
                }
            };
        }

        private static DBWS_Entity Entity(long id, string name, bool isSystem, params DBWS_EntityProperty[] properties)
        {
            return new DBWS_Entity
            {
                ID = id,
                AppID = -1,
                Name = name,
                IsSystem = isSystem,
                Properties = properties.ToList()
            };
        }

        private static DBWS_EntityProperty Property(long id, string name, bool isPrimaryKey)
        {
            return new DBWS_EntityProperty
            {
                ID = id,
                EntityID = -1,
                Name = name,
                IsSystem = true,
                IsPrimaryKey = isPrimaryKey,
                Required = true,
                TypeID = isPrimaryKey ? (int)PropertyType.Number : (int)PropertyType.String
            };
        }

        private static string Json(DBWS_Application application)
        {
            return JsonSerializer.Serialize(application, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string ApiError(string message, string? property = null)
        {
            return JsonSerializer.Serialize(new { Code = "ERROR", Message = message, Property = property, Entity = (string?)null });
        }

        /// <summary>
        /// The stored application with everything that belongs to it, in the order it was added.
        /// </summary>
        private async Task<DBWS_Application> LoadAsync(string token)
        {
            var application = await _portal.WithDbContextAsync(db => db.Applications
                .AsNoTracking()
                .Include(x => x.Entities).ThenInclude(x => x.Properties)
                .Include(x => x.Reports).ThenInclude(x => x.Series)
                .Include(x => x.CustomEndpoints)
                .Include(x => x.Collaborates)
                .AsSplitQuery()
                .SingleAsync(x => x.Token == token));

            application.Entities = application.Entities.OrderBy(x => x.ID).ToList();
            application.Entities.ForEach(x => x.Properties = x.Properties.OrderBy(p => p.ID).ToList());

            return application;
        }

        private async Task<PortalAuditLog> AuditRowAsync(string applicationName)
        {
            return await _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .SingleAsync(x => x.EntityType == "Application" && x.EntityIdentifier == applicationName && x.Action == "Created"));
        }

        private static async Task<List<ApplicationResponse>> ListAsync(HttpClient client)
        {
            return (await (await client.GetAsync(Url)).ReadJsonAsync<ListResponse<ApplicationResponse>>()).Data;
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

        private async Task AssertNoRowAsync(string applicationName)
        {
            Assert.False(await _portal.WithDbContextAsync(db => db.Applications.AnyAsync(x => x.Name == applicationName)));
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.EntityIdentifier == applicationName)));
        }

        private async Task AssertNothingCreatedAsync(string applicationName)
        {
            Assert.Empty(_portal.ApiServer.Requests);
            await AssertNoRowAsync(applicationName);
        }

        private static async Task AssertValidationAsync(HttpResponseMessage response, string property, string? message)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);

            var detail = Assert.Single(error.Errors ?? throw new InvalidOperationException("No Errors."));
            Assert.Equal(property, detail.Property);
            Assert.False(string.IsNullOrWhiteSpace(detail.Message));

            if (message is not null)
            {
                Assert.Equal(message, detail.Message);
            }
        }

        private static async Task AssertUpstreamAsync(HttpResponseMessage response, string message)
        {
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.UpstreamError, error.Code);
            Assert.Equal(message, error.Message);
        }

        private static async Task AssertForbiddenAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
        }

        private static async Task AssertUnauthorizedAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(PortalErrorCode.Unauthorized, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }
    }
}
