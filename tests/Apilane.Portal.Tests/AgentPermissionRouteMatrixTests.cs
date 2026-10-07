using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    public partial class AgentPermissionsApiTests
    {
        // This is the expected public policy, independent of the controller annotations. A new
        // route, wrong area, or read-only annotation on a mutation must require a policy decision.
        private static readonly PermissionRoute[] ExpectedPermissionRoutes =
        {
            Human("POST", "/clones"),
            Human("GET", "/clones/{operationId}"),
            Read("/comparison", "schema", "entities", "security", "custom-endpoints"),
            Read("/custom-endpoints", "custom-endpoints"),
            Read("/custom-endpoints/{id}", "custom-endpoints"),
            Write("POST", "/custom-endpoints", "custom-endpoints"),
            Write("PUT", "/custom-endpoints/{id}", "custom-endpoints"),
            Delete("/custom-endpoints/{id}", "custom-endpoints"),
            new("POST", "/custom-endpoints/preview", AgentPermissionAccess.Read, new[] { "custom-endpoints" }),
            Read("/email-settings", "email-settings"),
            Human("PUT", "/email-settings"),
            new("GET", "/permissions", null, Array.Empty<string>(), Discovery: true),
            Read("/reports", "reports"),
            Read("/reports/{reportId}", "reports"),
            Write("POST", "/reports", "reports"),
            Write("PUT", "/reports/{reportId}", "reports"),
            Delete("/reports/{reportId}", "reports"),
            Write("PUT", "/reports/layout", "reports"),
            Read("/security", "security"),
            Write("PUT", "/security/settings", "security"),
            Write("PUT", "/security/rules", "security"),
            Human("GET", "/collaborators"),
            Human("POST", "/collaborators"),
            Human("GET", "/collaborators/available-agents"),
            Human("PUT", "/collaborators/{id}/permissions"),
            Human("DELETE", "/collaborators/{id}"),
            Read("/entities", "entities"),
            Read("/entities/{entity}", "entities"),
            Write("POST", "/entities", "entities"),
            Write("PUT", "/entities/{entity}", "entities"),
            Delete("/entities/{entity}", "entities"),
            Human("POST", "/entities/{entity}/rename"),
            Read("/entities/{entity}/constraints", "entities"),
            Write("PUT", "/entities/{entity}/constraints", "entities"),
            Read("/entities/{entity}/default-order", "entities"),
            Write("PUT", "/entities/{entity}/default-order", "entities"),
            Read("/entities/{entity}/report-fields", "reports"),
            new("GET", string.Empty, null, Array.Empty<string>(), Discovery: true),
            Write("PUT", string.Empty, "application"),
            Human("DELETE", string.Empty),
            Human("GET", "/connection-info"),
            Read("/audit-log", "audit-log"),
            Write("POST", "/cache-reset", "application"),
            Write("PUT", "/status", "application"),
            Write("POST", "/rebuild", "rebuild"),
            Read("/entities/{entity}/properties", "entities"),
            Read("/entities/{entity}/properties/{property}", "entities"),
            Write("POST", "/entities/{entity}/properties", "entities"),
            Write("PUT", "/entities/{entity}/properties/{property}", "entities"),
            Delete("/entities/{entity}/properties/{property}", "entities"),
            Human("POST", "/entities/{entity}/properties/{property}/rename"),
            Read("/schema-import/diff", "schema", "entities", "security", "custom-endpoints"),
            Write("POST", "/schema-import", "schema")
        };

        [Fact]
        public void Every_Application_Route_Should_Require_The_Expected_Resource_And_Operation()
        {
            var actions = _portal.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
                .ApiDescriptionGroups.Items.SelectMany(x => x.Items)
                .Where(x => (x.RelativePath ?? string.Empty).StartsWith("api/v1/applications/{appToken}", StringComparison.Ordinal))
                .ToList();

            Assert.Equal(ExpectedPermissionRoutes.Select(x => $"{x.Method} {x.Path}").OrderBy(x => x, StringComparer.Ordinal),
                actions.Select(x => $"{x.HttpMethod} {RelativePermissionPath(x.RelativePath ?? string.Empty)}").OrderBy(x => x, StringComparer.Ordinal));

            foreach (var expected in ExpectedPermissionRoutes)
            {
                var action = Assert.Single(actions, x => x.HttpMethod == expected.Method && RelativePermissionPath(x.RelativePath ?? string.Empty) == expected.Path);
                var metadata = action.ActionDescriptor.EndpointMetadata;
                Assert.Equal(expected.Discovery, metadata.OfType<AgentPermissionDiscoveryAttribute>().Any());
                Assert.Equal(expected.Access is null && !expected.Discovery, metadata.OfType<NoAgentAttribute>().Any());
                // NoAgent takes precedence over any permission inherited from its controller.
                var requirements = metadata.OfType<NoAgentAttribute>().Any()
                    ? new List<AgentPermissionAttribute>() : metadata.OfType<AgentPermissionAttribute>().ToList();
                Assert.Equal(expected.Resources.OrderBy(x => x, StringComparer.Ordinal), requirements.Select(x => x.Resource).OrderBy(x => x, StringComparer.Ordinal));
                Assert.All(requirements, requirement => Assert.Equal(expected.Access, requirement.GetAccess(expected.Method)));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Every_Gated_Route_Should_Refuse_Missing_Or_Insufficient_Rights_Before_Binding(bool retainRead)
        {
            var scene = await CreateSceneAsync();
            var catalogue = (await ReadPermissionsAsync(scene.Agent, scene.First)).Resources;

            foreach (var route in ExpectedPermissionRoutes.Where(x => x.Access.HasValue && (!retainRead || x.Access != AgentPermissionAccess.Read)))
            {
                // Grant every unrelated area: accidentally assigning the route to another area
                // must fail this test. Read alone must also never permit a mutation or deletion.
                var grants = catalogue.Select(resource => Grant(resource.Resource,
                    read: resource.CanRead && (retainRead || !route.Resources.Contains(resource.Resource)),
                    write: resource.CanWrite && !route.Resources.Contains(resource.Resource),
                    delete: resource.CanDelete && !route.Resources.Contains(resource.Resource))).ToArray();
                await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, grants);
                var auditBefore = await scene.Schema.AuditAsync(scene.First);
                var schemaBefore = await scene.Schema.StoredSchemaAsync(scene.First);

                using var request = PermissionRequest(route, scene.First.Token);
                using var response = await scene.Agent.SendAsync(request);
                Assert.True(response.StatusCode == System.Net.HttpStatusCode.Forbidden,
                    $"{route.Method} {route.Path}, retainRead={retainRead}: expected 403 before binding, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                await AssertForbiddenAsync(response);
                Assert.Empty(_portal.ApiServer.Requests);
                Assert.Equal(schemaBefore, await scene.Schema.StoredSchemaAsync(scene.First));
                Assert.Equal(auditBefore, await scene.Schema.AuditAsync(scene.First));
            }
        }

        [Fact]
        public async Task Every_Read_Operation_Should_Work_With_Only_Its_Required_Resources()
        {
            var scene = await CreateSceneAsync();
            var report = new DBWS_ReportPanel
            {
                AppID = scene.First.ID, Title = "Readable report", TypeID = (int)ReportType.Grid,
                W = 6, H = 4, MaxRecords = 10, DateModified = DateTime.UtcNow
            };
            await _portal.WithDbContextAsync(db =>
            {
                db.Reports.Add(report);
                return db.SaveChangesAsync();
            });

            foreach (var route in ExpectedPermissionRoutes.Where(x => x.Access == AgentPermissionAccess.Read))
            {
                var grants = route.Resources.Select(x => Grant(x, read: true)).ToArray();
                await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, grants);
                await ReplaceAsync(scene, scene.Second, scene.SecondCollaborationId, grants);
                var path = route.Path.Replace("{entity}", "Orders", StringComparison.Ordinal)
                    .Replace("{property}", "Amount", StringComparison.Ordinal)
                    .Replace("{id}", Assert.Single(scene.First.CustomEndpoints).ID.ToString(), StringComparison.Ordinal)
                    .Replace("{reportId}", report.ID.ToString(), StringComparison.Ordinal);
                path += route.Path switch
                {
                    "/comparison" => $"?Target={scene.Second.Token}",
                    "/schema-import/diff" => $"?Source={scene.Second.Token}",
                    "/entities/{entity}/report-fields" => "?Type=Grid",
                    _ => string.Empty
                };
                _portal.ResetApiServer();
                _portal.ApiServer.Respond(FakeApiServer.StatsDistinctPath, HttpStatusCode.OK, "[]");
                var auditBefore = await scene.Schema.AuditAsync(scene.First);
                using var request = new HttpRequestMessage(new HttpMethod(route.Method), Url(scene.First) + path);
                if (route.Method == "POST")
                {
                    request.Content = new { Name = "Preview", Query = "SELECT 1" }.ToJsonContent();
                }
                using var response = await scene.Agent.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.OK,
                    $"{route.Method} {route.Path}: expected 200 with only its required Read grants, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                Assert.Equal(auditBefore, await scene.Schema.AuditAsync(scene.First));
                Assert.All(_portal.ApiServer.Requests, upstream => Assert.Equal(FakeApiServer.StatsDistinctPath, upstream.Path));
            }
        }

        [Fact]
        public async Task Every_Human_Only_Route_Should_Stay_Forbidden_With_All_Agent_Grants()
        {
            var scene = await CreateSceneAsync();
            var catalogue = (await ReadPermissionsAsync(scene.Agent, scene.First)).Resources;
            await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId,
                catalogue.Select(x => Grant(x.Resource, x.CanRead, x.CanWrite, x.CanDelete)).ToArray());
            var auditBefore = await scene.Schema.AuditAsync(scene.First);

            foreach (var route in ExpectedPermissionRoutes.Where(x => x.Access is null && !x.Discovery))
            {
                using var request = PermissionRequest(route, scene.First.Token);
                using var response = await scene.Agent.SendAsync(request);
                await AssertForbiddenAsync(response);
            }

            Assert.Equal(auditBefore, await scene.Schema.AuditAsync(scene.First));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Discovery_Should_Match_The_Entire_Policy_Matrix_For_Each_Independent_Grant()
        {
            var scene = await CreateSceneAsync();
            var catalogue = (await ReadPermissionsAsync(scene.Agent, scene.First)).Resources;
            var policies = new List<AgentPermissionGrant[]> { Array.Empty<AgentPermissionGrant>() };
            foreach (var resource in catalogue)
            {
                if (resource.CanRead)
                {
                    policies.Add(new[] { Grant(resource.Resource, read: true) });
                }
                if (resource.CanWrite)
                {
                    policies.Add(new[] { Grant(resource.Resource, read: resource.CanRead, write: true) });
                }
                if (resource.CanDelete)
                {
                    policies.Add(new[] { Grant(resource.Resource, read: resource.CanRead, delete: true) });
                }
            }
            policies.Add(catalogue.Select(x => Grant(x.Resource, x.CanRead, x.CanWrite, x.CanDelete)).ToArray());

            foreach (var policy in policies)
            {
                await ReplaceAsync(scene, scene.First, scene.FirstCollaborationId, policy);
                var discovery = await ReadPermissionsAsync(scene.Agent, scene.First);
                var operations = discovery.Operations.Where(x => !x.Path.EndsWith("/permission-unclassified-test", StringComparison.Ordinal)).ToList();
                Assert.Equal(ExpectedPermissionRoutes.Length, operations.Count);
                foreach (var expected in ExpectedPermissionRoutes)
                {
                    var operation = Assert.Single(operations, x => x.Method == expected.Method && RelativePermissionPath(x.Path) == expected.Path);
                    var allowed = expected.Discovery || (expected.Access.HasValue && expected.Resources.All(resource =>
                        policy.Any(grant => grant.Resource == resource && (expected.Access switch
                        {
                            AgentPermissionAccess.Read => grant.Read,
                            AgentPermissionAccess.Write => grant.Write,
                            AgentPermissionAccess.Delete => grant.Delete,
                            _ => false
                        }))));
                    Assert.True(operation.AllowedForThisApplication == allowed,
                        $"Incorrect discovery for {expected.Method} {expected.Path} with {string.Join(", ", policy.Select(x => $"{x.Resource}: read={x.Read}, write={x.Write}, delete={x.Delete}"))}");
                    Assert.Equal(expected.Resources.OrderBy(x => x, StringComparer.Ordinal), operation.Requirements.Select(x => x.Resource).OrderBy(x => x, StringComparer.Ordinal));
                    Assert.All(operation.Requirements, requirement => Assert.Equal(expected.Access, requirement.Access));
                }
            }
        }

        private record PermissionRoute(string Method, string Path, AgentPermissionAccess? Access, string[] Resources, bool Discovery = false);

        private static PermissionRoute Read(string path, params string[] resources)
        {
            return new PermissionRoute("GET", path, AgentPermissionAccess.Read, resources);
        }

        private static PermissionRoute Write(string method, string path, string resource)
        {
            return new PermissionRoute(method, path, AgentPermissionAccess.Write, new[] { resource });
        }

        private static PermissionRoute Delete(string path, string resource)
        {
            return new PermissionRoute("DELETE", path, AgentPermissionAccess.Delete, new[] { resource });
        }

        private static PermissionRoute Human(string method, string path)
        {
            return new PermissionRoute(method, path, null, Array.Empty<string>());
        }

        private static string RelativePermissionPath(string path)
        {
            var normalized = Regex.Replace(path.TrimStart('/'), @"\{([^}:]+):[^}]+\}", "{$1}");
            return normalized["api/v1/applications/{appToken}".Length..];
        }

        private static HttpRequestMessage PermissionRequest(PermissionRoute route, string appToken)
        {
            var path = route.Path.Replace("{entity}", "Orders", StringComparison.Ordinal)
                .Replace("{property}", "Amount", StringComparison.Ordinal)
                .Replace("{id}", "1", StringComparison.Ordinal)
                .Replace("{reportId}", "1", StringComparison.Ordinal)
                .Replace("{operationId}", Guid.Empty.ToString(), StringComparison.Ordinal);
            var request = new HttpRequestMessage(new HttpMethod(route.Method), $"/api/v1/applications/{appToken}{path}");
            if (route.Method is "POST" or "PUT")
            {
                // A bad payload would produce 400 if authorization accidentally let it through.
                request.Content = new StringContent("{", Encoding.UTF8, "application/json");
            }
            return request;
        }
    }
}
