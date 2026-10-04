using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
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
    /// PUT /applications/{appToken}, PUT .../status, POST .../rebuild and DELETE /applications/{appToken}.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class ApplicationSettingsApiTests
    {
        private const string ListUrl = "/api/v1/applications";
        private const string RebuildPath = "/api/Application/Rebuild";
        private const string DegeneratePath = "/api/Application/Degenerate";

        private readonly PortalFactory _portal;

        public ApplicationSettingsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Edit ----------

        [Fact]
        public async Task Update_Should_Rename_Replace_The_Connection_String_And_Reset_The_Cache()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.AppUrl, Body("Renamed app", "Server=new-db;Database=app").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var application = await response.ReadJsonAsync<ApplicationResponse>();
            Assert.Equal(scene.Token, application.Token);
            Assert.Equal("Renamed app", application.Name);
            Assert.Equal("SQLServer", application.DatabaseType);
            Assert.True(application.HasConnectionString);
            Assert.True(application.IsOwner);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal("Renamed app", stored.Name);
            Assert.Equal("Server=new-db;Database=app", stored.ConnectionString);
            Assert.Equal((int)DatabaseType.SQLServer, stored.DatabaseType);

            // The one call an edit makes.
            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"{scene.Server.ServerUrl}/api/Application/ClearCache", request.Url);
            await AssertPortalHeadersAsync(request, scene.Token, scene.OwnerEmail);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Update_Without_A_Connection_String_Should_Keep_The_Stored_One(bool sendNull)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var body = new Dictionary<string, object?> { ["Name"] = "Kept connection" };

            if (sendNull)
            {
                body["ConnectionString"] = null;
            }

            var response = await scene.Owner.PutAsync(scene.AppUrl, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((await response.ReadJsonAsync<ApplicationResponse>()).HasConnectionString);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal("Kept connection", stored.Name);
            Assert.Equal(scene.ConnectionString, stored.ConnectionString);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Update_With_An_Empty_Connection_String_Should_Return_400_And_Change_Nothing(string connectionString)
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PutAsync(scene.AppUrl, Body("Not saved", connectionString).ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "ConnectionString: Required");
            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Update_Without_A_Stored_Connection_String_Should_Require_One_Unless_Sqlite()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetStoredAsync(scene.AppId, x => x.ConnectionString = null);

            var missing = await scene.Owner.PutAsync(scene.AppUrl, new { Name = "First set" }.ToJsonContent());

            await EntityScene.AssertValidationAsync(missing, "ConnectionString: Required");
            Assert.Null((await LoadAsync(scene.AppId)).ConnectionString);
            Assert.Empty(_portal.ApiServer.Requests);

            var given = await scene.Owner.PutAsync(scene.AppUrl, Body("First set", "Server=first;Database=app").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, given.StatusCode);
            Assert.True((await given.ReadJsonAsync<ApplicationResponse>()).HasConnectionString);
            Assert.Equal("Server=first;Database=app", (await LoadAsync(scene.AppId)).ConnectionString);
        }

        [Fact]
        public async Task Update_Sqlite_Application_Should_Ignore_The_Connection_String()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetStoredAsync(scene.AppId, x =>
            {
                x.DatabaseType = (int)DatabaseType.SQLLite;
                x.ConnectionString = null;
            });

            var given = await scene.Owner.PutAsync(scene.AppUrl, Body("Sqlite app", "Data Source=elsewhere.db").ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, given.StatusCode);

            var application = await given.ReadJsonAsync<ApplicationResponse>();
            Assert.Equal("SQLLite", application.DatabaseType);
            Assert.False(application.HasConnectionString);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal("Sqlite app", stored.Name);
            Assert.Null(stored.ConnectionString);

            // Nothing stored and nothing sent is fine for SQLite.
            var missing = await scene.Owner.PutAsync(scene.AppUrl, new { Name = "Sqlite again" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
            Assert.Equal("Sqlite again", (await LoadAsync(scene.AppId)).Name);
        }

        [Fact]
        public async Task Sqlite_Application_With_A_Stored_Connection_String_Should_Report_None()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // A connection string left on a SQLite application: the API server ignores it.
            await SetStoredAsync(scene.AppId, x =>
            {
                x.DatabaseType = (int)DatabaseType.SQLLite;
                x.ConnectionString = "Data Source=legacy.db";
            });

            var read = await scene.Owner.GetAsync(scene.AppUrl);

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.False((await read.ReadJsonAsync<ApplicationResponse>()).HasConnectionString);

            var updated = await scene.Owner.PutAsync(scene.AppUrl, new { Name = "Sqlite legacy" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.False((await updated.ReadJsonAsync<ApplicationResponse>()).HasConnectionString);

            // Ignored, not cleared.
            var stored = await LoadAsync(scene.AppId);
            Assert.Equal("Sqlite legacy", stored.Name);
            Assert.Equal("Data Source=legacy.db", stored.ConnectionString);
        }

        [Theory]
        [InlineData(1)]
        [InlineData("SQLLite")]
        public async Task Update_DatabaseType_In_The_Body_Should_Not_Skip_The_Connection_String_Rule(object databaseType)
        {
            var scene = await CreateSceneAsync();
            await SetStoredAsync(scene.AppId, x => x.ConnectionString = null);

            var body = new Dictionary<string, object?> { ["Name"] = "Claims SQLite", ["DatabaseType"] = databaseType };

            var response = await scene.Owner.PutAsync(scene.AppUrl, body.ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "ConnectionString: Required");

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal((int)DatabaseType.SQLServer, stored.DatabaseType);
            Assert.Equal(scene.Name, stored.Name);
        }

        [Theory]
        [InlineData(2)]
        [InlineData("SQLServer")]
        public async Task Update_DatabaseType_In_The_Body_Should_Not_Set_A_Connection_String_On_Sqlite(object databaseType)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetStoredAsync(scene.AppId, x =>
            {
                x.DatabaseType = (int)DatabaseType.SQLLite;
                x.ConnectionString = null;
            });

            var body = new Dictionary<string, object?>
            {
                ["Name"] = "Claims SQL Server",
                ["DatabaseType"] = databaseType,
                ["ConnectionString"] = "Server=sneaky;Database=app"
            };

            var response = await scene.Owner.PutAsync(scene.AppUrl, body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("SQLLite", (await response.ReadJsonAsync<ApplicationResponse>()).DatabaseType);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal((int)DatabaseType.SQLLite, stored.DatabaseType);
            Assert.Null(stored.ConnectionString);
        }

        [Theory]
        [InlineData(null, "Name: Required")]
        [InlineData("", "Name: Required")]
        [InlineData("    ", "Name: Required")]
        [InlineData("abc", "Name: Must be 4 to 100 characters")]
        [InlineData("101", "Name: Must be 4 to 100 characters")]
        public async Task Update_Invalid_Name_Should_Return_400_And_Change_Nothing(string? name, string expected)
        {
            var scene = await CreateSceneAsync();

            // A number stands for a text of that length.
            var value = int.TryParse(name, out var length) ? new string('n', length) : name;

            var response = await scene.Owner.PutAsync(scene.AppUrl, new Dictionary<string, object?> { ["Name"] = value }.ToJsonContent());

            await EntityScene.AssertValidationAsync(response, expected);
            await AssertUnchangedAsync(scene);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(100)]
        public async Task Update_Name_Of_4_To_100_Characters_Should_Be_Saved_As_Sent(int length)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // Not trimmed.
            var name = " " + new string('n', length - 1);

            var response = await scene.Owner.PutAsync(scene.AppUrl, new { Name = name }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(name, (await LoadAsync(scene.AppId)).Name);
        }

        [Fact]
        public async Task Update_When_The_Cache_Reset_Fails_Should_Save_And_Warn()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.InternalServerError, EntityScene.ApiError("Down"));

            var response = await scene.Owner.PutAsync(scene.AppUrl, new { Name = "Saved anyway" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            EntityScene.AssertWarning(response);
            Assert.Equal("Saved anyway", (await LoadAsync(scene.AppId)).Name);
        }

        [Fact]
        public async Task Update_Should_Audit_The_Change_With_The_Connection_String_Masked()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.AppUrl, Body("Renamed", "Server=new;Database=app").ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var row = Assert.Single(await AuditRowsAsync(scene.OwnerEmail));

            Assert.Equal("Application | Renamed | Modified", $"{row.EntityType} | {row.EntityIdentifier} | {row.Action}");
            Assert.Equal(scene.AppId, row.AppID);

            // Compared before masking, so the change is recorded; the values never are.
            Assert.Equal(
                new[] { "ConnectionString: *** -> ***", $"Name: {scene.Name} -> Renamed" },
                Changes(row));
        }

        [Fact]
        public async Task Update_With_The_Same_Values_Should_Write_No_Audit_Row()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PutAsync(scene.AppUrl, Body(scene.Name, scene.ConnectionString).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        // ---------- Status ----------

        [Fact]
        public async Task Status_Should_Take_The_Application_Offline_And_Online_And_Reset_The_Cache_Each_Time()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var offline = await scene.Owner.PutAsync(scene.StatusUrl, new { Online = false }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, offline.StatusCode);
            Assert.False((await offline.ReadJsonAsync<ApplicationResponse>()).Online);
            Assert.False((await LoadAsync(scene.AppId)).Online);

            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal($"{scene.Server.ServerUrl}/api/Application/ClearCache", request.Url);
            await AssertPortalHeadersAsync(request, scene.Token, scene.OwnerEmail);

            var online = await scene.Owner.PutAsync(scene.StatusUrl, new { Online = true }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, online.StatusCode);
            Assert.True((await online.ReadJsonAsync<ApplicationResponse>()).Online);
            Assert.True((await LoadAsync(scene.AppId)).Online);
            Assert.Equal(2, _portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath).Count);

            // One row per change, with the Online column only.
            var audit = await AuditRowsAsync(scene.OwnerEmail);
            Assert.Equal(2, audit.Count);
            Assert.All(audit, x => Assert.Equal("Application | Modified", $"{x.EntityType} | {x.Action}"));
            Assert.All(audit, x => Assert.Equal(scene.AppId, x.AppID));
            Assert.Equal(new[] { "Online: True -> False" }, Changes(audit[0]));
            Assert.Equal(new[] { "Online: False -> True" }, Changes(audit[1]));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Status_Same_Value_Should_Change_Nothing_But_Still_Reset_The_Cache(bool online)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            await SetStoredAsync(scene.AppId, x => x.Online = online);

            for (var i = 0; i < 2; i++)
            {
                var response = await scene.Owner.PutAsync(scene.StatusUrl, new { Online = online }.ToJsonContent());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(online, (await response.ReadJsonAsync<ApplicationResponse>()).Online);
            }

            Assert.Equal(online, (await LoadAsync(scene.AppId)).Online);
            Assert.Equal(2, _portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath).Count);
            Assert.Empty(await AuditRowsAsync(scene.OwnerEmail));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("{\"Online\":null}")]
        public async Task Status_Without_A_Value_Should_Return_400_And_Change_Nothing(string json)
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PutAsync(scene.StatusUrl, EntityScene.Json(json));

            await EntityScene.AssertValidationAsync(response, "Online: Required");
            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Status_When_The_Cache_Reset_Fails_Should_Save_And_Warn()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Unreachable(FakeApiServer.ClearCachePath);

            var response = await scene.Owner.PutAsync(scene.StatusUrl, new { Online = false }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            EntityScene.AssertWarning(response);
            Assert.False((await LoadAsync(scene.AppId)).Online);
        }

        // ---------- Rebuild ----------

        [Fact]
        public async Task Rebuild_Should_Call_Rebuild_Then_Reset_The_Cache_And_Change_Nothing_In_The_Portal()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(scene.RebuildUrl, null);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(response.Headers.Contains(ApiServerCacheReset.WarningHeaderName));

            var requests = _portal.ApiServer.Requests;
            Assert.Equal(
                new[] { $"GET {scene.Server.ServerUrl}/api/Application/Rebuild", $"GET {scene.Server.ServerUrl}/api/Application/ClearCache" },
                requests.Select(x => $"{x.Method} {x.Url}"));

            foreach (var request in requests)
            {
                await AssertPortalHeadersAsync(request, scene.Token, scene.OwnerEmail);
            }

            await AssertUnchangedAsync(scene, expectedRequests: 2);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.Unauthorized)]
        public async Task Rebuild_When_The_Api_Server_Fails_Should_Return_502_And_Not_Reset_The_Cache(HttpStatusCode upstream)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(RebuildPath, upstream, EntityScene.ApiError("Rebuild failed"));

            var response = await scene.Owner.PostAsync(scene.RebuildUrl, null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.UpstreamError, error.Code);
            Assert.Equal("Rebuild failed", error.Message);

            Assert.Single(_portal.ApiServer.Requests);
            await AssertUnchangedAsync(scene, expectedRequests: 1);
        }

        [Fact]
        public async Task Rebuild_When_The_Api_Server_Cannot_Be_Reached_Should_Return_502()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Unreachable(RebuildPath);

            var response = await scene.Owner.PostAsync(scene.RebuildUrl, null);

            await EntityScene.AssertErrorAsync(response, HttpStatusCode.BadGateway, PortalErrorCode.UpstreamError);
            await AssertUnchangedAsync(scene, expectedRequests: 1);
        }

        [Fact]
        public async Task Rebuild_When_The_Api_Server_Refuses_Should_Return_400_With_Its_Message()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(RebuildPath, HttpStatusCode.BadRequest, EntityScene.ApiError("Database already exists!"));

            var response = await scene.Owner.PostAsync(scene.RebuildUrl, null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal("Database already exists!", error.Message);
            await AssertUnchangedAsync(scene, expectedRequests: 1);
        }

        [Fact]
        public async Task Rebuild_When_The_Cache_Reset_Fails_Should_Answer_204_With_A_Warning()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.InternalServerError, EntityScene.ApiError("Down"));

            var response = await scene.Owner.PostAsync(scene.RebuildUrl, null);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            EntityScene.AssertWarning(response);
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_Should_Degenerate_Then_Remove_The_Application_And_Everything_It_Owns()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var appId = scene.AppId;
            var entityIds = await _portal.WithDbContextAsync(db => db.Entities.Where(x => x.AppID == appId).Select(x => x.ID).ToListAsync());
            Assert.NotEmpty(entityIds);

            var response = await scene.Owner.DeleteAsync(scene.AppUrl);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            // No cache reset afterwards: the application is gone.
            var request = Assert.Single(_portal.ApiServer.Requests);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"{scene.Server.ServerUrl}/api/Application/Degenerate", request.Url);
            await AssertPortalHeadersAsync(request, scene.Token, scene.OwnerEmail);

            var left = await _portal.WithDbContextAsync(async db => new[]
            {
                await db.Applications.CountAsync(x => x.ID == appId),
                await db.Entities.CountAsync(x => x.AppID == appId),
                await db.EntityProperties.CountAsync(x => entityIds.Contains(x.EntityID)),
                await db.CustomEndpoints.CountAsync(x => x.AppID == appId),
                await db.Collaborations.CountAsync(x => x.AppID == appId),
                await db.Reports.CountAsync(x => x.AppID == appId),
                await db.ReportSeries.CountAsync(x => x.Panel.AppID == appId)
            });

            Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0 }, left);

            // The server stays.
            var serverId = scene.Server.ID;
            Assert.True(await _portal.WithDbContextAsync(db => db.Servers.AnyAsync(x => x.ID == serverId)));
        }

        [Fact]
        public async Task Delete_Should_Remove_The_Application_From_The_Lists_Of_The_Owner_And_The_Collaborator()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            Assert.Contains(scene.Token, await ListTokensAsync(scene.Owner));
            Assert.Contains(scene.Token, await ListTokensAsync(scene.Collaborator));

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.AppUrl)).StatusCode);

            Assert.DoesNotContain(scene.Token, await ListTokensAsync(scene.Owner));
            Assert.DoesNotContain(scene.Token, await ListTokensAsync(scene.Collaborator));
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(scene.AppUrl), "Application");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.DeleteAsync(scene.AppUrl), "Application");
        }

        [Fact]
        public async Task Delete_Should_Write_A_Deleted_Audit_Row_For_Everything_The_Application_Owned()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync(scene.AppUrl)).StatusCode);

            var audit = await AuditRowsAsync(scene.OwnerEmail);

            // The application, its collaborator, its custom endpoint, its entities and their
            // properties; not the report, which the database removes on its own.
            var expected = new[]
            {
                $"Application | {scene.Name}",
                $"Collaboration | {scene.CollaboratorEmail}",
                "Custom Endpoint | Totals",
                "Entity | Users", "Property | ID", "Property | Email", "Property | Nickname",
                "Entity | Files", "Property | ID",
                "Entity | Customers", "Property | ID", "Property | Name",
                "Entity | Orders", "Property | ID", "Property | Owner", "Property | Created", "Property | Customer_ID", "Property | Agent_ID",
                "Property | Amount", "Property | Code", "Property | Secret", "Property | Paid",
                "Entity | Invoices", "Property | ID"
            };

            Assert.Equal(
                expected.OrderBy(x => x, StringComparer.Ordinal),
                audit.Select(x => $"{x.EntityType} | {x.EntityIdentifier}").OrderBy(x => x, StringComparer.Ordinal));
            Assert.All(audit, x => Assert.Equal("Deleted", x.Action));

            // A property row gets its application from its entity, which is being deleted too: it carries none.
            Assert.All(audit.Where(x => x.EntityType != "Property"), x => Assert.Equal(scene.AppId, x.AppID));
            Assert.All(audit.Where(x => x.EntityType == "Property"), x => Assert.Null(x.AppID));
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.BadRequest)]
        public async Task Delete_When_Degenerate_Fails_Should_Leave_The_Application_In_Place(HttpStatusCode upstream, HttpStatusCode expected)
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(DegeneratePath, upstream, EntityScene.ApiError("Could not drop the database"));

            var response = await scene.Owner.DeleteAsync(scene.AppUrl);

            Assert.Equal(expected, response.StatusCode);
            Assert.Equal("Could not drop the database", (await response.ReadJsonAsync<ErrorResponse>()).Message);

            Assert.Single(_portal.ApiServer.Requests);
            await AssertUnchangedAsync(scene, expectedRequests: 1);
            Assert.Contains(scene.Token, await ListTokensAsync(scene.Owner));
            Assert.Contains(scene.Token, await ListTokensAsync(scene.Collaborator));
        }

        [Fact]
        public async Task Delete_When_The_Api_Server_Cannot_Be_Reached_Should_Return_502_And_Keep_The_Application()
        {
            var scene = await CreateSceneAsync();
            _portal.ApiServer.Unreachable(DegeneratePath);

            var response = await scene.Owner.DeleteAsync(scene.AppUrl);

            await EntityScene.AssertErrorAsync(response, HttpStatusCode.BadGateway, PortalErrorCode.UpstreamError);
            await AssertUnchangedAsync(scene, expectedRequests: 1);
        }

        [Fact]
        public async Task Delete_When_The_Portal_Cannot_Remove_After_Degenerate_Should_Return_500_That_Says_So()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            // The Portal database refuses to remove this one application; the API server has already dropped it.
            // DDL takes no parameters; the only value in it is the numeric ID.
            var trigger = $"block_delete_{scene.AppId}";
            var create = $"CREATE TRIGGER {trigger} BEFORE DELETE ON Applications WHEN OLD.ID = {scene.AppId} BEGIN SELECT RAISE(ABORT, 'blocked'); END";
            var drop = $"DROP TRIGGER IF EXISTS {trigger}";
            await _portal.WithDbContextAsync(db => db.Database.ExecuteSqlRawAsync(create));

            try
            {
                var response = await scene.Owner.DeleteAsync(scene.AppUrl);

                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Error, error.Code);
                Assert.Equal("The application was removed from the API server but could not be removed from the Portal.", error.Message);
                Assert.Single(_portal.ApiServer.RequestsTo(DegeneratePath));

                // Row and contents still there, no audit rows, no cache reset (one request in total).
                await AssertUnchangedAsync(scene, expectedRequests: 1);
            }
            finally
            {
                await _portal.WithDbContextAsync(db => db.Database.ExecuteSqlRawAsync(drop));
            }
        }

        // ---------- Who may ----------

        [Fact]
        public async Task Collaborator_Should_Edit_Set_Status_Rebuild_And_Delete_Like_The_Owner()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var updated = await scene.Collaborator.PutAsync(scene.AppUrl, Body("Edited by collaborator", "Server=collab;Database=app").ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

            var application = await updated.ReadJsonAsync<ApplicationResponse>();
            Assert.False(application.IsOwner);
            Assert.Null(application.CollaboratorCount);

            var status = await scene.Collaborator.PutAsync(scene.StatusUrl, new { Online = false }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            Assert.False((await status.ReadJsonAsync<ApplicationResponse>()).IsOwner);

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Collaborator.PostAsync(scene.RebuildUrl, null)).StatusCode);

            var stored = await LoadAsync(scene.AppId);
            Assert.Equal("Edited by collaborator", stored.Name);
            Assert.Equal("Server=collab;Database=app", stored.ConnectionString);
            Assert.False(stored.Online);

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Collaborator.DeleteAsync(scene.AppUrl)).StatusCode);
            Assert.Null(await LoadOrNullAsync(scene.AppId));

            // Every call was made with the collaborator's own API token.
            foreach (var request in _portal.ApiServer.Requests)
            {
                await AssertPortalHeadersAsync(request, scene.Token, scene.CollaboratorEmail);
            }

            Assert.Equal(
                new[] { "ClearCache", "ClearCache", "Rebuild", "ClearCache", "Degenerate" },
                _portal.ApiServer.Requests.Select(x => x.Path.Split('/').Last()));
        }

        [Fact]
        public async Task Stranger_Admin_And_Unknown_Token_Should_Get_404_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            var admin = await _portal.CreateAdminClientAsync();

            foreach (var client in new[] { scene.Stranger, admin })
            {
                foreach (var send in AllWrites(scene.Token))
                {
                    await EntityScene.AssertNotFoundAsync(await send(client), "Application");
                }
            }

            foreach (var send in AllWrites(Guid.NewGuid().ToString()))
            {
                await EntityScene.AssertNotFoundAsync(await send(scene.Owner), "Application");
            }

            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Anonymous_Should_Get_401_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            var anonymous = _portal.CreateAnonymousClient();

            foreach (var send in AllWrites(scene.Token))
            {
                await EntityScene.AssertErrorAsync(await send(anonymous), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            }

            await AssertUnchangedAsync(scene);
        }

        [Fact]
        public async Task Writes_Without_The_Csrf_Header_Should_Get_403_And_Change_Nothing()
        {
            var scene = await CreateSceneAsync();
            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            foreach (var send in AllWrites(scene.Token))
            {
                var response = await send(scene.Owner);

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Forbidden, error.Code);
                Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
            }

            await AssertUnchangedAsync(scene);
        }

        // ---------- Secrets ----------

        [Fact]
        public async Task No_Response_Should_Carry_The_Connection_String_Or_The_Encryption_Key()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();
            const string newConnectionString = "Server=brand-new-secret-host;Database=app";

            var texts = new List<string>
            {
                await (await scene.Owner.GetAsync(scene.AppUrl)).Content.ReadAsStringAsync(),
                await (await scene.Owner.GetAsync(ListUrl)).Content.ReadAsStringAsync(),
                await (await scene.Collaborator.GetAsync(ListUrl)).Content.ReadAsStringAsync(),
                await (await scene.Owner.PutAsync(scene.AppUrl, new { Name = "Secret keeper" }.ToJsonContent())).Content.ReadAsStringAsync(),
                await (await scene.Owner.PutAsync(scene.StatusUrl, new { Online = false }.ToJsonContent())).Content.ReadAsStringAsync()
            };

            // The new value is checked in the answer that stored it, and in the reads after it.
            var replaced = await scene.Owner.PutAsync(scene.AppUrl, Body("Secret keeper", newConnectionString).ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            texts.Add(await replaced.Content.ReadAsStringAsync());
            texts.Add(await (await scene.Owner.GetAsync(scene.AppUrl)).Content.ReadAsStringAsync());
            texts.Add(await (await scene.Collaborator.GetAsync(scene.AppUrl)).Content.ReadAsStringAsync());

            var encryptedKey = (await LoadAsync(scene.AppId)).EncryptionKey;

            Assert.All(texts, text =>
            {
                Assert.Contains("\"HasConnectionString\":true", text);
                Assert.DoesNotContain(scene.ConnectionString, text);
                Assert.DoesNotContain(newConnectionString, text);
                Assert.DoesNotContain("brand-new-secret-host", text);
                Assert.DoesNotContain("\"ConnectionString\"", text);
                Assert.DoesNotContain(scene.EncryptionKey, text);
                Assert.DoesNotContain(encryptedKey, text);
            });
        }

        // ---------- Contract ----------

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Warning_Header_Of_Every_Settings_Write()
        {
            var client = await _portal.CreateUserClientAsync();
            var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json")) ?? throw new InvalidOperationException("No document.");

            const string app = "/api/v1/applications/{appToken}";

            Assert.NotNull(document["paths"]?[app]?["put"]?["responses"]?["200"]?["headers"]?["Warning"]);
            Assert.NotNull(document["paths"]?[$"{app}/status"]?["put"]?["responses"]?["200"]?["headers"]?["Warning"]);
            Assert.NotNull(document["paths"]?[$"{app}/rebuild"]?["post"]?["responses"]?["204"]?["headers"]?["Warning"]);

            // A delete resets no cache: the application is gone from the API server.
            Assert.Null(document["paths"]?[app]?["delete"]?["responses"]?["204"]?["headers"]);
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
            string Name,
            string ConnectionString,
            string EncryptionKey)
        {
            public string AppUrl => $"{ListUrl}/{Token}";

            public string StatusUrl => $"{AppUrl}/status";

            public string RebuildUrl => $"{AppUrl}/rebuild";
        }

        /// <summary>
        /// Three new users and an online SQL Server application of the owner, shared with the
        /// collaborator, with entities and properties, a custom endpoint and a report.
        /// </summary>
        private async Task<Scene> CreateSceneAsync()
        {
            var (ownerEmail, ownerPassword) = await _portal.CreateUserAsync();
            var (collaboratorEmail, collaboratorPassword) = await _portal.CreateUserAsync();
            var (strangerEmail, strangerPassword) = await _portal.CreateUserAsync();

            var server = await _portal.CreateServerAsync();
            var name = $"settings-{Guid.NewGuid():N}";
            var seeded = await _portal.CreateApplicationAsync(server.ID, ownerEmail, name, collaboratorEmail);

            await AddContentsAsync(seeded.Application.ID);

            return new Scene(
                ownerEmail,
                await _portal.CreateSignedInClientAsync(ownerEmail, ownerPassword),
                collaboratorEmail,
                await _portal.CreateSignedInClientAsync(collaboratorEmail, collaboratorPassword),
                await _portal.CreateSignedInClientAsync(strangerEmail, strangerPassword),
                server,
                seeded.Application.Token,
                seeded.Application.ID,
                name,
                seeded.Application.ConnectionString ?? throw new InvalidOperationException("No connection string."),
                seeded.EncryptionKey);
        }

        private Task<int> AddContentsAsync(long appId)
        {
            return _portal.WithDbContextAsync(db =>
            {
                db.Entities.AddRange(EntityScene.SeedEntities(appId));
                db.CustomEndpoints.Add(new DBWS_CustomEndpoint { AppID = appId, Name = "Totals", Query = "select 1", DateModified = DateTime.UtcNow });
                db.Reports.Add(new DBWS_ReportPanel
                {
                    AppID = appId,
                    Title = "Orders per day",
                    MaxRecords = 10,
                    W = 6,
                    H = 4,
                    DateModified = DateTime.UtcNow,
                    Series = new List<DBWS_ReportSeries>
                    {
                        new DBWS_ReportSeries { Label = "Orders", Entity = "Orders", GroupBy = "Created", Property = "ID", DateModified = DateTime.UtcNow }
                    }
                });

                return db.SaveChangesAsync();
            });
        }

        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
            _portal.ApiServer.Respond(RebuildPath, HttpStatusCode.OK, string.Empty);
            _portal.ApiServer.Respond(DegeneratePath, HttpStatusCode.OK, string.Empty);
        }

        private static Dictionary<string, object?> Body(string name, string? connectionString)
        {
            return new Dictionary<string, object?> { ["Name"] = name, ["ConnectionString"] = connectionString };
        }

        /// <summary>
        /// The four writes of this slice, each with a valid body.
        /// </summary>
        private static List<Func<HttpClient, Task<HttpResponseMessage>>> AllWrites(string token)
        {
            var appUrl = $"{ListUrl}/{token}";

            return new List<Func<HttpClient, Task<HttpResponseMessage>>>
            {
                client => client.PutAsync(appUrl, Body("Not allowed", "Server=nope;Database=app").ToJsonContent()),
                client => client.PutAsync($"{appUrl}/status", new { Online = false }.ToJsonContent()),
                client => client.PostAsync($"{appUrl}/rebuild", null),
                client => client.DeleteAsync(appUrl)
            };
        }

        private async Task<DBWS_Application?> LoadOrNullAsync(long appId)
        {
            return await _portal.WithDbContextAsync(db => db.Applications.AsNoTracking().FirstOrDefaultAsync(x => x.ID == appId));
        }

        private async Task<DBWS_Application> LoadAsync(long appId)
        {
            return await LoadOrNullAsync(appId) ?? throw new InvalidOperationException($"Application {appId} is gone.");
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
        /// The application is as the scene seeded it, with all its contents, nobody caused an audit
        /// row and the API server got only the expected number of requests.
        /// </summary>
        private async Task AssertUnchangedAsync(Scene scene, int expectedRequests = 0)
        {
            var stored = await LoadAsync(scene.AppId);
            Assert.Equal(scene.Name, stored.Name);
            Assert.Equal(scene.ConnectionString, stored.ConnectionString);
            Assert.Equal((int)DatabaseType.SQLServer, stored.DatabaseType);
            Assert.True(stored.Online);

            var appId = scene.AppId;
            var counts = await _portal.WithDbContextAsync(async db => new[]
            {
                await db.Entities.CountAsync(x => x.AppID == appId),
                await db.CustomEndpoints.CountAsync(x => x.AppID == appId),
                await db.Collaborations.CountAsync(x => x.AppID == appId),
                await db.Reports.CountAsync(x => x.AppID == appId)
            });

            Assert.Equal(new[] { 5, 1, 1, 1 }, counts);
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.AppID == appId)));
            Assert.Equal(expectedRequests, _portal.ApiServer.Requests.Count);
        }

        private async Task<List<string>> ListTokensAsync(HttpClient client)
        {
            var list = await (await client.GetAsync(ListUrl)).ReadJsonAsync<ListResponse<ApplicationResponse>>();

            return list.Data.Select(x => x.Token).ToList();
        }

        private Task<List<PortalAuditLog>> AuditRowsAsync(string userEmail)
        {
            return _portal.WithDbContextAsync(db => db.AuditLogs
                .AsNoTracking()
                .Where(x => x.UserEmail == userEmail)
                .OrderBy(x => x.ID)
                .ToListAsync());
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
