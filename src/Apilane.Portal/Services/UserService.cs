using Apilane.Common;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class UserService : IUserService
    {
        private const string EntityName = "User";

        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalAccessService _portalAccessService;

        public UserService(
            ApplicationDbContext dbContext,
            IPortalAccessService portalAccessService)
        {
            _dbContext = dbContext;
            _portalAccessService = portalAccessService;
        }

        public async Task<List<UserResponse>> GetAllAsync()
        {
            var currentUser = await _portalAccessService.GetCurrentUserAsync();
            var adminRoleId = await GetAdminRoleIdAsync();

            var adminUserIds = await _dbContext.UserRoles
                .Where(x => x.RoleId == adminRoleId)
                .Select(x => x.UserId)
                .ToListAsync();

            // Only the columns that are shown: the password hash and the tokens stay in the database.
            var users = await _dbContext.Users
                .AsNoTracking()
                .OrderByDescending(x => x.LastLogin)
                .Select(x => new { x.Id, x.Email, x.LastLogin })
                .ToListAsync();

            return users
                .Select(x => ToResponse(x.Id, x.Email, x.LastLogin, adminUserIds.Contains(x.Id), x.Id == currentUser.Id))
                .ToList();
        }

        public async Task<UserResponse> SetRoleAsync(string userId, UserRoleRequest request)
        {
            var isAdmin = request.IsAdmin
                ?? throw PortalException.Validation(nameof(UserRoleRequest.IsAdmin), "Required");

            // Tracked on purpose: the audit log takes the e-mail of a role change from the tracked users.
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId)
                ?? throw PortalException.NotFound(EntityName);

            var currentUser = await _portalAccessService.GetCurrentUserAsync();

            // Also what keeps at least one administrator on the instance.
            if (user.Id == currentUser.Id)
            {
                throw PortalException.Conflict("Cannot change your own role", EntityName);
            }

            var adminRoleId = await GetAdminRoleIdAsync();

            var userRole = await _dbContext.UserRoles
                .FirstOrDefaultAsync(x => x.UserId == user.Id && x.RoleId == adminRoleId);

            if (isAdmin && userRole is null)
            {
                _dbContext.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = adminRoleId });
            }
            else if (!isAdmin && userRole is not null)
            {
                _dbContext.UserRoles.Remove(userRole);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // A parallel request for the same user can make the same change between the check above
                // and this save (duplicate key on promote, no row left on demote). The role is then already
                // what was asked for. Any other save failure must not be reported as success.
                var hasAdminRole = await _dbContext.UserRoles
                    .AnyAsync(x => x.UserId == user.Id && x.RoleId == adminRoleId);

                if (hasAdminRole != isAdmin)
                {
                    throw;
                }
            }

            return ToResponse(user.Id, user.Email, user.LastLogin, isAdmin, isCurrentUser: false);
        }

        private async Task<string> GetAdminRoleIdAsync()
        {
            var roles = await _dbContext.Roles.AsNoTracking().ToListAsync();

            var adminRole = roles.SingleOrDefault(x => string.Equals(x.Name, Globals.AdminRoleName, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception("Role admin not found");

            return adminRole.Id;
        }

        private static UserResponse ToResponse(string id, string? email, DateTime lastLogin, bool isAdmin, bool isCurrentUser)
        {
            return new UserResponse
            {
                ID = id,
                Email = email ?? string.Empty,
                IsAdmin = isAdmin,
                IsCurrentUser = isCurrentUser,
                // Stored as UTC; SQLite gives the value back without that mark.
                LastLogin = DateTime.SpecifyKind(lastLogin, DateTimeKind.Utc)
            };
        }
    }
}
