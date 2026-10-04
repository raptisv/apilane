using Apilane.Common;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.Internal
{
    /// <summary>
    /// What every API server asks the Portal (PortalInfoService in Apilane.Api.Core): the definition
    /// of an application, and whether a Portal user may manage it. Internal: not part of /api/v1,
    /// not in the OpenAPI document, not for browsers.
    /// Anonymous because these calls carry no login cookie: the caller proves itself with the
    /// installation key (InstallationKeyFilter).
    /// The answers are the stored records as they are, secrets included, with property names as in
    /// the models and enums as numbers, because that is how the API server reads them. So this is
    /// not a PortalApiControllerBase controller, and a Portal and its API servers must run the
    /// same version.
    /// </summary>
    [AllowAnonymous]
    [ServiceFilter(typeof(InstallationKeyFilter))]
    [ApiExplorerSettings(IgnoreApi = true)]
    [Route("api/internal/applications/{appToken}")]
    public class InternalApplicationsController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;

        public InternalApplicationsController(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        /// <summary>
        /// The application with its server, entities, properties, custom endpoints and
        /// collaborators. An unknown token answers 200 with 'null'.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get(string appToken)
        {
            // Split query: as one query, the sibling collections (collaborators, custom endpoints, entity
            // properties) multiply into every combination, each row repeating all application columns,
            // which made large applications slow enough to time out the API's call.
            // No tracking: the result is only serialized, never saved.
            var application = await _dbContext.Applications
                .AsNoTracking()
                .AsSplitQuery()
                .Include(a => a.Collaborates)
                .Include(a => a.CustomEndpoints)
                .Include(a => a.Server)
                .Include(a => a.Entities)
                .ThenInclude(e => e.Properties)
                .SingleOrDefaultAsync(x => x.Token == appToken);

            return new JsonResult(application);
        }

        /// <summary>
        /// 'true' when the Portal user whose token is in the Authorization header ("Bearer {token}")
        /// may manage the application: its owner, a collaborator, or an administrator (for any
        /// token). 'false' for everyone else, and without a token or with one that is nobody's.
        /// </summary>
        [HttpGet("access")]
        public async Task<IActionResult> GetAccess(string appToken)
        {
            var userToken = GetBearerToken();

            if (string.IsNullOrWhiteSpace(userToken))
            {
                return new JsonResult(false);
            }

            var user = await _dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.AdminAuthToken == userToken);

            if (user is null)
            {
                return new JsonResult(false);
            }

            var isAdmin = await _dbContext.UserRoles
                .Where(x => x.UserId == user.Id)
                .Join(_dbContext.Roles, x => x.RoleId, x => x.Id, (_, role) => role.Name)
                .AnyAsync(x => x == Globals.AdminRoleName);

            if (isAdmin)
            {
                return new JsonResult(true);
            }

            var tokens = await _dbContext.Applications
                .Where(x => x.Token == appToken)
                .Where(x => x.UserID == user.Id || x.Collaborates.Any(c => c.UserEmail == user.Email))
                .Select(x => x.Token)
                .ToListAsync();

            // Exact match, whatever collation the database compares with.
            return new JsonResult(tokens.Any(x => string.Equals(x, appToken, StringComparison.Ordinal)));
        }

        private string GetBearerToken()
        {
            var parts = Request.Headers.Authorization.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
        }
    }
}
