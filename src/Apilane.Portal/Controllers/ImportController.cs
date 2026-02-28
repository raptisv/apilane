using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Controllers
{
    [Authorize]
    public class ImportController : BaseWebApplicationController
    {
        public ImportController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {
        }

        [HttpGet]
        public IActionResult Index()
        {
            var otherApps = Applications
                .Where(a => !a.Token.Equals(Application.Token, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Name)
                .ToList();

            ViewBag.OtherApplications = otherApps;
            return View("~/Views/Application/Import.cshtml", Application);
        }

        /// <summary>
        /// Returns an <see cref="ImportSchemaRequest"/> JSON pre-filled with everything
        /// the source application has that the current (target) application does not.
        /// Only entities, properties, constraints and security rules are included.
        /// </summary>
        [HttpGet]
        public IActionResult GetImportFromDiff(string sourceAppToken)
        {
            if (string.IsNullOrWhiteSpace(sourceAppToken))
            {
                return BadRequest("Source application token is required.");
            }

            if (sourceAppToken.Equals(Application.Token, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest("Cannot diff an application against itself.");
            }

            var sourceApp = Applications
                .SingleOrDefault(a => a.Token.Equals(sourceAppToken, StringComparison.OrdinalIgnoreCase));

            if (sourceApp is null)
            {
                return BadRequest($"Source application '{sourceAppToken}' not found.");
            }

            var importRequest = BuildDiffImport(sourceApp, Application);

            return Json(importRequest);
        }

        /// <summary>
        /// Import entities, properties, constraints and security rules in bulk.
        /// Existing items are validated against the provided metadata and skipped if identical,
        /// or an error is returned on metadata mismatch. New items are created in FK-dependency order.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Index([FromBody] ImportSchemaRequest request)
        {
            try
            {
                var warnings = await ProcessImportAsync(request);

                return Json(new { Success = true, Warnings = warnings });
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Error = ex.Message });
            }
        }

        private async Task<List<string>> ProcessImportAsync(ImportSchemaRequest request)
        {
            var warnings = new List<string>();

            if (request.Entities.Any())
            {
                await ProcessEntitiesAsync(request.Entities, warnings);
            }

            if (request.Security != null && request.Security.Any())
            {
                await ProcessSecurityAsync(request.Security, warnings);
            }

            if (request.CustomEndpoints != null && request.CustomEndpoints.Any())
            {
                await ProcessCustomEndpointsAsync(request.CustomEndpoints, warnings);
            }

            // Reset the API's cached application state so changes are reflected immediately.
            await ResetAppAsync(Application.Server.ServerUrl, Application.Token, PortalUserAuthToken);

            return warnings;
        }
        private async Task ProcessEntitiesAsync(List<ImportEntityItem> entities, List<string> warnings)
        {
            // Build a synthetic application from the import request in order to reuse
            // the existing GroupEntitesByFKReferences ordering logic (same as BuildApplicationAsync).
            var syntheticApp = new DBWS_Application
            {
                DifferentiationEntity = Application.DifferentiationEntity,
                Entities = entities.Select(e => new DBWS_Entity
                {
                    Name = e.Name,
                    EntConstraints = e.Constraints.Any()
                        ? JsonSerializer.Serialize(e.Constraints)
                        : null,
                    Properties = new()
                }).ToList()
            };

            var groups = syntheticApp.GroupEntitesByFKReferences();

            // Mirror of BuildApplicationAsync: process in FK-dependency order, taking the
            // maximum level when an entity appears multiple times due to multiple FK chains.
            var orderedEntities = entities
                .OrderBy(e => groups.Flat
                    .Where(x => x.ID.Equals(e.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Level)
                    .DefaultIfEmpty(0)
                    .Max())
                .ToList();

            foreach (var importEntity in orderedEntities)
            {
                await ProcessSingleEntityAsync(importEntity, warnings);
            }
        }

        private async Task ProcessSingleEntityAsync(ImportEntityItem importEntity, List<string> warnings)
        {
            var existingEntity = Application.Entities
                .SingleOrDefault(x => x.Name.Equals(importEntity.Name, StringComparison.OrdinalIgnoreCase));

            if (existingEntity != null)
            {
                // Entity already exists — validate that its metadata matches the import payload.
                if (existingEntity.RequireChangeTracking != importEntity.RequireChangeTracking)
                {
                    throw new Exception(
                        $"Entity '{importEntity.Name}': 'RequireChangeTracking' mismatch " +
                        $"(existing: {existingEntity.RequireChangeTracking}, import: {importEntity.RequireChangeTracking}).");
                }

                if (existingEntity.HasDifferentiationProperty != importEntity.HasDifferentiationProperty)
                {
                    throw new Exception(
                        $"Entity '{importEntity.Name}': 'HasDifferentiationProperty' mismatch " +
                        $"(existing: {existingEntity.HasDifferentiationProperty}, import: {importEntity.HasDifferentiationProperty}).");
                }

                warnings.Add($"Entity '{importEntity.Name}' already exists — skipped creation.");
            }
            else
            {
                // Entity does not exist — create it using the same flow as ApplicationController.EntityCreate.

                // Retrieve the system properties and initial constraints from the API server.
                var sysPropsResponse = await ApiHttpService.GetAsync(
                    $"{Application.Server.ServerUrl}/api/Application/GetSystemPropertiesAndConstraints?entityHasDifferentiationProperty={importEntity.HasDifferentiationProperty}",
                    Application.Token,
                    PortalUserAuthToken);

                var initialData = sysPropsResponse.Match(
                    json => JsonSerializer.Deserialize<EntityPropertiesConstrainsDto>(json)
                        ?? throw new Exception($"Invalid response when fetching system properties for entity '{importEntity.Name}'."),
                    code => throw new Exception($"Could not fetch system properties for entity '{importEntity.Name}' | HTTP {code}."));

                var newEntity = new DBWS_Entity
                {
                    Name = importEntity.Name,
                    Description = importEntity.Description,
                    RequireChangeTracking = importEntity.RequireChangeTracking,
                    HasDifferentiationProperty = importEntity.HasDifferentiationProperty,
                    IsReadOnly = false,
                    IsSystem = false,
                    AppID = Application.ID,
                    ID = 0,
                    Properties = initialData.Properties,
                    EntConstraints = JsonSerializer.Serialize(initialData.Constraints)
                };

                newEntity.Properties.ForEach(p => p.ID = 0);

                DBContext.Entities.Add(newEntity);

                var createResponse = await ApiHttpService.PostAsync(
                    $"{Application.Server.ServerUrl}/api/Application/GenerateEntity",
                    Application.Token,
                    PortalUserAuthToken,
                    newEntity);

                createResponse.Match(
                    _ => string.Empty,
                    code => throw new Exception($"Could not create entity '{importEntity.Name}' on API server | HTTP {code}."));

                await DBContext.SaveChangesAsync();

                // Reload the newly created entity so we have its database-assigned ID.
                existingEntity = DBContext.Entities
                    .Include(e => e.Properties)
                    .Single(e => e.AppID == Application.ID
                        && e.Name.ToLower() == importEntity.Name.ToLower());

                // Keep the in-memory application list in sync for subsequent entities that
                // may reference this one through FK constraints.
                Application.Entities.Add(existingEntity);
            }

            // Process properties for this entity (whether it was just created or already existed).
            foreach (var importProp in importEntity.Properties)
            {
                await ProcessSinglePropertyAsync(existingEntity, importProp, warnings);
            }

            // Process constraints for this entity.
            if (importEntity.Constraints.Any())
            {
                await ProcessConstraintsAsync(existingEntity, importEntity.Constraints, warnings);
            }
        }

        private async Task ProcessSinglePropertyAsync(
            DBWS_Entity entity,
            ImportPropertyItem importProp,
            List<string> warnings)
        {
            var existingProp = entity.Properties
                .SingleOrDefault(x => x.Name.Equals(importProp.Name, StringComparison.OrdinalIgnoreCase));

            if (existingProp != null)
            {
                // Property already exists — validate that the key metadata is identical.
                if (existingProp.TypeID != importProp.TypeID)
                {
                    throw new Exception(
                        $"Property '{entity.Name}.{importProp.Name}': 'TypeID' mismatch " +
                        $"(existing: {existingProp.TypeID}, import: {importProp.TypeID}).");
                }

                if (existingProp.Required != importProp.Required)
                {
                    throw new Exception(
                        $"Property '{entity.Name}.{importProp.Name}': 'Required' mismatch " +
                        $"(existing: {existingProp.Required}, import: {importProp.Required}).");
                }

                if (existingProp.Encrypted != importProp.Encrypted)
                {
                    throw new Exception(
                        $"Property '{entity.Name}.{importProp.Name}': 'Encrypted' mismatch " +
                        $"(existing: {existingProp.Encrypted}, import: {importProp.Encrypted}).");
                }

                if (existingProp.DecimalPlaces != importProp.DecimalPlaces)
                {
                    throw new Exception(
                        $"Property '{entity.Name}.{importProp.Name}': 'DecimalPlaces' mismatch " +
                        $"(existing: {existingProp.DecimalPlaces}, import: {importProp.DecimalPlaces}).");
                }

                if (existingProp.Maximum != importProp.Maximum)
                {
                    throw new Exception(
                        $"Property '{entity.Name}.{importProp.Name}': 'Maximum' mismatch " +
                        $"(existing: {existingProp.Maximum}, import: {importProp.Maximum}).");
                }

                if (existingProp.Minimum != importProp.Minimum)
                {
                    throw new Exception(
                        $"Property '{entity.Name}.{importProp.Name}': 'Minimum' mismatch " +
                        $"(existing: {existingProp.Minimum}, import: {importProp.Minimum}).");
                }

                if (!string.Equals(existingProp.ValidationRegex, importProp.ValidationRegex, StringComparison.Ordinal))
                {
                    throw new Exception(
                        $"Property '{entity.Name}.{importProp.Name}': 'ValidationRegex' mismatch " +
                        $"(existing: '{existingProp.ValidationRegex}', import: '{importProp.ValidationRegex}').");
                }

                warnings.Add($"Property '{entity.Name}.{importProp.Name}' already exists — skipped creation.");
                return;
            }

            // Property does not exist — create it using the same flow as EntityController.PropertyCreate.
            var newProp = new DBWS_EntityProperty
            {
                EntityID = entity.ID,
                Name = importProp.Name,
                TypeID = importProp.TypeID,
                Required = importProp.Required,
                Minimum = importProp.Minimum,
                Maximum = importProp.Maximum,
                DecimalPlaces = importProp.DecimalPlaces,
                Encrypted = importProp.Encrypted,
                ValidationRegex = importProp.ValidationRegex,
                Description = importProp.Description,
                IsSystem = false,
                IsPrimaryKey = false,
                ID = 0
            };

            DBContext.EntityProperties.Add(newProp);

            var createResponse = await ApiHttpService.PostAsync(
                $"{Application.Server.ServerUrl}/api/Application/GenerateProperty?Entity={entity.Name}",
                Application.Token,
                PortalUserAuthToken,
                newProp);

            createResponse.Match(
                _ => string.Empty,
                code => throw new Exception(
                    $"Could not create property '{entity.Name}.{importProp.Name}' on API server | HTTP {code}."));

            await DBContext.SaveChangesAsync();

            // Keep in-memory state in sync for subsequent property/constraint checks.
            entity.Properties.Add(newProp);
        }

        private async Task ProcessConstraintsAsync(
            DBWS_Entity entity,
            List<EntityConstraint> importConstraints,
            List<string> warnings)
        {
            var currentConstraints = entity.Constraints; // Parsed from entity.EntConstraints JSON.
            var mergedConstraints = new List<EntityConstraint>(currentConstraints);
            var anyAdded = false;

            foreach (var importConstraint in importConstraints)
            {
                if (string.IsNullOrWhiteSpace(importConstraint.Properties))
                {
                    continue;
                }

                // Exact match: same TypeID and same Properties string → already exists, skip.
                var exactMatch = currentConstraints.Any(c =>
                    c.TypeID == importConstraint.TypeID
                    && string.Equals(
                        c.Properties?.Trim(),
                        importConstraint.Properties?.Trim(),
                        StringComparison.OrdinalIgnoreCase));

                if (exactMatch)
                {
                    warnings.Add($"Constraint on entity '{entity.Name}' (TypeID={importConstraint.TypeID}, Properties='{importConstraint.Properties}') already exists — skipped.");
                    continue;
                }

                // For FK constraints: detect a conflicting constraint that targets the same local
                // column with a different FK target or logic.
                if (importConstraint.TypeID == (int)ConstraintType.ForeignKey)
                {
                    var importFk = importConstraint.GetForeignKeyProperties();

                    var conflict = currentConstraints
                        .Where(c => c.TypeID == (int)ConstraintType.ForeignKey
                            && !string.IsNullOrWhiteSpace(c.Properties))
                        .FirstOrDefault(c =>
                        {
                            try
                            {
                                var existing = c.GetForeignKeyProperties();
                                return string.Equals(existing.Property, importFk.Property, StringComparison.OrdinalIgnoreCase);
                            }
                            catch
                            {
                                return false;
                            }
                        });

                    if (conflict != null)
                    {
                        throw new Exception(
                            $"Entity '{entity.Name}': FK constraint on local property '{importFk.Property}' " +
                            $"already exists with different configuration (existing: '{conflict.Properties}', import: '{importConstraint.Properties}').");
                    }
                }

                mergedConstraints.Add(importConstraint);
                anyAdded = true;
            }

            if (!anyAdded)
            {
                return;
            }

            // Persist the merged constraints list — mirrors EntityController.Constraints POST.
            var validMerged = mergedConstraints
                .Where(x => !string.IsNullOrWhiteSpace(x.Properties))
                .DistinctBy(x => x.Properties)
                .ToList();

            entity.EntConstraints = JsonSerializer.Serialize(validMerged);

            var createResponse = await ApiHttpService.PostAsync(
                $"{Application.Server.ServerUrl}/api/Application/GenerateConstraints?Entity={entity.Name}",
                Application.Token,
                PortalUserAuthToken,
                validMerged);

            createResponse.Match(
                _ => string.Empty,
                code => throw new Exception(
                    $"Could not set constraints on entity '{entity.Name}' on API server | HTTP {code}."));

            await DBContext.SaveChangesAsync();
        }

        private async Task ProcessCustomEndpointsAsync(List<ImportCustomEndpointItem> customEndpoints, List<string> warnings)
        {
            foreach (var importCe in customEndpoints)
            {
                var existing = await DBContext.CustomEndpoints
                    .FirstOrDefaultAsync(ce => ce.AppID == Application.ID
                        && ce.Name.ToLower() == importCe.Name.ToLower());

                if (existing != null)
                {
                    warnings.Add($"Custom endpoint '{importCe.Name}' already exists — skipped creation.");
                    continue;
                }

                var newCe = new DBWS_CustomEndpoint
                {
                    AppID = Application.ID,
                    Name = importCe.Name,
                    Description = importCe.Description,
                    Query = importCe.Query,
                    ID = 0
                };

                DBContext.CustomEndpoints.Add(newCe);
                await DBContext.SaveChangesAsync();
            }
        }

        private async Task ProcessSecurityAsync(List<DBWS_Security> importSecurities, List<string> warnings)
        {
            var currentSecurity = Application.Security_List;
            var anyAdded = false;

            foreach (var importItem in importSecurities)
            {
                var existing = currentSecurity
                    .SingleOrDefault(s => s.ToUniqueStringShort()
                        .Equals(importItem.ToUniqueStringShort(), StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Same key — validate that the full configuration matches.
                    if (!existing.ToUniqueStringLong()
                        .Equals(importItem.ToUniqueStringLong(), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception(
                            $"Security item '{importItem.NameDescriptive()}' already exists with different configuration " +
                            $"(existing: '{existing.ToUniqueStringLong()}', import: '{importItem.ToUniqueStringLong()}').");
                    }

                    warnings.Add($"Security item '{importItem.NameDescriptive()}' already exists — skipped.");
                    continue;
                }

                currentSecurity.Add(importItem);
                anyAdded = true;
            }

            if (!anyAdded)
            {
                return;
            }

            // Persist — mirrors ApplicationController.Security POST.
            Application.Security = JsonSerializer.Serialize(currentSecurity);

            DBContext.Attach(Application);
            DBContext.Entry(Application).Property(x => x.Security).IsModified = true;

            await DBContext.SaveChangesAsync();
        }
        /// <summary>
        /// Builds an <see cref="ImportSchemaRequest"/> containing everything present in
        /// <paramref name="source"/> that is absent from <paramref name="target"/>.
        /// Entities that exist in both apps are included only when they have new non-system
        /// properties or new constraints. Security items that already exist in the target
        /// (by short key) with different configuration are omitted to avoid import errors.
        /// </summary>
        private static ImportSchemaRequest BuildDiffImport(
            DBWS_Application source,
            DBWS_Application target)
        {
            var result = new ImportSchemaRequest();

            // -- Entities, properties, constraints --------------------------------------
            foreach (var srcEntity in source.Entities.Where(e => !e.IsSystem))
            {
                var tgtEntity = target.Entities
                    .SingleOrDefault(e => e.Name.Equals(srcEntity.Name, StringComparison.OrdinalIgnoreCase));

                if (tgtEntity is null)
                {
                    // Whole entity is new in target - include all non-system, non-PK properties
                    // and all non-system constraints.
                    var importItem = new ImportEntityItem
                    {
                        Name = srcEntity.Name,
                        Description = srcEntity.Description,
                        RequireChangeTracking = srcEntity.RequireChangeTracking,
                        HasDifferentiationProperty = srcEntity.HasDifferentiationProperty,
                        IsNew = true,
                        Properties = srcEntity.Properties
                            .Where(p => !p.IsSystem && !p.IsPrimaryKey)
                            .Select(p => new ImportPropertyItem
                            {
                                Name = p.Name,
                                TypeID = p.TypeID,
                                Required = p.Required,
                                Minimum = p.Minimum,
                                Maximum = p.Maximum,
                                DecimalPlaces = p.DecimalPlaces,
                                Encrypted = p.Encrypted,
                                ValidationRegex = p.ValidationRegex,
                                Description = p.Description
                            })
                            .ToList(),
                        Constraints = srcEntity.Constraints
                            .Where(c => !c.IsSystem && !string.IsNullOrWhiteSpace(c.Properties))
                            .ToList()
                    };

                    result.Entities.Add(importItem);
                }
                else
                {
                    // Entity exists in target - include only new non-system, non-PK properties
                    // and new constraints.
                    var newProperties = srcEntity.Properties
                        .Where(p => !p.IsSystem && !p.IsPrimaryKey
                            && !tgtEntity.Properties.Any(tp =>
                                tp.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
                        .Select(p => new ImportPropertyItem
                        {
                            Name = p.Name,
                            TypeID = p.TypeID,
                            Required = p.Required,
                            Minimum = p.Minimum,
                            Maximum = p.Maximum,
                            DecimalPlaces = p.DecimalPlaces,
                            Encrypted = p.Encrypted,
                            ValidationRegex = p.ValidationRegex,
                            Description = p.Description
                        })
                        .ToList();

                    var tgtConstraintKeys = tgtEntity.Constraints
                        .Select(c => $"{c.TypeID}|{c.Properties?.Trim().ToLowerInvariant()}")
                        .ToHashSet();

                    var newConstraints = srcEntity.Constraints
                        .Where(c => !c.IsSystem 
                            && !string.IsNullOrWhiteSpace(c.Properties)
                            && !tgtConstraintKeys.Contains($"{c.TypeID}|{c.Properties?.Trim().ToLowerInvariant()}"))
                        .ToList();

                    if (newProperties.Any() || newConstraints.Any())
                    {
                        result.Entities.Add(new ImportEntityItem
                        {
                            Name = srcEntity.Name,
                            Description = srcEntity.Description,
                            RequireChangeTracking = srcEntity.RequireChangeTracking,
                            HasDifferentiationProperty = srcEntity.HasDifferentiationProperty,
                            IsNew = false,
                            Properties = newProperties,
                            Constraints = newConstraints
                        });
                    }
                }
            }

            // -- Security ---------------------------------------------------------------
            // Mirror the SecurityItemExists guard from CompareApplications: only include
            // security items whose referenced entity / custom endpoint actually exists
            // in the source application.
            var targetShortKeys = target.Security_List
                .Select(s => s.ToUniqueStringShort())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var srcSec in source.Security_List
                .Where(s => SecurityItemExistsInSource(source, s)))
            {
                var shortKey = srcSec.ToUniqueStringShort();

                if (targetShortKeys.Contains(shortKey))
                {
                    // Already in target - skip.
                    continue;
                }

                result.Security.Add(srcSec);
            }

            // -- Custom Endpoints -------------------------------------------------------
            foreach (var srcCe in source.CustomEndpoints)
            {
                var alreadyInTarget = target.CustomEndpoints
                    .Any(ce => ce.Name.Equals(srcCe.Name, StringComparison.OrdinalIgnoreCase));

                if (!alreadyInTarget)
                {
                    result.CustomEndpoints.Add(new ImportCustomEndpointItem
                    {
                        Name = srcCe.Name,
                        Description = srcCe.Description,
                        Query = srcCe.Query
                    });
                }
            }

            return result;
            static bool SecurityItemExistsInSource(DBWS_Application app, DBWS_Security sec)
            {
                if (sec.TypeID_Enum == SecurityTypes.Entity)
                {
                    return app.Entities.Any(e => e.Name.Equals(sec.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (sec.TypeID_Enum == SecurityTypes.CustomEndpoint)
                {
                    return app.CustomEndpoints.Any(ce => ce.Name.Equals(sec.Name, StringComparison.OrdinalIgnoreCase));
                }

                return false;
            }
        }

    }
}
