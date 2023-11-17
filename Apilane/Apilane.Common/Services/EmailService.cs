using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Mail;
using System.Text;

namespace Apilane.Common.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;

        public EmailService(ILogger<EmailService> logger)
        {
            _logger = logger;
        }

        public void SendMail(EmailInfo info)
        {
            try
            {
                _logger.LogInformation($"(SendMail) => {string.Join(",", info.Recipients)} | {info.Subject} | {info.Body}");

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"(SendMail) => {ex.Message} {string.Join(",", info.Recipients)} {info.Subject} {info.Body}");
            }
        }
    }
}
