using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApplicationEmailSettingsService : IApplicationEmailSettingsService
    {
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

            // Empty text is stored as null, which is what the Razor form stores for an empty box.
            application.MailServer = NullIfEmpty(request.MailServer);
            application.MailServerPort = request.MailServerPort;
            application.MailFromAddress = NullIfEmpty(request.MailFromAddress);
            application.MailFromDisplayName = NullIfEmpty(request.MailFromDisplayName);
            application.MailUserName = NullIfEmpty(request.MailUserName);
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
