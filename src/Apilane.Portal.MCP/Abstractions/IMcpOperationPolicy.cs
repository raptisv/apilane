using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using System.Collections.Generic;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The host decides which management endpoints are agent operations. MVC still performs
    /// the current per-application authorization when each operation is dispatched.
    /// </summary>
    public interface IMcpOperationPolicy
    {
        bool IsAvailable(ControllerActionDescriptor action, RouteEndpoint endpoint, string method);
        IEnumerable<McpOperationPermission> GetPermissions(Endpoint endpoint, string method);
    }

    public sealed record McpOperationPermission(string Resource, string Access);
}
