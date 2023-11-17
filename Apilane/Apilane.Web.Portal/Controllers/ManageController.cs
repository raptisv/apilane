using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Apilane.Web.Portal.Abstractions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Web.Portal.Controllers
{
    [Authorize]
    public class ManageController : Controller
    {
        private SignInManager<ApplicationUser> _signInManager;
        private UserManager<ApplicationUser> _userManager;
        private readonly IPortalSettingsService _portalSettingsService;
        private readonly IEmailService _emailService;

        public ManageController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IPortalSettingsService portalSettingsService,
            IEmailService emailService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _portalSettingsService = portalSettingsService;
            _emailService = emailService;
        }

        // GET: /Manage/ChangePassword
        public ActionResult ChangePassword()
        {
            return View();
        }

        // POST: /Manage/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (User.Identity is null)
            {
                throw new System.Exception("Unauthorized");
            }

            var user = await _userManager.FindByIdAsync(User.Identity.GetUserId()) ?? throw new System.Exception("Unauthorized");

            var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
            if (result.Succeeded)
            {
                user = await _userManager.FindByIdAsync(User.Identity.GetUserId());
                if (user is not null)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                }

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
                        Subject = "Password changed",
                        Body = "Your password has been changed successfully.<br/><br/> This is just to confirm that it was you that made this change. If not, click on <a href=" + Url.Action("ForgotPassword", "Account", new { }, protocol: Request.Scheme) + ">this link</a> to reset your password.",
                        Recipients = new string[] { User.Identity.GetUserEmail() }
                    });
                }

                return RedirectToAction("Login", "Account");
            }
            AddErrors(result);
            return View(model);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _userManager != null)
            {
                _userManager.Dispose();
                _userManager = null!;
            }

            base.Dispose(disposing);
        }

        #region Helpers

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
        }

        #endregion
    }
}