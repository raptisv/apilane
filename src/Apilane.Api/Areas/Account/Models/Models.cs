using Apilane.Common.Attributes;
using Apilane.Common.Security;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Api.Areas.Account.Models
{
    public class ForgotPasswordViewModel
    {
        [AttrRequired]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = null!;
    }

    public class ResetPasswordViewModel
    {
        [AttrRequired]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = null!;

        [AttrRequired]
        [StringLength(PasswordPolicy.MaximumLength, ErrorMessage = "The {0} must be between {2} and {1} characters long.", MinimumLength = PasswordPolicy.MinimumLength)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = null!;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = null!;
    }
}