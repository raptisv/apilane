using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum EmailEventsPlaceholders
    {
        [Display(Name = "The url that the user has to follow in order to confirm the email address")]
        confirmation_url,
        [Display(Name = "The url that the user has to follow in order to reset the password")]
        reset_password_url
    }
}
