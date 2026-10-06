using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace Apilane.Common.Services
{
    public class EmailService : IEmailService
    {
        private const string LinkPlaceholder = "[link]";

        // The reset and confirmation placeholders are replaced in the subject as well as in the body
        private static readonly Regex LinkPattern = new Regex(@"https?://\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ILogger<EmailService> _logger;

        public EmailService(ILogger<EmailService> logger)
        {
            _logger = logger;
        }

        public void SendMail(EmailInfo info)
        {
            // The log gets who the mail went to, what it is about and what became of it, never what is in it:
            // the body of a confirmation or a password reset mail holds a live token link, and the subject
            // can hold one too. Worked out before the try, as the catch needs both.
            var recipients = string.Join(",", info.Recipients ?? Array.Empty<string>());
            var subject = LinkPattern.Replace(info.Subject ?? string.Empty, LinkPlaceholder);

            try
            {
                MailMessage mMessage = new MailMessage()
                {
                    IsBodyHtml = true,
                    Subject = info.Subject,
                    Body = info.Body,
                    From = new MailAddress(info.MailFromAddress, info.MailFromDisplayName, Encoding.UTF8)
                };

                if (info.Recipients != null && info.Recipients.Length > 0)
                    mMessage.To.Add(string.Join(",", info.Recipients));

                if (info.Recipients_CC != null && info.Recipients_CC.Length > 0)
                    mMessage.CC.Add(string.Join(",", info.Recipients_CC));

                if (info.Recipients_BCC != null && info.Recipients_BCC.Length > 0)
                    mMessage.Bcc.Add(string.Join(",", info.Recipients_BCC));

                SmtpClient mailClient = new SmtpClient();
                mailClient.Host = info.MailServer;
                mailClient.Port = info.MailServerPort > 0 ? info.MailServerPort : 25;
                mailClient.EnableSsl = true;
                mailClient.Credentials = new System.Net.NetworkCredential(info.MailUserName, info.MailPassword);

                mailClient.Send(mMessage);

                mMessage.Dispose();

                _logger.LogInformation("(SendMail) => {Recipients} | {Subject} | sent", recipients, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "(SendMail) => {Recipients} | {Subject} | failed: {Reason}", recipients, subject, ex.Message);
            }
        }
    }
}
