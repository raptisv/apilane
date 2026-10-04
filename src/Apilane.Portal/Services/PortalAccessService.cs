using Apilane.Common;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class PortalAccessService : IPortalAccessService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApplicationDbContext _dbContext;

        private bool _resolved;
        private ApplicationUser? _currentUser;

        public PortalAccessService(
            IHttpContextAccessor httpContextAccessor,
            ApplicationDbContext dbContext)
        {
            _httpContextAccessor = httpContextAccessor;
            _dbContext = dbContext;
        }

        private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

        public bool IsAdmin => Principal?.IsInRole(Globals.AdminRoleName) ?? false;

        public async Task<ApplicationUser?> FindCurrentUserAsync()
        {
            if (_resolved)
            {
                return _currentUser;
            }

            _currentUser = await ResolveAsync();
            _resolved = true;

            return _currentUser;
        }

        public async Task<ApplicationUser> GetCurrentUserAsync()
        {
            return await FindCurrentUserAsync()
                ?? throw PortalException.Unauthorized();
        }

        private async Task<ApplicationUser?> ResolveAsync()
        {
            var identity = Principal?.Identity;

            if (identity is null || !identity.IsAuthenticated)
            {
                return null;
            }

            var userId = identity.GetUserId();
            var sessionToken = identity.GetPortalUserAuthToken();

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionToken))
            {
                return null;
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == userId);

            // The cookie alone is not enough: the token it carries must still be the one stored
            // for the user. Signing out, or signing in again elsewhere, changes the stored value.
            if (user is null
                || string.IsNullOrWhiteSpace(user.AdminAuthToken)
                || !string.Equals(user.AdminAuthToken, sessionToken, StringComparison.Ordinal))
            {
                return null;
            }

            return user;
        }
    }
}
