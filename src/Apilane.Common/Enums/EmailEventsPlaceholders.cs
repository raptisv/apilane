using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum EmailEventsPlaceholders
    {
        [Display(Name = "The url that the user has to follow in order to confirm the email address")]
        confirmation_url,
        [Display(Name = "The url that the user has to follow in order to reset the password")]
        reset_password_url,
        [Display(Name = "The confirmation token alone (URL-safe), to build your own link and confirm the email address with the Email/ConfirmEmail endpoint")]
        confirmation_token,
        [Display(Name = "The password reset token alone (URL-safe), to build your own link and reset the password with the Email/ResetPassword endpoint")]
        reset_password_token
    }
}
