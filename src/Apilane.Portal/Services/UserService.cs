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
        private const string AgentEntityName = "Agent";

        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalAccessService _portalAccessService;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserService(
            ApplicationDbContext dbContext,
            IPortalAccessService portalAccessService,
            UserManager<ApplicationUser> userManager)
        {
            _dbContext = dbContext;
            _portalAccessService = portalAccessService;
            _userManager = userManager;
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

            // An agent's key is refused under /api/v1/admin whatever its role, so the role would only mislead.
            if (isAdmin && PortalAgent.IsAgent(user.Email))
            {
                throw PortalException.Conflict("An agent cannot be an administrator", EntityName);
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

        public async Task<AgentCreatedResponse> CreateAgentAsync(CreateAgentRequest request)
        {
            var email = request.Name + PortalAgent.EmailSuffix;
            var now = DateTime.UtcNow;

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DateRegistered = now,
                LastLogin = now,
                // An agent never signs in, so it gets its token here: the Portal calls the API servers
                // with it on the agent's behalf. The agent cannot read it (GET session/api-token is refused).
                AdminAuthToken = Guid.NewGuid().ToString()
            };

            var key = PortalAgent.NewKey(out var keyId, out var secretHash);

            // The user and its key are saved together or not at all.
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            // Without a password: nobody can sign in as an agent.
            var result = await _userManager.CreateAsync(user);

            if (!result.Succeeded)
            {
                throw PortalException.Validation(
                    nameof(CreateAgentRequest.Name),
                    result.Errors.Any(x => x.Code == nameof(IdentityErrorDescriber.DuplicateUserName))
                        ? "An agent with this name already exists"
                        : string.Join(" ", result.Errors.Select(x => x.Description)));
            }

            _dbContext.AgentKeys.Add(new PortalAgentKey { UserId = user.Id, KeyId = keyId, SecretHash = secretHash });
            await _dbContext.SaveChangesAsync();

            await transaction.CommitAsync();

            return new AgentCreatedResponse { ID = user.Id, Email = email, Key = key };
        }

        public async Task DeleteAgentAsync(string userId)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId);

            // People are not deleted here.
            if (user is null || !PortalAgent.IsAgent(user.Email))
            {
                throw PortalException.NotFound(AgentEntityName);
            }

            // What was shared with the agent must not wait for a later agent of the same name.
            // The address is matched whatever its letter case. Tracked, so each removal is audited.
            var email = (user.Email ?? string.Empty).ToLowerInvariant();

            var shares = await _dbContext.Collaborations
                .Where(x => x.UserEmail.ToLower() == email)
                .ToListAsync();

            _dbContext.Collaborations.RemoveRange(shares);

            // The key goes with the user (cascade), and with it the agent's access.
            _dbContext.Users.Remove(user);

            await _dbContext.SaveChangesAsync();
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
