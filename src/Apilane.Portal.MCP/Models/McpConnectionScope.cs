using System;
using System.Linq;
using System.Security.Claims;

namespace Apilane.Portal.Models
{
    /// <summary>
    /// The connection's approved application boundary, in addition to the agent's live grants.
    /// Only the MCP OAuth authenticator creates these claims.
    /// </summary>
    public static class McpConnectionScope
    {
        public const string ConnectionId = "McpConnectionId";
        public const string ApplicationToken = "McpApplicationToken";
        public const string AuthorizedBy = "McpAuthorizedBy";

        public static bool IsConnection(ClaimsPrincipal? principal)
        {
            return principal?.HasClaim(x => x.Type == ConnectionId) == true;
        }

        public static string[] GetApplicationTokens(ClaimsPrincipal? principal)
        {
            return principal?.FindAll(ApplicationToken).Select(x => x.Value).Distinct(StringComparer.Ordinal).ToArray()
                ?? Array.Empty<string>();
        }
    }
}
