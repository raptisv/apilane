using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    /// <summary>
    /// Identity rebuilds the principal of a login cookie every validation interval (30 minutes).
    /// That refresh must not start a new session: this validator hands the session token of the
    /// incoming cookie to <see cref="AppClaimsPrincipalFactory"/>,
    /// which then keeps it instead of minting a new one. A token that was already revoked stays revoked.
    /// </summary>
    public class PortalSecurityStampValidator : SecurityStampValidator<ApplicationUser>
    {
        /// <summary>
        /// HttpContext.Items key that holds the cookie's session token while the principal is refreshed.
        /// </summary>
        public const string SessionTokenItemKey = "Apilane.Portal.RefreshedSessionToken";

        public PortalSecurityStampValidator(
            IOptions<SecurityStampValidatorOptions> options,
            SignInManager<ApplicationUser> signInManager,
            ILoggerFactory logger)
            : base(options, signInManager, logger)
        {
        }

        protected override async Task SecurityStampVerified(ApplicationUser user, CookieValidatePrincipalContext context)
        {
            var items = context.HttpContext.Items;

            items[SessionTokenItemKey] = context.Principal?.Identity?.GetPortalUserAuthToken() ?? string.Empty;

            try
            {
                await base.SecurityStampVerified(user, context);
            }
            finally
            {
                items.Remove(SessionTokenItemKey);
            }
        }
    }
}
