using Apilane.Common;
using Apilane.Common.Security;
using Apilane.Portal.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Apilane.Portal.Api.Internal
{
    /// <summary>
    /// Lets a request through only when it carries the installation key in the x-installation-key
    /// header; anything else gets 401 with no body. The key is never read from the address, so it
    /// does not end up in request logs, and it is compared in constant time.
    /// </summary>
    public class InstallationKeyFilter : IAuthorizationFilter
    {
        private readonly IPortalSettingsService _portalSettingsService;

        public InstallationKeyFilter(IPortalSettingsService portalSettingsService)
        {
            _portalSettingsService = portalSettingsService;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var expected = _portalSettingsService.Get().InstallationKey;
            var sent = context.HttpContext.Request.Headers[Globals.InstallationKeyHeaderName].ToString();

            if (!SecureCompare.AreEqual(expected, sent))
            {
                context.Result = new UnauthorizedResult();
            }
        }
    }
}
