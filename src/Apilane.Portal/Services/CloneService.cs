using Apilane.Common;
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
        private const int PageSize = 1000;

        // How long the status of a finished or failed operation can still be read.
        private static readonly TimeSpan ProgressRetention = TimeSpan.FromMinutes(30);

        private readonly ConcurrentDictionary<string, CloneProgressInfo> _operations = new();
        private readonly IApiHttpService _apiHttpService;
        private readonly ILogger<CloneService> _logger;

        public CloneService(
            IApiHttpService apiHttpService,
            ILogger<CloneService> logger)
        {
            _apiHttpService = apiHttpService;
            _logger = logger;
        }

        public string StartCloneAsync(
            DBWS_Application sourceApplication,
            DBWS_Application applicationToClone,
            DBWS_Server targetServer,
            string portalUserAuthToken,
            string installationKey,
            bool cloneData,
            List<string>? entitiesToClone)
        {
            var operationId = Guid.NewGuid().ToString("N");

            var progress = new CloneProgressInfo
            {
                OperationId = operationId,
                Status = CloneStatus.Pending,
                StartedAtUtc = DateTime.UtcNow,
                ClonedApplicationToken = applicationToClone.Token,
                SourceApplicationToken = sourceApplication.Token,
                // Whoever starts a clone is made its owner.
                StartedByUserId = applicationToClone.UserID
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
                        installationKey,
                        cloneData,
                        entitiesToClone,
                        progress);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Clone operation {OperationId} failed: {Message}", operationId, ex.Message);
                    // The status last: whoever reads a final status finds its details set.
                    progress.ErrorMessage = ex.Message;
                    progress.Status = CloneStatus.Failed;
                }
                finally
                {
                    // Completed or failed, the entry is forgotten after a while: the singleton keeps no operation for good.
                    _ = Task.Delay(ProgressRetention).ContinueWith(t => _operations.TryRemove(operationId, out _), TaskScheduler.Default);
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
            string installationKey,
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
                $"{targetServer.ServerUrl}/api/ApplicationNew/Generate",
                applicationToClone.Token,
                portalUserAuthToken,
                skeletonApp,
                new Dictionary<string, string> { [Globals.InstallationKeyHeaderName] = installationKey });

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

            progress.CompletedAtUtc = DateTime.UtcNow;
            progress.Status = CloneStatus.Completed;

            _logger.LogInformation("Clone {OperationId} completed successfully. {EntitiesCreated} entities created, {TotalRecords} records imported.",
                progress.OperationId, progress.EntitiesCreated, progress.TotalRecordsImported);
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

            // First pass: count total records for ETA calculation, from the total that a page of one
            // record answers with.
            var entityRecordCounts = new Dictionary<string, int>();
            foreach (var entity in entitiesToProcess)
            {
                try
                {
                    var firstPage = await _apiHttpService.GetDataPageAsync(
                        sourceApplication.Server.ServerUrl,
                        sourceApplication.Token,
                        entity.Name,
                        1,
                        1,
                        portalUserAuthToken);

                    if (firstPage.IsError(out var error))
                    {
                        throw new Exception(error.ToString());
                    }

                    var count = (int)Math.Min(firstPage.Value.Total, int.MaxValue);

                    entityRecordCounts[entity.Name] = count;
                    progress.TotalRecordsAllEntities += count;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to count records for entity {EntityName}: {Message}", entity.Name, ex.Message);
                    entityRecordCounts[entity.Name] = 0;
                }
            }

            // Second pass: import data entity by entity, page by page, so only one page is in memory
            // and each record is read once.
            foreach (var entity in entitiesToProcess)
            {
                progress.CurrentEntityCloningDataName = entity.Name;
                progress.CurrentEntityTotalRecords = entityRecordCounts.GetValueOrDefault(entity.Name, 0);
                progress.CurrentEntityImportedRecords = 0;

                for (var pageIndex = 1; ; pageIndex++)
                {
                    var page = await _apiHttpService.GetDataPageAsync(
                        sourceApplication.Server.ServerUrl,
                        sourceApplication.Token,
                        entity.Name,
                        pageIndex,
                        PageSize,
                        portalUserAuthToken);

                    if (page.IsError(out var error))
                    {
                        throw new Exception(error.ToString());
                    }

                    var records = page.Value.Data;

                    if (records.Count == 0)
                    {
                        break;
                    }

                    _logger.LogInformation("Clone {OperationId} | Importing '{EntityName}' records {Start}-{End}/{Total}",
                        progress.OperationId, entity.Name, progress.CurrentEntityImportedRecords, progress.CurrentEntityImportedRecords + records.Count, page.Value.Total);

                    // Insert current page
                    await _apiHttpService.ImportDataAsync(
                        targetServer.ServerUrl,
                        clonedAppToken,
                        entity.Name,
                        records,
                        portalUserAuthToken);

                    // Update progress
                    progress.CurrentEntityImportedRecords += records.Count;
                    progress.TotalRecordsImported += records.Count;

                    // A page that is not full is the last one.
                    if (records.Count < PageSize)
                    {
                        break;
                    }
                }

                progress.EntitiesDataCloned++;
            }

            progress.CurrentEntityCloningDataName = null;
        }
    }
}
