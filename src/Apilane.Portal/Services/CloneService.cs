using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class CloneService : ICloneService
    {
        private readonly ConcurrentDictionary<string, CloneProgressInfo> _operations = new();
        private readonly IApiHttpService _apiHttpService;
        private readonly ILogger<CloneService> _logger;
        private readonly PortalConfiguration _portalConfiguration;

        public CloneService(
            IApiHttpService apiHttpService,
            ILogger<CloneService> logger,
            PortalConfiguration portalConfiguration)
        {
            _apiHttpService = apiHttpService;
            _logger = logger;
            _portalConfiguration = portalConfiguration;
        }

        public string StartCloneAsync(
            DBWS_Application sourceApplication,
            DBWS_Application applicationToClone,
            DBWS_Server targetServer,
            string portalUserAuthToken,
            bool cloneData,
            List<string>? entitiesToClone)
        {
            var operationId = Guid.NewGuid().ToString("N");

            var progress = new CloneProgressInfo
            {
                OperationId = operationId,
                Status = CloneStatus.Pending,
                StartedAtUtc = DateTime.UtcNow,
                ClonedApplicationToken = applicationToClone.Token
            };

            _operations[operationId] = progress;

            // Fire and forget - the progress is tracked via the ConcurrentDictionary
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteFullCloneAsync(
                        sourceApplication,
                        applicationToClone,
                        targetServer,
                        portalUserAuthToken,
                        cloneData,
                        entitiesToClone,
                        progress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Clone operation {OperationId} failed: {Message}", operationId, ex.Message);
                    progress.Status = CloneStatus.Failed;
                    progress.ErrorMessage = ex.Message;
                }
            });

            return operationId;
        }

        public CloneProgressInfo? GetProgress(string operationId)
        {
            _operations.TryGetValue(operationId, out var progress);
            return progress;
        }

        private async Task ExecuteFullCloneAsync(
            DBWS_Application sourceApplication,
            DBWS_Application applicationToClone,
            DBWS_Server targetServer,
            string portalUserAuthToken,
            bool cloneData,
            List<string>? entitiesToClone,
            CloneProgressInfo progress)
        {
            // --- Phase 1: Create application with system entities only ---
            progress.Status = CloneStatus.CreatingApplication;

            // Separate system entities from non-system entities
            var systemEntities = applicationToClone.Entities.Where(e => e.IsSystem).ToList();
            var nonSystemEntities = applicationToClone.Entities.Where(e => !e.IsSystem).ToList();

            progress.TotalEntitiesToCreate = nonSystemEntities.Count;

            // Create a copy of the application with only system entities for the initial Generate call
            var skeletonApp = JsonSerializer.Deserialize<DBWS_Application>(JsonSerializer.Serialize(applicationToClone))
                ?? throw new Exception("Failed to create skeleton application");
            skeletonApp.Entities = systemEntities;

            _logger.LogInformation("Clone {OperationId} | Creating application with {SystemCount} system entities",
                progress.OperationId, systemEntities.Count);

            var apiResponseGenerate = await _apiHttpService.PostAsync(
                $"{targetServer.ServerUrl}/api/ApplicationNew/Generate?installationKey={_portalConfiguration.InstallationKey}",
                applicationToClone.Token,
                portalUserAuthToken,
                skeletonApp);

            if (apiResponseGenerate.IsError(out var generateError))
            {
                throw new Exception($"Could not create application on Api server | Error code {generateError}");
            }

            // --- Phase 2: Create non-system entities one by one ---
            progress.Status = CloneStatus.CreatingEntities;

            // Order by FK references to avoid constraint issues
            var groups = sourceApplication.GroupEntitesByFKReferences();
            var orderedNonSystemEntities = nonSystemEntities
                .OrderBy(e => groups.Flat.Where(x => x.ID.Equals(e.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Level).DefaultIfEmpty(0).Max())
                .ToList();

            foreach (var entity in orderedNonSystemEntities)
            {
                progress.CurrentEntityCreatingName = entity.Name;

                _logger.LogInformation("Clone {OperationId} | Creating entity '{EntityName}' ({Current}/{Total})",
                    progress.OperationId, entity.Name, progress.EntitiesCreated + 1, orderedNonSystemEntities.Count);

                var apiResponseEntity = await _apiHttpService.PostAsync(
                    $"{targetServer.ServerUrl}/api/Application/GenerateEntity",
                    applicationToClone.Token,
                    portalUserAuthToken,
                    entity);

                if (apiResponseEntity.IsError(out var entityError))
                {
                    throw new Exception($"Failed to create entity '{entity.Name}' | Error code {entityError}");
                }

                progress.EntitiesCreated++;
            }

            progress.CurrentEntityCreatingName = null;

            // --- Phase 3: Clone data (if requested) ---
            if (cloneData)
            {
                await ExecuteCloneDataAsync(
                    sourceApplication,
                    applicationToClone.Token,
                    targetServer,
                    portalUserAuthToken,
                    entitiesToClone,
                    progress);
            }

            progress.Status = CloneStatus.Completed;
            progress.CompletedAtUtc = DateTime.UtcNow;

            _logger.LogInformation("Clone {OperationId} completed successfully. {EntitiesCreated} entities created, {TotalRecords} records imported.",
                progress.OperationId, progress.EntitiesCreated, progress.TotalRecordsImported);

            // Schedule cleanup of the progress entry after 30 minutes
            _ = Task.Delay(TimeSpan.FromMinutes(30)).ContinueWith(t => _operations.TryRemove(progress.OperationId, out _));
        }

        private async Task ExecuteCloneDataAsync(
            DBWS_Application sourceApplication,
            string clonedAppToken,
            DBWS_Server targetServer,
            string portalUserAuthToken,
            List<string>? entitiesToClone,
            CloneProgressInfo progress)
        {
            progress.Status = CloneStatus.CloningData;

            // Order by referenced entities, to avoid missing entities during constraint creation.
            var groups = sourceApplication.GroupEntitesByFKReferences();

            var entitiesOrderedByFKReferences = sourceApplication.Entities
                .OrderBy(e => groups.Flat.Where(x => x.ID.Equals(e.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Level).DefaultIfEmpty(0).Max())
                .ToList();

            // Filter to selected entities only (if specified)
            var entitiesToProcess = entitiesOrderedByFKReferences
                .Where(e => !e.Name.Equals("Files")) // Never clone files
                .Where(e => entitiesToClone == null || entitiesToClone.Count == 0 || entitiesToClone.Contains(e.Name))
                .ToList();

            progress.TotalEntitiesToCloneData = entitiesToProcess.Count;

            // First pass: count total records for ETA calculation
            var entityRecordCounts = new Dictionary<string, int>();
            foreach (var entity in entitiesToProcess)
            {
                try
                {
                    var entityData = await _apiHttpService.GetAllDataAsync(
                        sourceApplication.Server.ServerUrl,
                        sourceApplication.Token,
                        entity.Name,
                        portalUserAuthToken);

                    if (entityData.IsError(out var error))
                    {
                        throw new Exception(error.ToString());
                    }

                    entityRecordCounts[entity.Name] = entityData.Value.Data.Count;
                    progress.TotalRecordsAllEntities += entityData.Value.Data.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to count records for entity {EntityName}: {Message}", entity.Name, ex.Message);
                    entityRecordCounts[entity.Name] = 0;
                }
            }

            // Second pass: import data entity by entity
            foreach (var entity in entitiesToProcess)
            {
                progress.CurrentEntityCloningDataName = entity.Name;
                progress.CurrentEntityTotalRecords = entityRecordCounts.GetValueOrDefault(entity.Name, 0);
                progress.CurrentEntityImportedRecords = 0;

                var entityData = await _apiHttpService.GetAllDataAsync(
                    sourceApplication.Server.ServerUrl,
                    sourceApplication.Token,
                    entity.Name,
                    portalUserAuthToken);

                if (entityData.IsError(out var error))
                {
                    throw new Exception(error.ToString());
                }

                int pageIndex = 0;
                int pageSize = 1000;
                var currentPage = entityData.Value.Data.Skip(pageIndex * pageSize).Take(pageSize).ToList();

                while (currentPage.Count > 0)
                {
                    var recordStart = pageIndex * pageSize;
                    var recordEnd = (pageIndex * pageSize) + pageSize;
                    var recordCount = entityData.Value.Data.Count;
                    recordEnd = recordCount >= recordEnd ? recordEnd : recordCount;

                    _logger.LogInformation("Clone {OperationId} | Importing '{EntityName}' records {Start}-{End}/{Total}",
                        progress.OperationId, entity.Name, recordStart, recordEnd, recordCount);

                    // Insert current page
                    await _apiHttpService.ImportDataAsync(
                        targetServer.ServerUrl,
                        clonedAppToken,
                        entity.Name,
                        currentPage,
                        portalUserAuthToken);

                    // Update progress
                    progress.CurrentEntityImportedRecords += currentPage.Count;
                    progress.TotalRecordsImported += currentPage.Count;

                    // Go to next page
                    pageIndex++;
                    currentPage = entityData.Value.Data.Skip(pageIndex * pageSize).Take(pageSize).ToList();
                }

                progress.EntitiesDataCloned++;
            }

            progress.CurrentEntityCloningDataName = null;
        }
    }
}
