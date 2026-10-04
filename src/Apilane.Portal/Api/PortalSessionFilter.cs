using Apilane.Portal.Abstractions;
using Microsoft.AspNetCore.Authorization;
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
            if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                return;
            }

            var user = await _portalAccessService.FindCurrentUserAsync();

            if (user is null)
            {
                context.Result = PortalApiErrors.Result(context.HttpContext, PortalException.Unauthorized());
            }
        }
    }
}
