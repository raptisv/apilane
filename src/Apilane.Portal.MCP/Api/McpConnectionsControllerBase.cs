using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// Browser approval and revocation of MCP connections. Connections act as an existing agent,
    /// within a fixed set of applications owned by the approving person.
    /// </summary>
    [Route("api/v1/mcp")]
    [ApiController]
    [Authorize]
    [Produces("application/json")]
    public abstract class McpConnectionsControllerBase : ControllerBase
    {
        private readonly IMcpAuthorizationService _authorization;

        protected McpConnectionsControllerBase(IMcpAuthorizationService authorization)
        {
            _authorization = authorization;
        }

        /// <summary>
        /// Gets a pending client's authorization request and eligible existing agents. Each agent
        /// includes only applications the caller owns and that are already shared with the agent.
        /// </summary>
        [HttpGet("authorize")]
        [ProducesResponseType(typeof(McpAuthorizationResponse), StatusCodes.Status200OK)]
        public Task<McpAuthorizationResponse> Authorize([FromQuery] string requestId)
        {
            Response.Headers.CacheControl = "no-store";
            return _authorization.GetAuthorizationAsync(requestId);
        }

        /// <summary>
        /// Approves the selected existing agent for a named, 30-day MCP connection, limited to
        /// currently owned applications shared with that agent. Returns the client's exact callback.
        /// </summary>
        [HttpPost("authorize")]
        [ProducesResponseType(typeof(McpRedirectResponse), StatusCodes.Status200OK)]
        public Task<McpRedirectResponse> Approve(McpApproveRequest request)
        {
            Response.Headers.CacheControl = "no-store";
            return _authorization.ApproveAsync(request);
        }

        /// <summary>
        /// Refuses a pending request and returns its registered callback with access_denied.
        /// </summary>
        [HttpPost("authorize/deny")]
        [ProducesResponseType(typeof(McpRedirectResponse), StatusCodes.Status200OK)]
        public Task<McpRedirectResponse> Deny(McpDenyRequest request)
        {
            Response.Headers.CacheControl = "no-store";
            return _authorization.DenyAsync(request.RequestId);
        }

        /// <summary>
        /// Lists the caller's MCP connections; administrators can see all connections.
        /// Includes effective application scope, expiry and revocation, without credentials.
        /// </summary>
        [HttpGet("connections")]
        [ProducesResponseType(typeof(List<McpConnectionResponse>), StatusCodes.Status200OK)]
        public Task<List<McpConnectionResponse>> Connections()
        {
            Response.Headers.CacheControl = "no-store";
            return _authorization.GetConnectionsAsync();
        }

        /// <summary>
        /// Immediately revokes a connection and all its tokens. The approving person or an
        /// administrator may revoke it. Previously revoked connections remain revoked.
        /// </summary>
        [HttpDelete("connections/{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Revoke(string id)
        {
            await _authorization.RevokeAsync(id);
            return NoContent();
        }
    }
}
