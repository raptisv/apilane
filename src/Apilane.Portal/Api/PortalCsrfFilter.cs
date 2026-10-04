using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Cross-site request forgery guard of the management API. The session lives in a cookie, which
    /// a browser also sends on requests started by other sites. Those sites cannot add a custom
    /// header without a CORS preflight, so every request that changes data must carry
    /// 'X-Apilane-Portal: 1'. Runs as an authorization filter, before model binding and the action,
    /// and applies to anonymous endpoints as well. A request authenticated by an agent key carries
    /// no cookie that counts, so it needs no header.
    /// </summary>
    public class PortalCsrfFilter : IAuthorizationFilter
    {
        public const string HeaderName = "X-Apilane-Portal";
        public const string HeaderValue = "1";

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var request = context.HttpContext.Request;

            if (HttpMethods.IsGet(request.Method)
                || HttpMethods.IsHead(request.Method)
                || HttpMethods.IsOptions(request.Method)
                || HttpMethods.IsTrace(request.Method))
            {
                return;
            }

            // A browser never sends an agent key on its own, so a request let in by one cannot be forged.
            if (context.HttpContext.User.Identity?.AuthenticationType == PortalAgent.AuthenticationType)
            {
                return;
            }

            if (request.Headers[HeaderName] != HeaderValue)
            {
                context.Result = PortalApiErrors.Result(
                    context.HttpContext,
                    PortalException.Forbidden($"Requests that change data must send the header '{HeaderName}: {HeaderValue}'."));
            }
        }
    }
}
