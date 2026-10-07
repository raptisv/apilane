using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public partial class AgentPermissionsApiTests
    {
        private readonly PortalFactory _portal;

        public AgentPermissionsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task New_And_Legacy_Shares_Should_Default_To_Read_Only(bool legacyShare)
        {
            var scene = await CreateSceneAsync(legacyShare);
            var permissions = await ReadPermissionsAsync(scene.Agent, scene.First);

            Assert.True(permissions.IsAgent);
            Assert.Equal(9, permissions.Resources.Count);
            Assert.Equal(permissions.Resources.Select(x => x.Resource), permissions.Permissions.Select(x => x.Resource));
            foreach (var resource in permissions.Resources)
            {
                var grant = Assert.Single(permissions.Permissions, x => x.Resource == resource.Resource);
                Assert.Equal(resource.CanRead, grant.Read);
                Assert.False(grant.Write);
                Assert.False(grant.Delete);
            }

            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync($"{Url(scene.First)}/entities")).StatusCode);
            await AssertForbiddenAsync(await scene.Agent.PutAsync($"{Url(scene.First)}/status", new { Online = false }.ToJsonContent()));
            await AssertForbiddenAsync(await scene.Agent.DeleteAsync($"{Url(scene.First)}/entities/Orders"));
            await AssertForbiddenAsync(await scene.Agent.PostAsync($"{Url(scene.First)}/rebuild", null));
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.True(await _portal.WithDbContextAsync(db => db.Applications.Where(x => x.ID == scene.First.ID).Select(x => x.Online).SingleAsync()));
        }

        [Fact]
        public async Task Add_Should_Apply_An_Explicit_Policy_And_Keep_Other_Resources_Denied()
        {
            var scene = await CreateSceneAsync();
            var app = await scene.Schema.AddApplicationAsync("explicit", FillApplication);
            var added = await scene.Schema.Owner.PostAsync($"{Url(app)}/collaborators", new
            {
                Email = scene.Created.Email,
                Permissions = new[] { Grant("security", read: true) }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            var permissions = await ReadPermissionsAsync(scene.Agent, app);
            Assert.True(Assert.Single(permissions.Permissions, x => x.Resource == "security").Read);
            Assert.All(permissions.Permissions.Where(x => x.Resource != "security"), x => Assert.False(x.Read || x.Write || x.Delete));
            await AssertForbiddenAsync(await scene.Agent.GetAsync($"{Url(app)}/entities"));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Security_Read_Only_Should_Read_The_Settings_And_Refuse_Both_Writes_Before_Any_Effects()
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("security", read: true));
            _portal.ApiServer.Respond(FakeApiServer.StatsDistinctPath, HttpStatusCode.OK, "[]");

            var read = await scene.Agent.GetAsync($"{Url(scene.First)}/security");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(60, (await read.ReadJsonAsync<SecurityResponse>()).Settings.AuthTokenExpireMinutes);

            _portal.ResetApiServer();
            var before = await scene.Schema.StoredSchemaAsync(scene.First);
            var auditBefore = await scene.Schema.AuditAsync(scene.First);
            await AssertForbiddenAsync(await scene.Agent.PutAsync($"{Url(scene.First)}/security/settings", new
            {
                AuthTokenExpireMinutes = 120,
                ForceSingleLogin = true,
                AllowLoginUnconfirmedEmail = false,
                AllowUserRegister = false,
                MaxAllowedFileSizeInKB = 512,
                ClientIPsLogic = "Block",
                ClientIPs = Array.Empty<string>()
            }.ToJsonContent()));
            await AssertForbiddenAsync(await scene.Agent.PutAsync($"{Url(scene.First)}/security/rules", new { Rules = Array.Empty<object>() }.ToJsonContent()));

            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(before, await scene.Schema.StoredSchemaAsync(scene.First));
            Assert.Equal(auditBefore, await scene.Schema.AuditAsync(scene.First));
            Assert.Equal(60, await _portal.WithDbContextAsync(db => db.Applications.Where(x => x.ID == scene.First.ID).Select(x => x.AuthTokenExpireMinutes).SingleAsync()));
        }

        [Fact]
        public async Task Grants_Should_Be_Per_Application_And_Revoked_Immediately_For_The_Same_Key()
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("custom-endpoints", read: true, write: true));
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
            var firstEndpoint = Assert.Single(scene.First.CustomEndpoints);
            var secondEndpoint = Assert.Single(scene.Second.CustomEndpoints);
            var edit = new { Name = "TopOrders", Query = "SELECT 2", Description = "Allowed edit" };

            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.PutAsync($"{Url(scene.First)}/custom-endpoints/{firstEndpoint.ID}", edit.ToJsonContent())).StatusCode);
            await AssertForbiddenAsync(await scene.Agent.PutAsync($"{Url(scene.Second)}/custom-endpoints/{secondEndpoint.ID}", edit.ToJsonContent()));
            Assert.Equal("SELECT 1", await _portal.WithDbContextAsync(db => db.CustomEndpoints.Where(x => x.ID == secondEndpoint.ID).Select(x => x.Query).SingleAsync()));

            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId);
            _portal.ResetApiServer();
            var revoked = await ReadPermissionsAsync(scene.Agent, scene.First);
            Assert.All(revoked.Permissions, x => Assert.False(x.Read || x.Write || x.Delete));
            await AssertForbiddenAsync(await scene.Agent.GetAsync($"{Url(scene.First)}/custom-endpoints"));
            await AssertForbiddenAsync(await scene.Agent.PutAsync($"{Url(scene.First)}/custom-endpoints/{firstEndpoint.ID}", edit.ToJsonContent()));
            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync($"{Url(scene.Second)}/custom-endpoints")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync(Url(scene.First))).StatusCode);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Empty_Policy_Should_Keep_Discovery_Available_But_Refuse_Every_Feature_Read()
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId);
            var permissions = await ReadPermissionsAsync(scene.Agent, scene.First);
            Assert.All(permissions.Permissions, x => Assert.False(x.Read || x.Write || x.Delete));

            foreach (var path in new[]
            {
                "entities", "entities/Orders", "entities/Orders/properties", "entities/Orders/properties/Amount",
                "entities/Orders/constraints", "entities/Orders/default-order", "entities/Orders/report-fields?Type=Grid",
                "security", "custom-endpoints", "reports", "email-settings", "audit-log"
            })
            {
                await AssertForbiddenAsync(await scene.Agent.GetAsync($"{Url(scene.First)}/{path}"));
            }

            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync(Url(scene.First))).StatusCode);
            var apps = await (await scene.Agent.GetAsync("/api/v1/applications")).ReadJsonAsync<ListResponse<ApplicationResponse>>();
            Assert.Contains(apps.Data, x => x.Token == scene.First.Token);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Permission_Editing_Should_Be_Owner_Only_And_Restricted_To_Agent_Collaborators()
        {
            var scene = await CreateSceneAsync();
            var url = PolicyUrl(scene.First, scene.FirstCollaborationId);
            var body = new { Permissions = new[] { Grant("application", write: true) } };
            var admin = await _portal.CreateAdminClientAsync();

            await AssertForbiddenAsync(await scene.Schema.Collaborator.PutAsync(url, body.ToJsonContent()));
            await AssertForbiddenAsync(await scene.Agent.PutAsync(url, body.ToJsonContent()));
            Assert.Equal(HttpStatusCode.NotFound, (await scene.Schema.Stranger.PutAsync(url, body.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsync(url, body.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _portal.CreateAnonymousClient().PutAsync(url, body.ToJsonContent())).StatusCode);

            var humanId = await _portal.WithDbContextAsync(db => db.Collaborations
                .Where(x => x.AppID == scene.First.ID && x.UserEmail == scene.Schema.CollaboratorEmail).Select(x => x.ID).SingleAsync());
            Assert.Equal(HttpStatusCode.BadRequest, (await scene.Schema.Owner.PutAsync(PolicyUrl(scene.First, humanId), body.ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.Schema.Owner.PutAsync(PolicyUrl(scene.First, scene.SecondCollaborationId), body.ToJsonContent())).StatusCode);
            Assert.All((await ReadPermissionsAsync(scene.Agent, scene.First)).Permissions, x => Assert.False(x.Write || x.Delete));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Add_With_A_Policy_For_A_Person_Should_Refuse_Without_Creating_The_Share()
        {
            var scene = await CreateSceneAsync();
            var response = await scene.Schema.Owner.PostAsync($"{Url(scene.First)}/collaborators", new
            {
                Email = scene.Schema.StrangerEmail,
                Permissions = new[] { Grant("entities", read: true) }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.Collaborations.AnyAsync(x => x.AppID == scene.First.ID && x.UserEmail == scene.Schema.StrangerEmail)));
        }

        [Theory]
        [InlineData("{\"Permissions\":[{\"Resource\":\"unknown\",\"Read\":true}]}")]
        [InlineData("{\"Permissions\":[{\"Resource\":\"entities\",\"Write\":true}]}")]
        [InlineData("{\"Permissions\":[{\"Resource\":\"entities\",\"Delete\":true}]}")]
        [InlineData("{\"Permissions\":[{\"Resource\":\"security\",\"Read\":true,\"Delete\":true}]}")]
        [InlineData("{\"Permissions\":[{\"Resource\":\"audit-log\",\"Read\":true,\"Write\":true}]}")]
        [InlineData("{\"Permissions\":[{\"Resource\":\"email-settings\",\"Read\":true,\"Write\":true}]}")]
        [InlineData("{\"Permissions\":[{\"Resource\":\"entities\",\"Read\":true},{\"Resource\":\"entities\",\"Read\":true}]}")]
        [InlineData("{\"Permissions\":[null]}")]
        [InlineData("{\"Permissions\":null}")]
        [InlineData("{}")]
        public async Task Invalid_Policies_Should_Be_Refused_And_Keep_The_Current_Policy(string json)
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("application", write: true));
            var response = await scene.Schema.Owner.PutAsync(PolicyUrl(scene.First, scene.FirstCollaborationId), EntityScene.Json(json));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(PortalErrorCode.Validation, (await response.ReadJsonAsync<ErrorResponse>()).Code);
            var after = await ReadPermissionsAsync(scene.Agent, scene.First);
            Assert.True(Assert.Single(after.Permissions, x => x.Resource == "application").Write);
            Assert.All(after.Permissions.Where(x => x.Resource != "application"), x => Assert.False(x.Read || x.Write || x.Delete));
        }

        [Fact]
        public async Task Custom_Endpoint_Preview_Should_Need_Read_Without_Write_Or_Executing_The_Query()
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("custom-endpoints", read: true));
            var body = new { Name = "Preview", Query = "SELECT {Amount}" };
            var response = await scene.Agent.PostAsync($"{Url(scene.First)}/custom-endpoints/preview", body.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, await _portal.WithDbContextAsync(db => db.CustomEndpoints.CountAsync(x => x.AppID == scene.First.ID)));
            Assert.Empty(_portal.ApiServer.Requests);
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId);
            await AssertForbiddenAsync(await scene.Agent.PostAsync($"{Url(scene.First)}/custom-endpoints/preview", body.ToJsonContent()));
        }

        [Theory]
        [InlineData("entities")]
        [InlineData("security")]
        [InlineData("custom-endpoints")]
        [InlineData("schema")]
        public async Task Comparison_And_Diff_Should_Check_Each_Resource_On_Both_Applications(string missing)
        {
            var scene = await CreateSceneAsync();
            var complete = ComparisonGrants();
            var limited = complete.Where(x => x.Resource != missing).ToArray();

            foreach (var denyFirst in new[] { true, false })
            {
                await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, denyFirst ? limited : complete);
                await ReplaceAsync(scene, scene.Second, scene.SecondCollaborationId, denyFirst ? complete : limited);
                await AssertForbiddenAsync(await scene.Agent.GetAsync($"{Url(scene.First)}/comparison?Target={scene.Second.Token}"));
                await AssertForbiddenAsync(await scene.Agent.GetAsync($"{Url(scene.First)}/schema-import/diff?Source={scene.Second.Token}"));
            }

            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, complete);
            await ReplaceAsync(scene, scene.Second, scene.SecondCollaborationId, complete);
            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync($"{Url(scene.First)}/comparison?Target={scene.Second.Token}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.Agent.GetAsync($"{Url(scene.First)}/schema-import/diff?Source={scene.Second.Token}")).StatusCode);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Theory]
        [InlineData("entities")]
        [InlineData("security")]
        [InlineData("custom-endpoints")]
        [InlineData("schema")]
        public async Task Mixed_Schema_Import_Should_Refuse_All_Changes_When_Any_Required_Write_Is_Missing(string missing)
        {
            var scene = await CreateSceneAsync();
            var grants = ComparisonGrants();
            foreach (var grant in grants)
            {
                grant.Write = grant.Resource != missing;
            }
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, grants);
            var before = await scene.Schema.StoredSchemaAsync(scene.First);
            var auditBefore = await scene.Schema.AuditAsync(scene.First);

            var response = await scene.Agent.PostAsync($"{Url(scene.First)}/schema-import", new
            {
                Entities = new[] { new { Name = "Products", IsNew = true, Properties = Array.Empty<object>(), Constraints = Array.Empty<object>() } },
                Security = new[] { new { TypeID = 0, Name = "Orders", RoleID = "AUTHENTICATED", Action = "get", Record = 0 } },
                CustomEndpoints = new[] { new { Name = "NewQuery", Query = "SELECT 2" } }
            }.ToJsonContent());

            await AssertForbiddenAsync(response);
            Assert.Equal(before, await scene.Schema.StoredSchemaAsync(scene.First));
            Assert.Equal(auditBefore, await scene.Schema.AuditAsync(scene.First));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Schema_Import_Should_Require_Only_The_Constituent_Resources_In_The_Body()
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId,
                Grant("schema", read: true, write: true), Grant("custom-endpoints", read: true, write: true));
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
            var response = await scene.Agent.PostAsync($"{Url(scene.First)}/schema-import", new
            {
                CustomEndpoints = new[] { new { Name = "AllowedQuery", Query = "SELECT 3" } }
            }.ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(await _portal.WithDbContextAsync(db => db.CustomEndpoints.AnyAsync(x => x.AppID == scene.First.ID && x.Name == "AllowedQuery")));
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Theory]
        [InlineData("entities", "entities/Orders", FakeApiServer.DegenerateEntityPath)]
        [InlineData("entities", "entities/Orders/properties/Amount", FakeApiServer.DegeneratePropertyPath)]
        [InlineData("custom-endpoints", "custom-endpoints", "")]
        public async Task Delete_Should_Require_Its_Own_Grant_And_Work_Without_Write(string resource, string path, string upstream)
        {
            var scene = await CreateSceneAsync();
            var endpointId = Assert.Single(scene.First.CustomEndpoints).ID;
            var url = $"{Url(scene.First)}/{path}" + (path == "custom-endpoints" ? $"/{endpointId}" : string.Empty);
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant(resource, read: true, write: true));
            await AssertForbiddenAsync(await scene.Agent.DeleteAsync(url));
            Assert.Empty(_portal.ApiServer.Requests);

            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant(resource, read: true, delete: true));
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
            if (upstream.Length > 0)
            {
                _portal.ApiServer.Respond(upstream, HttpStatusCode.OK, string.Empty);
            }

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Agent.DeleteAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await scene.Agent.GetAsync(url)).StatusCode);
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
        }

        [Fact]
        public async Task Rebuild_Should_Require_Explicit_Write_And_Not_Open_Application_Delete_Or_Secrets()
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("application", write: true));
            await AssertForbiddenAsync(await scene.Agent.PostAsync($"{Url(scene.First)}/rebuild", null));
            Assert.Empty(_portal.ApiServer.Requests);

            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("rebuild", write: true));
            _portal.ApiServer.Respond("/api/Application/Rebuild", HttpStatusCode.OK, string.Empty);
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Agent.PostAsync($"{Url(scene.First)}/rebuild", null)).StatusCode);
            Assert.Single(_portal.ApiServer.RequestsTo("/api/Application/Rebuild"));

            _portal.ResetApiServer();
            await AssertForbiddenAsync(await scene.Agent.DeleteAsync(Url(scene.First)));
            await AssertForbiddenAsync(await scene.Agent.GetAsync($"{Url(scene.First)}/connection-info"));
            await AssertForbiddenAsync(await scene.Agent.GetAsync("/api/v1/session/api-token"));
            await AssertForbiddenAsync(await scene.Agent.GetAsync($"{Url(scene.First)}/collaborators"));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Report_Delete_Should_Require_Delete_And_Work_Without_Write()
        {
            var scene = await CreateSceneAsync();
            var report = new DBWS_ReportPanel
            {
                AppID = scene.First.ID, Title = "Agent report", TypeID = (int)ReportType.Grid,
                W = 6, H = 4, MaxRecords = 10, DateModified = DateTime.UtcNow
            };
            await _portal.WithDbContextAsync(db =>
            {
                db.Reports.Add(report);
                return db.SaveChangesAsync();
            });
            var url = $"{Url(scene.First)}/reports/{report.ID}";
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("reports", read: true, write: true));
            await AssertForbiddenAsync(await scene.Agent.DeleteAsync(url));
            Assert.True(await _portal.WithDbContextAsync(db => db.Reports.AnyAsync(x => x.ID == report.ID)));

            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("reports", read: true, delete: true));
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Agent.DeleteAsync(url)).StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.Reports.AnyAsync(x => x.ID == report.ID)));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Discovery_Should_Describe_Current_Operations_And_Conditional_Requirements()
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId,
                Grant("security", read: true), Grant("custom-endpoints", read: true, delete: true), Grant("schema", read: true, write: true));
            var discovery = await ReadPermissionsAsync(scene.Agent, scene.First);

            Assert.NotEmpty(discovery.Restrictions);
            Assert.True(Operation(discovery, "GET", "/permissions").AllowedForThisApplication);
            Assert.True(Operation(discovery, "GET", "/security").AllowedForThisApplication);
            Assert.False(Operation(discovery, "PUT", "/security/rules").AllowedForThisApplication);
            Assert.False(Operation(discovery, "GET", "/entities").AllowedForThisApplication);
            Assert.False(Operation(discovery, "GET", "/connection-info").AllowedForThisApplication);
            Assert.False(Operation(discovery, "POST", "/rebuild").AllowedForThisApplication);

            var preview = Operation(discovery, "POST", "/custom-endpoints/preview");
            Assert.True(preview.AllowedForThisApplication);
            Assert.Equal(AgentPermissionAccess.Read, Assert.Single(preview.Requirements).Access);
            Assert.False(Operation(discovery, "POST", "/custom-endpoints").AllowedForThisApplication);
            Assert.True(Operation(discovery, "DELETE", "/custom-endpoints/{id:long}").AllowedForThisApplication);
            Assert.False(Operation(discovery, "GET", "/comparison").AllowedForThisApplication);
            Assert.False(string.IsNullOrWhiteSpace(Operation(discovery, "GET", "/comparison").AdditionalRequirements));
            Assert.False(string.IsNullOrWhiteSpace(Operation(discovery, "POST", "/schema-import").AdditionalRequirements));

            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId);
            var after = await ReadPermissionsAsync(scene.Agent, scene.First);
            Assert.False(Operation(after, "GET", "/security").AllowedForThisApplication);
            Assert.False(Operation(after, "DELETE", "/custom-endpoints/{id:long}").AllowedForThisApplication);
            Assert.True(Operation(after, "GET", "/permissions").AllowedForThisApplication);
        }

        [Fact]
        public async Task Discovery_And_OpenApi_Should_Keep_Access_Enum_Values_As_Read_Write_And_Delete_Strings()
        {
            var scene = await CreateSceneAsync();
            var response = await scene.Agent.GetAsync($"{Url(scene.First)}/permissions");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            // The wire contract stays readable by agents and existing clients even though the
            // server now uses an enum. Checking raw JSON catches an accidental numeric converter.
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var accesses = json.RootElement.GetProperty("Operations").EnumerateArray()
                .SelectMany(x => x.GetProperty("Requirements").EnumerateArray())
                .Select(x => x.GetProperty("Access"))
                .ToList();
            Assert.NotEmpty(accesses);
            Assert.All(accesses, x => Assert.Equal(JsonValueKind.String, x.ValueKind));
            Assert.Equal(new[] { "Delete", "Read", "Write" }, accesses.Select(x => x.GetString()).Distinct().OrderBy(x => x, StringComparer.Ordinal));

            // The strict contract reader must understand those same strings without registering
            // a test-only converter, including both mutation operations and read-only POST.
            var discovery = await response.ReadJsonAsync<ApplicationPermissionsResponse>();
            Assert.Equal(AgentPermissionAccess.Read, Assert.Single(Operation(discovery, "POST", "/custom-endpoints/preview").Requirements).Access);
            Assert.Equal(AgentPermissionAccess.Write, Assert.Single(Operation(discovery, "PUT", "/security/rules").Requirements).Access);
            Assert.Equal(AgentPermissionAccess.Delete, Assert.Single(Operation(discovery, "DELETE", "/custom-endpoints/{id:long}").Requirements).Access);

            var specification = await scene.Schema.Owner.GetAsync("/swagger/v1/swagger.json");
            Assert.Equal(HttpStatusCode.OK, specification.StatusCode);
            using var openApi = JsonDocument.Parse(await specification.Content.ReadAsStringAsync());
            var accessSchema = openApi.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(nameof(AgentPermissionAccess));
            Assert.Equal("string", accessSchema.GetProperty("type").GetString());
            Assert.Equal(new[] { "Read", "Write", "Delete" }, accessSchema.GetProperty("enum").EnumerateArray().Select(x => x.GetString()));
        }

        [Fact]
        public async Task Permission_Creation_And_Changes_Should_Be_Audited_In_Their_Application()
        {
            var scene = await CreateSceneAsync();
            var created = Assert.Single(await _portal.WithDbContextAsync(db => db.AuditLogs.AsNoTracking()
                .Where(x => x.AppID == scene.First.ID && x.EntityType == "Agent Permissions").ToListAsync()));
            Assert.Equal("Created", created.Action);
            Assert.Equal(scene.Created.Email, created.EntityIdentifier);
            Assert.Equal(scene.Schema.OwnerEmail, created.UserEmail);

            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("security", read: true, write: true));
            var updated = Assert.Single(await _portal.WithDbContextAsync(db => db.AuditLogs.AsNoTracking()
                .Where(x => x.AppID == scene.First.ID && x.EntityType == "Agent Permissions" && x.Action == "Modified").ToListAsync()));
            Assert.Equal(scene.Created.Email, updated.EntityIdentifier);
            Assert.Equal(scene.Schema.OwnerEmail, updated.UserEmail);
            Assert.Contains("PermissionsJson", updated.Changes ?? string.Empty);
            Assert.DoesNotContain(scene.Created.Key, updated.Changes ?? string.Empty);
        }

        [Fact]
        public async Task Deleting_An_Agent_Should_Remove_All_Of_Its_Application_Policies()
        {
            var scene = await CreateSceneAsync();
            var admin = await _portal.CreateAdminClientAsync();
            var ids = new[] { scene.FirstCollaborationId, scene.SecondCollaborationId };
            Assert.Equal(2, await _portal.WithDbContextAsync(db => db.AgentPermissions.CountAsync(x => ids.Contains(x.CollaborationId))));

            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/admin/agents/{scene.Created.ID}")).StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.AgentPermissions.AnyAsync(x => ids.Contains(x.CollaborationId))));
            Assert.Equal(HttpStatusCode.Unauthorized, (await scene.Agent.GetAsync($"{Url(scene.First)}/permissions")).StatusCode);
        }

        [Fact]
        public async Task Removing_A_Share_Should_Delete_Its_Policy_And_Sharing_Again_Should_Start_Read_Only()
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("application", write: true));
            Assert.True(await _portal.WithDbContextAsync(db => db.AgentPermissions.AnyAsync(x => x.CollaborationId == scene.FirstCollaborationId)));

            Assert.Equal(HttpStatusCode.NoContent, (await scene.Schema.Owner.DeleteAsync($"{Url(scene.First)}/collaborators/{scene.FirstCollaborationId}")).StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.AgentPermissions.AnyAsync(x => x.CollaborationId == scene.FirstCollaborationId)));
            Assert.Equal(HttpStatusCode.NotFound, (await scene.Agent.GetAsync($"{Url(scene.First)}/permissions")).StatusCode);

            var added = await scene.Schema.Owner.PostAsync($"{Url(scene.First)}/collaborators", new { Email = scene.Created.Email }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);
            Assert.All((await ReadPermissionsAsync(scene.Agent, scene.First)).Permissions, x => Assert.False(x.Write || x.Delete));
        }

        [Theory]
        [InlineData("not-json")]
        [InlineData("null")]
        [InlineData("[null]")]
        [InlineData("[{\"Resource\":\"unknown\",\"Read\":true}]")]
        [InlineData("[{\"Resource\":\"entities\",\"Write\":true}]")]
        [InlineData("[{\"Resource\":\"email-settings\",\"Write\":true},{\"Resource\":\"entities\",\"Read\":true}]")]
        [InlineData("[{\"Resource\":\"entities\",\"Read\":true},{\"Resource\":\"entities\",\"Read\":true}]")]
        public async Task Corrupt_Stored_Policy_Should_Deny_Access_Instead_Of_Restoring_Defaults(string storedPolicy)
        {
            var scene = await CreateSceneAsync();
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, Grant("entities", read: true, write: true));
            await _portal.WithDbContextAsync(async db =>
            {
                var policy = await db.AgentPermissions.SingleAsync(x => x.CollaborationId == scene.FirstCollaborationId);
                policy.PermissionsJson = storedPolicy;
                return await db.SaveChangesAsync();
            });

            Assert.All((await ReadPermissionsAsync(scene.Agent, scene.First)).Permissions, x => Assert.False(x.Read || x.Write || x.Delete));
            await AssertForbiddenAsync(await scene.Agent.GetAsync($"{Url(scene.First)}/entities"));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Unclassified_Application_Action_Should_Default_To_Denied_For_Agents()
        {
            var scene = await CreateSceneAsync();
            var url = $"{Url(scene.First)}/permission-unclassified-test";
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Schema.Owner.GetAsync(url)).StatusCode);
            await AssertForbiddenAsync(await scene.Agent.GetAsync(url));
        }

        [Fact]
        public void Application_Actions_Should_Explicitly_Declare_Their_Agent_Access()
        {
            var actions = _portal.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
                .ApiDescriptionGroups.Items.SelectMany(x => x.Items)
                .Where(x => (x.RelativePath ?? string.Empty).StartsWith("api/v1/applications/{appToken}", StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(actions);
            var missing = actions.Where(x => !x.ActionDescriptor.EndpointMetadata.Any(metadata =>
                    metadata is NoAgentAttribute || metadata is AgentPermissionAttribute || metadata is AgentPermissionDiscoveryAttribute))
                .Select(x => $"{x.HttpMethod} {x.RelativePath}");
            Assert.Empty(missing);
        }

        [Fact]
        public async Task EnsureSchemaUpdated_Should_Create_The_Policy_Table_And_Its_Cascade_Without_An_Explicit_Migration()
        {
            var folder = Directory.CreateTempSubdirectory("apilane-permissions-upgrade-").FullName;
            try
            {
                var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(folder, "Apilane.db")};Pooling=False")
                    .UseApplicationServiceProvider(_portal.Services).Options;
                await using var db = new ApplicationDbContext(options, _portal.Services.GetRequiredService<ILogger<ApplicationDbContext>>());
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync("DROP TABLE \"AgentPermissions\"");

                db.EnsureSchemaUpdated();
                db.EnsureSchemaUpdated();

                var app = new DBWS_Application
                {
                    Token = Guid.NewGuid().ToString(), Name = "Upgrade", UserID = "owner", EncryptionKey = "test-key",
                    Server = new DBWS_Server { Name = "Upgrade", ServerUrl = "https://upgrade.example.test" }
                };
                var collaborator = new DBWS_Collaborate { Application = app, UserEmail = "upgrade@agent.local" };
                db.AgentPermissions.Add(new PortalAgentPermission { Collaboration = collaborator, PermissionsJson = "[]" });
                await db.SaveChangesAsync();
                Assert.Single(await db.AgentPermissions.AsNoTracking().ToListAsync());

                // Delete without loading the policy: the new table's actual foreign key must cascade.
                db.ChangeTracker.Clear();
                await db.Collaborations.Where(x => x.ID == collaborator.ID).ExecuteDeleteAsync();
                Assert.Empty(await db.AgentPermissions.AsNoTracking().ToListAsync());
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        private record Scene(SchemaScene Schema, AgentCreatedResponse Created, HttpClient Agent,
            DBWS_Application First, DBWS_Application Second, long FirstCollaborationId, long SecondCollaborationId);

        private async Task<Scene> CreateSceneAsync(bool legacyShare = false)
        {
            var admin = await _portal.CreateAdminClientAsync();
            var createdResponse = await admin.PostAsync("/api/v1/admin/agents", new { Name = $"rights-{Guid.NewGuid():N}".Substring(0, 24) }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            var created = await createdResponse.ReadJsonAsync<AgentCreatedResponse>();
            var schema = await SchemaScene.CreateAsync(_portal);
            var first = await schema.AddApplicationAsync("permission-first", FillApplication);
            var second = await schema.AddApplicationAsync("permission-second", FillApplication);
            var firstId = await ShareAsync(schema.Owner, first, created.Email, legacyShare);
            var secondId = await ShareAsync(schema.Owner, second, created.Email, legacyShare);
            var agent = _portal.CreateCookielessClient();
            agent.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
            agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Key);
            return new Scene(schema, created, agent, first, second, firstId, secondId);
        }

        private async Task<long> ShareAsync(HttpClient owner, DBWS_Application application, string email, bool legacyShare)
        {
            if (legacyShare)
            {
                return await _portal.WithDbContextAsync(async db =>
                {
                    var collaboration = new DBWS_Collaborate { AppID = application.ID, UserEmail = email, DateModified = DateTime.UtcNow };
                    db.Collaborations.Add(collaboration);
                    await db.SaveChangesAsync();
                    return collaboration.ID;
                });
            }

            var response = await owner.PostAsync($"{Url(application)}/collaborators", new { Email = email }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync<CollaboratorAddedResponse>()).ID;
        }

        private static void FillApplication(DBWS_Application application)
        {
            application.Security = "[]";
            application.Entities.Add(SchemaScene.Users());
            application.Entities.Add(SchemaScene.Custom("Orders", new[] { EntityScene.Property("Amount", PropertyType.Number, decimalPlaces: 2) }));
            application.CustomEndpoints.Add(SchemaScene.Endpoint("TopOrders", "SELECT 1"));
        }

        private static AgentPermissionGrant Grant(string resource, bool read = false, bool write = false, bool delete = false)
        {
            return new AgentPermissionGrant { Resource = resource, Read = read, Write = write, Delete = delete };
        }

        private static AgentPermissionGrant[] ComparisonGrants()
        {
            return new[] { Grant("schema", read: true), Grant("entities", read: true), Grant("security", read: true), Grant("custom-endpoints", read: true) };
        }

        private static AgentPermissionOperation Operation(ApplicationPermissionsResponse discovery, string method, string pathSuffix)
        {
            return Assert.Single(discovery.Operations, x => x.Method == method && x.Path == "/api/v1/applications/{appToken}" + pathSuffix);
        }

        private static async Task ReplaceAsync(Scene scene, DBWS_Application application, long collaboratorId, params AgentPermissionGrant[] permissions)
        {
            var response = await scene.Schema.Owner.PutAsync(PolicyUrl(application, collaboratorId), new { Permissions = permissions }.ToJsonContent());
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Permission update failed ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }

        private static async Task<ApplicationPermissionsResponse> ReadPermissionsAsync(HttpClient client, DBWS_Application application)
        {
            var response = await client.GetAsync($"{Url(application)}/permissions");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync<ApplicationPermissionsResponse>();
        }

        private static async Task AssertForbiddenAsync(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"Expected 403, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        private static string Url(DBWS_Application application)
        {
            return SchemaScene.AppUrl(application);
        }

        private static string PolicyUrl(DBWS_Application application, long collaboratorId)
        {
            return $"{Url(application)}/collaborators/{collaboratorId}/permissions";
        }
    }

    /// <summary>
    /// A deliberately unclassified endpoint proves that adding an application action cannot
    /// silently grant it to every agent. Hidden from the production API contract.
    /// </summary>
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("api/v1/applications/{appToken}/permission-unclassified-test")]
    public class UnclassifiedAgentPermissionTestController : PortalApplicationApiControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return NoContent();
        }
    }
}
