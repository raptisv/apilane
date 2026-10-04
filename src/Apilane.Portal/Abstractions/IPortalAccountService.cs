using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Signing in and out, registering and passwords of portal users, for the management API.
    /// Built on Identity's SignInManager and UserManager; every sign-in starts a new session
    /// (<see cref="Services.AppClaimsPrincipalFactory"/>).
    /// </summary>
    public interface IPortalAccountService
    {
        /// <summary>
        /// The session of the signed-in caller. Throws UNAUTHORIZED when there is none.
        /// </summary>
        Task<SessionResponse> GetSessionAsync();

        /// <summary>
        /// The token the signed-in caller uses on the API servers. Throws UNAUTHORIZED when there is no session.
        /// </summary>
        Task<ApiTokenResponse> GetApiTokenAsync();

        /// <summary>
        /// Signs in with a persistent cookie. Throws UNAUTHORIZED 'Invalid login attempt.' for any failure.
        /// </summary>
        Task<SessionResponse> SignInAsync(SignInRequest request);

        /// <summary>
        /// Removes the cookie. When the cookie still names the current session of its user, the
        /// session is also ended for every other holder of it. Never fails for a missing or stale cookie.
        /// </summary>
        Task SignOutAsync();

        /// <summary>
        /// Creates a user and signs it in with a session cookie. Throws FORBIDDEN when registration
        /// is turned off and VALIDATION for what Identity refuses.
        /// </summary>
        Task<SessionResponse> RegisterAsync(RegisterRequest request);

        /// <summary>
        /// Mails a reset link when a user has this e-mail; does nothing otherwise. Throws CONFLICT
        /// when the instance mail is not configured.
        /// </summary>
        Task RequestPasswordResetAsync(ForgotPasswordRequest request);

        /// <summary>
        /// Sets a new password with the code of a reset mail. Throws the same VALIDATION error for
        /// a bad code and for an unknown e-mail.
        /// </summary>
        Task ResetPasswordAsync(ResetPasswordRequest request);

        /// <summary>
        /// Changes the password of the signed-in caller and signs it in again with a session cookie.
        /// </summary>
        Task ChangePasswordAsync(ChangePasswordRequest request);
    }
}
