using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using System;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApplicationEmailSettingsService : IApplicationEmailSettingsService
    {
        public const string PasswordAgainMessage = "Enter the password again when you change the mail server or user name";

        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IApplicationWriteScope _applicationWriteScope;

        public ApplicationEmailSettingsService(
            IApplicationAccessService applicationAccessService,
            IApplicationWriteScope applicationWriteScope)
        {
            _applicationAccessService = applicationAccessService;
            _applicationWriteScope = applicationWriteScope;
        }

        public async Task<EmailSettingsResponse> GetAsync(string appToken)
        {
            return ToResponse(await _applicationAccessService.GetApplicationAsync(appToken));
        }

        public async Task<EmailSettingsResponse> UpdateAsync(string appToken, EmailSettingsRequest request)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            // Empty text is stored as null.
            var mailServer = NullIfEmpty(request.MailServer);
            var mailUserName = NullIfEmpty(request.MailUserName);

            // A collaborator who may not learn the stored password must not send it to a host or
            // account of their choice. Clearing a value sends it nowhere, so only a new value counts.
            if (request.MailPassword is null
                && !string.IsNullOrWhiteSpace(application.MailPassword)
                && ((mailServer is not null && !string.Equals(mailServer, application.MailServer, StringComparison.Ordinal))
                    || (mailUserName is not null && !string.Equals(mailUserName, application.MailUserName, StringComparison.Ordinal))))
            {
                throw PortalException.Validation(nameof(EmailSettingsRequest.MailPassword), PasswordAgainMessage);
            }

            application.MailServer = mailServer;
            application.MailServerPort = request.MailServerPort;
            application.MailFromAddress = NullIfEmpty(request.MailFromAddress);
            application.MailFromDisplayName = NullIfEmpty(request.MailFromDisplayName);
            application.MailUserName = mailUserName;
            application.EmailConfirmationRedirectUrl = NullIfEmpty(request.EmailConfirmationRedirectUrl);

            // The password is never sent to the client, so a client that does not change it sends null.
            if (request.MailPassword is not null)
            {
                application.MailPassword = NullIfEmpty(request.MailPassword);
            }

            // Nothing to call: the cache reset is how the API server learns about the change.
            await _applicationWriteScope.SaveAsync(application);

            return ToResponse(application);
        }

        private static string? NullIfEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static EmailSettingsResponse ToResponse(DBWS_Application application)
        {
            return new EmailSettingsResponse
            {
                MailServer = application.MailServer,
                MailServerPort = application.MailServerPort,
                MailFromAddress = application.MailFromAddress,
                MailFromDisplayName = application.MailFromDisplayName,
                MailUserName = application.MailUserName,
                HasMailPassword = !string.IsNullOrWhiteSpace(application.MailPassword),
                EmailConfirmationRedirectUrl = application.EmailConfirmationRedirectUrl,
                // The same rule the API server uses before it sends a mail.
                IsMailSetup = application.GetEmailSettings() is not null
            };
        }
    }
}
