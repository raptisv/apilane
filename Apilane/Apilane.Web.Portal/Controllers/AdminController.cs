using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Web.Portal.Abstractions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Web.Portal.Controllers
{
    [Authorize(Roles = Globals.AdminRoleName)]
    public class AdminController : BaseWebController
    {
        private readonly IPortalSettingsService _portalSettingsService;
        private readonly PortalConfiguration _portalConfiguration;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService,
            PortalConfiguration portalConfiguration,
            IPortalSettingsService portalSettingsService,
            RoleManager<IdentityRole> roleManager)
            : base(dbContext, apiHttpService)
        {
            _portalConfiguration = portalConfiguration;
            _portalSettingsService = portalSettingsService;
            _roleManager = roleManager;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            ViewBag.AllServers = DBContext.Servers.Include(x => x.Applications).ToList();
            ViewBag.AllApplications = DBContext.Applications.Include(x => x.Server).ToList();
        }

        public IActionResult Users()
        {
            var users = DBContext.Users.ToList();
            var roles = _roleManager.Roles.ToList();
            var usersRoles = DBContext.UserRoles.ToList();

            return View((users, roles, usersRoles));
        }

        [HttpPost]
        public async Task<IActionResult> SetUserRole(string userId, bool setAsAdmin)
        {
            var user = DBContext.Users.ToList().Single(x => x.Id == userId);

            var currentUserId = User.Identity?.GetUserId() ?? throw new Exception("Unuatohrized");
            if (user.Id == currentUserId)
            {
                throw new Exception("Cannot change your own role");
            }

            var adminRole = _roleManager.Roles.ToList().SingleOrDefault(x => (x.Name ?? string.Empty).Equals(Globals.AdminRoleName, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception($"Role admin not found");

            var userRole = new IdentityUserRole<string>() { UserId = userId, RoleId = adminRole.Id };

            if (setAsAdmin)
            {
                DBContext.UserRoles.Add(userRole);
            }
            else
            {
                DBContext.UserRoles.Remove(userRole);
            }

            await DBContext.SaveChangesAsync();

            return RedirectToAction("Users", "Admin");
        }

        public IActionResult Servers()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ServerCreate()
        {
            return View("ServerAddEdit");
        }

        [HttpGet]
        public IActionResult ServerEdit(long id)
        {
            var item = DBContext.Servers.Single(x => x.ID == id);
            return View("ServerAddEdit", item);
        }

        [HttpPost]
        public async Task<IActionResult> ServerEdit(DBWS_Server model)
        {
            List<string> updateValues = new List<string>() {
                nameof(DBWS_Server.Name),
                nameof(DBWS_Server.ServerUrl)
            };

            // Remove all keys, allow only needed
            foreach (var key in ModelState.Keys)
            {
                if (!updateValues.Contains(key))
                {
                    ModelState.Remove(key);
                }
            }

            if (!Uri.IsWellFormedUriString(model.ServerUrl, UriKind.Absolute))
            {
                ModelState.AddModelError((nameof(DBWS_Server.ServerUrl)), "Not a valid url");
            }

            if (!ModelState.IsValid)
                return View("ServerAddEdit", model);

            try
            {
                // Edit
                if (model.ID > 0)
                {
                    var server = DBContext.Servers.Single(x => x.ID == model.ID);
                    server.Name = model.Name;
                    server.ServerUrl = model.ServerUrl;
                    updateValues.ForEach(x => DBContext.Entry(server).Property(x).IsModified = true);

                    await DBContext.SaveChangesAsync();

                    return RedirectToAction("Servers", "Admin");
                }
                else
                {
                    // Create
                    DBContext.Add(new DBWS_Server()
                    {
                        ID = 0,
                        Name = model.Name,
                        DateModified = DateTime.UtcNow,
                        ServerUrl = model.ServerUrl
                    });

                    await DBContext.SaveChangesAsync();

                    return RedirectToAction("Servers", "Admin");
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View("ServerAddEdit", model);
            }
        }

        [HttpGet]
        public IActionResult ServerDelete(long id)
        {
            var item = DBContext.Servers.Single(x => x.ID == id);
            return View("ServerDelete", item);
        }

        [HttpPost]
        public async Task<IActionResult> ServerDelete(DBWS_Server model)
        {
            try
            {
                var server = DBContext.Servers.Single(x => x.ID == model.ID);

                if (server.Applications.Count > 0)
                {
                    throw new Exception($"The server cannot be deleted since it has {server.Applications.Count} Application bound on it");
                }

                DBContext.Remove(server);

                await DBContext.SaveChangesAsync();

                return RedirectToAction("Servers", "Admin");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View("ServerDelete", model);
            }
        }

        public IActionResult UserApplications()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ApplicationData(string AppToken)
        {
            return View(GetApplication(AppToken));
        }

        [HttpGet]
        public IActionResult ApplicationLogs(string AppToken)
        {
            return View(GetApplication(AppToken));
        }

        [HttpGet]
        public IActionResult Settings()
        {
            return View(_portalSettingsService.Get());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(GlobalSettings model)
        {
            var portalSettings = _portalSettingsService.Get();

            if (!ModelState.IsValid)
            {
                return View(portalSettings);
            }

            try
            {
                // If not pre-existed create

                if (portalSettings == null)
                {
                    DBContext.GlobalSettings.Add(model);
                    await DBContext.SaveChangesAsync();
                }
                else
                {
                    // Else update existing
                    DBContext.Entry(portalSettings).State = EntityState.Detached;

                    model.ID = portalSettings.ID;
                    DBContext.Attach(model);
                    DBContext.Entry(model).Property(x => x.InstanceTitle).IsModified = true;
                    DBContext.Entry(model).Property(x => x.InstallationKey).IsModified = true;
                    DBContext.Entry(model).Property(x => x.AllowRegisterToPortal).IsModified = true;
                    DBContext.Entry(model).Property(x => x.MailFromAddress).IsModified = true;
                    DBContext.Entry(model).Property(x => x.MailFromDisplayName).IsModified = true;
                    DBContext.Entry(model).Property(x => x.MailPassword).IsModified = true;
                    DBContext.Entry(model).Property(x => x.MailServer).IsModified = true;
                    DBContext.Entry(model).Property(x => x.MailServerPort).IsModified = true;
                    DBContext.Entry(model).Property(x => x.MailUserName).IsModified = true;
                    await DBContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
            }

            return View(_portalSettingsService.Get());
        }

        [HttpGet]
        public IActionResult EntityData(string AppToken, string EntityName)
        {
            var application = GetApplication(AppToken);
            return View("~/Views/Entity/Data.cshtml", (application, application.Entities.Single(x => x.Name.Equals(EntityName))));
        }

        [HttpGet]
        public async Task<IActionResult> BackupDatabase()
        {
            // Copy the db file next to another file to avoid process lock on the existing database file
            var tempFileName = $"ApilaneDB.bak";
            try
            {
                System.IO.File.Copy(Path.Combine(_portalConfiguration.FilesPath, "Apilane.db"), tempFileName);
                var fileContent = await System.IO.File.ReadAllBytesAsync(tempFileName);
                return File(fileContent, "application/text", "Apilane.db");
            }
            finally
            {
                if (System.IO.File.Exists(tempFileName))
                {
                    System.IO.File.Delete(tempFileName);
                }
            }
        }

        private DBWS_Application GetApplication(string token)
        {
            return DBContext.Applications.Single(x => x.Token == token);
        }
    }
}