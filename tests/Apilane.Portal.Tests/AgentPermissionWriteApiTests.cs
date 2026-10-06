using Apilane.Common.Enums;
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
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class AgentPermissionWriteApiTests
    {
        private readonly PortalFactory _portal;

        public AgentPermissionWriteApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Theory]
        [InlineData("entities", "entity-create")]
        [InlineData("entities", "entity-update")]
        [InlineData("entities", "entity-rename")]
        [InlineData("entities", "property-create")]
        [InlineData("entities", "property-update")]
        [InlineData("entities", "property-rename")]
        [InlineData("entities", "constraints")]
        [InlineData("entities", "default-order")]
        [InlineData("security", "security-settings")]
        [InlineData("security", "security-rules")]
        [InlineData("reports", "report-create")]
        [InlineData("reports", "report-update")]
        [InlineData("reports", "report-layout")]
        [InlineData("email-settings", "email-settings")]
        [InlineData("application", "application-settings")]
        [InlineData("application", "cache-reset")]
        public async Task Write_Should_Require_The_Correct_Resource_And_Apply_Only_After_It_Is_Granted(string resource, string operation)
        {
            var scene = await CreateSceneAsync();
            var call = RequestFor(operation, scene.ReportId);
            var before = await SnapshotAsync(scene.Entity.AppId);

            // The default read-only policy refuses every mutation, before database or API-server effects.
            await AssertDeniedWithoutEffectsAsync(scene, call, before);

            var discovery = await (await scene.Agent.GetAsync($"{scene.Entity.AppUrl}/permissions"))
                .ReadJsonAsync<ApplicationPermissionsResponse>();

            // Giving every other writable area access must not accidentally authorize this endpoint.
            // This catches a route classified under the wrong resource, even when it is classified.
            await SetPermissionsAsync(scene, discovery.Resources.Select(x => new AgentPermissionGrant
            {
                Resource = x.Resource,
                Read = x.CanRead,
                Write = x.CanWrite && x.Resource != resource
            }).ToArray());
            await AssertDeniedWithoutEffectsAsync(scene, call, before);

            // The intended resource alone is sufficient; no unrelated read/write or Delete is granted.
            var metadata = Assert.Single(discovery.Resources, x => x.Resource == resource);
            await SetPermissionsAsync(scene, new AgentPermissionGrant { Resource = resource, Read = metadata.CanRead, Write = true });
            ScriptApiServer();
            var response = await SendAsync(scene.Agent, scene.Entity.AppUrl, call);
            Assert.True(response.StatusCode == call.Status, $"{operation}: {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(call.UpstreamPaths, _portal.ApiServer.Requests.Select(x => x.Path));

            if (operation == "cache-reset")
            {
                Assert.Equal(before, await SnapshotAsync(scene.Entity.AppId));
            }
            else
            {
                Assert.NotEqual(before, await SnapshotAsync(scene.Entity.AppId));
                Assert.NotEmpty(await scene.Entity.AuditRowsAsync(scene.Email));
                await AssertSavedAsync(scene, operation);
            }

            // Revocation is effective on the very next request made with the same key.
            await SetPermissionsAsync(scene);
            _portal.ResetApiServer();
            var after = await SnapshotAsync(scene.Entity.AppId);
            var auditCount = (await scene.Entity.AuditRowsAsync(scene.Email)).Count;
            await AssertForbiddenAsync(await SendAsync(scene.Agent, scene.Entity.AppUrl, call));
            Assert.Equal(after, await SnapshotAsync(scene.Entity.AppId));
            Assert.Equal(auditCount, (await scene.Entity.AuditRowsAsync(scene.Email)).Count);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        private record Scene(EntityScene Entity, HttpClient Agent, string Email, long CollaborationId, long ReportId);
        private record Call(HttpMethod Method, string Path, object? Body, HttpStatusCode Status, params string[] UpstreamPaths);

        private async Task<Scene> CreateSceneAsync()
        {
            var entity = await EntityScene.CreateAsync(_portal);
            var admin = await _portal.CreateAdminClientAsync();
            var createdResponse = await admin.PostAsync("/api/v1/admin/agents", new { Name = $"writes-{Guid.NewGuid():N}".Substring(0, 24) }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
            var created = await createdResponse.ReadJsonAsync<AgentCreatedResponse>();
            var shared = await entity.Owner.PostAsync($"{entity.AppUrl}/collaborators", new { created.Email }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, shared.StatusCode);
            var collaboration = await shared.ReadJsonAsync<CollaboratorAddedResponse>();
            var report = new DBWS_ReportPanel
            {
                AppID = entity.AppId, Title = "Original report", TypeID = (int)ReportType.Line,
                W = 6, H = 4, MaxRecords = 10, DateModified = DateTime.UtcNow
            };
            await _portal.WithDbContextAsync(async db =>
            {
                (await db.Applications.SingleAsync(x => x.ID == entity.AppId)).Security = "[]";
                db.Reports.Add(report);
                return await db.SaveChangesAsync();
            });
            var agent = _portal.CreateCookielessClient();
            agent.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
            agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.Key);
            return new Scene(entity, agent, created.Email, collaboration.ID, report.ID);
        }

        private async Task SetPermissionsAsync(Scene scene, params AgentPermissionGrant[] grants)
        {
            var response = await scene.Entity.Owner.PutAsync($"{scene.Entity.AppUrl}/collaborators/{scene.CollaborationId}/permissions",
                new { Permissions = grants }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private async Task AssertDeniedWithoutEffectsAsync(Scene scene, Call call, string before)
        {
            await AssertForbiddenAsync(await SendAsync(scene.Agent, scene.Entity.AppUrl, call));
            Assert.Equal(before, await SnapshotAsync(scene.Entity.AppId));
            Assert.Empty(await scene.Entity.AuditRowsAsync(scene.Email));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        private static async Task AssertForbiddenAsync(HttpResponseMessage response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"Expected 403: {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(PortalErrorCode.Forbidden, (await response.ReadJsonAsync<ErrorResponse>()).Code);
        }

        private static Task<HttpResponseMessage> SendAsync(HttpClient client, string appUrl, Call call)
        {
            return client.SendAsync(new HttpRequestMessage(call.Method, appUrl + call.Path) { Content = call.Body?.ToJsonContent() });
        }

        private static Call RequestFor(string operation, long reportId)
        {
            var cache = FakeApiServer.ClearCachePath;
            var report = new
            {
                Title = "Allowed report", Type = "Line", MaxRecords = 25,
                Series = new[] { new { Label = "Orders", Entity = "Orders", GroupBy = "Created.Year", Property = "ID.Count" } }
            };
            return operation switch
            {
                "entity-create" => new(HttpMethod.Post, "/entities", new { Name = "Products", Description = "Allowed creation" }, HttpStatusCode.Created,
                    FakeApiServer.GetSystemPropertiesAndConstraintsPath, FakeApiServer.GenerateEntityPath, cache),
                "entity-update" => new(HttpMethod.Put, "/entities/Orders", new { Description = "Allowed edit", RequireChangeTracking = true }, HttpStatusCode.OK, cache),
                "entity-rename" => new(HttpMethod.Post, "/entities/Invoices/rename", new { NewName = "Renamed" }, HttpStatusCode.OK, FakeApiServer.RenameEntityPath, cache),
                "property-create" => new(HttpMethod.Post, "/entities/Orders/properties", new { Name = "Notes", Type = "String" }, HttpStatusCode.Created, FakeApiServer.GeneratePropertyPath, cache),
                "property-update" => new(HttpMethod.Put, "/entities/Orders/properties/Amount", new { Description = "Allowed edit", Minimum = 1, Maximum = 99 }, HttpStatusCode.OK, cache),
                "property-rename" => new(HttpMethod.Post, "/entities/Orders/properties/Paid/rename", new { NewName = "Settled" }, HttpStatusCode.OK, FakeApiServer.RenameEntityPropertyPath, cache),
                "constraints" => new(HttpMethod.Put, "/entities/Orders/constraints", new { Constraints = new[] { new { Type = "Unique", Properties = new[] { "Amount" } } } }, HttpStatusCode.OK, FakeApiServer.GenerateConstraintsPath, cache),
                "default-order" => new(HttpMethod.Put, "/entities/Orders/default-order", new { Items = new[] { new { Property = "Amount", Direction = "desc" } } }, HttpStatusCode.OK, cache),
                "security-settings" => new(HttpMethod.Put, "/security/settings", new
                {
                    AuthTokenExpireMinutes = 123, ForceSingleLogin = true, AllowLoginUnconfirmedEmail = false,
                    AllowUserRegister = false, MaxAllowedFileSizeInKB = 512, ClientIPsLogic = "Block", ClientIPs = Array.Empty<string>()
                }, HttpStatusCode.OK, cache),
                "security-rules" => new(HttpMethod.Put, "/security/rules", new
                {
                    Rules = new[] { new { Type = "Entity", Name = "Orders", RoleID = "AUTHENTICATED", Action = "get", Record = "All", Properties = new[] { "Amount" } } }
                }, HttpStatusCode.OK, cache),
                "report-create" => new(HttpMethod.Post, "/reports", report, HttpStatusCode.Created),
                "report-update" => new(HttpMethod.Put, $"/reports/{reportId}", report, HttpStatusCode.OK),
                "report-layout" => new(HttpMethod.Put, "/reports/layout", new { Items = new[] { new { ID = reportId, X = 2, Y = 3, Width = 4, Height = 5 } } }, HttpStatusCode.NoContent),
                "email-settings" => new(HttpMethod.Put, "/email-settings", new
                {
                    MailServer = "smtp.agent.test", MailServerPort = 587, MailFromAddress = "agent@example.test",
                    MailFromDisplayName = "Agent", MailUserName = "agent", MailPassword = "test-password", EmailConfirmationRedirectUrl = "https://example.test/confirmed"
                }, HttpStatusCode.OK, cache),
                "application-settings" => new(HttpMethod.Put, string.Empty, new { Name = "Allowed application" }, HttpStatusCode.OK, cache),
                "cache-reset" => new(HttpMethod.Post, "/cache-reset", null, HttpStatusCode.NoContent, cache),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        private void ScriptApiServer()
        {
            _portal.ApiServer.Respond(FakeApiServer.GetSystemPropertiesAndConstraintsPath, HttpStatusCode.OK,
                JsonSerializer.Serialize(new { Properties = new[] { EntityScene.Property("ID", PropertyType.Number, isSystem: true, isPrimaryKey: true) }, Constraints = Array.Empty<EntityConstraint>() }));
            foreach (var path in new[] { FakeApiServer.GenerateEntityPath, FakeApiServer.RenameEntityPath,
                FakeApiServer.GeneratePropertyPath, FakeApiServer.RenameEntityPropertyPath, FakeApiServer.GenerateConstraintsPath })
            {
                _portal.ApiServer.Respond(path, HttpStatusCode.OK, string.Empty);
            }
            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
        }

        private async Task<string> SnapshotAsync(long appId)
        {
            return await _portal.WithDbContextAsync(async db => JsonSerializer.Serialize(new
            {
                Application = await db.Applications.AsNoTracking().Where(x => x.ID == appId).Select(x => new
                {
                    x.Name, x.Security, x.AuthTokenExpireMinutes, x.ForceSingleLogin, x.AllowLoginUnconfirmedEmail,
                    x.AllowUserRegister, x.MaxAllowedFileSizeInKB, x.ClientIPsLogic, x.ClientIPsValue,
                    x.MailServer, x.MailServerPort, x.MailFromAddress, x.MailFromDisplayName,
                    x.MailUserName, x.MailPassword, x.EmailConfirmationRedirectUrl
                }).SingleAsync(),
                Entities = await db.Entities.AsNoTracking().Where(x => x.AppID == appId).OrderBy(x => x.ID).Select(x => new
                {
                    x.ID, x.Name, x.Description, x.RequireChangeTracking, x.EntConstraints, x.EntDefaultOrder,
                    Properties = x.Properties.OrderBy(p => p.ID).Select(p => new { p.ID, p.Name, p.Description, p.Minimum, p.Maximum }).ToList()
                }).ToListAsync(),
                Reports = await db.Reports.AsNoTracking().Where(x => x.AppID == appId).OrderBy(x => x.ID).Select(x => new
                {
                    x.ID, x.Title, x.TypeID, x.MaxRecords, x.X, x.Y, x.W, x.H,
                    Series = x.Series.OrderBy(s => s.ID).Select(s => new { s.Entity, s.Label, s.GroupBy, s.Property }).ToList()
                }).ToListAsync()
            }));
        }

        private async Task AssertSavedAsync(Scene scene, string operation)
        {
            var entities = await scene.Entity.LoadEntitiesAsync();
            var orders = Assert.Single(entities, x => x.Name == "Orders");
            switch (operation)
            {
                case "entity-create":
                    Assert.Contains(entities, x => x.Name == "Products" && x.Description == "Allowed creation");
                    break;
                case "entity-update":
                    Assert.Equal("Allowed edit", orders.Description);
                    Assert.True(orders.RequireChangeTracking);
                    break;
                case "entity-rename":
                    Assert.Contains(entities, x => x.Name == "Renamed");
                    Assert.DoesNotContain(entities, x => x.Name == "Invoices");
                    break;
                case "property-create":
                    Assert.Contains(orders.Properties, x => x.Name == "Notes" && x.TypeID == (int)PropertyType.String);
                    break;
                case "property-update":
                    var amount = Assert.Single(orders.Properties, x => x.Name == "Amount");
                    Assert.Equal("Allowed edit", amount.Description);
                    Assert.Equal(1, amount.Minimum);
                    Assert.Equal(99, amount.Maximum);
                    break;
                case "property-rename":
                    Assert.Contains(orders.Properties, x => x.Name == "Settled");
                    Assert.DoesNotContain(orders.Properties, x => x.Name == "Paid");
                    break;
                case "constraints":
                    Assert.Contains(orders.Constraints ?? new List<EntityConstraint>(), x => !x.IsSystem && x.Properties == "Amount");
                    break;
                case "default-order":
                    Assert.Equal("[{\"Property\":\"Amount\",\"Direction\":\"desc\"}]", orders.EntDefaultOrder);
                    break;
                case "report-create":
                case "report-update":
                    var report = await _portal.WithDbContextAsync(db => db.Reports.AsNoTracking().Include(x => x.Series).SingleAsync(x => x.AppID == scene.Entity.AppId && x.Title == "Allowed report"));
                    Assert.Equal(25, report.MaxRecords);
                    Assert.Equal("ID.Count", Assert.Single(report.Series).Property);
                    break;
                case "report-layout":
                    var layout = await _portal.WithDbContextAsync(db => db.Reports.AsNoTracking().SingleAsync(x => x.ID == scene.ReportId));
                    Assert.Equal((2, 3, 4, 5), (layout.X, layout.Y, layout.W, layout.H));
                    break;
                default:
                    var app = await _portal.WithDbContextAsync(db => db.Applications.AsNoTracking().SingleAsync(x => x.ID == scene.Entity.AppId));
                    if (operation == "security-settings")
                    {
                        Assert.Equal(123, app.AuthTokenExpireMinutes);
                    }
                    else if (operation == "security-rules")
                    {
                        var rule = Assert.Single(JsonSerializer.Deserialize<List<DBWS_Security>>(app.Security ?? string.Empty) ?? new List<DBWS_Security>());
                        Assert.Equal("Orders", rule.Name);
                        Assert.Equal("Amount", rule.Properties);
                    }
                    else if (operation == "email-settings")
                    {
                        Assert.Equal("smtp.agent.test", app.MailServer);
                        Assert.Equal("test-password", app.MailPassword);
                    }
                    else
                    {
                        Assert.Equal("Allowed application", app.Name);
                    }
                    break;
            }
        }
    }
}
