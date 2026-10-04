using Apilane.Common.Abstractions;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;

namespace Apilane.Portal.Services
{
    public class PortalMailService : IPortalMailService
    {
        private readonly IPortalSettingsService _portalSettingsService;
        private readonly IEmailService _emailService;

        public PortalMailService(
            IPortalSettingsService portalSettingsService,
            IEmailService emailService)
        {
            _portalSettingsService = portalSettingsService;
            _emailService = emailService;
        }

        public bool IsConfigured()
        {
            return _portalSettingsService.Get().IsMailSetup();
        }

        public bool Send(string recipient, string subject, string htmlBody)
        {
            var settings = _portalSettingsService.Get();

            if (!settings.IsMailSetup())
            {
                return false;
            }

            // IsMailSetup has checked every value; the fallbacks only satisfy the compiler.
            _emailService.SendMail(new EmailInfo
            {
                MailServer = settings.MailServer ?? string.Empty,
                MailServerPort = settings.MailServerPort ?? 25,
                MailUserName = settings.MailUserName ?? string.Empty,
                MailPassword = settings.MailPassword ?? string.Empty,
                MailFromAddress = settings.MailFromAddress ?? string.Empty,
                MailFromDisplayName = settings.MailFromDisplayName ?? string.Empty,
                Subject = subject,
                Body = htmlBody,
                Recipients = new[] { recipient }
            });

            return true;
        }
    }
}
