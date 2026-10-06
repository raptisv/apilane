using Apilane.Portal.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Checks current grants before binding or executing an application action. Unclassified
    /// application endpoints are closed to agents, including endpoints added in the future.
    /// </summary>
    public class PortalAgentPermissionFilter : IAsyncAuthorizationFilter
    {
        private readonly IPortalAccessService _portalAccessService;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IAgentPermissionService _agentPermissionService;

        public PortalAgentPermissionFilter(
            IPortalAccessService portalAccessService,
            IApplicationAccessService applicationAccessService,
            IAgentPermissionService agentPermissionService)
        {
            _portalAccessService = portalAccessService;
            _applicationAccessService = applicationAccessService;
            _agentPermissionService = agentPermissionService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            try
            {
                var user = await _portalAccessService.GetCurrentUserAsync();
                if (!PortalAgent.IsAgent(user.Email))
                {
                    return;
                }

                var metadata = context.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<NoAgentAttribute>().Any())
                {
                    throw PortalException.Forbidden(PortalAgent.RefusedMessage);
                }

                var appToken = context.RouteData.Values["appToken"]?.ToString() ?? string.Empty;
                var application = await _applicationAccessService.GetApplicationAsync(appToken);
                if (metadata.OfType<AgentPermissionDiscoveryAttribute>().Any())
                {
                    return;
                }

                var requirements = metadata.OfType<AgentPermissionAttribute>().ToList();
                if (requirements.Count == 0)
                {
                    throw PortalException.Forbidden(PortalAgent.RefusedMessage);
                }

                foreach (var requirement in requirements)
                {
                    var access = requirement.GetAccess(context.HttpContext.Request.Method);
                    await _agentPermissionService.DemandAsync(application, requirement.Resource, access);
                }
            }
            catch (PortalException exception)
            {
                context.Result = PortalApiErrors.Result(context.HttpContext, exception);
            }
        }
    }
}
