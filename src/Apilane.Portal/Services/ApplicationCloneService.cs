using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApplicationCloneService : IApplicationCloneService
    {
        private const string OperationEntityName = "CloneOperation";

        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalAccessService _portalAccessService;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IPortalSettingsService _portalSettingsService;
        private readonly ICloneService _cloneService;

        public ApplicationCloneService(
            ApplicationDbContext dbContext,
            IPortalAccessService portalAccessService,
            IApplicationAccessService applicationAccessService,
            IPortalSettingsService portalSettingsService,
            ICloneService cloneService)
        {
            _dbContext = dbContext;
            _portalAccessService = portalAccessService;
            _applicationAccessService = applicationAccessService;
            _portalSettingsService = portalSettingsService;
            _cloneService = cloneService;
        }

        public async Task<CloneStartedResponse> StartAsync(string appToken, CloneApplicationRequest request)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();

            // With entities and properties: they are part of the copy.
            var source = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            var databaseType = ApplicationProvisioningService.ParseDatabaseType(request.DatabaseType);
            var connectionString = ApplicationProvisioningService.GetConnectionString(databaseType, request.ConnectionString);

            // [Required] on the contract already answers 400 for the two missing values.
            var cloneData = request.CloneData
                ?? throw PortalException.Validation(nameof(CloneApplicationRequest.CloneData), "Required");
            var serverId = request.ServerID
                ?? throw PortalException.Validation(nameof(CloneApplicationRequest.ServerID), "Required");

            // Entities is ignored when no records are copied, as the contract says.
            if (cloneData)
            {
                ValidateEntities(source, request.Entities);
            }

            var server = await _dbContext.Servers.FirstOrDefaultAsync(x => x.ID == serverId)
                ?? throw PortalException.NotFound("Server");

            var clone = Copy(source);

            clone.Token = Guid.NewGuid().ToString();
            clone.Name = source.Name + " - Clone";
            clone.UserID = user.Id;
            clone.AdminEmail = user.Email;
            clone.Server = server;
            clone.ServerID = server.ID;
            clone.DatabaseType = (int)databaseType;
            clone.ConnectionString = connectionString;

            // The encryption key stays the one of the source: copied records encrypted with it could not be read with another.

            // The installation key is the stored one (Instance > Settings), read here because the routine
            // runs after this request: it can not read the Portal database. It is read before the clone
            // is saved, so that a failure leaves no clone behind. Only the first call of the routine,
            // Generate, carries the key.
            var installationKey = _portalSettingsService.Get().InstallationKey;

            // Saved before anything exists on the API server: the clone is
            // listed at once, and stays listed when the operation fails.
            _dbContext.Applications.Add(clone);
            await _dbContext.SaveChangesAsync();

            // The API server is called with the caller's own token for as long as the operation runs.
            var operationId = _cloneService.StartCloneAsync(
                source,
                clone,
                server,
                user.AdminAuthToken ?? string.Empty,
                installationKey,
                cloneData,
                request.Entities);

            return new CloneStartedResponse
            {
                OperationId = operationId,
                ClonedApplicationToken = clone.Token
            };
        }

        public async Task<CloneOperationResponse> GetAsync(string appToken, string operationId)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            var progress = _cloneService.GetProgress(operationId);

            // The operation of another application or of another user gets the answer of one that
            // does not exist, so an id can not be probed.
            if (progress is null
                || !string.Equals(progress.SourceApplicationToken, application.Token, StringComparison.Ordinal)
                || !string.Equals(progress.StartedByUserId, user.Id, StringComparison.Ordinal))
            {
                throw PortalException.NotFound(OperationEntityName);
            }

            return new CloneOperationResponse
            {
                OperationId = progress.OperationId,
                Status = progress.Status.ToString(),
                ErrorMessage = progress.ErrorMessage,
                // Kept inside the documented range: the source can gain records between the count and the copy.
                OverallPercentage = Math.Clamp(progress.OverallPercentage, 0, 100),
                TotalEntitiesToCreate = progress.TotalEntitiesToCreate,
                EntitiesCreated = progress.EntitiesCreated,
                CurrentEntityCreatingName = progress.CurrentEntityCreatingName,
                TotalEntitiesToCloneData = progress.TotalEntitiesToCloneData,
                EntitiesDataCloned = progress.EntitiesDataCloned,
                CurrentEntityCloningDataName = progress.CurrentEntityCloningDataName,
                CurrentEntityTotalRecords = progress.CurrentEntityTotalRecords,
                CurrentEntityImportedRecords = progress.CurrentEntityImportedRecords,
                TotalRecordsAllEntities = progress.TotalRecordsAllEntities,
                TotalRecordsImported = progress.TotalRecordsImported,
                EstimatedRemainingSeconds = progress.EstimatedRemainingSeconds is double seconds ? Math.Max(0, seconds) : null,
                StartedAtUtc = progress.StartedAtUtc,
                CompletedAtUtc = progress.CompletedAtUtc,
                ClonedApplicationToken = progress.ClonedApplicationToken ?? string.Empty
            };
        }

        /// <summary>
        /// A name that is not an entity of the application is refused: the clone routine would
        /// skip it without a word.
        /// </summary>
        private static void ValidateEntities(DBWS_Application source, List<string>? entities)
        {
            var names = entities ?? new List<string>();
            var errors = new List<ErrorDetail>();

            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];

                if (!source.Entities.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal)))
                {
                    errors.Add(new ErrorDetail
                    {
                        Property = $"{nameof(CloneApplicationRequest.Entities)}[{i}]",
                        Message = $"Entity '{name}' does not exist"
                    });
                }
            }

            if (errors.Count > 0)
            {
                throw PortalException.Validation(errors);
            }
        }

        /// <summary>
        /// A copy of the application that shares no object with it and has no IDs, so the Portal
        /// database stores it as new rows. Everything the application row holds is copied, its
        /// security and mail settings included, and its entities, properties and custom endpoints.
        /// </summary>
        private static DBWS_Application Copy(DBWS_Application source)
        {
            var clone = JsonSerializer.Deserialize<DBWS_Application>(JsonSerializer.Serialize(source))
                ?? throw new InvalidOperationException("The application could not be copied.");

            clone.ID = 0;

            // Entities and properties are added in the order they were created.
            clone.Entities = clone.Entities.OrderBy(x => x.ID).ToList();

            foreach (var entity in clone.Entities)
            {
                entity.ID = 0;
                entity.AppID = -1;
                entity.Properties = entity.Properties.OrderBy(x => x.ID).ToList();

                foreach (var property in entity.Properties)
                {
                    property.ID = 0;
                    property.EntityID = -1;
                }
            }

            foreach (var customEndpoint in clone.CustomEndpoints)
            {
                customEndpoint.ID = 0;
                customEndpoint.AppID = -1;
            }

            // Reports and collaborators are not copied.
            clone.Reports = new List<DBWS_ReportPanel>();
            clone.Collaborates = new List<DBWS_Collaborate>();

            return clone;
        }
    }
}
