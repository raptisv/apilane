using Apilane.Common.Enums;
using Apilane.Common.Utilities;
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

        public static Dictionary<string, string> UserProperties = new Dictionary<string, string>()
        {
            { "ID", "The user id" },
            { "Username", "The username" },
            { "Email", "The user email" }
        };

        public static List<object> GetEventsPlaceholdersAndDescriptions()
        {
            var result = new List<object>();
            foreach (var ev in EmailEvents)
            {
                foreach (var prop in UserProperties)
                {
                    result.Add(new
                    {
                        Event = ev.Code.ToString(),
                        Name = $"{{Users.{prop.Key}}}",
                        Description = prop.Value
                    });
                }

                if (!ev.IsUserTriggeredSameAsAcceptingTheEmail)
                {
                    foreach (var prop in UserProperties)
                    {
                        result.Add(new
                        {
                            Event = ev.Code.ToString(),
                            Name = $"{{Users.From.{prop.Key}}}",
                            Description = prop.Value
                        });
                    }
                }

                foreach (var ph in ev.Placeholders)
                {
                    result.Add(new
                    {
                        Event = ev.Code.ToString(),
                        Name = $"{{{ph.ToString()}}}",
                        Description = EnumProvider<EmailEventsPlaceholders>.GetDisplayValue(ph)
                    });
                }
            }

            return result;
        }

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
