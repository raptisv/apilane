using System;

namespace Apilane.Portal.Models
{
    public class McpClient
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string RedirectUrisJson { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class McpAuthorizationRequest
    {
        public required string Id { get; set; }
        public required string ClientId { get; set; }
        public required string RedirectUri { get; set; }
        public required string CodeChallenge { get; set; }
        public required string State { get; set; }
        public required string Resource { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool Used { get; set; }
    }

    public class McpConnection
    {
        public required string Id { get; set; }
        public required string Name { get; set; }
        public required string ClientId { get; set; }
        public required string AgentId { get; set; }
        public required string AgentKeyId { get; set; }
        public required string AgentKeyHash { get; set; }
        public required string OwnerId { get; set; }
        public required string OwnerSecurityStamp { get; set; }
        public required string Resource { get; set; }
        public required string ApplicationIdsJson { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }

    public class McpAuthorizationCode
    {
        public required string Hash { get; set; }
        public required string ConnectionId { get; set; }
        public required string RedirectUri { get; set; }
        public required string CodeChallenge { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool Used { get; set; }
    }

    public class McpToken
    {
        public required string Hash { get; set; }
        public required string ConnectionId { get; set; }
        public bool IsRefresh { get; set; }
        public DateTime ExpiresAt { get; set; }
        // Used refresh tokens are retained until the connection expires so replay revokes it.
        public bool Used { get; set; }
    }
}
