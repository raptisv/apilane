using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    // Renaming the controller changes the contract: operation ids are built from its name.
    [Route("api/v1/account")]
    public class PortalAccountController : PortalApiControllerBase
    {
        private readonly IPortalAccountService _portalAccountService;

        public PortalAccountController(IPortalAccountService portalAccountService)
        {
            _portalAccountService = portalAccountService;
        }

        /// <summary>
        /// Registers a portal user and signs it in (session cookie). The e-mail is the user name.
        /// Answers 403 FORBIDDEN when registration is turned off on this instance (see GET /api/v1/instance).
        /// No session is needed. At most a few calls per minute from one IP address, then 429.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting(PortalRateLimitOptions.AccountPolicy)]
        [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
        public async Task<ActionResult<SessionResponse>> Register(RegisterRequest request)
        {
            var session = await _portalAccountService.RegisterAsync(request);

            return StatusCode(StatusCodes.Status201Created, session);
        }

        /// <summary>
        /// Mails a password-reset link to the user with this e-mail. The answer is 202 whether or
        /// not such a user exists. Answers 409 CONFLICT when the instance cannot send mail
        /// (see GET /api/v1/instance). No session is needed. At most a few calls per minute from
        /// one IP address, then 429.
        /// </summary>
        [HttpPost("password-reset-requests")]
        [AllowAnonymous]
        [EnableRateLimiting(PortalRateLimitOptions.AccountPolicy)]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> RequestPasswordReset(ForgotPasswordRequest request)
        {
            await _portalAccountService.RequestPasswordResetAsync(request);

            return StatusCode(StatusCodes.Status202Accepted);
        }

        /// <summary>
        /// Sets a new password with the code from a password-reset mail. A wrong or expired code
        /// and an unknown e-mail get the same 400 answer. Does not sign in; every existing session
        /// of the user ends. No session is needed.
        /// </summary>
        [HttpPost("password-resets")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
        {
            await _portalAccountService.ResetPasswordAsync(request);

            return NoContent();
        }

        /// <summary>
        /// Changes the password of the signed-in user. The answer sets a new session cookie, so the
        /// caller stays signed in; every other session of the user ends. A wrong OldPassword is a
        /// 400 on OldPassword.
        /// </summary>
        [HttpPut("password")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
        {
            await _portalAccountService.ChangePasswordAsync(request);

            return NoContent();
        }
    }
}
