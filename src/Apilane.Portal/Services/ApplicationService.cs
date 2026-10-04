using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Utilities;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Api.V1.Mapping;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApplicationService : IApplicationService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IPortalAccessService _portalAccessService;
        private readonly IApiServerClient _apiServerClient;
        private readonly IApplicationWriteScope _applicationWriteScope;
        private readonly IAuditLogService _auditLogService;
        private readonly ILogger<ApplicationService> _logger;

        public ApplicationService(
            ApplicationDbContext dbContext,
            IApplicationAccessService applicationAccessService,
            IPortalAccessService portalAccessService,
            IApiServerClient apiServerClient,
            IApplicationWriteScope applicationWriteScope,
            IAuditLogService auditLogService,
            ILogger<ApplicationService> logger)
        {
            _dbContext = dbContext;
            _applicationAccessService = applicationAccessService;
            _portalAccessService = portalAccessService;
            _apiServerClient = apiServerClient;
            _applicationWriteScope = applicationWriteScope;
            _auditLogService = auditLogService;
            _logger = logger;
        }

        public async Task<List<ApplicationResponse>> GetAllAsync()
        {
            var user = await _portalAccessService.GetCurrentUserAsync();
            var applications = await _applicationAccessService.GetVisibleApplicationsAsync();

            return applications
                .Select(x => x.ToResponse(user.Id))
                .ToList();
        }

        public async Task<ApplicationResponse> GetAsync(string appToken)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            return application.ToResponse(user.Id);
        }

        public async Task<ConnectionInfoResponse> GetConnectionInfoAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            return new ConnectionInfoResponse
            {
                Token = application.Token,
                ServerUrl = application.Server.ServerUrl,
                EncryptionKey = application.EncryptionKey.Decrypt(Globals.EncryptionKey)
            };
        }

        public async Task<ListResponse<AuditLogEntryResponse>> GetAuditLogAsync(string appToken, PageQuery page)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            return await _auditLogService.QueryAsync(application.ID, page);
        }

        public async Task ResetCacheAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationOrAnyForAdminAsync(appToken);

            await _apiServerClient.ClearCacheAsync(application);
        }

        public async Task<ApplicationResponse> UpdateAsync(string appToken, UpdateApplicationRequest request)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            // The stored type decides, never the request.
            if (application.DatabaseType != (int)DatabaseType.SQLLite)
            {
                if (request.ConnectionString is not null)
                {
                    if (string.IsNullOrWhiteSpace(request.ConnectionString))
                    {
                        throw PortalException.Validation(nameof(UpdateApplicationRequest.ConnectionString), "Required");
                    }

                    application.ConnectionString = request.ConnectionString;
                }
                else if (string.IsNullOrWhiteSpace(application.ConnectionString))
                {
                    throw PortalException.Validation(nameof(UpdateApplicationRequest.ConnectionString), "Required");
                }
            }

            application.Name = request.Name;

            // Nothing to call: the cache reset is how the API server learns about the change.
            await _applicationWriteScope.SaveAsync(application);

            return application.ToResponse(user.Id);
        }

        public async Task<ApplicationResponse> SetStatusAsync(string appToken, SetApplicationStatusRequest request)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            // [Required] on the contract already answers 400 for a missing value.
            application.Online = request.Online
                ?? throw PortalException.Validation(nameof(SetApplicationStatusRequest.Online), "Required");

            // The API server reads the status from its cache, so the reset runs even when the value
            // did not change: it brings the API server in line with the Portal.
            await _applicationWriteScope.SaveAsync(application);

            return application.ToResponse(user.Id);
        }

        public async Task RebuildAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            // Nothing changes in the Portal; the scope still resets the cache after a successful rebuild.
            await _applicationWriteScope.SaveAsync(application, () => _apiServerClient.RebuildAsync(application));
        }

        public async Task DeleteAsync(string appToken)
        {
            // Entities and properties are loaded, so they are removed and audited
            // with the application, like its collaborators and custom endpoints. Reports go with it in the database.
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            await _apiServerClient.DegenerateAsync(application);

            _dbContext.Applications.Remove(application);

            // No cache reset: the application no longer exists on the API server.
            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "An application was removed from the API server but not from the Portal | Application {Token}", application.Token);

                throw new PortalException(
                    StatusCodes.Status500InternalServerError,
                    PortalErrorCode.Error,
                    "The application was removed from the API server but could not be removed from the Portal.");
            }
        }
    }
}
