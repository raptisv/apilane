using Apilane.Common.Models;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Host integration boundary. MCP storage uses the host's database transaction while
    /// account/access information is read through projections of the host's existing records.
    /// </summary>
    public interface IMcpPortalStore
    {
        DbContext Context { get; }
        DbSet<McpClient> McpClients { get; }
        DbSet<McpAuthorizationRequest> McpAuthorizationRequests { get; }
        DbSet<McpConnection> McpConnections { get; }
        DbSet<McpAuthorizationCode> McpAuthorizationCodes { get; }
        DbSet<McpToken> McpTokens { get; }
        IQueryable<McpPortalUser> Users { get; }
        IQueryable<McpPortalAgentKey> AgentKeys { get; }
        IQueryable<DBWS_Application> Applications { get; }
        Uri? PublicUrl { get; }
        string AgentAuthenticationType { get; }
        bool IsAgent(string? email);
        Task<McpPortalUser> GetCurrentUserAsync();
        Task<bool> IsAdministratorAsync(string userId);
        Exception Forbidden(string? message = null);
        Exception Validation(string property, string message);
        Exception NotFound(string entity);
        Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message);
    }

    public sealed class McpPortalUser
    {
        public required string Id { get; set; }
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public string? SecurityStamp { get; set; }
        public bool LockoutEnabled { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public string? AdminAuthToken { get; set; }
    }

    public sealed class McpPortalAgentKey
    {
        public required string UserId { get; set; }
        public required string KeyId { get; set; }
        public required string SecretHash { get; set; }
    }
}
