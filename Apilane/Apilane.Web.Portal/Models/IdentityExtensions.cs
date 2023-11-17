using System.Security.Claims;
using System.Security.Principal;

namespace Apilane.Web.Portal.Models
{
    public static class IdentityExtensions
    {
        public static string GetUserId(this IIdentity identity)
        {
            var claim = ((ClaimsIdentity)identity).FindFirst("Id");
            return (claim != null) ? claim.Value : string.Empty;
        }

        public static string GetUserEmail(this IIdentity identity)
        {
            var claim = ((ClaimsIdentity)identity).FindFirst("UserEmail");
            return (claim != null) ? claim.Value : string.Empty;
        }

        public static string GetPortalUserAuthToken(this IIdentity identity)
        {
            var claim = ((ClaimsIdentity)identity).FindFirst("PortalUserAuthToken");
            return (claim != null) ? claim.Value : string.Empty;
        }
    }
}