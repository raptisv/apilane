using Apilane.Portal.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Rejects a request whose login cookie is still valid but whose session has been revoked.
    /// Runs as an authorization filter so it comes before model binding and the action.
    /// </summary>
    public class PortalSessionFilter : IAsyncAuthorizationFilter
    {
        private readonly IPortalAccessService _portalAccessService;

        public PortalSessionFilter(IPortalAccessService portalAccessService)
        {
            _portalAccessService = portalAccessService;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var allowAnonymous = context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
            if (allowAnonymous && context.HttpContext.User.Identity?.IsAuthenticated != true)
            {
                return;
            }

            var user = await _portalAccessService.FindCurrentUserAsync();

            if (user is null)
            {
                if (!allowAnonymous)
                {
                    context.Result = PortalApiErrors.Result(context.HttpContext, PortalException.Unauthorized());
                }
            }
            else if (PortalAgent.IsAgent(user.Email)
                && (context.ActionDescriptor.EndpointMetadata.OfType<NoAgentAttribute>().Any()
                    || context.HttpContext.Request.Path.StartsWithSegments("/" + PortalAdminApiControllerBase.RoutePrefix)
                    || (HttpMethods.IsDelete(context.HttpContext.Request.Method)
                        && !context.ActionDescriptor.EndpointMetadata.OfType<AgentPermissionAttribute>().Any())))
            {
                // Also cover a legacy agent account with a login cookie: permission checks must
                // depend on the account, not on which authentication mechanism it used.
                context.Result = PortalApiErrors.Result(context.HttpContext, PortalException.Forbidden(PortalAgent.RefusedMessage));
            }
        }
    }
}
