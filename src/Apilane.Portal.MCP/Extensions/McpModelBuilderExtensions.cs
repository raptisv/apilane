using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace Apilane.Portal.Extensions
{
    /// <summary>
    /// Adds MCP persistence to the Portal's existing database model, preserving its transaction
    /// boundary with the users, agent keys and application ownership that authorize connections.
    /// </summary>
    public static class McpModelBuilderExtensions
    {
        public static void ConfigureMcpModel(this ModelBuilder builder)
        {
            builder.Entity<McpClient>().ToTable("McpClients").HasKey(x => x.Id);
            builder.Entity<McpAuthorizationRequest>().ToTable("McpAuthorizationRequests").HasKey(x => x.Id);
            builder.Entity<McpAuthorizationRequest>().HasIndex(x => x.ClientId);
            builder.Entity<McpAuthorizationRequest>().HasOne<McpClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<McpConnection>().ToTable("McpConnections").HasKey(x => x.Id);
            builder.Entity<McpConnection>().HasIndex(x => x.OwnerId);
            builder.Entity<McpConnection>().HasOne<McpClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<McpAuthorizationCode>().ToTable("McpAuthorizationCodes").HasKey(x => x.Hash);
            builder.Entity<McpAuthorizationCode>().HasOne<McpConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<McpToken>().ToTable("McpTokens").HasKey(x => x.Hash);
            builder.Entity<McpToken>().HasOne<McpConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
