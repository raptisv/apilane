using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// Attaches the Portal's cookie session, CSRF, error and no-agent guards to the
    /// MCP module's browser control API. The implementation lives in the MCP project.
    /// </summary>
    [NoAgent]
    [ServiceFilter(typeof(PortalCsrfFilter), Order = -1)]
    [ServiceFilter(typeof(PortalSessionFilter))]
    [ServiceFilter(typeof(PortalApiExceptionFilter))]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public sealed class McpConnectionsController : McpConnectionsControllerBase
    {
        public McpConnectionsController(IMcpAuthorizationService authorization) : base(authorization)
        {
        }
    }
}
