using Apilane.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    /// <summary>
    /// Builds the principal of the login cookie. A sign-in starts a new session: the user gets a
    /// new session token, stored on the user and carried in the cookie next to the user's id and
    /// e-mail. A periodic refresh of the cookie keeps the token it has (see
    /// <see cref="PortalSecurityStampValidator"/>).
    /// </summary>
    public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AppClaimsPrincipalFactory(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IOptions<IdentityOptions> options,
            ApplicationDbContext dbContext,
            IHttpContextAccessor httpContextAccessor)
            : base(userManager, roleManager, options)
        {
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        public async override Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
        {
            string portalUserAuthToken;

            // A periodic refresh of the login cookie is not a sign-in: the session keeps the token it has.
            if (_httpContextAccessor.HttpContext?.Items[PortalSecurityStampValidator.SessionTokenItemKey] is string refreshedSessionToken)
            {
                portalUserAuthToken = refreshedSessionToken;
            }
            else
            {
                // Create a new token
                portalUserAuthToken = Guid.NewGuid().ToString();

                user.LastLogin = DateTime.UtcNow;
                user.AdminAuthToken = portalUserAuthToken;
                _dbContext.Attach(user);
                _dbContext.Entry(user).Property(x => x.AdminAuthToken).IsModified = true;
                _dbContext.Entry(user).Property(x => x.LastLogin).IsModified = true;
                await _dbContext.SaveChangesAsync();
            }

            var principal = await base.CreateAsync(user);

            var userIdentity = (ClaimsIdentity)(principal?.Identity
                ?? throw new Exception("User not logged in"));

            userIdentity.AddClaims(new[] { new Claim("Id", user.Id) });
            userIdentity.AddClaims(new[] { new Claim("PortalUserAuthToken", portalUserAuthToken) });
            userIdentity.AddClaims(new[] { new Claim("UserEmail", user.Email ?? string.Empty) });

            return principal;
        }
    }
}
