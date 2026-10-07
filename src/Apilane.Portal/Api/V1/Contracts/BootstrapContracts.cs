using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    public class BootstrapResponse
    {
        /// <summary>
        /// The instance requires its first administrator to choose an email and password before it can be used.
        /// </summary>
        [Required]
        public bool Required { get; set; }
    }

    public class BootstrapRequest
    {
        /// <summary>
        /// The email address to use for the first administrator. Addresses at agent.local are reserved.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [EmailAddress(ErrorMessage = AccountMessages.Email)]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The randomly generated credential printed once in the Portal server's startup output.
        /// </summary>
        [Required(ErrorMessage = AccountMessages.Required)]
        [StringLength(100)]
        public string TemporaryPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = AccountMessages.Required)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = AccountMessages.PasswordLength)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = AccountMessages.Required)]
        [Compare(nameof(Password), ErrorMessage = AccountMessages.PasswordsDiffer)]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
