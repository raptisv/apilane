using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Apilane.Web.Portal.Abstractions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Apilane.Web.Portal.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private SignInManager<ApplicationUser> _signInManager;
        private UserManager<ApplicationUser> _userManager;
        private ApplicationDbContext _dbContext;
        protected IWebHostEnvironment _webHostEnvironment;
        private IPortalSettingsService _portalSettingsService;
        private readonly IEmailService _emailService;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext dbContext,
            IWebHostEnvironment webHostEnvironment,
            IPortalSettingsService portalSettingsService,
            IEmailService emailService)
        {
            _webHostEnvironment = webHostEnvironment;
            _signInManager = signInManager;
            _userManager = userManager;
            _portalSettingsService = portalSettingsService;
            _emailService = emailService;
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<IActionResult> LogOff()
        {
            var userId = User.Identity?.GetUserId()
                ?? throw new Exception("User not logged in");

            var currentUser = await _userManager.FindByIdAsync(userId);
            currentUser!.AdminAuthToken = null;

            _dbContext.Attach(currentUser);
            _dbContext.Entry(currentUser).Property(x => x.AdminAuthToken).IsModified = true;
            await _dbContext.SaveChangesAsync();

            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        // GET: /Account/Login
        [AllowAnonymous]
        public IActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
        {
            private ApplicationDbContext _dbontextInner;

            public AppClaimsPrincipalFactory(
                UserManager<ApplicationUser> userManager,
                RoleManager<IdentityRole> roleManager,
                IOptions<IdentityOptions> options,
                ApplicationDbContext dbontextInner)
                : base(userManager, roleManager, options)
            {
                _dbontextInner = dbontextInner;
            }

            public async override Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
            {
                // Create a new token
                string portalUserAuthToken = Guid.NewGuid().ToString();

                user.LastLogin = DateTime.UtcNow;
                user.AdminAuthToken = portalUserAuthToken;
                _dbontextInner.Attach(user);
                _dbontextInner.Entry(user).Property(x => x.AdminAuthToken).IsModified = true;
                _dbontextInner.Entry(user).Property(x => x.LastLogin).IsModified = true;
                await _dbontextInner.SaveChangesAsync();

                var principal = await base.CreateAsync(user);

                var userIdentity = (ClaimsIdentity)(principal?.Identity
                    ?? throw new Exception("User not logged in"));

                userIdentity.AddClaims(new[] { new Claim("Id", user.Id) });
                userIdentity.AddClaims(new[] { new Claim("PortalUserAuthToken", portalUserAuthToken) });
                userIdentity.AddClaims(new[] { new Claim("UserEmail", user.Email ?? string.Empty) });

                return principal;
            }
        }

        //
        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string? returnUrl)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, true, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                return RedirectToLocal(returnUrl);
            }
            else
            {
                ModelState.AddModelError("", "Invalid login attempt.");
                return View(model);
            }
        }

        [AllowAnonymous]
        public IActionResult Register()
        {
            var portalSettings = _portalSettingsService.Get();

            if (!portalSettings.AllowRegisterToPortal)
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            var portalSettings = _portalSettingsService.Get();

            if (!portalSettings.AllowRegisterToPortal)
            {
                return RedirectToAction("Login", "Account");
            }

            if (Utils.GetString(model.Password).Length < 8)
            {
                ModelState.AddModelError(nameof(RegisterViewModel.Password), "Minimum length is 8 characters");
            }

            if (ModelState.IsValid)
            {
                var user = new ApplicationUser { UserName = model.Email, Email = model.Email, DateRegistered = DateTime.UtcNow };

                var result = await _userManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);

                    return RedirectToAction("Index", "Applications");
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("CustomError", error.Description);
                }
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [AllowAnonymous]
        public ActionResult AppEmailConfirmed()
        {
            return View();
        }

        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            var portalSettings = _portalSettingsService.Get();

            if (!portalSettings.IsMailSetup())
            {
                ModelState.AddModelError("", "Email settings not set for instance, please contact admin.");
                return View(model);
            }

            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(model.Email);
                if (user is not null)
                {
                    // For more information on how to enable account confirmation and password reset please visit https://go.microsoft.com/fwlink/?LinkID=320771
                    // Send an email with this link
                    string code = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var callbackUrl = Url.Action("ResetPassword", "Account", new { userId = user.Id, code = code }, protocol: Request.Scheme);
                    string EmailContent = System.IO.File.ReadAllText(Path.Combine(_webHostEnvironment.WebRootPath, "EmailTemplates/FORGOT_PASSWORD.html"));
                    EmailContent = EmailContent.Replace("{PLACEHOLDER}", callbackUrl);

                    _emailService.SendMail(new EmailInfo()
                    {
                        MailFromAddress = portalSettings.MailFromAddress!,
                        MailFromDisplayName = portalSettings.MailFromDisplayName!,
                        MailPassword = portalSettings.MailPassword!,
                        MailServer = portalSettings.MailServer!,
                        MailServerPort = portalSettings.MailServerPort ?? 25,
                        MailUserName = portalSettings.MailUserName!,
                        Subject = "Reset password",
                        Body = EmailContent,
                        Recipients = new string[] { model.Email }
                    });
                }

                // Don't reveal that the user does not exist or is not confirmed
                return RedirectToAction("ForgotPasswordConfirmation", "Account");
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        [AllowAnonymous]
        public ActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        [AllowAnonymous]
        public ActionResult ResetPassword(string code)
        {
            return code == null ? View("Error") : View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                // Don't reveal that the user does not exist
                return RedirectToAction("ResetPasswordConfirmation", "Account");
            }
            var result = await _userManager.ResetPasswordAsync(user, model.Code, model.Password);
            if (result.Succeeded)
            {
                return RedirectToAction("ResetPasswordConfirmation", "Account");
            }
            AddErrors(result);
            return View();
        }

        [AllowAnonymous]
        public ActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_userManager != null)
                {
                    _userManager.Dispose();
                    _userManager = null!;
                }
            }

            base.Dispose(disposing);
        }


        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }
        }

        private ActionResult RedirectToLocal(string? returnUrl)
        {
            //// Local url
            //if (Url.IsLocalUrl(returnUrl))
            //{
            //    return Redirect(returnUrl);
            //}

            //// Same domain
            //if (!string.IsNullOrWhiteSpace(returnUrl) &&
            //    new Uri(returnUrl).Host.Equals(HttpContext.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
            //{
            //    return Redirect(returnUrl);
            //}

            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Applications");
        }
    }
}