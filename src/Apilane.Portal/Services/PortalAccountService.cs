using Apilane.Common;
using Apilane.Common.Utilities;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class PortalAccountService : IPortalAccountService
    {
        private const string InvalidLoginMessage = "Invalid login attempt.";
        private const string RegistrationDisabledMessage = "Registration is turned off on this instance.";
        private const string MailNotConfiguredMessage = "Email settings not set for instance, please contact admin.";

        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalAccessService _portalAccessService;
        private readonly IPortalSettingsService _portalSettingsService;
        private readonly IPortalMailService _portalMailService;
        private readonly IPortalLinkBuilder _portalLinkBuilder;
        private readonly IWebHostEnvironment _environment;

        public PortalAccountService(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ApplicationDbContext dbContext,
            IPortalAccessService portalAccessService,
            IPortalSettingsService portalSettingsService,
            IPortalMailService portalMailService,
            IPortalLinkBuilder portalLinkBuilder,
            IWebHostEnvironment environment)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _dbContext = dbContext;
            _portalAccessService = portalAccessService;
            _portalSettingsService = portalSettingsService;
            _portalMailService = portalMailService;
            _portalLinkBuilder = portalLinkBuilder;
            _environment = environment;
        }

        public async Task<SessionResponse> GetSessionAsync()
        {
            var user = await _portalAccessService.GetCurrentUserAsync();

            return ToSession(user, _portalAccessService.IsAdmin);
        }

        public async Task<ApiTokenResponse> GetApiTokenAsync()
        {
            var user = await _portalAccessService.GetCurrentUserAsync();

            // Never empty here: a user without a stored token has no session.
            return new ApiTokenResponse { Token = user.AdminAuthToken ?? string.Empty };
        }

        public async Task<SessionResponse> SignInAsync(SignInRequest request)
        {
            // The e-mail is the user name. Persistent cookie and no lockout.
            var result = await _signInManager.PasswordSignInAsync(request.Email, request.Password, isPersistent: true, lockoutOnFailure: false);

            if (!result.Succeeded)
            {
                // One answer for an unknown e-mail, a wrong password and a user that may not sign in.
                throw new PortalException(StatusCodes.Status401Unauthorized, PortalErrorCode.Unauthorized, InvalidLoginMessage);
            }

            var user = await _userManager.FindByNameAsync(request.Email)
                ?? throw new Exception("The user that just signed in was not found.");

            return await ToNewSessionAsync(user);
        }

        public async Task SignOutAsync()
        {
            var current = await _portalAccessService.FindCurrentUserAsync();

            // Only the holder of the current session may end it for everyone. A stale cookie
            // (the user signed in again elsewhere) is just removed and leaves the newer session alone.
            if (current is not null)
            {
                var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == current.Id);

                if (user is not null)
                {
                    user.AdminAuthToken = null;
                    await _dbContext.SaveChangesAsync();
                }
            }

            await _signInManager.SignOutAsync();
        }

        public async Task<SessionResponse> RegisterAsync(RegisterRequest request)
        {
            if (!_portalSettingsService.Get().AllowRegisterToPortal)
            {
                throw PortalException.Forbidden(RegistrationDisabledMessage);
            }

            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                DateRegistered = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, request.Password);

            if (!result.Succeeded)
            {
                throw IdentityFailure(result, error => error.Code.StartsWith("Password", StringComparison.Ordinal)
                    ? nameof(RegisterRequest.Password)
                    : nameof(RegisterRequest.Email));
            }

            // A session cookie, where the login sets a persistent one.
            await _signInManager.SignInAsync(user, isPersistent: false);

            return await ToNewSessionAsync(user);
        }

        public async Task RequestPasswordResetAsync(ForgotPasswordRequest request)
        {
            if (!_portalMailService.IsConfigured())
            {
                throw PortalException.Conflict(MailNotConfiguredMessage);
            }

            var user = await _userManager.FindByEmailAsync(request.Email);

            // An unknown e-mail is not an error: it gets the same 202 answer as a known one.
            if (user is null)
            {
                return;
            }

            var code = await _userManager.GeneratePasswordResetTokenAsync(user);

            var template = await File.ReadAllTextAsync(Path.Combine(_environment.WebRootPath, "EmailTemplates", "FORGOT_PASSWORD.html"));

            _portalMailService.Send(
                request.Email,
                "Reset password",
                template.Replace("{PLACEHOLDER}", _portalLinkBuilder.ResetPassword(code)));
        }

        public async Task ResetPasswordAsync(ResetPasswordRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);

            // The answer Identity gives for a bad code, so an unknown e-mail looks the same.
            var result = user is null
                ? IdentityResult.Failed(_userManager.ErrorDescriber.InvalidToken())
                : await _userManager.ResetPasswordAsync(user, request.Code, request.Password);

            if (user is null || !result.Succeeded)
            {
                throw IdentityFailure(result, error => error.Code == nameof(IdentityErrorDescriber.InvalidToken)
                    ? nameof(ResetPasswordRequest.Code)
                    : nameof(ResetPasswordRequest.Password));
            }

            // A reset must end the sessions that exist: the cookie's stamp is re-checked only every
            // 30 minutes and the token itself never expires.
            user.AdminAuthToken = null;
            await _dbContext.SaveChangesAsync();
        }

        public async Task ChangePasswordAsync(ChangePasswordRequest request)
        {
            var current = await _portalAccessService.GetCurrentUserAsync();

            var user = await _userManager.FindByIdAsync(current.Id)
                ?? throw PortalException.Unauthorized();

            var result = await _userManager.ChangePasswordAsync(user, request.OldPassword, request.NewPassword);

            if (!result.Succeeded)
            {
                throw IdentityFailure(result, error => error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)
                    ? nameof(ChangePasswordRequest.OldPassword)
                    : nameof(ChangePasswordRequest.NewPassword));
            }

            // The new password made the cookie of this request invalid. Signing in again issues a
            // new one (a session cookie) and ends the user's other sessions.
            await _signInManager.SignInAsync(user, isPersistent: false);

            _portalMailService.Send(
                user.Email ?? string.Empty,
                "Password changed",
                "Your password has been changed successfully.<br/><br/> This is just to confirm that it was you that made this change. " +
                $"If not, click on <a href=\"{_portalLinkBuilder.ForgotPassword()}\">this link</a> to reset your password.");
        }

        /// <summary>
        /// The session of a user that signed in during this request. The role is read from the
        /// database, the way the new cookie got it, because the request's own principal is not
        /// that of the new session.
        /// </summary>
        private async Task<SessionResponse> ToNewSessionAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);

            return ToSession(user, roles.Contains(Globals.AdminRoleName));
        }

        private SessionResponse ToSession(ApplicationUser user, bool isAdmin)
        {
            return new SessionResponse
            {
                Email = user.Email ?? string.Empty,
                IsAdmin = isAdmin,
                InstanceTitle = _portalSettingsService.Get().InstanceTitle,
                Version = typeof(Program).Assembly.GetVersion()
            };
        }

        /// <summary>
        /// Every Identity error becomes one entry of Errors[], on the request property it is about.
        /// </summary>
        private static PortalException IdentityFailure(IdentityResult result, Func<IdentityError, string> property)
        {
            return new PortalException(
                StatusCodes.Status400BadRequest,
                PortalErrorCode.Validation,
                PortalApiErrors.ValidationMessage,
                errors: result.Errors
                    .Select(x => new ErrorDetail { Property = property(x), Message = x.Description })
                    .ToList());
        }
    }
}
