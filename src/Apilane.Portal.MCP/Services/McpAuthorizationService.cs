using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class McpAuthorizationService : IMcpAuthorizationService
    {
        private readonly IMcpPortalStore _store;

        public McpAuthorizationService(IMcpPortalStore store)
        {
            _store = store;
        }

        public string Resource
        {
            get
            {
                var origin = _store.PublicUrl;
                if (origin is null || (origin.Scheme != Uri.UriSchemeHttps && !origin.IsLoopback))
                {
                    throw new McpOAuthException("temporarily_unavailable", "MCP requires an HTTPS PublicUrl (HTTP is allowed only for local development).", 503);
                }
                return origin.GetLeftPart(UriPartial.Authority) + "/api/mcp";
            }
        }

        public async Task<McpClientRegistrationResponse> RegisterAsync(McpClientRegistrationRequest request)
        {
            _ = Resource;
            var name = request.ClientName?.Trim() ?? "MCP client";
            var redirects = request.RedirectUris;
            if (name.Length is < 1 or > 100 || name.Any(char.IsControl)
                || redirects is null || redirects.Length is < 1 or > 5
                || redirects.Any(x => !ValidRedirect(x))
                || (request.TokenEndpointAuthMethod is not null && request.TokenEndpointAuthMethod != "none")
                || (request.ResponseTypes is not null && (request.ResponseTypes.Length != 1 || request.ResponseTypes[0] != "code"))
                || (request.GrantTypes is not null && (request.GrantTypes.Length is < 1 or > 2
                    || !request.GrantTypes.Contains("authorization_code")
                    || request.GrantTypes.Any(x => x is not "authorization_code" and not "refresh_token"))))
            {
                throw new McpOAuthException("invalid_client_metadata", "Use a public client with code flow and exact HTTP loopback callback addresses (or a known hosted client).");
            }

            // Registrations abandoned before consent cannot permanently consume capacity.
            // Preserve every client referenced by a connection (including revoked history),
            // and an older client whose browser authorization is currently in progress.
            await using var transaction = await _store.Context.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var cutoff = now.AddHours(-1);
            await _store.McpClients.Where(registration => registration.CreatedAt < cutoff
                && !_store.McpConnections.Any(connection => connection.ClientId == registration.Id)
                && !_store.McpAuthorizationRequests.Any(pending => pending.ClientId == registration.Id && !pending.Used && pending.ExpiresAt > now))
                .ExecuteDeleteAsync();
            if (await _store.McpClients.CountAsync(registration => !_store.McpConnections.Any(connection => connection.ClientId == registration.Id)) >= 10000)
            {
                throw new McpOAuthException("temporarily_unavailable", "Client registration capacity has been reached.", 503);
            }
            var client = new McpClient
            {
                Id = NewSecret("mcpc_"), Name = name,
                RedirectUrisJson = JsonSerializer.Serialize(redirects.Distinct(StringComparer.Ordinal).ToArray()),
                CreatedAt = DateTime.UtcNow
            };
            _store.McpClients.Add(client);
            await _store.Context.SaveChangesAsync();
            await transaction.CommitAsync();
            return new McpClientRegistrationResponse { ClientId = client.Id, ClientName = name, RedirectUris = redirects };
        }

        public async Task<string> BeginAuthorizationAsync(string clientId, string redirectUri, string challenge, string state, string resource)
        {
            if (resource != Resource || challenge.Length != 43 || !challenge.All(IsBase64Url)
                || state.Length is < 1 or > 2048 || state.Any(char.IsControl))
            {
                throw new McpOAuthException("invalid_request", "A valid resource, state and S256 code challenge are required.");
            }
            await using var transaction = await _store.Context.Database.BeginTransactionAsync();
            var client = await _store.McpClients.AsNoTracking().FirstOrDefaultAsync(x => x.Id == clientId);
            if (client is null)
            {
                throw new McpOAuthException("invalid_client", "This client registration is unknown or expired. Register the client again.");
            }
            if (!(JsonSerializer.Deserialize<string[]>(client.RedirectUrisJson) ?? []).Contains(redirectUri, StringComparer.Ordinal))
            {
                // Never redirect an invalid request to an unverified callback.
                throw new McpOAuthException("invalid_request", "The client or exact redirect URI is not registered.");
            }
            var now = DateTime.UtcNow;
            await _store.McpAuthorizationRequests.Where(x => x.ExpiresAt < now).ExecuteDeleteAsync();
            if (await _store.McpAuthorizationRequests.CountAsync(x => x.ClientId == clientId && !x.Used) >= 20)
            {
                throw new McpOAuthException("temporarily_unavailable", "Too many pending authorization requests.", 429);
            }
            var id = NewSecret("mcpr_");
            _store.McpAuthorizationRequests.Add(new McpAuthorizationRequest
            {
                Id = Hash(id), ClientId = clientId, RedirectUri = redirectUri, CodeChallenge = challenge,
                State = state, Resource = resource, ExpiresAt = now.AddMinutes(10)
            });
            await _store.Context.SaveChangesAsync();
            await transaction.CommitAsync();
            return QueryHelpers.AddQueryString("/mcp/authorize", "requestId", id);
        }

        public async Task<McpAuthorizationResponse> GetAuthorizationAsync(string requestId)
        {
            var owner = await RequirePersonAsync();
            var pending = await GetPendingAsync(requestId);
            var client = await _store.McpClients.AsNoTracking().SingleAsync(x => x.Id == pending.ClientId);
            var agents = await (from user in _store.Users.AsNoTracking()
                                join key in _store.AgentKeys.AsNoTracking() on user.Id equals key.UserId
                                select new { user.Id, user.Email }).ToListAsync();
            var owned = await _store.Applications.AsNoTracking().Include(x => x.Collaborates).Where(x => x.UserID == owner.Id).ToListAsync();
            return new McpAuthorizationResponse
            {
                RequestId = requestId, ClientName = client.Name, RedirectUri = pending.RedirectUri,
                Agents = agents.Where(x => _store.IsAgent(x.Email))
                    .Select(x => new McpAgentResponse
                    {
                        Id = x.Id, Name = AgentName(x.Email),
                        Applications = Summaries(owned.Where(a => a.UserID == x.Id || a.Collaborates.Any(c => c.UserEmail == x.Email)))
                    })
                    .Where(x => x.Applications.Count > 0)
                    .OrderBy(x => x.Name).ToList()
            };
        }

        public async Task<McpRedirectResponse> ApproveAsync(McpApproveRequest request)
        {
            var owner = await RequirePersonAsync();
            var name = request.Name.Trim();
            if (name.Length is < 1 or > 100 || name.Any(char.IsControl))
            {
                throw _store.Validation("Name", "Enter a connection name of 1 to 100 characters.");
            }
            var tokens = request.ApplicationTokens;
            if (tokens is null || tokens.Count is < 1 or > 1000
                || tokens.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 256)
                || tokens.Distinct(StringComparer.Ordinal).Count() != tokens.Count)
            {
                throw _store.Validation("ApplicationTokens", "Choose between 1 and 1000 distinct applications from the authorization summary.");
            }
            await using var transaction = await _store.Context.Database.BeginTransactionAsync();
            var pending = await GetPendingAsync(request.RequestId);
            var agent = await _store.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.AgentId);
            var key = await _store.AgentKeys.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == request.AgentId);
            if (agent is null || !_store.IsAgent(agent.Email) || key is null)
            {
                throw _store.Validation("AgentId", "Choose an existing agent with an active key.");
            }
            var allowed = await OwnedAgentApplicationsAsync(owner.Id, agent);
            var selected = allowed.Where(x => tokens.Contains(x.Token, StringComparer.Ordinal)).ToList();
            if (selected.Count != tokens.Count)
            {
                throw _store.Validation("ApplicationTokens", "Every selected application must still be owned by you and shared with this agent. Reload the authorization request.");
            }
            if (await _store.McpAuthorizationRequests.Where(x => x.Id == pending.Id && !x.Used).ExecuteUpdateAsync(s => s.SetProperty(x => x.Used, true)) != 1)
            {
                throw _store.NotFound("Authorization request");
            }
            var now = DateTime.UtcNow;
            var connection = new McpConnection
            {
                Id = Guid.NewGuid().ToString("N"), Name = name, ClientId = pending.ClientId,
                AgentId = agent.Id, AgentKeyId = key.KeyId, AgentKeyHash = key.SecretHash,
                OwnerId = owner.Id, OwnerSecurityStamp = owner.SecurityStamp ?? string.Empty,
                Resource = pending.Resource, ApplicationIdsJson = JsonSerializer.Serialize(selected.Select(x => x.ID).ToArray()),
                CreatedAt = now, ExpiresAt = now.AddDays(30)
            };
            var code = NewSecret("mcpa_");
            _store.McpConnections.Add(connection);
            _store.McpAuthorizationCodes.Add(new McpAuthorizationCode
            {
                Hash = Hash(code), ConnectionId = connection.Id, RedirectUri = pending.RedirectUri,
                CodeChallenge = pending.CodeChallenge, ExpiresAt = now.AddMinutes(2)
            });
            await _store.Context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Redirect(pending, "code", code);
        }

        public async Task<McpRedirectResponse> DenyAsync(string requestId)
        {
            await RequirePersonAsync();
            var pending = await GetPendingAsync(requestId);
            if (await _store.McpAuthorizationRequests.Where(x => x.Id == pending.Id && !x.Used).ExecuteUpdateAsync(s => s.SetProperty(x => x.Used, true)) != 1)
            {
                throw _store.NotFound("Authorization request");
            }
            return Redirect(pending, "error", "access_denied");
        }

        public async Task<List<McpConnectionResponse>> GetConnectionsAsync()
        {
            var current = await _store.GetCurrentUserAsync();
            if (_store.IsAgent(current.Email))
            {
                throw _store.Forbidden();
            }
            var admin = await IsAdministratorAsync(current.Id);
            var rows = await (from connection in _store.McpConnections.AsNoTracking()
                              where admin || connection.OwnerId == current.Id
                              join client in _store.McpClients.AsNoTracking() on connection.ClientId equals client.Id
                              join agent in _store.Users.AsNoTracking() on connection.AgentId equals agent.Id into agents
                              from agent in agents.DefaultIfEmpty()
                              join owner in _store.Users.AsNoTracking() on connection.OwnerId equals owner.Id into owners
                              from owner in owners.DefaultIfEmpty()
                              orderby connection.CreatedAt descending
                              select new { Connection = connection, ClientName = client.Name, AgentEmail = agent == null ? null : agent.Email, OwnerEmail = owner == null ? null : owner.Email }).ToListAsync();
            var result = new List<McpConnectionResponse>();
            foreach (var x in rows)
            {
                var applications = await EffectiveApplicationsAsync(x.Connection);
                result.Add(new McpConnectionResponse
                {
                    Id = x.Connection.Id, Name = x.Connection.Name, ClientName = x.ClientName,
                    AgentId = x.Connection.AgentId, AgentName = AgentName(x.AgentEmail), AuthorizedByEmail = x.OwnerEmail ?? "Deleted user",
                    Applications = Summaries(applications),
                    IsUsable = applications.Count > 0 && await GetValidConnectionAsync(x.Connection.Id) is not null,
                    // SQLite returns stored UTC dates without their kind. Keep expiry/activity
                    // unambiguous in browsers running in a different time zone.
                    CreatedAt = DateTime.SpecifyKind(x.Connection.CreatedAt, DateTimeKind.Utc),
                    LastUsedAt = x.Connection.LastUsedAt is DateTime lastUsed ? DateTime.SpecifyKind(lastUsed, DateTimeKind.Utc) : null,
                    ExpiresAt = DateTime.SpecifyKind(x.Connection.ExpiresAt, DateTimeKind.Utc),
                    RevokedAt = x.Connection.RevokedAt is DateTime revoked ? DateTime.SpecifyKind(revoked, DateTimeKind.Utc) : null
                });
            }
            return result;
        }

        public async Task RevokeAsync(string id)
        {
            var current = await _store.GetCurrentUserAsync();
            if (_store.IsAgent(current.Email))
            {
                throw _store.Forbidden();
            }
            var connection = await _store.McpConnections.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw _store.NotFound("Connection");
            if (connection.OwnerId != current.Id && !await IsAdministratorAsync(current.Id))
            {
                throw _store.NotFound("Connection");
            }
            connection.RevokedAt ??= DateTime.UtcNow;
            await _store.Context.SaveChangesAsync();
        }

        public async Task<McpTokenResponse> ExchangeAsync(McpTokenRequest request)
        {
            if (request.Resource != Resource)
            {
                throw new McpOAuthException("invalid_target", "The resource does not match this MCP server.");
            }
            await using var transaction = await _store.Context.Database.BeginTransactionAsync();
            McpConnection? connection;
            var now = DateTime.UtcNow;
            if (request.GrantType == "authorization_code")
            {
                var verifier = request.CodeVerifier;
                if (string.IsNullOrEmpty(request.Code) || request.Code.Length > 128 || verifier is null
                    || verifier.Length is < 43 or > 128 || !verifier.All(IsVerifierCharacter))
                {
                    throw InvalidGrant();
                }
                var hash = Hash(request.Code);
                var code = await _store.McpAuthorizationCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Hash == hash);
                connection = code is null ? null : await GetValidConnectionAsync(code.ConnectionId);
                if (code is null || connection is null || code.ExpiresAt <= now
                    || connection.ClientId != request.ClientId || code.RedirectUri != request.RedirectUri
                    || code.CodeChallenge != WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))))
                {
                    throw InvalidGrant();
                }
                if (code.Used || await _store.McpAuthorizationCodes.Where(x => x.Hash == hash && !x.Used).ExecuteUpdateAsync(s => s.SetProperty(x => x.Used, true)) != 1)
                {
                    await RevokeReplayAsync(connection.Id);
                    await transaction.CommitAsync();
                    throw InvalidGrant();
                }
            }
            else if (request.GrantType == "refresh_token")
            {
                if (string.IsNullOrEmpty(request.RefreshToken) || request.RefreshToken.Length > 128)
                {
                    throw InvalidGrant();
                }
                var hash = Hash(request.RefreshToken);
                var token = await _store.McpTokens.AsNoTracking().FirstOrDefaultAsync(x => x.Hash == hash && x.IsRefresh);
                connection = token is null ? null : await GetValidConnectionAsync(token.ConnectionId);
                if (token is null || connection is null || token.ExpiresAt <= now || connection.ClientId != request.ClientId)
                {
                    throw InvalidGrant();
                }
                if (token.Used || await _store.McpTokens.Where(x => x.Hash == hash && !x.Used).ExecuteUpdateAsync(s => s.SetProperty(x => x.Used, true)) != 1)
                {
                    await RevokeReplayAsync(connection.Id);
                    await transaction.CommitAsync();
                    throw InvalidGrant();
                }
            }
            else
            {
                throw new McpOAuthException("unsupported_grant_type", "Only authorization_code and refresh_token are supported.");
            }
            if ((await EffectiveApplicationsAsync(connection)).Count == 0)
            {
                throw InvalidGrant();
            }
            var response = new McpTokenResponse { AccessToken = NewSecret("mcpat_"), RefreshToken = NewSecret("mcprt_") };
            var accessExpiry = now.AddSeconds(response.ExpiresIn);
            if (accessExpiry > connection.ExpiresAt)
            {
                accessExpiry = connection.ExpiresAt;
                response.ExpiresIn = Math.Max(1, (int)(accessExpiry - now).TotalSeconds);
            }
            _store.McpTokens.Add(new McpToken { Hash = Hash(response.AccessToken), ConnectionId = connection.Id, ExpiresAt = accessExpiry });
            _store.McpTokens.Add(new McpToken { Hash = Hash(response.RefreshToken), ConnectionId = connection.Id, IsRefresh = true, ExpiresAt = connection.ExpiresAt });
            await _store.Context.SaveChangesAsync();
            await transaction.CommitAsync();
            return response;
        }

        public async Task<ClaimsPrincipal?> AuthenticateAsync(string accessToken)
        {
            if (!accessToken.StartsWith("mcpat_", StringComparison.Ordinal) || accessToken.Length > 128)
            {
                return null;
            }
            var hash = Hash(accessToken);
            var now = DateTime.UtcNow;
            var token = await _store.McpTokens.AsNoTracking().FirstOrDefaultAsync(x => x.Hash == hash && !x.IsRefresh && !x.Used && x.ExpiresAt > now);
            var connection = token is null ? null : await GetValidConnectionAsync(token.ConnectionId);
            if (connection is null)
            {
                return null;
            }
            var agent = await _store.Users.AsNoTracking().SingleAsync(x => x.Id == connection.AgentId);
            var applications = await EffectiveApplicationsAsync(connection);
            if (applications.Count == 0)
            {
                return null;
            }
            await _store.McpConnections.Where(x => x.Id == connection.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, now));
            var claims = new List<Claim>
            {
                new Claim("Id", agent.Id), new Claim("UserEmail", agent.Email ?? string.Empty),
                new Claim("PortalUserAuthToken", agent.AdminAuthToken ?? string.Empty),
                new Claim("McpConnectionId", connection.Id), new Claim("McpAuthorizedBy", connection.OwnerId)
            };
            claims.AddRange(applications.Select(x => new Claim("McpApplicationToken", x.Token)));
            claims.AddRange(applications.Select(x => new Claim("McpApplicationId", x.ID.ToString(CultureInfo.InvariantCulture))));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, _store.AgentAuthenticationType));
        }

        private async Task<McpConnection?> GetValidConnectionAsync(string id)
        {
            var connection = await _store.McpConnections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (connection is null || connection.RevokedAt is not null || connection.ExpiresAt <= DateTime.UtcNow || connection.Resource != Resource)
            {
                return null;
            }
            var owner = await _store.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == connection.OwnerId);
            var agent = await _store.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == connection.AgentId);
            var key = await _store.AgentKeys.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == connection.AgentId);
            if (owner is null || _store.IsAgent(owner.Email) || !owner.EmailConfirmed
                || string.IsNullOrEmpty(owner.SecurityStamp) || owner.SecurityStamp != connection.OwnerSecurityStamp
                || (owner.LockoutEnabled && owner.LockoutEnd > DateTimeOffset.UtcNow)
                || agent is null || !_store.IsAgent(agent.Email) || key is null
                || key.KeyId != connection.AgentKeyId || key.SecretHash != connection.AgentKeyHash)
            {
                return null;
            }
            return connection;
        }

        private async Task<McpPortalUser> RequirePersonAsync()
        {
            var user = await _store.GetCurrentUserAsync();
            if (_store.IsAgent(user.Email) || !user.EmailConfirmed || string.IsNullOrEmpty(user.SecurityStamp))
            {
                throw _store.Forbidden("Sign in with a confirmed personal account to connect an agent.");
            }
            return user;
        }

        private Task<List<DBWS_Application>> OwnedAgentApplicationsAsync(string ownerId, McpPortalUser agent)
        {
            return _store.Applications.AsNoTracking().Where(x => x.UserID == ownerId
                && (x.UserID == agent.Id || x.Collaborates.Any(c => c.UserEmail == agent.Email))).ToListAsync();
        }

        private async Task<List<DBWS_Application>> EffectiveApplicationsAsync(McpConnection connection)
        {
            var ids = JsonSerializer.Deserialize<long[]>(connection.ApplicationIdsJson) ?? [];
            var agent = await _store.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == connection.AgentId);
            if (agent is null || !_store.IsAgent(agent.Email))
            {
                return [];
            }
            var applications = await OwnedAgentApplicationsAsync(connection.OwnerId, agent);
            return applications.Where(x => ids.Contains(x.ID)).ToList();
        }

        private static List<McpApplicationResponse> Summaries(IEnumerable<DBWS_Application> applications)
        {
            return applications.OrderBy(x => x.Name).Select(x => new McpApplicationResponse { Token = x.Token, Name = x.Name }).ToList();
        }

        private Task<bool> IsAdministratorAsync(string id)
        {
            return _store.IsAdministratorAsync(id);
        }

        private async Task<McpAuthorizationRequest> GetPendingAsync(string requestId)
        {
            if (string.IsNullOrEmpty(requestId) || requestId.Length > 128)
            {
                throw _store.NotFound("Authorization request");
            }
            var hash = Hash(requestId);
            return await _store.McpAuthorizationRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == hash && !x.Used && x.ExpiresAt > DateTime.UtcNow)
                ?? throw _store.NotFound("Authorization request");
        }

        private async Task RevokeReplayAsync(string id)
        {
            await _store.McpConnections.Where(x => x.Id == id && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow));
        }

        private static McpRedirectResponse Redirect(McpAuthorizationRequest request, string name, string value)
        {
            return new McpRedirectResponse
            {
                RedirectUrl = QueryHelpers.AddQueryString(request.RedirectUri, new Dictionary<string, string?> { [name] = value, ["state"] = request.State })
            };
        }

        // Callbacks of hosted clients that cannot listen on the loopback of this machine. Exact match only.
        private static readonly string[] HostedCallbacks =
        [
            "https://claude.ai/api/mcp/auth_callback",
            "https://claude.com/api/mcp/auth_callback"
        ];

        private static bool ValidRedirect(string? value)
        {
            return value is not null && Array.IndexOf(HostedCallbacks, value) >= 0 || ValidLoopbackRedirect(value);
        }

        private static bool ValidLoopbackRedirect(string? value)
        {
            return value is not null && value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttp && uri.Host is "localhost" or "127.0.0.1" or "[::1]"
                && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.Query)
                && !value.Any(char.IsControl);
        }

        private static bool IsBase64Url(char value)
        {
            return char.IsAsciiLetterOrDigit(value) || value is '-' or '_';
        }

        private static bool IsVerifierCharacter(char value)
        {
            return IsBase64Url(value) || value is '.' or '~';
        }

        private string AgentName(string? email)
        {
            return _store.IsAgent(email) && email is not null ? email[..email.LastIndexOf('@')] : "Deleted agent";
        }

        private static string Hash(string value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }

        private static string NewSecret(string prefix)
        {
            return prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        }

        private static McpOAuthException InvalidGrant()
        {
            return new McpOAuthException("invalid_grant", "The authorization grant is invalid, expired or revoked.");
        }
    }

    public class McpOAuthException : Exception
    {
        public string Error { get; }
        public int StatusCode { get; }

        public McpOAuthException(string error, string message, int statusCode = 400) : base(message)
        {
            Error = error;
            StatusCode = statusCode;
        }
    }
}
