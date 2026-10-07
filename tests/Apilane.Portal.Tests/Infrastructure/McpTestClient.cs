using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Tests.Infrastructure
{
    /// <summary>
    /// A real OAuth connection and Streamable HTTP client, using the protocol revision sent by
    /// clients that initialize a session. No Portal session cookie or CSRF header is sent to MCP.
    /// </summary>
    public sealed class McpTestClient : IDisposable
    {
        public const string Endpoint = "/api/mcp";
        public const string Resource = "https://portal.test/api/mcp";
        public const string ProtocolVersion = "2025-11-25";
        public const string RedirectUri = "http://127.0.0.1:54321/callback";

        private int _requestId;

        public HttpClient Client { get; }
        public McpTokenResponse Tokens { get; }
        public string ClientId { get; }
        public string ConnectionId { get; }
        public string ConnectionName { get; }

        private McpTestClient(HttpClient client, McpTokenResponse tokens, string clientId, string connectionId, string connectionName)
        {
            Client = client;
            Tokens = tokens;
            ClientId = clientId;
            ConnectionId = connectionId;
            ConnectionName = connectionName;
        }

        /// <summary>
        /// Registers a public client, opens consent, approves an existing agent as the owner,
        /// and exchanges the returned code with S256 PKCE over the actual HTTP endpoints.
        /// </summary>
        public static async Task<McpTestClient> AuthorizeAsync(PortalFactory portal, HttpClient owner, string agentId)
        {
            using var oauth = portal.CreateCookielessClient();
            oauth.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
            var registration = await oauth.PostAsync(Endpoint + "/oauth/register", new McpClientRegistrationRequest
            {
                ClientName = "MCP integration tests",
                RedirectUris = [RedirectUri],
                GrantTypes = ["authorization_code", "refresh_token"],
                ResponseTypes = ["code"],
                TokenEndpointAuthMethod = "none"
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
            var registered = await registration.ReadJsonAsync<McpClientRegistrationResponse>();
            var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var state = Guid.NewGuid().ToString("N");
            var authorization = await oauth.GetAsync(QueryHelpers.AddQueryString(Endpoint + "/oauth/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = registered.ClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state,
                ["resource"] = Resource
            }));
            Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
            var location = authorization.Headers.Location ?? throw new InvalidOperationException("Authorization did not redirect to consent.");
            var consentUri = new Uri(new Uri("https://portal.test"), location);
            var requestId = QueryHelpers.ParseQuery(consentUri.Query)["requestId"].ToString();
            Assert.False(string.IsNullOrEmpty(requestId));
            var reviewResponse = await owner.GetAsync(QueryHelpers.AddQueryString("/api/v1/mcp/authorize", "RequestId", requestId));
            Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
            var review = await reviewResponse.ReadJsonAsync<McpAuthorizationResponse>();
            var reviewedAgent = Assert.Single(review.Agents, x => x.Id == agentId);
            var connectionName = "MCP test " + Guid.NewGuid().ToString("N");
            var approval = await owner.PostAsync("/api/v1/mcp/authorize", new McpApproveRequest
            {
                RequestId = requestId,
                AgentId = agentId,
                ApplicationTokens = reviewedAgent.Applications.Select(x => x.Token).ToList(),
                Name = connectionName
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
            var redirect = await approval.ReadJsonAsync<McpRedirectResponse>();
            var callback = QueryHelpers.ParseQuery(new Uri(redirect.RedirectUrl).Query);
            Assert.Equal(state, callback["state"].ToString());
            var exchange = await oauth.PostAsync(Endpoint + "/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = registered.ClientId,
                ["code"] = callback["code"].ToString(),
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = verifier,
                ["resource"] = Resource
            }));
            Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
            var tokens = await exchange.ReadJsonAsync<McpTokenResponse>();
            var connectionId = await portal.WithDbContextAsync(db => db.McpConnections.AsNoTracking()
                .Where(x => x.Name == connectionName).Select(x => x.Id).SingleAsync());
            var client = portal.CreateCookielessClient();
            Configure(client);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            return new McpTestClient(client, tokens, registered.ClientId, connectionId, connectionName);
        }

        public static void Configure(HttpClient client)
        {
            client.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            client.DefaultRequestHeaders.Add("MCP-Protocol-Version", ProtocolVersion);
        }

        public static object Initialization()
        {
            return new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { },
                clientInfo = new { name = "Apilane integration tests", version = "1.0" }
            };
        }

        public async Task<JsonElement> InitializeAsync()
        {
            using var response = await Client.PostAsync(Endpoint, new
            {
                jsonrpc = "2.0", id = ++_requestId, method = "initialize", @params = Initialization()
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var envelope = await ReadEnvelopeAsync(response);
            var result = Result(envelope);
            Assert.Equal(ProtocolVersion, result.GetProperty("protocolVersion").GetString());
            if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
            {
                Client.DefaultRequestHeaders.Add("Mcp-Session-Id", Assert.Single(values));
            }
            using var initialized = await Client.PostAsync(Endpoint, new
            {
                jsonrpc = "2.0", method = "notifications/initialized", @params = new { }
            }.ToJsonContent());
            Assert.Equal(HttpStatusCode.Accepted, initialized.StatusCode);
            return result;
        }

        public async Task<JsonElement> RequestAsync(string method, object? parameters = null)
        {
            var requestParameters = JsonSerializer.SerializeToElement(parameters ?? new { });
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new
                {
                    jsonrpc = "2.0", id = ++_requestId, method, @params = requestParameters
                }.ToJsonContent()
            };
            request.Headers.Add("Mcp-Method", method);
            if (requestParameters.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                request.Headers.Add("Mcp-Name", name.GetString());
            }
            using var response = await Client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"MCP {method} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return Result(await ReadEnvelopeAsync(response));
        }

        public Task<JsonElement> ToolAsync(string name, object? arguments = null)
        {
            return RequestAsync("tools/call", new { name, arguments = arguments ?? new { } });
        }

        private static JsonElement Result(JsonElement envelope)
        {
            Assert.False(envelope.TryGetProperty("error", out var error), error.ToString());
            return envelope.GetProperty("result");
        }

        private static async Task<JsonElement> ReadEnvelopeAsync(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                content = string.Join("\n", content.Split('\n')
                    .Where(x => x.StartsWith("data:", StringComparison.Ordinal))
                    .Select(x => x.Substring(5).TrimStart(' ').TrimEnd('\r')));
            }
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }

        public void Dispose()
        {
            Client.Dispose();
        }
    }
}
