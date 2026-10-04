using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class InstanceSettingsService : IInstanceSettingsService
    {
        private readonly ApplicationDbContext _dbContext;

        public InstanceSettingsService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<InstanceSettingsResponse> GetAsync()
        {
            return ToResponse(await FindAsync());
        }

        public async Task<InstanceResponse> GetPublicAsync()
        {
            var settings = await FindAsync();

            return new InstanceResponse
            {
                InstanceTitle = settings.InstanceTitle,
                AllowRegister = settings.AllowRegisterToPortal,
                IsMailSetup = settings.IsMailSetup()
            };
        }

        public async Task<InstanceSettingsResponse> UpdateAsync(InstanceSettingsRequest request)
        {
            var settings = await FindAsync();

            settings.InstanceTitle = request.InstanceTitle;

            // [Required] on the contract already answers 400 for a missing value.
            settings.AllowRegisterToPortal = request.AllowRegisterToPortal
                ?? throw PortalException.Validation(nameof(InstanceSettingsRequest.AllowRegisterToPortal), "Required");

            // Empty text is stored as null.
            settings.MailServer = NullIfEmpty(request.MailServer);
            settings.MailServerPort = request.MailServerPort;
            settings.MailUserName = NullIfEmpty(request.MailUserName);
            settings.MailFromAddress = NullIfEmpty(request.MailFromAddress);
            settings.MailFromDisplayName = NullIfEmpty(request.MailFromDisplayName);

            // The secrets are never sent to the client, so a client that does not change them sends null.
            if (request.InstallationKey is not null)
            {
                settings.InstallationKey = request.InstallationKey;
            }

            if (request.MailPassword is not null)
            {
                settings.MailPassword = NullIfEmpty(request.MailPassword);
            }

            await _dbContext.SaveChangesAsync();

            return ToResponse(settings);
        }

        // Tracked, so that a change to it is saved and audited.
        private async Task<GlobalSettings> FindAsync()
        {
            return await _dbContext.GlobalSettings.SingleOrDefaultAsync()
                ?? throw new Exception("No portal settings setup");
        }

        private static string? NullIfEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static InstanceSettingsResponse ToResponse(GlobalSettings settings)
        {
            return new InstanceSettingsResponse
            {
                InstanceTitle = settings.InstanceTitle,
                AllowRegisterToPortal = settings.AllowRegisterToPortal,
                HasInstallationKey = !string.IsNullOrWhiteSpace(settings.InstallationKey),
                MailServer = settings.MailServer,
                MailServerPort = settings.MailServerPort,
                MailUserName = settings.MailUserName,
                HasMailPassword = !string.IsNullOrWhiteSpace(settings.MailPassword),
                MailFromAddress = settings.MailFromAddress,
                MailFromDisplayName = settings.MailFromDisplayName,
                IsMailSetup = settings.IsMailSetup()
            };
        }
    }
}
