using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// The e-mail and password of a portal user, for signing in.
    /// </summary>
    public class SignInRequest
    {
        [Required(ErrorMessage = AccountMessages.Required)]
        [EmailAddress(ErrorMessage = AccountMessages.Email)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = AccountMessages.Required)]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// A new portal user. The e-mail is also the user name.
    /// </summary>
    public class RegisterRequest
    {
        [Required(ErrorMessage = AccountMessages.Required)]
        [EmailAddress(ErrorMessage = AccountMessages.Email)]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// 8 to 100 characters.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = AccountMessages.PasswordLength)]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Must equal Password.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [Compare(nameof(Password), ErrorMessage = AccountMessages.PasswordsDiffer)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Asks for a password-reset mail for the user with this e-mail.
    /// </summary>
    public class ForgotPasswordRequest
    {
        [Required(ErrorMessage = AccountMessages.Required)]
        [EmailAddress(ErrorMessage = AccountMessages.Email)]
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>
    /// Sets a new password with the code from a password-reset mail.
    /// </summary>
    public class ResetPasswordRequest
    {
        /// <summary>
        /// The e-mail of the user the reset mail was sent to.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [EmailAddress(ErrorMessage = AccountMessages.Email)]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The 'code' value of the link in the reset mail.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// 8 to 100 characters.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = AccountMessages.PasswordLength)]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Must equal Password.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [Compare(nameof(Password), ErrorMessage = AccountMessages.PasswordsDiffer)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// The signed-in user's current password and the new one.
    /// </summary>
    public class ChangePasswordRequest
    {
        [Required(ErrorMessage = AccountMessages.Required)]
        public string OldPassword { get; set; } = string.Empty;

        /// <summary>
        /// 8 to 100 characters.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = AccountMessages.PasswordLength)]
        public string NewPassword { get; set; } = string.Empty;

        /// <summary>
        /// Must equal NewPassword.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [Compare(nameof(NewPassword), ErrorMessage = AccountMessages.NewPasswordsDiffer)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    internal static class AccountMessages
    {
        public const string Required = "Required";
        public const string Email = "Not a valid email address";
        public const string PasswordLength = "Must be 8 to 100 characters";
        public const string PasswordsDiffer = "The password and confirmation password do not match.";
        public const string NewPasswordsDiffer = "The new password and confirmation password do not match.";
    }
}
