using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Controllers
{
    [AllowAnonymous]
    public class CollaborateController : BaseWebApplicationController
    {
        private readonly IEmailService _emailService;
        private readonly IPortalSettingsService _portalSettingsService;

        public CollaborateController(
            IPortalSettingsService portalSettingsService,
            ApplicationDbContext dbContext,
            IEmailService emailService,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {
            _emailService = emailService;
            _portalSettingsService = portalSettingsService;
        }

        public ActionResult Index()
        {
            ValidateUser();

            return View(Application);
        }

        private void ValidateUser()
        {
            var userId = User.Identity?.GetUserId()
                ?? throw new Exception("User not logged in");

            if (!Application.UserID.Equals(userId))
            {
                throw new Exception("Cannot share this application");
            }
        }

        private ActionResult RedirectToIndex()
        {
            return RedirectToRoute("AppRoute", new { appid = Application.Token, controller = "Collaborate", action = "Index" });
        }

        [HttpGet]
        public IActionResult Share()
        {
            ValidateUser();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Share(DBWS_Collaborate model)
        {
            ValidateUser();

            var UpdateValues = new List<string>()
            {
                nameof(DBWS_Collaborate.UserEmail)
            };

            // Remove all keys, allow only needed
            foreach (var key in ModelState.Keys)
            {
                if (!UpdateValues.Contains(key))
                {
                    ModelState.Remove(key);
                }
            }

            try
            {
                model.UserEmail = Utils.GetString(model.UserEmail);
                if (!Utils.IsValidEmail(model.UserEmail))
                {
                    ModelState.AddModelError(nameof(DBWS_Collaborate.UserEmail), "Invalid email address");
                }

                if (Application.Collaborates.Any(x => x.UserEmail.Equals(model.UserEmail, StringComparison.OrdinalIgnoreCase)))
                {
                    ModelState.AddModelError(nameof(DBWS_Collaborate.UserEmail), "Already shared with that user");
                }

                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                model.AppID = Application.ID;

                DBContext.Collaborations.Add(model);
                await DBContext.SaveChangesAsync();

                var portalSettings = _portalSettingsService.Get();

                if (portalSettings.IsMailSetup())
                {
                    _emailService.SendMail(new EmailInfo()
                    {
                        MailFromAddress = portalSettings.MailFromAddress!,
                        MailFromDisplayName = portalSettings.MailFromDisplayName!,
                        MailPassword = portalSettings.MailPassword!,
                        MailServer = portalSettings.MailServer!,
                        MailServerPort = portalSettings.MailServerPort ?? 25,
                        MailUserName = portalSettings.MailUserName!,
                        Subject = $"{portalSettings.InstanceTitle} - admin rights to {Application.Name}",
                        Body = $"User {Application.AdminEmail} shared administrator rights to application <b>{Application.Name}</b> with you.<br/>Navigate to the <a href='{(Request.Scheme + Uri.SchemeDelimiter + Request.Host)}'>portal</a> to access the application.",
                        Recipients = new string[] { model.UserEmail }
                    });
                }

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Unshare(long ID)
        {
            ValidateUser();
            return View(Application.Collaborates.Single(x => x.ID == ID));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unshare(DBWS_Collaborate model)
        {
            ValidateUser();

            try
            {
                DBContext.Remove(DBContext.Collaborations.Single(x => x.ID == model.ID));
                await DBContext.SaveChangesAsync();

                return RedirectToIndex();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("CustomError", ex.Message);
                return View(model);
            }
        }
    }
}