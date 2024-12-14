using Apilane.Common;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Data;
using System.Linq;

namespace Apilane.Portal.Controllers
{
    [AllowAnonymous]
    public class InfoController : Controller
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalSettingsService _portalSettingsService;

        public InfoController(
            ApplicationDbContext dbContext, 
            IPortalSettingsService portalSettingsService)
        {
            _dbContext = dbContext;
            _portalSettingsService = portalSettingsService;
        }

        /// <summary>
        /// Loads the application info when requested from api
        /// </summary>
        [HttpGet]
        public IActionResult GetApplication(string appToken, string key)
        {
            var portalSettings = _portalSettingsService.Get() ?? throw new Exception("No portal settings");

            // Simple key validation
            if (!portalSettings.InstallationKey.Equals(key))
            {
                return Unauthorized();
            }

            var application = _dbContext.Applications
                .Include(a => a.Collaborates)
                .Include(a => a.CustomEndpoints)
                .Include(a => a.Server)
                .Include(a => a.Entities)
                .ThenInclude(e => e.Properties)
                .SingleOrDefault(x => x.Token == appToken);

            return Json(application);
        }

        [HttpGet]
        public IActionResult UserOwnsApplication(string authToken, string appToken, string key)
        {
            var portalSettings = _portalSettingsService.Get() ?? throw new Exception("No portal settings");

            // Simple key validation
            if (!portalSettings.InstallationKey.Equals(key))
            {
                return Unauthorized();
            }

            // Get the user
            var user = GetUser(authToken);

            if (user.User != null)
            {
                // Get shared app Ids
                var userCollaborates = _dbContext.Collaborations.Where(x => x.UserEmail == user.User.Email).ToList();

                // Get user and shared apps
                var userApplications = _dbContext.Applications.ToList()
                    .Where(x => x.UserID == user.User.Id || userCollaborates.Select(c => c.AppID).Contains(x.ID)).ToList();

                var userCanAccessApp = user.IsGlobalAdmin || userApplications.Any(x => x.Token.Equals(appToken));

                return Json(userCanAccessApp);
            }

            return Json(false);
        }

        private (ApplicationUser? User, bool IsGlobalAdmin) GetUser(string authToken)
        { 
            if (string.IsNullOrWhiteSpace(authToken))
            {
                return (null, false);
            }

            var roles = _dbContext.Roles.ToList();
            var currentUser = _dbContext.Users.SingleOrDefault(x => x.AdminAuthToken == authToken);
            var userId = currentUser?.Id;
            var userRole = _dbContext.UserRoles.Where(x => x.UserId == userId).FirstOrDefault();
            var isGlobalAdmin = userRole != null && roles.Any(r => r.Id == userRole.RoleId && r.Name == Globals.AdminRoleName);

            return (currentUser, isGlobalAdmin);
        }
    }
}