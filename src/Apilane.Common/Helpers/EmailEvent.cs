using Apilane.Common.Enums;
using System.Collections.Generic;

namespace Apilane.Common.Helpers
{
    public class EmailEvent
    {
        public EmailEventsCodes Code { get; set; }
        public string Description { get; set; } = null!;
        public string DefaultSubject { get; set; } = null!;
        public string DefaultContent { get; set; } = null!;
        public bool IsUserTriggeredSameAsAcceptingTheEmail { get; set; }
        public List<EmailEventsPlaceholders> Placeholders { get; set; } = null!;

        public static Dictionary<string, string> UserProperties = new()
        {
            { "ID", "The user id" },
            { "Username", "The username" },
            { "Email", "The user email" }
        };

        public static List<EmailEvent> EmailEvents = new()
        {
            new()
            {
                IsUserTriggeredSameAsAcceptingTheEmail = true,
                Code = EmailEventsCodes.UserRegisterConfirmation,
                Description = "Email confirmation",
                Placeholders = new List<EmailEventsPlaceholders>() { EmailEventsPlaceholders.confirmation_url },
                DefaultSubject = $"Welcome!",
                DefaultContent = $"Thank you for creating an account.<br/>" +
                                 $"Please click on this <a href=\"{{{EmailEventsPlaceholders.confirmation_url}}}\">link</a> to confirm your email address"
            },
            new()
            {
                IsUserTriggeredSameAsAcceptingTheEmail = true,
                Code = EmailEventsCodes.UserForgotPassword,
                Description = "Reset password",
                Placeholders = new List<EmailEventsPlaceholders>() { EmailEventsPlaceholders.reset_password_url },
                DefaultSubject = $"Reset Password",
                DefaultContent = $"Please click on this <a href=\"{{{EmailEventsPlaceholders.reset_password_url}}}\">link</a> to reset your password"
            }
        };
    }
}
