using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Portal.Services
{
    /// <summary>
    /// Exposes only the Portal's explicitly classified agent management operations to MCP.
    /// </summary>
    public sealed class PortalMcpOperationPolicy : IMcpOperationPolicy
    {
        public bool IsAvailable(ControllerActionDescriptor action, RouteEndpoint endpoint, string method)
        {
            if (endpoint.Metadata.GetMetadata<NoAgentAttribute>() is not null
                || typeof(PortalAdminApiControllerBase).IsAssignableFrom(action.ControllerTypeInfo))
            {
                return false;
            }
            if (typeof(PortalApplicationApiControllerBase).IsAssignableFrom(action.ControllerTypeInfo))
            {
                return endpoint.Metadata.GetMetadata<AgentPermissionAttribute>() is not null
                    || endpoint.Metadata.GetMetadata<AgentPermissionDiscoveryAttribute>() is not null;
            }
            return action.ControllerTypeInfo.AsType() == typeof(PortalApplicationsController)
                && action.ActionName == nameof(PortalApplicationsController.List) && HttpMethods.IsGet(method);
        }

        public IEnumerable<McpOperationPermission> GetPermissions(Endpoint endpoint, string method)
        {
            return endpoint.Metadata.GetOrderedMetadata<AgentPermissionAttribute>()
                .Select(permission => new McpOperationPermission(permission.Resource, permission.GetAccess(method).ToString()));
        }
    }
}
