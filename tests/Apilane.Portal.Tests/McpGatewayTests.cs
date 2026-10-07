using Apilane.Common.Models;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class McpGatewayTests
    {
        private readonly PortalFactory _portal;

        public McpGatewayTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Theory]
        [InlineData("/api/mcp")]
        [InlineData("/API/MCP")]
        [InlineData("/api/mcp/")]
        [InlineData("/API/MCP/")]
        public async Task Endpoint_Should_Require_A_Delegated_Token_Instead_Of_A_Portal_Cookie_Or_Agent_Key(string endpoint)
        {
            var scene = await CreateSceneAsync();
            using var anonymous = _portal.CreateCookielessClient();
            McpTestClient.Configure(anonymous);
            var request = new { jsonrpc = "2.0", id = 1, method = "initialize", @params = McpTestClient.Initialization() };

            using var missing = await anonymous.PostAsync(endpoint, request.ToJsonContent());
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            Assert.Contains("resource_metadata=", missing.Headers.WwwAuthenticate.ToString());
            Assert.Null(missing.Headers.Location);

            McpTestClient.Configure(scene.Owner);
            using var cookie = await scene.Owner.PostAsync(endpoint, request.ToJsonContent());
            Assert.Equal(HttpStatusCode.Unauthorized, cookie.StatusCode);

            anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scene.Agent.Key);
            using var agentKey = await anonymous.PostAsync(endpoint, request.ToJsonContent());
            Assert.Equal(HttpStatusCode.Unauthorized, agentKey.StatusCode);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Current_Protocol_Should_List_And_Call_Tools_Without_An_Initialize_Handshake()
        {
            var scene = await CreateSceneAsync();
            var (otherOwner, _) = await _portal.CreateUserAsync();
            var server = await _portal.CreateServerAsync();
            var outside = (await _portal.CreateApplicationAsync(server.ID, otherOwner, "Outside current protocol consent", scene.Agent.Email)).Application;
            using var mcp = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            mcp.Client.DefaultRequestHeaders.Remove("MCP-Protocol-Version");
            mcp.Client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2026-07-28");
            var metadata = new Dictionary<string, object>
            {
                ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                ["io.modelcontextprotocol/clientInfo"] = new { name = "Apilane current protocol test", version = "1.0" },
                ["io.modelcontextprotocol/clientCapabilities"] = new { }
            };

            var listed = await mcp.RequestAsync("tools/list", new { _meta = metadata });
            Assert.Equal(new[] { "apilane_call", "apilane_operations" },
                listed.GetProperty("tools").EnumerateArray().Select(x => x.GetProperty("name").GetString()).OrderBy(x => x));
            var call = await mcp.RequestAsync("tools/call", new
            {
                _meta = metadata,
                name = "apilane_call",
                arguments = new { operationId = "PortalApplications_List" }
            });
            Assert.False(IsError(call));
            var result = Data(call);
            Assert.Equal(200, result.GetProperty("status").GetInt32());
            var application = Assert.Single(result.GetProperty("data").GetProperty("Data").EnumerateArray());
            Assert.Equal(scene.Shared.Token, application.GetProperty("Token").GetString());
            Assert.DoesNotContain(outside.Token, result.ToString());
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Initialize_And_Tools_Should_Support_The_2025_11_25_Streamable_Http_Client()
        {
            var scene = await CreateSceneAsync();
            using var mcp = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);

            var initialization = await mcp.InitializeAsync();
            Assert.True(initialization.GetProperty("capabilities").TryGetProperty("tools", out _));
            Assert.False(string.IsNullOrEmpty(initialization.GetProperty("serverInfo").GetProperty("name").GetString()));
            var listed = await mcp.RequestAsync("tools/list");
            var tools = listed.GetProperty("tools").EnumerateArray().ToArray();
            Assert.Equal(new[] { "apilane_call", "apilane_operations" }, tools.Select(x => x.GetProperty("name").GetString()).OrderBy(x => x));
            Assert.All(tools, x => Assert.Equal("object", x.GetProperty("inputSchema").GetProperty("type").GetString()));

            var catalog = Data(await mcp.ToolAsync("apilane_operations"));
            var operations = catalog.GetProperty("operations").EnumerateArray().ToArray();
            Assert.Contains(operations, x => x.GetProperty("operationId").GetString() == "PortalApplication_Get");
            Assert.Contains(operations, x => x.GetProperty("operationId").GetString() == "ApplicationReports_Get");
            Assert.All(operations, x => Assert.DoesNotContain(":long", x.GetProperty("path").GetString() ?? string.Empty));

            var described = Data(await mcp.ToolAsync("apilane_operations", new { operationId = "PortalApplication_SetStatus" }));
            var operation = Assert.Single(described.GetProperty("operations").EnumerateArray());
            Assert.Equal("PortalApplication_SetStatus", operation.GetProperty("operationId").GetString());
            Assert.Equal("PUT", operation.GetProperty("method").GetString());
            Assert.Equal("application", Assert.Single(operation.GetProperty("permissions").EnumerateArray()).GetProperty("resource").GetString());
            Assert.True(operation.GetProperty("contract").TryGetProperty("requestBody", out _));
            Assert.True(described.GetProperty("schemas").EnumerateObject().Any());

            var read = Data(await mcp.ToolAsync("apilane_call", new
            {
                operationId = "PortalApplication_Get", path = new { appToken = scene.Shared.Token }
            }));
            Assert.Equal(200, read.GetProperty("status").GetInt32());
            Assert.Equal(scene.Shared.Token, read.GetProperty("data").GetProperty("Token").GetString());
            Assert.False(read.GetProperty("data").GetProperty("IsOwner").GetBoolean());
            Assert.DoesNotContain(scene.EncryptionKey, read.ToString());
            Assert.DoesNotContain(scene.Shared.ConnectionString ?? string.Empty, read.ToString());
        }

        [Fact]
        public async Task NoAgent_And_Administrator_Operations_Should_Be_Absent_And_Uncallable()
        {
            var scene = await CreateSceneAsync();
            using var mcp = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            await mcp.InitializeAsync();
            var catalog = Data(await mcp.ToolAsync("apilane_operations"));
            var ids = catalog.GetProperty("operations").EnumerateArray().Select(x => x.GetProperty("operationId").GetString()).ToArray();
            var refused = new[]
            {
                "AdminApplications_List", "PortalApplications_Create", "PortalApplication_Delete",
                "PortalApplication_GetConnectionInfo", "Entities_Rename", "Properties_Rename",
                "ApplicationEmailSettings_Update", "Collaborators_UpdatePermissions"
            };
            foreach (var operationId in refused)
            {
                Assert.DoesNotContain(operationId, ids);
                var call = await mcp.ToolAsync("apilane_call", new
                {
                    operationId, path = new { appToken = scene.Shared.Token }, body = new { Name = "Changed" }
                });
                Assert.True(IsError(call), $"{operationId} was callable by MCP.");
            }
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(scene.Shared.Name, await _portal.WithDbContextAsync(db => db.Applications
                .Where(x => x.ID == scene.Shared.ID).Select(x => x.Name).SingleAsync()));
        }

        [Fact]
        public async Task Calls_Should_Apply_Live_Grants_And_Mvc_Validation_And_Audit_As_The_Agent()
        {
            var scene = await CreateSceneAsync();
            using var mcp = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            await mcp.InitializeAsync();
            var arguments = new { operationId = "PortalApplication_SetStatus", path = new { appToken = scene.Shared.Token }, body = new { Online = false } };

            var denied = await mcp.ToolAsync("apilane_call", arguments);
            Assert.True(IsError(denied));
            Assert.Equal(403, Data(denied).GetProperty("status").GetInt32());
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.True(await IsOnlineAsync(scene.Shared.ID));

            await SetApplicationPermissionAsync(scene, write: true);
            var invalid = await mcp.ToolAsync("apilane_call", new
            {
                operationId = "PortalApplication_SetStatus", path = new { appToken = scene.Shared.Token }, body = new { Online = "not a boolean" }
            });
            Assert.True(IsError(invalid));
            Assert.Equal(400, Data(invalid).GetProperty("status").GetInt32());
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.True(await IsOnlineAsync(scene.Shared.ID));

            _portal.ApiServer.Respond(FakeApiServer.ClearCachePath, HttpStatusCode.OK, "\"OK\"");
            var allowed = await mcp.ToolAsync("apilane_call", arguments);
            Assert.False(IsError(allowed));
            Assert.Equal(200, Data(allowed).GetProperty("status").GetInt32());
            Assert.False(await IsOnlineAsync(scene.Shared.ID));
            Assert.Single(_portal.ApiServer.RequestsTo(FakeApiServer.ClearCachePath));
            var audit = await _portal.WithDbContextAsync(db => db.AuditLogs.AsNoTracking()
                .Where(x => x.UserId == scene.Agent.ID && x.EntityType == "Application").ToListAsync());
            Assert.Equal(scene.Agent.Email, Assert.Single(audit).UserEmail);

            await SetApplicationPermissionAsync(scene, write: false);
            _portal.ResetApiServer();
            var revoked = await mcp.ToolAsync("apilane_call", new
            {
                operationId = "PortalApplication_SetStatus", path = new { appToken = scene.Shared.Token }, body = new { Online = true }
            });
            Assert.True(IsError(revoked));
            Assert.Equal(403, Data(revoked).GetProperty("status").GetInt32());
            Assert.False(await IsOnlineAsync(scene.Shared.ID));
            Assert.Empty(_portal.ApiServer.Requests);
            Assert.Equal(audit.Count, await _portal.WithDbContextAsync(db => db.AuditLogs.CountAsync(x => x.UserId == scene.Agent.ID)));
        }

        [Fact]
        public async Task Application_List_And_Secondary_Targets_Should_Stay_Within_The_Consent_Scope()
        {
            var scene = await CreateSceneAsync();
            var server = await _portal.CreateServerAsync();
            var (otherOwner, _) = await _portal.CreateUserAsync();
            var outside = (await _portal.CreateApplicationAsync(server.ID, otherOwner, "Other owner's application", scene.Agent.Email)).Application;
            using var mcp = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            await mcp.InitializeAsync();
            // Sharing another application after approval must require a new consent grant.
            var addedLater = (await _portal.CreateApplicationAsync(server.ID, scene.OwnerEmail, "Shared after consent", scene.Agent.Email)).Application;

            var list = Data(await mcp.ToolAsync("apilane_call", new { operationId = "PortalApplications_List" }));
            Assert.Equal(200, list.GetProperty("status").GetInt32());
            var visible = Assert.Single(list.GetProperty("data").GetProperty("Data").EnumerateArray());
            Assert.Equal(scene.Shared.Token, visible.GetProperty("Token").GetString());
            foreach (var application in new[] { outside, addedLater })
            {
                var read = await mcp.ToolAsync("apilane_call", new
                {
                    operationId = "PortalApplication_Get", path = new { appToken = application.Token }
                });
                Assert.True(IsError(read));
                Assert.Equal(404, Data(read).GetProperty("status").GetInt32());
                var comparison = await mcp.ToolAsync("apilane_call", new
                {
                    operationId = "ApplicationComparison_Get", path = new { appToken = scene.Shared.Token }, query = new { Target = application.Token }
                });
                Assert.True(IsError(comparison));
                Assert.Equal(404, Data(comparison).GetProperty("status").GetInt32());
            }
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Connection_Or_Share_Revocation_Should_Stop_The_Same_Mcp_Client_Immediately(bool removeShare)
        {
            var scene = await CreateSceneAsync();
            using var mcp = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            await mcp.InitializeAsync();
            Assert.False(IsError(await mcp.ToolAsync("apilane_call", new
            {
                operationId = "PortalApplication_Get", path = new { appToken = scene.Shared.Token }
            })));

            var url = removeShare
                ? $"/api/v1/applications/{scene.Shared.Token}/collaborators/{scene.CollaborationId}"
                : $"/api/v1/mcp/connections/{mcp.ConnectionId}";
            using var revoked = await scene.Owner.DeleteAsync(url);
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
            using var next = await mcp.Client.PostAsync(McpTestClient.Endpoint, new
            {
                jsonrpc = "2.0", id = 99, method = "tools/list", @params = new { }
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        private async Task<Scene> CreateSceneAsync()
        {
            using var admin = await _portal.CreateAdminClientAsync();
            var created = await admin.PostAsync("/api/v1/admin/agents", new { Name = "mcp-" + Guid.NewGuid().ToString("N")[..20] }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var agent = await created.ReadJsonAsync<AgentCreatedResponse>();
            var (ownerEmail, password) = await _portal.CreateUserAsync();
            var owner = await _portal.CreateSignedInClientAsync(ownerEmail, password);
            var server = await _portal.CreateServerAsync();
            var shared = await _portal.CreateApplicationAsync(server.ID, ownerEmail, "MCP application", agent.Email);
            var collaborationId = await _portal.WithDbContextAsync(db => db.Collaborations
                .Where(x => x.AppID == shared.Application.ID && x.UserEmail == agent.Email).Select(x => x.ID).SingleAsync());
            return new Scene(agent, owner, ownerEmail, shared.Application, shared.EncryptionKey, collaborationId);
        }

        private async Task SetApplicationPermissionAsync(Scene scene, bool write)
        {
            using var response = await scene.Owner.PutAsync($"/api/v1/applications/{scene.Shared.Token}/collaborators/{scene.CollaborationId}/permissions", new
            {
                Permissions = new[] { new AgentPermissionGrant { Resource = AgentPermissionResources.Application, Write = write } }
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private Task<bool> IsOnlineAsync(long applicationId)
        {
            return _portal.WithDbContextAsync(db => db.Applications.Where(x => x.ID == applicationId).Select(x => x.Online).SingleAsync());
        }

        private static bool IsError(JsonElement result)
        {
            return result.TryGetProperty("isError", out var error) && error.GetBoolean();
        }

        private static JsonElement Data(JsonElement result)
        {
            Assert.True(result.TryGetProperty("structuredContent", out var data), $"The tool did not return structured content: {result}");
            return data;
        }

        private record Scene(AgentCreatedResponse Agent, HttpClient Owner, string OwnerEmail, DBWS_Application Shared, string EncryptionKey, long CollaborationId);
    }
}
