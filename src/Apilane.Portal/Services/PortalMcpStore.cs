using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    /// <summary>
    /// Supplies the MCP module with the Portal's existing identity, access and database records.
    /// Account/key projections are read-only; OAuth records share the Portal's transactions.
    /// </summary>
    public sealed class PortalMcpStore : IMcpPortalStore
    {
        private readonly ApplicationDbContext _db;
        private readonly IPortalAccessService _access;
        private readonly PortalConfiguration _configuration;

        public PortalMcpStore(ApplicationDbContext db, IPortalAccessService access, PortalConfiguration configuration)
        {
            _db = db;
            _access = access;
            _configuration = configuration;
        }

        public DbContext Context => _db;
        public DbSet<McpClient> McpClients => _db.McpClients;
        public DbSet<McpAuthorizationRequest> McpAuthorizationRequests => _db.McpAuthorizationRequests;
        public DbSet<McpConnection> McpConnections => _db.McpConnections;
        public DbSet<McpAuthorizationCode> McpAuthorizationCodes => _db.McpAuthorizationCodes;
        public DbSet<McpToken> McpTokens => _db.McpTokens;
        public IQueryable<DBWS_Application> Applications => _db.Applications;
        public Uri? PublicUrl => _configuration.PublicUrl;
        public string AgentAuthenticationType => PortalAgent.AuthenticationType;
        public IQueryable<McpPortalUser> Users => _db.Users.Select(user => new McpPortalUser
        {
            Id = user.Id, Email = user.Email, EmailConfirmed = user.EmailConfirmed,
            SecurityStamp = user.SecurityStamp, LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd, AdminAuthToken = user.AdminAuthToken
        });
        public IQueryable<McpPortalAgentKey> AgentKeys => _db.AgentKeys.Select(key => new McpPortalAgentKey
        {
            UserId = key.UserId, KeyId = key.KeyId, SecretHash = key.SecretHash
        });

        public bool IsAgent(string? email)
        {
            return PortalAgent.IsAgent(email);
        }

        public async Task<McpPortalUser> GetCurrentUserAsync()
        {
            var user = await _access.GetCurrentUserAsync();
            return new McpPortalUser
            {
                Id = user.Id, Email = user.Email, EmailConfirmed = user.EmailConfirmed,
                SecurityStamp = user.SecurityStamp, LockoutEnabled = user.LockoutEnabled,
                LockoutEnd = user.LockoutEnd, AdminAuthToken = user.AdminAuthToken
            };
        }

        public Task<bool> IsAdministratorAsync(string userId)
        {
            return (from userRole in _db.UserRoles
                    join role in _db.Roles on userRole.RoleId equals role.Id
                    where userRole.UserId == userId && role.Name == Globals.AdminRoleName
                    select userRole).AnyAsync();
        }

        public Exception Forbidden(string? message = null)
        {
            return message is null ? PortalException.Forbidden() : PortalException.Forbidden(message);
        }

        public Exception Validation(string property, string message)
        {
            return PortalException.Validation(property, message);
        }

        public Exception NotFound(string entity)
        {
            return PortalException.NotFound(entity);
        }

        public Task WriteErrorAsync(HttpContext context, int statusCode, string code, string message)
        {
            return PortalApiErrors.WriteAsync(context, statusCode, code, message);
        }
    }
}
