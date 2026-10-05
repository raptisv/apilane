using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    /// POST /applications/{appToken}/clones and GET /applications/{appToken}/clones/{operationId}.
    /// A clone runs in the background and calls the fake API server, which all test classes share:
    /// every test that starts one waits for its end before it finishes.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class ApplicationCloneApiTests
    {
        private const string DataPath = "/api/data/get";
        private const string ImportDataPath = "/api/Application/ImportData";
        private const string ApiServerRefusal = "Cannot open database requested by the login.";

        private readonly PortalFactory _portal;

        public ApplicationCloneApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- Start ----------

        [Fact]
        public async Task Start_Should_Return_202_And_Save_The_Clone_As_An_Application_Of_The_Caller()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var response = await scene.Owner.PostAsync(ClonesUrl(scene.Token), Body(target.ID, "SQLLite", "Server=ignored").ToJsonContent());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var started = await response.ReadJsonAsync<CloneStartedResponse>();
            Assert.Matches("^[0-9a-f]{32}$", started.OperationId);
            Assert.True(Guid.TryParse(started.ClonedApplicationToken, out _));
            Assert.NotEqual(scene.Token, started.ClonedApplicationToken);
            Assert.Equal($"{ClonesUrl(scene.Token)}/{started.OperationId}", response.Headers.Location?.ToString());

            // The Location is a real address.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync(response.Headers.Location)).StatusCode);

            await WaitForEndAsync(started.OperationId);

            var source = await LoadAsync(scene.Token);
            var clone = await LoadAsync(started.ClonedApplicationToken);
            var owner = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Email == scene.OwnerEmail));

            Assert.Equal(source.Name + " - Clone", clone.Name);
            Assert.Equal(owner.Id, clone.UserID);
            Assert.Equal(scene.OwnerEmail, clone.AdminEmail);
            Assert.Equal(target.ID, clone.ServerID);
            Assert.Equal((int)DatabaseType.SQLLite, clone.DatabaseType);
            Assert.Null(clone.ConnectionString);

            // What the application itself holds is copied, the encryption key included.
            Assert.Equal(source.EncryptionKey, clone.EncryptionKey);
            Assert.Equal(source.Online, clone.Online);
            Assert.Equal("[]", clone.Security);
            Assert.Equal(45, clone.AuthTokenExpireMinutes);
            Assert.Equal("source-mail-password", clone.MailPassword);

            // Entities, properties and custom endpoints are copied as new rows, in the same order.
            Assert.NotEqual(source.ID, clone.ID);
            Assert.Equal(new[] { "Users", "Files", "Customers", "Orders", "Invoices" }, clone.Entities.Select(x => x.Name));
            Assert.Equal(
                source.Entities.Select(x => $"{x.Name} | {x.IsSystem} | {x.EntConstraints} | {string.Join(",", x.Properties.Select(p => $"{p.Name}:{p.TypeID}:{p.Encrypted}"))}"),
                clone.Entities.Select(x => $"{x.Name} | {x.IsSystem} | {x.EntConstraints} | {string.Join(",", x.Properties.Select(p => $"{p.Name}:{p.TypeID}:{p.Encrypted}"))}"));
            Assert.Empty(source.Entities.Select(x => x.ID).Intersect(clone.Entities.Select(x => x.ID)));
            Assert.All(clone.Entities, x => Assert.Equal(clone.ID, x.AppID));
            Assert.Equal("GetOrders", Assert.Single(clone.CustomEndpoints).Name);
            Assert.NotEqual(source.CustomEndpoints[0].ID, clone.CustomEndpoints[0].ID);

            // Reports and collaborators are not.
            Assert.Empty(clone.Reports);
            Assert.Empty(clone.Collaborates);

            // The source is as it was.
            Assert.Single(source.Reports);
            Assert.Single(source.Collaborates);
            Assert.Single(source.CustomEndpoints);
            Assert.Equal(5, source.Entities.Count);

            // The clone is an application like any other from now on.
            var listed = await scene.Owner.GetAsync($"/api/v1/applications/{started.ClonedApplicationToken}");
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

            var application = await listed.ReadJsonAsync<ApplicationResponse>();
            Assert.True(application.IsOwner);
            Assert.Equal("SQLLite", application.DatabaseType);
            Assert.False(application.HasConnectionString);
            Assert.Equal(target.ID, application.Server.ID);
        }

        [Fact]
        public async Task Start_Should_Run_The_Clone_Routine_On_The_Target_Server_As_The_Caller()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID));
            await WaitForEndAsync(started.OperationId);

            var bearer = $"Bearer {await StoredTokenAsync(scene.OwnerEmail)}";
            var installationKey = await _portal.StoredInstallationKeyAsync();
            var requests = _portal.ApiServer.Requests;

            // The application with its system entities first, then one call per other entity.
            // Nothing is read from the source and its cache is not reset: nothing of it changed.
            Assert.Equal(
                new[]
                {
                    $"POST {target.ServerUrl}/api/ApplicationNew/Generate",
                    $"POST {target.ServerUrl}/api/Application/GenerateEntity",
                    $"POST {target.ServerUrl}/api/Application/GenerateEntity",
                    $"POST {target.ServerUrl}/api/Application/GenerateEntity"
                },
                requests.Select(x => $"{x.Method} {x.Url}"));

            Assert.All(requests, x =>
            {
                Assert.Equal(started.ClonedApplicationToken, x.Headers["x-application-token"]);
                Assert.Equal("portal", x.Headers["x-client-id"]);
                Assert.Equal(bearer, x.Headers["Authorization"]);
            });

            Assert.Equal(installationKey, requests[0].Headers["x-installation-key"]);
            Assert.All(requests.Skip(1), x => Assert.False(x.Headers.ContainsKey("x-installation-key")));

            var generated = JsonSerializer.Deserialize<DBWS_Application>(requests[0].Body) ?? throw new InvalidOperationException("No Generate body.");
            Assert.Equal(started.ClonedApplicationToken, generated.Token);
            Assert.Equal((int)DatabaseType.SQLLite, generated.DatabaseType);
            Assert.Equal(new[] { "Users", "Files" }, generated.Entities.Select(x => x.Name));

            Assert.Equal(
                new[] { "Customers", "Invoices", "Orders" },
                requests.Skip(1).Select(x => JsonNode.Parse(x.Body)?["Name"]?.GetValue<string>()).OrderBy(x => x));

            // The audit rows of a new application with its entities, properties and custom endpoint.
            var audit = await scene.AuditRowsAsync(scene.OwnerEmail);

            Assert.All(audit, x => Assert.Equal("Created", x.Action));
            Assert.Single(audit, x => x.EntityType == "Application" && x.EntityIdentifier.EndsWith(" - Clone", StringComparison.Ordinal));
            Assert.Equal(5, audit.Count(x => x.EntityType == "Entity"));
            Assert.Single(audit, x => x.EntityType == "Custom Endpoint" && x.EntityIdentifier == "GetOrders");
            Assert.DoesNotContain(audit, x => x.EntityType == "Report");
            Assert.DoesNotContain(audit, x => (x.Changes ?? string.Empty).Contains("source-mail-password"));
        }

        [Fact]
        public async Task Start_With_CloneData_Should_Copy_The_Records_Of_The_Named_Entities_Only_And_Never_Files()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID, cloneData: true, entities: new[] { "Orders", "Files" }));
            await WaitForEndAsync(started.OperationId);

            // Read from the source, twice (to count, then to copy), as the source application.
            var reads = _portal.ApiServer.RequestsTo(DataPath);
            Assert.Equal(2, reads.Count);
            Assert.All(reads, x =>
            {
                Assert.Equal($"{scene.ServerUrl}/api/data/get?entity=Orders&pageIndex=1&pageSize=1000&getTotal=true", x.Url);
                Assert.Equal(scene.Token, x.Headers["x-application-token"]);
            });

            // Written to the clone.
            var import = Assert.Single(_portal.ApiServer.RequestsTo(ImportDataPath));
            Assert.Equal($"{target.ServerUrl}/api/Application/ImportData?Entity=Orders", import.Url);
            Assert.Equal(started.ClonedApplicationToken, import.Headers["x-application-token"]);
            Assert.Equal(2, JsonNode.Parse(import.Body)?.AsArray().Count);

            // Every entity is created, whatever is copied.
            Assert.Equal(3, _portal.ApiServer.RequestsTo(FakeApiServer.GenerateEntityPath).Count);

            var operation = await GetOperationAsync(scene.Owner, scene.Token, started.OperationId);
            Assert.Equal("Completed", operation.Status);
            Assert.Equal(1, operation.TotalEntitiesToCloneData);
            Assert.Equal(1, operation.EntitiesDataCloned);
            Assert.Equal(2, operation.TotalRecordsAllEntities);
            Assert.Equal(2, operation.TotalRecordsImported);
        }

        [Fact]
        public async Task Start_With_CloneData_Should_Make_Its_Calls_In_Order_And_Audit_Everything_It_Creates()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID, "MySQL", "Server=db;Database=clone", cloneData: true, entities: new[] { "Orders", "Customers" }));
            await WaitForEndAsync(started.OperationId);

            var requests = _portal.ApiServer.Requests;

            // The application with its system entities, one call per other entity, the count of
            // every entity to copy, then each entity read and written in turn.
            Assert.Equal(
                new[]
                {
                    $"POST {target.ServerUrl}/api/ApplicationNew/Generate",
                    $"POST {target.ServerUrl}/api/Application/GenerateEntity",
                    $"POST {target.ServerUrl}/api/Application/GenerateEntity",
                    $"POST {target.ServerUrl}/api/Application/GenerateEntity",
                    $"GET {scene.ServerUrl}/api/data/get?entity=Customers&pageIndex=1&pageSize=1000&getTotal=true",
                    $"GET {scene.ServerUrl}/api/data/get?entity=Orders&pageIndex=1&pageSize=1000&getTotal=true",
                    $"GET {scene.ServerUrl}/api/data/get?entity=Customers&pageIndex=1&pageSize=1000&getTotal=true",
                    $"POST {target.ServerUrl}/api/Application/ImportData?Entity=Customers",
                    $"GET {scene.ServerUrl}/api/data/get?entity=Orders&pageIndex=1&pageSize=1000&getTotal=true",
                    $"POST {target.ServerUrl}/api/Application/ImportData?Entity=Orders"
                },
                requests.Select(x => $"{x.Method} {x.Url}"));

            // No header but these; the installation key goes with Generate only.
            Assert.Equal(
                new[] { "Accept", "Authorization", "x-application-token", "x-client-id", "x-installation-key" },
                requests[0].Headers.Keys.OrderBy(x => x));
            Assert.All(requests.Skip(1), x => Assert.Equal(
                new[] { "Accept", "Authorization", "x-application-token", "x-client-id" },
                x.Headers.Keys.OrderBy(k => k)));

            // The clone starts without reports and collaborators: Generate gets both lists, empty.
            var generated = JsonNode.Parse(requests[0].Body)?.AsObject() ?? throw new InvalidOperationException("No Generate body.");
            Assert.Empty(generated["Reports"]?.AsArray() ?? throw new InvalidOperationException("No Reports."));
            Assert.Empty(generated["Collaborates"]?.AsArray() ?? throw new InvalidOperationException("No Collaborates."));

            // Two records read and two written for each entity.
            Assert.All(_portal.ApiServer.RequestsTo(ImportDataPath), x => Assert.Equal(2, JsonNode.Parse(x.Body)?.AsArray().Count));

            // One audit row for the application, its custom endpoint, each of its 5 entities and
            // each of their 16 properties, all caused by the caller. None carries an application.
            var owner = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Email == scene.OwnerEmail));
            var audit = await scene.AuditRowsAsync(scene.OwnerEmail);

            Assert.Equal(
                new[] { "Application 1", "Custom Endpoint 1", "Entity 5", "Property 16" },
                audit.GroupBy(x => x.EntityType).Select(x => $"{x.Key} {x.Count()}").OrderBy(x => x));
            Assert.All(audit, x => Assert.Equal($"Created | {owner.Id} | ", $"{x.Action} | {x.UserId} | {x.AppID}"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Start_With_CloneData_And_No_Entities_Should_Copy_Every_Entity_Except_Files(bool emptyList)
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            // No list and an empty list mean the same: every entity.
            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID, cloneData: true, entities: emptyList ? Array.Empty<string>() : null));
            await WaitForEndAsync(started.OperationId);

            Assert.Equal(
                new[] { "Customers", "Invoices", "Orders", "Users" },
                _portal.ApiServer.RequestsTo(ImportDataPath).Select(x => x.Url.Split("?Entity=")[1]).OrderBy(x => x));
        }

        [Fact]
        public async Task Start_Without_CloneData_Should_Copy_No_Records()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            // The list is ignored, a name the application does not have included.
            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID, cloneData: false, entities: new[] { "Orders", "Nope" }));
            await WaitForEndAsync(started.OperationId);

            Assert.Empty(_portal.ApiServer.RequestsTo(DataPath));
            Assert.Empty(_portal.ApiServer.RequestsTo(ImportDataPath));
        }

        [Theory]
        [InlineData("SQLServer", DatabaseType.SQLServer)]
        [InlineData("MySQL", DatabaseType.MySQL)]
        [InlineData("PostgreSQL", DatabaseType.PostgreSQL)]
        public async Task Start_Other_Database_Should_Store_The_Connection_String_And_Not_Return_It(string databaseType, DatabaseType expected)
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var connectionString = $"Server=db-{Guid.NewGuid():N};Database=clone";
            var response = await scene.Owner.PostAsync(ClonesUrl(scene.Token), Body(target.ID, databaseType, connectionString).ToJsonContent());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.DoesNotContain(connectionString, await response.Content.ReadAsStringAsync());

            var started = await response.ReadJsonAsync<CloneStartedResponse>();
            await WaitForEndAsync(started.OperationId);

            var clone = await LoadAsync(started.ClonedApplicationToken);
            Assert.Equal((int)expected, clone.DatabaseType);
            Assert.Equal(connectionString, clone.ConnectionString);

            var status = await scene.Owner.GetAsync($"{ClonesUrl(scene.Token)}/{started.OperationId}");
            Assert.DoesNotContain(connectionString, await status.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Start_On_The_Server_Of_The_Source_Should_Work()
        {
            var scene = await CreateSceneAsync();
            ScriptApiServer();

            var sourceServerId = (await LoadAsync(scene.Token)).ServerID;
            var started = await StartAsync(scene.Owner, scene.Token, Body(sourceServerId));
            await WaitForEndAsync(started.OperationId);

            Assert.Equal(sourceServerId, (await LoadAsync(started.ClonedApplicationToken)).ServerID);
            Assert.All(_portal.ApiServer.Requests, x => Assert.StartsWith(scene.ServerUrl, x.Url));
        }

        // ---------- Start: rules ----------

        [Theory]
        [InlineData("ServerID", null, "Required")]
        [InlineData("DatabaseType", null, "Required")]
        [InlineData("DatabaseType", "Oracle", null)]
        [InlineData("DatabaseType", "sqlserver", null)]
        [InlineData("DatabaseType", "2", null)]
        [InlineData("CloneData", null, "Required")]
        [InlineData("ConnectionString", null, "Required")]
        [InlineData("ConnectionString", "", "Required")]
        [InlineData("ConnectionString", "   ", "Required")]
        public async Task Start_Invalid_Value_Should_Return_400_On_That_Property_And_Start_Nothing(string property, string? value, string? message)
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();

            var body = Body(target.ID, "SQLServer", "Server=db;Database=clone", cloneData: true);
            body[property] = value;

            var response = await scene.Owner.PostAsync(ClonesUrl(scene.Token), body.ToJsonContent());

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

            await AssertNothingStartedAsync(scene);
        }

        [Fact]
        public async Task Start_Without_ServerID_And_CloneData_Should_Return_400_On_Both()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PostAsync(ClonesUrl(scene.Token), new { DatabaseType = "SQLLite" }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(
                new[] { "CloneData: Required", "ServerID: Required" },
                ((await response.ReadJsonAsync<ErrorResponse>()).Errors ?? new List<ErrorDetail>()).Select(x => $"{x.Property}: {x.Message}").OrderBy(x => x));

            await AssertNothingStartedAsync(scene);
        }

        [Fact]
        public async Task Start_With_An_Entity_The_Application_Does_Not_Have_Should_Return_400_Naming_Each()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();

            // Names are case-sensitive, as everywhere.
            var body = Body(target.ID, cloneData: true, entities: new[] { "Orders", "Nope", "Files", "orders" });

            var response = await scene.Owner.PostAsync(ClonesUrl(scene.Token), body.ToJsonContent());

            await EntityScene.AssertValidationAsync(
                response,
                "Entities[1]: Entity 'Nope' does not exist",
                "Entities[3]: Entity 'orders' does not exist");

            await AssertNothingStartedAsync(scene);
        }

        [Fact]
        public async Task Start_Unknown_Server_Should_Return_404_And_Start_Nothing()
        {
            var scene = await CreateSceneAsync();

            var response = await scene.Owner.PostAsync(ClonesUrl(scene.Token), Body(long.MaxValue).ToJsonContent());

            await EntityScene.AssertNotFoundAsync(response, "Server");
            await AssertNothingStartedAsync(scene);
        }

        [Fact]
        public async Task Start_Without_The_Csrf_Header_Should_Return_403_And_Start_Nothing()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var response = await scene.Owner.PostAsync(ClonesUrl(scene.Token), Body(target.ID).ToJsonContent());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Forbidden, error.Code);
            Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);

            await AssertNothingStartedAsync(scene);
        }

        [Fact]
        public async Task Start_And_Status_Anonymous_Should_Return_401()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            var anonymous = _portal.CreateAnonymousClient();

            var start = await anonymous.PostAsync(ClonesUrl(scene.Token), Body(target.ID).ToJsonContent());
            var status = await anonymous.GetAsync($"{ClonesUrl(scene.Token)}/{Guid.NewGuid():N}");

            await EntityScene.AssertErrorAsync(start, HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            await EntityScene.AssertErrorAsync(status, HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            await AssertNothingStartedAsync(scene);
        }

        [Fact]
        public async Task Start_And_Status_For_A_Stranger_An_Admin_And_An_Unknown_Token_Should_Return_404_On_The_Application()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID));
            await WaitForEndAsync(started.OperationId);

            var requests = _portal.ApiServer.Requests.Count;

            var start = await scene.Stranger.PostAsync(ClonesUrl(scene.Token), Body(target.ID).ToJsonContent());
            var status = await scene.Stranger.GetAsync($"{ClonesUrl(scene.Token)}/{started.OperationId}");

            await EntityScene.AssertNotFoundAsync(start, "Application");
            await EntityScene.AssertNotFoundAsync(status, "Application");

            // The Admin role gives no access to the applications of others.
            var admin = await _portal.CreateAdminClientAsync();

            await EntityScene.AssertNotFoundAsync(await admin.PostAsync(ClonesUrl(scene.Token), Body(target.ID).ToJsonContent()), "Application");
            await EntityScene.AssertNotFoundAsync(await admin.GetAsync($"{ClonesUrl(scene.Token)}/{started.OperationId}"), "Application");

            // A token that does not exist, even with an operation of the caller.
            var unknown = ClonesUrl(Guid.NewGuid().ToString());

            await EntityScene.AssertNotFoundAsync(await scene.Owner.PostAsync(unknown, Body(target.ID).ToJsonContent()), "Application");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"{unknown}/{started.OperationId}"), "Application");

            // One clone: the owner's.
            Assert.Equal(requests, _portal.ApiServer.Requests.Count);
            Assert.Equal(1, await CountClonesAsync(scene));
        }

        [Fact]
        public async Task Collaborator_Should_Clone_And_Own_The_Clone()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var started = await StartAsync(scene.Collaborator, scene.Token, Body(target.ID));
            await WaitForEndAsync(started.OperationId);

            var collaborator = await _portal.WithDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(x => x.Email == scene.CollaboratorEmail));
            var clone = await LoadAsync(started.ClonedApplicationToken);

            Assert.Equal(collaborator.Id, clone.UserID);
            Assert.Equal(scene.CollaboratorEmail, clone.AdminEmail);
            Assert.Equal($"Bearer {collaborator.AdminAuthToken}", _portal.ApiServer.Requests[0].Headers["Authorization"]);

            var cloneUrl = $"/api/v1/applications/{started.ClonedApplicationToken}";
            Assert.True((await (await scene.Collaborator.GetAsync(cloneUrl)).ReadJsonAsync<ApplicationResponse>()).IsOwner);

            // The owner of the source has no part in the clone.
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(cloneUrl), "Application");

            Assert.Equal("Completed", (await GetOperationAsync(scene.Collaborator, scene.Token, started.OperationId)).Status);
        }

        // ---------- Status ----------

        [Fact]
        public async Task Status_Of_A_Completed_Operation_Should_Return_Its_Counters()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var before = DateTime.UtcNow;
            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID, cloneData: true));
            await WaitForEndAsync(started.OperationId);

            var operation = await GetOperationAsync(scene.Owner, scene.Token, started.OperationId);

            Assert.Equal(started.OperationId, operation.OperationId);
            Assert.Equal("Completed", operation.Status);
            Assert.Null(operation.ErrorMessage);
            Assert.Equal(100, operation.OverallPercentage);
            Assert.Equal(3, operation.TotalEntitiesToCreate);
            Assert.Equal(3, operation.EntitiesCreated);
            Assert.Null(operation.CurrentEntityCreatingName);
            Assert.Equal(4, operation.TotalEntitiesToCloneData);
            Assert.Equal(4, operation.EntitiesDataCloned);
            Assert.Null(operation.CurrentEntityCloningDataName);
            Assert.Equal(2, operation.CurrentEntityTotalRecords);
            Assert.Equal(2, operation.CurrentEntityImportedRecords);
            Assert.Equal(8, operation.TotalRecordsAllEntities);
            Assert.Equal(8, operation.TotalRecordsImported);
            Assert.Equal(0d, operation.EstimatedRemainingSeconds);
            Assert.Equal(DateTimeKind.Utc, operation.StartedAtUtc.Kind);
            Assert.InRange(operation.StartedAtUtc, before.AddSeconds(-1), DateTime.UtcNow);
            Assert.InRange(operation.CompletedAtUtc ?? DateTime.MinValue, operation.StartedAtUtc, DateTime.UtcNow);
            Assert.Equal(started.ClonedApplicationToken, operation.ClonedApplicationToken);
        }

        [Fact]
        public async Task Status_Of_A_Failed_Operation_Should_Return_The_Message_And_The_Clone_Should_Stay_Listed()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.BadRequest, EntityScene.ApiError(ApiServerRefusal));

            // The start itself is accepted: the API server is asked afterwards.
            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID, "SQLServer", "Server=nowhere"));
            await WaitForEndAsync(started.OperationId, CloneStatus.Failed);

            var operation = await GetOperationAsync(scene.Owner, scene.Token, started.OperationId);

            Assert.Equal("Failed", operation.Status);
            Assert.Equal(ApiServerRefusal, operation.ErrorMessage);
            Assert.Equal(0, operation.OverallPercentage);
            Assert.Equal(0, operation.EntitiesCreated);
            Assert.Equal(0d, operation.EstimatedRemainingSeconds);
            Assert.Null(operation.CompletedAtUtc);
            Assert.Equal(started.ClonedApplicationToken, operation.ClonedApplicationToken);

            // Only the first call was made.
            Assert.Single(_portal.ApiServer.Requests);

            // The application saved for the clone is left behind.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync($"/api/v1/applications/{started.ClonedApplicationToken}")).StatusCode);
        }

        [Fact]
        public async Task Status_Of_An_Operation_That_Failed_While_Copying_Records_Should_Keep_What_Was_Done_Before()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();
            _portal.ApiServer.Respond(ImportDataPath, HttpStatusCode.BadRequest, EntityScene.ApiError(ApiServerRefusal));

            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID, cloneData: true, entities: new[] { "Orders" }));
            await WaitForEndAsync(started.OperationId, CloneStatus.Failed);

            var operation = await GetOperationAsync(scene.Owner, scene.Token, started.OperationId);

            Assert.Equal("Failed", operation.Status);
            Assert.Equal(ApiServerRefusal, operation.ErrorMessage);
            Assert.Equal(0, operation.OverallPercentage);
            Assert.Equal(3, operation.EntitiesCreated);
            Assert.Equal(1, operation.TotalEntitiesToCloneData);
            Assert.Equal(0, operation.EntitiesDataCloned);
            Assert.Equal(0, operation.TotalRecordsImported);
            Assert.Null(operation.CompletedAtUtc);

            // The routine stopped at the refusal.
            Assert.Single(_portal.ApiServer.RequestsTo(ImportDataPath));

            // The application saved for the clone is left behind.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync($"/api/v1/applications/{started.ClonedApplicationToken}")).StatusCode);
        }

        [Fact]
        public async Task Status_Of_A_Running_Operation_Should_Return_The_Phase_And_Its_Counters()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            ScriptApiServer();

            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID));
            var progress = await WaitForEndAsync(started.OperationId);

            // The routine is done with the entry, so the test can put it back into each phase.
            progress.CompletedAtUtc = null;
            progress.Status = CloneStatus.Pending;
            progress.TotalEntitiesToCreate = 0;
            progress.EntitiesCreated = 0;

            var pending = await GetOperationAsync(scene.Owner, scene.Token, started.OperationId);
            Assert.Equal("Pending", pending.Status);
            Assert.Equal(0, pending.OverallPercentage);
            Assert.Null(pending.EstimatedRemainingSeconds);
            Assert.Null(pending.CompletedAtUtc);

            progress.Status = CloneStatus.CreatingApplication;
            Assert.Equal("CreatingApplication", (await GetOperationAsync(scene.Owner, scene.Token, started.OperationId)).Status);

            progress.Status = CloneStatus.CreatingEntities;
            progress.TotalEntitiesToCreate = 3;
            progress.EntitiesCreated = 1;
            progress.CurrentEntityCreatingName = "Orders";

            var creating = await GetOperationAsync(scene.Owner, scene.Token, started.OperationId);
            Assert.Equal("CreatingEntities", creating.Status);
            Assert.Equal(33, creating.OverallPercentage);
            Assert.Equal(3, creating.TotalEntitiesToCreate);
            Assert.Equal(1, creating.EntitiesCreated);
            Assert.Equal("Orders", creating.CurrentEntityCreatingName);
            Assert.Null(creating.EstimatedRemainingSeconds);

            progress.Status = CloneStatus.CloningData;
            progress.StartedAtUtc = DateTime.UtcNow.AddSeconds(-10);
            progress.EntitiesCreated = 3;
            progress.CurrentEntityCreatingName = null;
            progress.TotalEntitiesToCloneData = 4;
            progress.EntitiesDataCloned = 1;
            progress.CurrentEntityCloningDataName = "Customers";
            progress.CurrentEntityTotalRecords = 3000;
            progress.CurrentEntityImportedRecords = 1000;
            progress.TotalRecordsAllEntities = 8000;
            progress.TotalRecordsImported = 2000;

            var cloning = await GetOperationAsync(scene.Owner, scene.Token, started.OperationId);
            Assert.Equal("CloningData", cloning.Status);
            Assert.Equal(62, cloning.OverallPercentage);
            Assert.Null(cloning.CurrentEntityCreatingName);
            Assert.Equal(4, cloning.TotalEntitiesToCloneData);
            Assert.Equal(1, cloning.EntitiesDataCloned);
            Assert.Equal("Customers", cloning.CurrentEntityCloningDataName);
            Assert.Equal(3000, cloning.CurrentEntityTotalRecords);
            Assert.Equal(1000, cloning.CurrentEntityImportedRecords);
            Assert.Equal(8000, cloning.TotalRecordsAllEntities);
            Assert.Equal(2000, cloning.TotalRecordsImported);

            // 2000 records in 10 seconds, 6000 to go.
            Assert.InRange(cloning.EstimatedRemainingSeconds ?? 0, 29, 60);
            Assert.Null(cloning.CompletedAtUtc);
            Assert.Null(cloning.ErrorMessage);

            // The source gained records after they were counted: the answer stays inside its range.
            progress.TotalRecordsImported = 9000;

            var over = await GetOperationAsync(scene.Owner, scene.Token, started.OperationId);
            Assert.Equal(100, over.OverallPercentage);
            Assert.Equal(0d, over.EstimatedRemainingSeconds);
        }

        [Fact]
        public async Task Status_Should_Return_404_For_Another_User_Another_Application_And_An_Unknown_Id()
        {
            var scene = await CreateSceneAsync();
            var target = await _portal.CreateServerAsync();
            var other = await _portal.CreateApplicationAsync(target.ID, scene.OwnerEmail, $"other-{Guid.NewGuid():N}", scene.CollaboratorEmail);
            ScriptApiServer();

            var started = await StartAsync(scene.Owner, scene.Token, Body(target.ID));
            await WaitForEndAsync(started.OperationId);

            // The one who started it, under the application it clones.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync($"{ClonesUrl(scene.Token)}/{started.OperationId}")).StatusCode);

            // A collaborator of that application did not start it.
            await EntityScene.AssertNotFoundAsync(await scene.Collaborator.GetAsync($"{ClonesUrl(scene.Token)}/{started.OperationId}"), "CloneOperation");

            // Another application of the same user, and the clone itself.
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"{ClonesUrl(other.Application.Token)}/{started.OperationId}"), "CloneOperation");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"{ClonesUrl(started.ClonedApplicationToken)}/{started.OperationId}"), "CloneOperation");

            // No such operation.
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"{ClonesUrl(scene.Token)}/{Guid.NewGuid():N}"), "CloneOperation");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync($"{ClonesUrl(scene.Token)}/{started.OperationId.ToUpperInvariant()}"), "CloneOperation");
        }

        // ---------- Helpers ----------

        private static string ClonesUrl(string token)
        {
            return $"/api/v1/applications/{token}/clones";
        }

        private static Dictionary<string, object?> Body(long serverId, string databaseType = "SQLLite", string? connectionString = null, bool cloneData = false, string[]? entities = null)
        {
            return new Dictionary<string, object?>
            {
                ["ServerID"] = serverId,
                ["DatabaseType"] = databaseType,
                ["ConnectionString"] = connectionString,
                ["CloneData"] = cloneData,
                ["Entities"] = entities
            };
        }

        /// <summary>
        /// The scene's application with what a clone has to carry over, or leave behind: security
        /// and mail settings, a custom endpoint and a report.
        /// </summary>
        private async Task<EntityScene> CreateSceneAsync()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var appId = scene.AppId;

            await _portal.WithDbContextAsync(async db =>
            {
                var application = await db.Applications.SingleAsync(x => x.ID == appId);

                application.Security = "[]";
                application.AuthTokenExpireMinutes = 45;
                application.MailPassword = "source-mail-password";

                db.CustomEndpoints.Add(new DBWS_CustomEndpoint { AppID = appId, Name = "GetOrders", Query = "SELECT 1", DateModified = DateTime.UtcNow });
                db.Reports.Add(new DBWS_ReportPanel { AppID = appId, Title = "Orders per day", MaxRecords = 100, W = 6, H = 4, DateModified = DateTime.UtcNow });

                return await db.SaveChangesAsync();
            });

            return scene;
        }

        /// <summary>
        /// The API server answers every call of a clone the way a healthy one does. Each entity has two records.
        /// </summary>
        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.GeneratePath, HttpStatusCode.OK, "true");
            _portal.ApiServer.Respond(FakeApiServer.GenerateEntityPath, HttpStatusCode.OK, "true");
            _portal.ApiServer.Respond(DataPath, HttpStatusCode.OK, "{\"Data\":[{\"ID\":1,\"Code\":\"a\"},{\"ID\":2,\"Code\":\"b\"}],\"Total\":2}");
            _portal.ApiServer.Respond(ImportDataPath, HttpStatusCode.OK, "[1,2]");
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        private static async Task<CloneStartedResponse> StartAsync(HttpClient client, string token, Dictionary<string, object?> body)
        {
            var response = await client.PostAsync(ClonesUrl(token), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            return await response.ReadJsonAsync<CloneStartedResponse>();
        }

        /// <summary>
        /// Waits until the background routine is done with the operation, checks how it ended and
        /// returns its entry, the object the routine itself writes to. The final status is the
        /// routine's last write to it.
        /// </summary>
        private async Task<CloneProgressInfo> WaitForEndAsync(string operationId, CloneStatus expected = CloneStatus.Completed)
        {
            var progress = _portal.Services.GetRequiredService<ICloneService>().GetProgress(operationId)
                ?? throw new InvalidOperationException($"No clone operation {operationId}.");

            for (var attempt = 0; attempt < 400 && !IsFinal(progress); attempt++)
            {
                await Task.Delay(25);
            }

            Assert.True(IsFinal(progress), $"The clone did not end; it is {progress.Status}.");
            Assert.True(progress.Status == expected, $"The clone ended as {progress.Status}: {progress.ErrorMessage}");

            return progress;
        }

        private static bool IsFinal(CloneProgressInfo progress)
        {
            return progress.Status == CloneStatus.Completed || progress.Status == CloneStatus.Failed;
        }

        private static async Task<CloneOperationResponse> GetOperationAsync(HttpClient client, string token, string operationId)
        {
            var response = await client.GetAsync($"{ClonesUrl(token)}/{operationId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

            return await response.ReadJsonAsync<CloneOperationResponse>();
        }

        /// <summary>
        /// The stored application with everything that belongs to it, in the order it was added.
        /// </summary>
        private async Task<DBWS_Application> LoadAsync(string token)
        {
            var application = await _portal.WithDbContextAsync(db => db.Applications
                .AsNoTracking()
                .Include(x => x.Entities).ThenInclude(x => x.Properties)
                .Include(x => x.Reports)
                .Include(x => x.CustomEndpoints)
                .Include(x => x.Collaborates)
                .AsSplitQuery()
                .SingleAsync(x => x.Token == token));

            application.Entities = application.Entities.OrderBy(x => x.ID).ToList();
            application.Entities.ForEach(x => x.Properties = x.Properties.OrderBy(p => p.ID).ToList());

            return application;
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

        private async Task<int> CountClonesAsync(EntityScene scene)
        {
            var cloneName = (await LoadAsync(scene.Token)).Name + " - Clone";

            return await _portal.WithDbContextAsync(db => db.Applications.CountAsync(x => x.Name == cloneName));
        }

        /// <summary>
        /// No application was saved, nobody caused an audit row and the API server was not called.
        /// </summary>
        private async Task AssertNothingStartedAsync(EntityScene scene)
        {
            var appId = scene.AppId;

            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(0, await CountClonesAsync(scene));
            Assert.Empty(await scene.AuditRowsAsync(scene.OwnerEmail));
            Assert.False(await _portal.WithDbContextAsync(db => db.AuditLogs.AnyAsync(x => x.AppID == appId)));
        }
    }
}
