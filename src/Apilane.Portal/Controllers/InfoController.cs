using Apilane.Common;
using Apilane.Common.Security;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Data;
using System.Linq;

namespace Apilane.Portal.Controllers
{
    [AllowAnonymous]
    public class InfoController : Controller
    {
        private static bool _legacyKeyTransportLogged;

        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalSettingsService _portalSettingsService;
        private readonly ILogger<InfoController> _logger;

        public InfoController(
            ApplicationDbContext dbContext,
            IPortalSettingsService portalSettingsService,
            ILogger<InfoController> logger)
        {
            _dbContext = dbContext;
            _portalSettingsService = portalSettingsService;
            _logger = logger;
        }

        /// <summary>
        /// Loads the application info when requested from api
        /// </summary>
        [HttpGet]
        public IActionResult GetApplication(string appToken, string? key = null)
        {
            if (!IsInstallationKeyValid(key))
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
        public IActionResult UserOwnsApplication(string appToken, string? authToken = null, string? key = null)
        {
            if (!IsInstallationKeyValid(key))
            {
                return Unauthorized();
            }

            // Get the user. The token is expected in the Authorization header; the query parameter
            // is only a fallback for API instances that have not been upgraded yet.
            var user = GetUser(ResolveAuthToken(authToken));

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

        /// <summary>
        /// Accepts the installation key from the x-installation-key header, falling back to the
        /// legacy ?key= query parameter (deprecated: it ends up in logs and proxies) for API
        /// instances that have not been upgraded yet. The comparison is constant-time.
        /// </summary>
        private bool IsInstallationKeyValid(string? legacyQueryKey)
        {
            var portalSettings = _portalSettingsService.Get() ?? throw new Exception("No portal settings");

            var headerKey = Request.Headers[Globals.InstallationKeyHeaderName].ToString();
            var providedKey = !string.IsNullOrWhiteSpace(headerKey) ? headerKey : legacyQueryKey;

            if (string.IsNullOrWhiteSpace(headerKey) && !string.IsNullOrWhiteSpace(legacyQueryKey) && !_legacyKeyTransportLogged)
            {
                _legacyKeyTransportLogged = true;
                _logger.LogWarning("The API sent the installation key as a query parameter. Upgrade the API so it is sent in the '{Header}' header instead.", Globals.InstallationKeyHeaderName);
            }

            return SecureCompare.AreEqual(portalSettings.InstallationKey, providedKey);
        }

        /// <summary>
        /// Reads the portal user's token from the Authorization header, falling back to the legacy
        /// query parameter for API instances that have not been upgraded yet.
        /// </summary>
        private string ResolveAuthToken(string? legacyQueryToken)
        {
            var authorization = Request.Headers.Authorization.ToString();

            if (!string.IsNullOrWhiteSpace(authorization))
            {
                var parts = authorization.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts[parts.Length - 1];
            }

            return legacyQueryToken ?? string.Empty;
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