using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Apilane.Portal.Api.V1.Contracts
{
    public class McpAgentResponse
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required List<McpApplicationResponse> Applications { get; set; }
    }

    public class McpApplicationResponse
    {
        public required string Token { get; set; }
        public required string Name { get; set; }
    }

    public class McpAuthorizationResponse
    {
        public required string RequestId { get; set; }
        public required string ClientName { get; set; }
        public required string RedirectUri { get; set; }
        public required List<McpAgentResponse> Agents { get; set; }
    }

    public class McpApproveRequest
    {
        [Required, StringLength(128)]
        public required string RequestId { get; set; }
        [Required, StringLength(128)]
        public required string AgentId { get; set; }
        [Required, StringLength(100)]
        public required string Name { get; set; }
        [Required, MinLength(1), MaxLength(1000)]
        public required List<string> ApplicationTokens { get; set; }
    }

    public class McpDenyRequest
    {
        [Required, StringLength(128)]
        public required string RequestId { get; set; }
    }

    public class McpRedirectResponse
    {
        public required string RedirectUrl { get; set; }
    }

    public class McpConnectionResponse
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string ClientName { get; set; }
        public required string AgentId { get; set; }
        public required string AgentName { get; set; }
        public required string AuthorizedByEmail { get; set; }
        public required List<McpApplicationResponse> Applications { get; set; }
        [Required]
        public DateTime CreatedAt { get; set; }
        [Required]
        public bool IsUsable { get; set; }
        public DateTime? LastUsedAt { get; set; }
        [Required]
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }

    // OAuth uses its standard field names independently of the management API's PascalCase.
    public class McpClientRegistrationRequest
    {
        [JsonPropertyName("client_name")]
        public string? ClientName { get; set; }
        [JsonPropertyName("redirect_uris")]
        public string[]? RedirectUris { get; set; }
        [JsonPropertyName("grant_types")]
        public string[]? GrantTypes { get; set; }
        [JsonPropertyName("response_types")]
        public string[]? ResponseTypes { get; set; }
        [JsonPropertyName("token_endpoint_auth_method")]
        public string? TokenEndpointAuthMethod { get; set; }
    }

    public class McpClientRegistrationResponse
    {
        [JsonPropertyName("client_id")]
        public required string ClientId { get; set; }
        [JsonPropertyName("client_name")]
        public required string ClientName { get; set; }
        [JsonPropertyName("redirect_uris")]
        public required string[] RedirectUris { get; set; }
        [JsonPropertyName("grant_types")]
        public string[] GrantTypes { get; set; } = ["authorization_code", "refresh_token"];
        [JsonPropertyName("response_types")]
        public string[] ResponseTypes { get; set; } = ["code"];
        [JsonPropertyName("token_endpoint_auth_method")]
        public string TokenEndpointAuthMethod { get; set; } = "none";
    }

    public class McpTokenRequest
    {
        public required string GrantType { get; set; }
        public required string ClientId { get; set; }
        public required string Resource { get; set; }
        public string? Code { get; set; }
        public string? RedirectUri { get; set; }
        public string? CodeVerifier { get; set; }
        public string? RefreshToken { get; set; }
    }

    public class McpTokenResponse
    {
        [JsonPropertyName("access_token")]
        public required string AccessToken { get; set; }
        [JsonPropertyName("refresh_token")]
        public required string RefreshToken { get; set; }
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; } = 900;
        [JsonPropertyName("scope")]
        public string Scope { get; set; } = "mcp";
    }
}
