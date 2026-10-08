using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    [Collection(PortalCollection.Name)]
    public class McpOAuthTests
    {
        private readonly PortalFactory _portal;

        public McpOAuthTests(PortalFactory portal)
        {
            _portal = portal;
        }

        [Theory]
        [InlineData("https://attacker.test/callback")]
        [InlineData("http://127.0.0.1.attacker.test/callback")]
        [InlineData("http://localhost@attacker.test/callback")]
        [InlineData("http://localhost:8080/callback#fragment")]
        [InlineData("http://localhost:8080/callback?code=preexisting")]
        [InlineData("file:///callback")]
        [InlineData(null)]
        public async Task Register_Should_Reject_Unconstrained_Callbacks(string? redirect)
        {
            using var client = _portal.CreateCookielessClient();
            var response = await client.PostAsync("/api/mcp/oauth/register", new { client_name = "Unsafe callback", redirect_uris = new[] { redirect } }.ToJsonContent());
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Theory]
        [InlineData("https://claude.ai/api/mcp/auth_callback")]
        [InlineData("https://claude.com/api/mcp/auth_callback")]
        public async Task Register_Should_Accept_The_Callback_Of_A_Hosted_Client(string redirect)
        {
            using var client = _portal.CreateCookielessClient();
            var response = await client.PostAsync("/api/mcp/oauth/register", new { client_name = "Claude", redirect_uris = new[] { redirect } }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        [Theory]
        [InlineData("https://claude.ai/api/mcp/auth_callback/")]
        [InlineData("https://claude.ai/api/mcp/auth_callback?x=1")]
        [InlineData("https://claude.ai.attacker.test/api/mcp/auth_callback")]
        [InlineData("https://CLAUDE.ai@attacker.test/api/mcp/auth_callback")]
        public async Task Register_Should_Reject_Lookalikes_Of_A_Hosted_Callback(string redirect)
        {
            using var client = _portal.CreateCookielessClient();
            var response = await client.PostAsync("/api/mcp/oauth/register", new { client_name = "Lookalike", redirect_uris = new[] { redirect } }.ToJsonContent());
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Discovery_Should_Use_Configured_Origin_And_Advertise_Pkce()
        {
            using var client = _portal.CreateCookielessClient();
            client.DefaultRequestHeaders.Host = "untrusted.example";
            var response = await client.GetAsync("/.well-known/oauth-authorization-server/api/mcp");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(McpTestClient.Resource, json.RootElement.GetProperty("issuer").GetString());
            Assert.Equal("S256", Assert.Single(json.RootElement.GetProperty("code_challenge_methods_supported").EnumerateArray()).GetString());
            Assert.Equal(McpTestClient.Resource + "/oauth/token", json.RootElement.GetProperty("token_endpoint").GetString());
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Token_Should_Return_OAuth_Error_For_Malformed_Or_Excessive_Form_Fields(bool malformedMultipart)
        {
            using var client = _portal.CreateCookielessClient();
            using var body = malformedMultipart
                ? new StringContent("missing boundary", Encoding.UTF8, "multipart/form-data")
                : new StringContent(string.Join("&", Enumerable.Range(0, 1025).Select(index => "f" + index + "=x")),
                    Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = await client.PostAsync("/api/mcp/oauth/token", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("invalid_request", json.RootElement.GetProperty("error").GetString());
            Assert.True(json.RootElement.TryGetProperty("error_description", out _));
            Assert.False(json.RootElement.TryGetProperty("Message", out _));
        }

        [Fact]
        public async Task Approval_Should_Require_Person_Cookie_And_Csrf_And_Show_Only_Owned_Shared_Apps()
        {
            var scene = await CreateSceneAsync();
            var pending = await BeginAsync();
            using var anonymous = _portal.CreateCookielessClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/mcp/authorize?requestId=" + pending.RequestId)).StatusCode);
            var summary = await (await scene.Owner.GetAsync("/api/v1/mcp/authorize?requestId=" + pending.RequestId)).ReadJsonAsync<McpAuthorizationResponse>();
            var agent = Assert.Single(summary.Agents);
            Assert.Equal(scene.Agent.ID, agent.Id);
            Assert.Equal(scene.Application.Application.Token, Assert.Single(agent.Applications).Token);

            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
            Assert.Equal(HttpStatusCode.Forbidden, (await scene.Owner.PostAsync("/api/v1/mcp/authorize", new McpApproveRequest
            {
                RequestId = pending.RequestId, AgentId = scene.Agent.ID, Name = "Blocked request",
                ApplicationTokens = [scene.Application.Application.Token]
            }.ToJsonContent())).StatusCode);
        }

        [Fact]
        public async Task Code_Should_Require_Exact_Redirect_Resource_And_Pkce_And_Revoke_On_Replay()
        {
            var scene = await CreateSceneAsync();
            var pending = await BeginAsync();
            var response = await scene.Owner.PostAsync("/api/v1/mcp/authorize", new McpApproveRequest
            {
                RequestId = pending.RequestId, AgentId = scene.Agent.ID, Name = "PKCE regression",
                ApplicationTokens = [scene.Application.Application.Token]
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var redirect = await response.ReadJsonAsync<McpRedirectResponse>();
            var code = QueryHelpers.ParseQuery(new Uri(redirect.RedirectUrl).Query)["code"].ToString();
            using var oauth = _portal.CreateCookielessClient();
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = pending.ClientId, ["code"] = code,
                ["redirect_uri"] = McpTestClient.RedirectUri, ["resource"] = McpTestClient.Resource,
                ["code_verifier"] = new string('x', 43)
            };
            Assert.Equal(HttpStatusCode.BadRequest, (await oauth.PostAsync("/api/mcp/oauth/token", new FormUrlEncodedContent(form))).StatusCode);
            form["code_verifier"] = pending.Verifier;
            form["redirect_uri"] = "http://127.0.0.1:54322/callback";
            Assert.Equal(HttpStatusCode.BadRequest, (await oauth.PostAsync("/api/mcp/oauth/token", new FormUrlEncodedContent(form))).StatusCode);
            form["redirect_uri"] = McpTestClient.RedirectUri;
            form["resource"] = "https://different.test/api/mcp";
            Assert.Equal(HttpStatusCode.BadRequest, (await oauth.PostAsync("/api/mcp/oauth/token", new FormUrlEncodedContent(form))).StatusCode);
            form["resource"] = McpTestClient.Resource;
            var exchange = await oauth.PostAsync("/api/mcp/oauth/token", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            var tokens = await exchange.ReadJsonAsync<McpTokenResponse>();
            Assert.NotNull(await AuthenticateAsync(tokens.AccessToken));
            Assert.Equal(HttpStatusCode.BadRequest, (await oauth.PostAsync("/api/mcp/oauth/token", new FormUrlEncodedContent(form))).StatusCode);
            Assert.Null(await AuthenticateAsync(tokens.AccessToken));
        }

        [Fact]
        public async Task Refresh_Should_Rotate_And_Replay_Should_Revoke_The_Whole_Connection()
        {
            var scene = await CreateSceneAsync();
            using var connection = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            using var oauth = _portal.CreateCookielessClient();
            var wrongClient = await RefreshAsync(oauth, "different-client", connection.Tokens.RefreshToken);
            Assert.Equal(HttpStatusCode.BadRequest, wrongClient.StatusCode);
            var refreshed = await RefreshAsync(oauth, connection.ClientId, connection.Tokens.RefreshToken);
            Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            var tokens = await refreshed.ReadJsonAsync<McpTokenResponse>();
            Assert.NotEqual(connection.Tokens.RefreshToken, tokens.RefreshToken);
            Assert.NotNull(await AuthenticateAsync(tokens.AccessToken));
            Assert.Null(await AuthenticateAsync(tokens.RefreshToken));
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(oauth, connection.ClientId, connection.Tokens.RefreshToken)).StatusCode);
            Assert.Null(await AuthenticateAsync(tokens.AccessToken));
            Assert.Null(await AuthenticateAsync(connection.Tokens.AccessToken));
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(oauth, connection.ClientId, tokens.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task Storage_Should_Contain_Hashes_And_Owner_Revoke_Should_Invalidate_Both_Tokens()
        {
            var scene = await CreateSceneAsync();
            using var connection = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            var hashes = await _portal.WithDbContextAsync(db => db.McpTokens.AsNoTracking().Where(x => x.ConnectionId == connection.ConnectionId).Select(x => x.Hash).ToListAsync());
            Assert.Equal(2, hashes.Count);
            Assert.All(hashes, hash => Assert.Matches("^[0-9A-F]{64}$", hash));
            Assert.DoesNotContain(connection.Tokens.AccessToken, hashes);
            Assert.DoesNotContain(connection.Tokens.RefreshToken, hashes);
            Assert.NotNull(await AuthenticateAsync(connection.Tokens.AccessToken));

            using var other = await _portal.CreateUserClientAsync();
            var others = await (await other.GetAsync("/api/v1/mcp/connections")).ReadJsonAsync<List<McpConnectionResponse>>();
            Assert.DoesNotContain(others, x => x.Id == connection.ConnectionId);
            Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync("/api/v1/mcp/connections/" + connection.ConnectionId)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await scene.Owner.DeleteAsync("/api/v1/mcp/connections/" + connection.ConnectionId)).StatusCode);
            var listed = await (await scene.Owner.GetAsync("/api/v1/mcp/connections")).ReadJsonAsync<List<McpConnectionResponse>>();
            var revoked = Assert.Single(listed, item => item.Id == connection.ConnectionId);
            // SQLite loses DateTime.Kind: the wire response must restore UTC for all four dates.
            Assert.Equal(DateTimeKind.Utc, revoked.CreatedAt.Kind);
            Assert.Equal(DateTimeKind.Utc, revoked.ExpiresAt.Kind);
            Assert.Equal(DateTimeKind.Utc, Assert.IsType<DateTime>(revoked.LastUsedAt).Kind);
            Assert.Equal(DateTimeKind.Utc, Assert.IsType<DateTime>(revoked.RevokedAt).Kind);
            Assert.Null(await AuthenticateAsync(connection.Tokens.AccessToken));
            using var oauth = _portal.CreateCookielessClient();
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(oauth, connection.ClientId, connection.Tokens.RefreshToken)).StatusCode);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Live_Agent_Key_Or_Owner_Security_Change_Should_Invalidate_Access(bool changeAgentKey)
        {
            var scene = await CreateSceneAsync();
            using var connection = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            Assert.NotNull(await AuthenticateAsync(connection.Tokens.AccessToken));
            await _portal.WithDbContextAsync(async db =>
            {
                if (changeAgentKey)
                {
                    var key = await db.AgentKeys.SingleAsync(x => x.UserId == scene.Agent.ID);
                    PortalAgent.NewKey(out var id, out var hash);
                    key.KeyId = id;
                    key.SecretHash = hash;
                }
                else
                {
                    var owner = await db.Users.SingleAsync(x => x.Email == scene.OwnerEmail);
                    owner.SecurityStamp = Guid.NewGuid().ToString();
                }
                return await db.SaveChangesAsync();
            });
            Assert.Null(await AuthenticateAsync(connection.Tokens.AccessToken));
        }

        [Fact]
        public async Task Scope_Should_Never_Grow_And_Ownership_Removal_Should_Deny_Access()
        {
            var scene = await CreateSceneAsync();
            using var connection = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            var later = await _portal.CreateApplicationAsync(scene.Application.Application.ServerID, scene.OwnerEmail, "Added after approval", scene.Agent.Email);
            var principal = await AuthenticateAsync(connection.Tokens.AccessToken);
            Assert.NotNull(principal);
            Assert.Equal(scene.Application.Application.Token, Assert.Single(principal.FindAll("McpApplicationToken")).Value);
            Assert.DoesNotContain(principal.FindAll("McpApplicationToken"), x => x.Value == later.Application.Token);
            await _portal.WithDbContextAsync(async db =>
            {
                var app = await db.Applications.SingleAsync(x => x.ID == scene.Application.Application.ID);
                app.UserID = "another-owner";
                return await db.SaveChangesAsync();
            });
            Assert.Null(await AuthenticateAsync(connection.Tokens.AccessToken));
        }

        [Fact]
        public async Task Denial_Should_Preserve_State_And_Consume_Request_Without_Creating_Connection()
        {
            var scene = await CreateSceneAsync();
            var pending = await BeginAsync();
            var response = await scene.Owner.PostAsync("/api/v1/mcp/authorize/deny", new McpDenyRequest { RequestId = pending.RequestId }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var redirect = await response.ReadJsonAsync<McpRedirectResponse>();
            var query = QueryHelpers.ParseQuery(new Uri(redirect.RedirectUrl).Query);
            Assert.Equal("access_denied", query["error"].ToString());
            Assert.Equal(pending.State, query["state"].ToString());
            Assert.Equal(HttpStatusCode.NotFound, (await scene.Owner.GetAsync("/api/v1/mcp/authorize?requestId=" + pending.RequestId)).StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.McpConnections.AnyAsync(x => x.ClientId == pending.ClientId)));
        }

        [Fact]
        public async Task Registration_Capacity_Should_Recover_Without_Removing_Connected_Clients()
        {
            var scene = await CreateSceneAsync();
            using var connection = await McpTestClient.AuthorizeAsync(_portal, scene.Owner, scene.Agent.ID);
            var prefix = "capacity-" + Guid.NewGuid().ToString("N") + "-";
            await _portal.WithDbContextAsync(async db =>
            {
                var connected = await db.McpClients.SingleAsync(x => x.Id == connection.ClientId);
                connected.CreatedAt = DateTime.UtcNow.AddHours(-2);
                db.McpClients.AddRange(Enumerable.Range(0, 10000).Select(index => new McpClient
                {
                    Id = prefix + index, Name = "Abandoned registration", RedirectUrisJson = "[]", CreatedAt = DateTime.UtcNow
                }));
                return await db.SaveChangesAsync();
            });
            using var oauth = _portal.CreateCookielessClient();
            var registration = new McpClientRegistrationRequest { ClientName = "Capacity recovery", RedirectUris = [McpTestClient.RedirectUri] };
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await oauth.PostAsync("/api/mcp/oauth/register", registration.ToJsonContent())).StatusCode);
            await _portal.WithDbContextAsync(db => db.McpClients.Where(x => x.Id.StartsWith(prefix))
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddHours(-2))));
            Assert.Equal(HttpStatusCode.Created, (await oauth.PostAsync("/api/mcp/oauth/register", registration.ToJsonContent())).StatusCode);
            Assert.False(await _portal.WithDbContextAsync(db => db.McpClients.AnyAsync(x => x.Id.StartsWith(prefix))));
            Assert.True(await _portal.WithDbContextAsync(db => db.McpClients.AnyAsync(x => x.Id == connection.ClientId)));
            Assert.NotNull(await AuthenticateAsync(connection.Tokens.AccessToken));
            Assert.Contains(await (await scene.Owner.GetAsync("/api/v1/mcp/connections")).ReadJsonAsync<List<McpConnectionResponse>>(), x => x.Id == connection.ConnectionId);
        }

        [Fact]
        public async Task Approval_Should_Snapshot_Only_Reviewed_Applications_And_Reject_Missing_Or_Invalid_Selection()
        {
            var scene = await CreateSceneAsync();
            var pending = await BeginAsync();
            var summary = await (await scene.Owner.GetAsync("/api/v1/mcp/authorize?requestId=" + pending.RequestId)).ReadJsonAsync<McpAuthorizationResponse>();
            var reviewed = Assert.Single(summary.Agents).Applications.Select(x => x.Token).ToList();
            var later = await _portal.CreateApplicationAsync(scene.Application.Application.ServerID, scene.OwnerEmail, "Shared after consent opened", scene.Agent.Email);
            var request = new McpApproveRequest
            {
                RequestId = pending.RequestId, AgentId = scene.Agent.ID, Name = "Reviewed scope", ApplicationTokens = reviewed
            };
            var missing = await scene.Owner.PostAsync("/api/v1/mcp/authorize", new { request.RequestId, request.AgentId, request.Name }.ToJsonContent());
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            foreach (var invalid in new[] { new List<string>(), new List<string> { reviewed[0], reviewed[0] }, new List<string> { " " }, new List<string> { Guid.NewGuid().ToString() } })
            {
                request.ApplicationTokens = invalid;
                Assert.Equal(HttpStatusCode.BadRequest, (await scene.Owner.PostAsync("/api/v1/mcp/authorize", request.ToJsonContent())).StatusCode);
            }
            request.ApplicationTokens = reviewed;
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.PostAsync("/api/v1/mcp/authorize", request.ToJsonContent())).StatusCode);
            var ids = await _portal.WithDbContextAsync(db => db.McpConnections.AsNoTracking().Where(x => x.ClientId == pending.ClientId).Select(x => x.ApplicationIdsJson).SingleAsync());
            Assert.Equal(scene.Application.Application.ID, Assert.Single(JsonSerializer.Deserialize<long[]>(ids) ?? []));
            Assert.DoesNotContain(later.Application.ID, JsonSerializer.Deserialize<long[]>(ids) ?? []);
        }

        private async Task<(HttpClient Owner, string OwnerEmail, AgentCreatedResponse Agent, SeededApplication Application)> CreateSceneAsync()
        {
            var owner = await _portal.CreateUserAsync();
            var client = await _portal.CreateSignedInClientAsync(owner.Email, owner.Password);
            using var admin = await _portal.CreateAdminClientAsync();
            var response = await admin.PostAsync("/api/v1/admin/agents", new { Name = "mcp" + Guid.NewGuid().ToString("N") }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var agent = await response.ReadJsonAsync<AgentCreatedResponse>();
            var server = await _portal.CreateServerAsync();
            var application = await _portal.CreateApplicationAsync(server.ID, owner.Email, "Owned MCP app", agent.Email);
            // The same agent's unrelated application must never appear in this person's consent.
            var outsider = await _portal.CreateUserAsync();
            await _portal.CreateApplicationAsync(server.ID, outsider.Email, "Other owner's app", agent.Email);
            return (client, owner.Email, agent, application);
        }

        private async Task<(string ClientId, string RequestId, string Verifier, string State)> BeginAsync()
        {
            using var oauth = _portal.CreateCookielessClient();
            var response = await oauth.PostAsync("/api/mcp/oauth/register", new McpClientRegistrationRequest
            {
                ClientName = "OAuth regression", RedirectUris = [McpTestClient.RedirectUri], TokenEndpointAuthMethod = "none"
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var client = await response.ReadJsonAsync<McpClientRegistrationResponse>();
            var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var state = Guid.NewGuid().ToString("N");
            var start = await oauth.GetAsync(QueryHelpers.AddQueryString("/api/mcp/oauth/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = client.ClientId, ["redirect_uri"] = McpTestClient.RedirectUri, ["resource"] = McpTestClient.Resource,
                ["response_type"] = "code", ["code_challenge_method"] = "S256", ["state"] = state,
                ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            }));
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            var location = start.Headers.Location ?? throw new InvalidOperationException("Missing consent redirect.");
            return (client.ClientId, QueryHelpers.ParseQuery(new Uri(new Uri("https://portal.test"), location).Query)["requestId"].ToString(), verifier, state);
        }

        private async Task<ClaimsPrincipal?> AuthenticateAsync(string token)
        {
            using var scope = _portal.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IMcpAuthorizationService>().AuthenticateAsync(token);
        }

        private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string clientId, string refreshToken)
        {
            return client.PostAsync("/api/mcp/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["client_id"] = clientId, ["resource"] = McpTestClient.Resource, ["refresh_token"] = refreshToken
            }));
        }
    }
}
