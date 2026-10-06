using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class SchemaImportService : ISchemaImportService
    {
        private const string StoppedNote = " The import stopped at this step; the steps before it stay applied.";
        private const string ForeignKeyFormat = "A foreign key is 'Property,Entity' or 'Property,Entity,ON_DELETE_NO_ACTION' (or ON_DELETE_SET_NULL, ON_DELETE_CASCADE)";
        private const string PropertyTypeMessage = "Must be 1 (String), 2 (Number), 3 (Boolean) or 4 (Date)";

        private readonly ApplicationDbContext _dbContext;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IPortalAccessService _portalAccessService;
        private readonly IAgentPermissionService _agentPermissionService;
        private readonly IApiServerClient _apiServerClient;
        private readonly IApiServerCacheReset _apiServerCacheReset;
        private readonly ILogger<SchemaImportService> _logger;

        public SchemaImportService(
            ApplicationDbContext dbContext,
            IApplicationAccessService applicationAccessService,
            IPortalAccessService portalAccessService,
            IAgentPermissionService agentPermissionService,
            IApiServerClient apiServerClient,
            IApiServerCacheReset apiServerCacheReset,
            ILogger<SchemaImportService> logger)
        {
            _dbContext = dbContext;
            _applicationAccessService = applicationAccessService;
            _portalAccessService = portalAccessService;
            _agentPermissionService = agentPermissionService;
            _apiServerClient = apiServerClient;
            _apiServerCacheReset = apiServerCacheReset;
            _logger = logger;
        }

        public async Task<SchemaImportRequest> GetDiffAsync(string appToken, string sourceAppToken)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            if (string.Equals(sourceAppToken, application.Token, StringComparison.OrdinalIgnoreCase))
            {
                throw PortalException.Validation("Source", "Cannot diff an application against itself");
            }

            var source = await _applicationAccessService.GetApplicationWithEntitiesAsync(sourceAppToken);

            foreach (var compared in new[] { application, source })
            {
                foreach (var resource in new[] { AgentPermissionResources.Schema, AgentPermissionResources.Entities,
                    AgentPermissionResources.Security, AgentPermissionResources.CustomEndpoints })
                {
                    await _agentPermissionService.DemandAsync(compared, resource);
                }
            }

            var entities = MissingEntities(source, application);
            var customEndpoints = MissingCustomEndpoints(source, application);

            return new SchemaImportRequest
            {
                Entities = entities,
                // The rules are checked against the application as importing this diff leaves it.
                Security = MissingSecurityRules(source, application, ApplicationAfterImport(application, entities, customEndpoints)),
                CustomEndpoints = customEndpoints
            };
        }

        public async Task<SchemaImportResponse> ImportAsync(string appToken, SchemaImportRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            var entities = request.Entities ?? new List<SchemaImportEntity>();
            var security = request.Security ?? new List<SchemaImportSecurityRule>();
            var customEndpoints = request.CustomEndpoints ?? new List<SchemaImportCustomEndpoint>();

            // Check the entire payload before any part of this non-atomic import can be applied.
            await _agentPermissionService.DemandAsync(application, AgentPermissionResources.Schema, AgentPermissionAccess.Write);
            if (entities.Count > 0)
            {
                await _agentPermissionService.DemandAsync(application, AgentPermissionResources.Entities, AgentPermissionAccess.Write);
            }
            if (security.Count > 0)
            {
                await _agentPermissionService.DemandAsync(application, AgentPermissionResources.Security, AgentPermissionAccess.Write);
            }
            if (customEndpoints.Count > 0)
            {
                await _agentPermissionService.DemandAsync(application, AgentPermissionResources.CustomEndpoints, AgentPermissionAccess.Write);
            }

            // What the application is once the entities and custom endpoints of the payload are in it:
            // the security rules are checked against it and stored under the names it gives their items.
            var imported = ApplicationAfterImport(application, entities, customEndpoints);

            // Before anything is applied.
            ThrowIfNotValid(application, imported, entities, security, customEndpoints);
            ThrowIfSystemEntityConstraints(application, entities);

            // Also before anything is applied: 409 when rules are sent and the stored ones cannot be read.
            var storedRules = security.Count > 0 ? SchemaDiff.SecurityRules(application) : new List<DBWS_Security>();

            var warnings = new List<string>();

            foreach (var index in InForeignKeyOrder(application, entities))
            {
                await ImportEntityAsync(application, entities[index], $"{nameof(SchemaImportRequest.Entities)}[{index}]", warnings);
            }

            await ImportSecurityAsync(application, imported, storedRules, security, warnings);
            await ImportCustomEndpointsAsync(application, customEndpoints, warnings);

            // Once, and only when every step went through.
            await _apiServerCacheReset.ResetAfterWriteAsync(application);

            return new SchemaImportResponse { Warnings = warnings };
        }

        // ---------- Diff ----------

        // Entities and properties are matched by name whatever the letter case, as the import matches them.
        private static List<SchemaImportEntity> MissingEntities(DBWS_Application source, DBWS_Application target)
        {
            var result = new List<SchemaImportEntity>();

            foreach (var sourceEntity in SchemaDiff.Entities(source).Where(x => !x.IsSystem))
            {
                var targetEntity = FindEntity(target, sourceEntity.Name);

                var properties = SchemaDiff.CustomProperties(sourceEntity)
                    .Where(x => targetEntity is null || FindProperty(targetEntity, x.Name) is null)
                    .Select(ToImportProperty)
                    .ToList();

                var targetKeys = targetEntity is null
                    ? new HashSet<string>()
                    : SchemaDiff.Constraints(targetEntity).Select(SchemaDiff.ConstraintKey).ToHashSet();

                var constraints = SchemaDiff.CustomConstraints(sourceEntity)
                    .Where(x => !targetKeys.Contains(SchemaDiff.ConstraintKey(x)))
                    .Select(x => new SchemaImportConstraint { IsSystem = x.IsSystem, TypeID = x.TypeID, Properties = x.Properties })
                    .ToList();

                // An entity the target has is listed only when something of it is missing.
                if (targetEntity is null || properties.Count > 0 || constraints.Count > 0)
                {
                    result.Add(new SchemaImportEntity
                    {
                        Name = sourceEntity.Name,
                        Description = sourceEntity.Description,
                        RequireChangeTracking = sourceEntity.RequireChangeTracking,
                        HasDifferentiationProperty = sourceEntity.HasDifferentiationProperty,
                        IsNew = targetEntity is null,
                        Properties = properties,
                        Constraints = constraints
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// The rules of the source the target lacks, as the import accepts them: it checks every rule
        /// against the target as the import leaves it (<paramref name="afterImport"/>), so a diff can be
        /// posted back as it is. A rule whose item or action the target would not have is left out; so is
        /// a property it would not have (the diff lists no custom property of Users or Files, and none
        /// the target has in another letter case) from the list of the rule, as the Security tab leaves
        /// it out of the rule it shows; and of two rules for the same cell the first wins, as there. A
        /// rule the target has with other values is left out too: importing it would stop the import.
        /// </summary>
        private static List<SchemaImportSecurityRule> MissingSecurityRules(DBWS_Application source, DBWS_Application target, DBWS_Application afterImport)
        {
            var targetKeys = SchemaDiff.SecurityRules(target)
                .Select(x => x.ToUniqueStringShort())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<SchemaImportSecurityRule>();

            foreach (var rule in SchemaDiff.SecurityRules(source))
            {
                if (!SchemaDiff.SecurityItemExists(source, rule, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(rule.RoleID)
                    || string.IsNullOrWhiteSpace(rule.Action)
                    || targetKeys.Contains(rule.ToUniqueStringShort()))
                {
                    continue;
                }

                var item = new SchemaImportSecurityRule
                {
                    Name = rule.Name,
                    TypeID = rule.TypeID,
                    RoleID = rule.RoleID,
                    Action = rule.Action,
                    // The API server treats anything but Owned as all records.
                    Record = rule.Record == (int)EndpointRecordAuthorization.Owned ? rule.Record : (int)EndpointRecordAuthorization.All,
                    Properties = rule.Properties,
                    RateLimit = rule.RateLimit is null
                        ? null
                        : new SchemaImportRateLimit { MaxRequests = rule.RateLimit.MaxRequests, TimeWindowType = rule.RateLimit.TimeWindowType }
                };

                if (SecurityRuleItemError(afterImport, item, nameof(SchemaImportRequest.Security), out var entity) is not null
                    || !offered.Add(rule.ToUniqueStringShort()))
                {
                    continue;
                }

                var allowed = entity is null ? new List<string>() : SecurityRuleChecks.AllowedProperties(afterImport, entity, item.Action.ToLowerInvariant());
                var listed = rule.GetProperties();
                var kept = listed.Where(x => allowed.Contains(x, StringComparer.Ordinal)).ToList();

                if (kept.Count < listed.Count)
                {
                    item.Properties = string.Join(",", kept);
                }

                result.Add(item);
            }

            return result;
        }

        private static List<SchemaImportCustomEndpoint> MissingCustomEndpoints(DBWS_Application source, DBWS_Application target)
        {
            return source.CustomEndpoints
                .OrderBy(x => x.ID)
                .Where(x => !target.CustomEndpoints.Any(t => t.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(x => new SchemaImportCustomEndpoint { Name = x.Name, Description = x.Description, Query = x.Query })
                .ToList();
        }

        private static SchemaImportProperty ToImportProperty(DBWS_EntityProperty property)
        {
            return new SchemaImportProperty
            {
                Name = property.Name,
                TypeID = property.TypeID,
                Required = property.Required,
                Minimum = property.Minimum,
                Maximum = property.Maximum,
                DecimalPlaces = property.DecimalPlaces,
                Encrypted = property.Encrypted,
                ValidationRegex = property.ValidationRegex,
                Description = property.Description
            };
        }

        // ---------- Import ----------

        /// <summary>
        /// What model validation cannot see, all of it known before the first step: a list item
        /// that is null, a constraint marked as a system one, a foreign key whose text cannot be
        /// read (the foreign-key order of the entities is computed from it), and a name or type
        /// its own create endpoint would refuse for an entity, property or custom endpoint the
        /// application does not have yet. What it has is matched and skipped whatever its name.
        /// A security rule is checked as PUT security/rules checks it, against the application
        /// as the import leaves it (<paramref name="imported"/>, see <see cref="ApplicationAfterImport"/>):
        /// the rules are applied after the entities, so an entity or a property of this same payload
        /// can be named, and one that is neither in the payload nor in the application cannot.
        /// </summary>
        private static void ThrowIfNotValid(
            DBWS_Application application,
            DBWS_Application imported,
            List<SchemaImportEntity> entities,
            List<SchemaImportSecurityRule> security,
            List<SchemaImportCustomEndpoint> customEndpoints)
        {
            var errors = new List<ErrorDetail>();

            for (var i = 0; i < entities.Count; i++)
            {
                var path = $"{nameof(SchemaImportRequest.Entities)}[{i}]";

                if (entities[i] is null)
                {
                    errors.Add(Error(path, "Required"));
                    continue;
                }

                var existing = FindEntity(application, entities[i].Name);

                if (existing is null)
                {
                    AddIfRefused(errors, $"{path}.{nameof(SchemaImportEntity.Name)}", EntityNameError(entities[i].Name));
                }

                var properties = entities[i].Properties ?? new List<SchemaImportProperty>();
                var constraints = entities[i].Constraints ?? new List<SchemaImportConstraint>();

                for (var j = 0; j < properties.Count; j++)
                {
                    var propertyPath = $"{path}.{nameof(SchemaImportEntity.Properties)}[{j}]";

                    if (properties[j] is null)
                    {
                        errors.Add(Error(propertyPath, "Required"));
                        continue;
                    }

                    var isNew = existing is null
                        ? !ComesWithNewEntity(application, entities[i], properties[j].Name)
                        : FindProperty(existing, properties[j].Name) is null;

                    if (isNew)
                    {
                        AddIfRefused(errors, $"{propertyPath}.{nameof(SchemaImportProperty.Name)}", PropertyNameError(properties[j].Name));

                        if (!Enum.IsDefined(typeof(PropertyType), properties[j].TypeID))
                        {
                            errors.Add(Error($"{propertyPath}.{nameof(SchemaImportProperty.TypeID)}", PropertyTypeMessage));
                        }
                    }
                }

                for (var j = 0; j < constraints.Count; j++)
                {
                    var constraintPath = $"{path}.{nameof(SchemaImportEntity.Constraints)}[{j}]";

                    if (constraints[j] is null)
                    {
                        errors.Add(Error(constraintPath, "Required"));
                        continue;
                    }

                    if (constraints[j].IsSystem)
                    {
                        errors.Add(Error($"{constraintPath}.{nameof(SchemaImportConstraint.IsSystem)}", "Must be false: system constraints come with the entity"));
                    }

                    if (constraints[j].TypeID == (int)ConstraintType.ForeignKey)
                    {
                        var foreignKey = ToStored(constraints[j]);

                        // The model takes any number as the on-delete action.
                        if (!IsForeignKey(foreignKey) || !Enum.IsDefined(foreignKey.GetForeignKeyProperties().FKLogic))
                        {
                            errors.Add(Error($"{constraintPath}.{nameof(SchemaImportConstraint.Properties)}", ForeignKeyFormat));
                        }
                    }
                }
            }

            AddSecurityRuleErrors(errors, imported, security);

            for (var i = 0; i < customEndpoints.Count; i++)
            {
                var path = $"{nameof(SchemaImportRequest.CustomEndpoints)}[{i}]";

                if (customEndpoints[i] is null)
                {
                    errors.Add(Error(path, "Required"));
                    continue;
                }

                var name = Utils.GetString(customEndpoints[i].Name);

                if (!HasCustomEndpoint(application, name) && !IsMatch(name, CustomEndpointNameRules.Pattern))
                {
                    errors.Add(Error($"{path}.{nameof(SchemaImportCustomEndpoint.Name)}", CustomEndpointNameRules.PatternMessage));
                }
            }

            if (errors.Count > 0)
            {
                throw PortalException.Validation(errors);
            }
        }

        /// <summary>
        /// The errors of the rules, in the order of the payload: for each rule the first problem that
        /// PUT security/rules finds in it (<see cref="SecurityRuleChecks"/>); unlike PUT, the import
        /// goes on to the next rule, as it lists every error of the payload. A second rule for the
        /// same type, name, role and action is refused only when it has other values than the first:
        /// PUT refuses any, while the import skips an identical one with a warning, as it does a rule
        /// the application has.
        /// </summary>
        private static void AddSecurityRuleErrors(List<ErrorDetail> errors, DBWS_Application imported, List<SchemaImportSecurityRule> security)
        {
            var root = nameof(SchemaImportRequest.Security);

            // Where the import would stop at the step of the second rule, with the entities already applied.
            var firstOfEach = new Dictionary<string, (int Index, string Values)>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < security.Count; i++)
            {
                var path = $"{root}[{i}]";

                if (security[i] is null)
                {
                    errors.Add(Error(path, "Required"));
                    continue;
                }

                var error = SecurityRuleError(imported, security[i], path);

                if (error is not null)
                {
                    errors.Add(error);
                    continue;
                }

                var stored = ToStored(imported, security[i]);

                if (!firstOfEach.TryGetValue(stored.ToUniqueStringShort(), out var first))
                {
                    firstOfEach.Add(stored.ToUniqueStringShort(), (i, stored.ToUniqueStringLong()));
                }
                else if (!first.Values.Equals(stored.ToUniqueStringLong(), StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(Error(path, $"Same type, name, role and action as {root}[{first.Index}], with other values"));
                }
            }
        }

        // The first problem of a rule, in the order PUT security/rules finds them.
        private static ErrorDetail? SecurityRuleError(DBWS_Application imported, SchemaImportSecurityRule rule, string path)
        {
            return SecurityRuleItemError(imported, rule, path, out var entity)
                ?? SecurityRuleChecks.CheckProperties(imported, entity, rule.Action.ToLowerInvariant(), ToStored(imported, rule).GetProperties(), path);
        }

        // What comes before the properties: the type, the item, the role, the action and the record. The
        // type and the record are numbers here and names in the rules editor: their messages say so. The
        // name of an item is matched whatever its letter case, as the API server matches it, as the diff
        // offers a rule and as the import matches an entity everywhere else; the rule is stored under the
        // spelling the item has (see ToStored).
        private static ErrorDetail? SecurityRuleItemError(DBWS_Application imported, SchemaImportSecurityRule rule, string path, out DBWS_Entity? entity)
        {
            entity = null;

            if (!Enum.IsDefined(typeof(SecurityTypes), rule.TypeID))
            {
                return Error($"{path}.{nameof(SchemaImportSecurityRule.TypeID)}", "Must be 0 (Entity), 1 (CustomEndpoint) or 2 (Schema)");
            }

            var error = SecurityRuleChecks.CheckItem(imported, (SecurityTypes)rule.TypeID, rule.Name, rule.RoleID, rule.Action.ToLowerInvariant(), path, StringComparison.OrdinalIgnoreCase, out entity);

            if (error is not null)
            {
                return error;
            }

            return Enum.IsDefined(typeof(EndpointRecordAuthorization), rule.Record)
                ? null
                : Error($"{path}.{nameof(SchemaImportSecurityRule.Record)}", "Must be 0 (All) or 1 (Owned)");
        }

        // The rule of PUT constraints: without it the import would be a way around its 403.
        private void ThrowIfSystemEntityConstraints(DBWS_Application application, List<SchemaImportEntity> entities)
        {
            if (_portalAccessService.IsAdmin)
            {
                return;
            }

            foreach (var item in entities)
            {
                var entity = FindEntity(application, item.Name);

                if (entity is not null
                    && entity.IsSystem
                    && (item.Constraints ?? new List<SchemaImportConstraint>()).Any(x => !string.IsNullOrWhiteSpace(x.Properties)))
                {
                    throw PortalException.Forbidden("Only an administrator can change the constraints of a system entity.");
                }
            }
        }

        /// <summary>
        /// The positions of the entities in the order to process them, computed the way the API
        /// server orders the tables of a new application:
        /// from Users (and the differentiation entity) down the foreign keys of the payload, an
        /// entity that is pointed to before the entities that point to it. Entities that chain
        /// does not reach keep the order of the payload among themselves and come last; entities
        /// without a foreign key come first.
        /// </summary>
        private static List<int> InForeignKeyOrder(DBWS_Application application, List<SchemaImportEntity> entities)
        {
            var payload = new DBWS_Application
            {
                DifferentiationEntity = application.DifferentiationEntity,
                Entities = entities
                    .Select(x => new DBWS_Entity
                    {
                        Name = x.Name,
                        EntConstraints = JsonSerializer.Serialize((x.Constraints ?? new List<SchemaImportConstraint>()).Select(ToStored)),
                        Properties = new List<DBWS_EntityProperty>()
                    })
                    .ToList()
            };

            var levels = payload.GroupEntitesByFKReferences().Flat;

            // An entity with several foreign keys is listed once per key: its deepest level counts.
            return Enumerable.Range(0, entities.Count)
                .OrderBy(i => levels
                    .Where(x => x.ID.Equals(entities[i].Name, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Level)
                    .DefaultIfEmpty(0)
                    .Max())
                .ToList();
        }

        private async Task ImportEntityAsync(DBWS_Application application, SchemaImportEntity item, string path, List<string> warnings)
        {
            var entity = FindEntity(application, item.Name);

            if (entity is not null)
            {
                if (entity.RequireChangeTracking != item.RequireChangeTracking)
                {
                    throw Stopped(
                        $"{path}.{nameof(SchemaImportEntity.RequireChangeTracking)}",
                        $"Entity '{item.Name}': 'RequireChangeTracking' mismatch (existing: {entity.RequireChangeTracking}, import: {item.RequireChangeTracking}).");
                }

                if (entity.HasDifferentiationProperty != item.HasDifferentiationProperty)
                {
                    throw Stopped(
                        $"{path}.{nameof(SchemaImportEntity.HasDifferentiationProperty)}",
                        $"Entity '{item.Name}': 'HasDifferentiationProperty' mismatch (existing: {entity.HasDifferentiationProperty}, import: {item.HasDifferentiationProperty}).");
                }

                warnings.Add($"Entity '{item.Name}' already exists — skipped creation.");
            }
            else
            {
                entity = await CreateEntityAsync(application, item, path);
            }

            var properties = item.Properties ?? new List<SchemaImportProperty>();

            for (var i = 0; i < properties.Count; i++)
            {
                await ImportPropertyAsync(application, entity, properties[i], $"{path}.{nameof(SchemaImportEntity.Properties)}[{i}]", warnings);
            }

            await ImportConstraintsAsync(application, entity, item.Constraints ?? new List<SchemaImportConstraint>(), $"{path}.{nameof(SchemaImportEntity.Constraints)}", warnings);
        }

        // The steps of POST entities, without its cache reset.
        private async Task<DBWS_Entity> CreateEntityAsync(DBWS_Application application, SchemaImportEntity item, string path)
        {
            var initial = await CallApiServerAsync(
                path,
                $"Reading the system properties of entity '{item.Name}' from the API server",
                () => _apiServerClient.GetSystemPropertiesAndConstraintsAsync(application, item.HasDifferentiationProperty));

            // The API server numbers what it returns; the Portal database gives the real IDs.
            initial.Properties.ForEach(x => x.ID = 0);

            var entity = new DBWS_Entity
            {
                AppID = application.ID,
                Name = item.Name,
                Description = item.Description,
                RequireChangeTracking = item.RequireChangeTracking,
                HasDifferentiationProperty = item.HasDifferentiationProperty,
                IsReadOnly = false,
                IsSystem = false,
                Properties = initial.Properties,
                EntConstraints = JsonSerializer.Serialize(initial.Constraints)
            };

            _dbContext.Entities.Add(entity);

            await CallApiServerThenSaveAsync(
                path,
                $"Creating entity '{item.Name}' on the API server",
                () => _apiServerClient.GenerateEntityAsync(application, entity));

            // For the entities, properties and constraints that come after it.
            if (!application.Entities.Contains(entity))
            {
                application.Entities.Add(entity);
            }

            return entity;
        }

        private async Task ImportPropertyAsync(DBWS_Application application, DBWS_Entity entity, SchemaImportProperty item, string path, List<string> warnings)
        {
            var name = $"{entity.Name}.{item.Name}";
            var existing = FindProperty(entity, item.Name);

            if (existing is not null)
            {
                // The description is not compared.
                ThrowIfDifferent(path, name, nameof(SchemaImportProperty.TypeID), existing.TypeID, item.TypeID);
                ThrowIfDifferent(path, name, nameof(SchemaImportProperty.Required), existing.Required, item.Required);
                ThrowIfDifferent(path, name, nameof(SchemaImportProperty.Encrypted), existing.Encrypted, item.Encrypted);
                ThrowIfDifferent(path, name, nameof(SchemaImportProperty.DecimalPlaces), existing.DecimalPlaces, item.DecimalPlaces);
                ThrowIfDifferent(path, name, nameof(SchemaImportProperty.Maximum), existing.Maximum, item.Maximum);
                ThrowIfDifferent(path, name, nameof(SchemaImportProperty.Minimum), existing.Minimum, item.Minimum);

                if (!string.Equals(existing.ValidationRegex, item.ValidationRegex, StringComparison.Ordinal))
                {
                    throw Stopped(
                        $"{path}.{nameof(SchemaImportProperty.ValidationRegex)}",
                        $"Property '{name}': 'ValidationRegex' mismatch (existing: '{existing.ValidationRegex}', import: '{item.ValidationRegex}').");
                }

                warnings.Add($"Property '{name}' already exists — skipped creation.");
                return;
            }

            // Its name and type were checked before the first step. The values are sent as they
            // are, not cleaned up by type as POST properties does.
            var property = new DBWS_EntityProperty
            {
                EntityID = entity.ID,
                Name = item.Name,
                TypeID = item.TypeID,
                Required = item.Required,
                Minimum = item.Minimum,
                Maximum = item.Maximum,
                DecimalPlaces = item.DecimalPlaces,
                Encrypted = item.Encrypted,
                ValidationRegex = item.ValidationRegex,
                Description = item.Description,
                IsSystem = false,
                IsPrimaryKey = false
            };

            _dbContext.EntityProperties.Add(property);

            await CallApiServerThenSaveAsync(
                path,
                $"Creating property '{name}' on the API server",
                () => _apiServerClient.GeneratePropertyAsync(application, entity.Name, property));

            if (!entity.Properties.Contains(property))
            {
                entity.Properties.Add(property);
            }
        }

        private async Task ImportConstraintsAsync(DBWS_Application application, DBWS_Entity entity, List<SchemaImportConstraint> items, string path, List<string> warnings)
        {
            var current = SchemaDiff.Constraints(entity);
            var merged = new List<EntityConstraint>(current);
            var anyAdded = false;

            for (var i = 0; i < items.Count; i++)
            {
                var item = ToStored(items[i]);

                if (string.IsNullOrWhiteSpace(item.Properties))
                {
                    continue;
                }

                if (current.Any(x => SchemaDiff.ConstraintKey(x) == SchemaDiff.ConstraintKey(item)))
                {
                    warnings.Add($"Constraint on entity '{entity.Name}' (TypeID={item.TypeID}, Properties='{item.Properties}') already exists — skipped.");
                    continue;
                }

                if (item.TypeID == (int)ConstraintType.ForeignKey)
                {
                    var property = item.GetForeignKeyProperties().Property;

                    // A stored foreign key that cannot be read is no conflict.
                    var conflict = current.FirstOrDefault(x =>
                        x.TypeID == (int)ConstraintType.ForeignKey
                        && IsForeignKey(x)
                        && string.Equals(x.GetForeignKeyProperties().Property, property, StringComparison.OrdinalIgnoreCase));

                    if (conflict is not null)
                    {
                        throw Stopped(
                            $"{path}[{i}].{nameof(SchemaImportConstraint.Properties)}",
                            $"Entity '{entity.Name}': FK constraint on local property '{property}' already exists with different configuration (existing: '{conflict.Properties}', import: '{item.Properties}').");
                    }
                }

                merged.Add(item);
                anyAdded = true;
            }

            if (!anyAdded)
            {
                return;
            }

            // The whole list, as the constraints page sends it: the API server adds what is new.
            var constraints = merged
                .Where(x => !string.IsNullOrWhiteSpace(x.Properties))
                .DistinctBy(x => x.Properties)
                .ToList();

            entity.EntConstraints = JsonSerializer.Serialize(constraints);

            await CallApiServerThenSaveAsync(
                path,
                $"Setting the constraints of entity '{entity.Name}' on the API server",
                () => _apiServerClient.GenerateConstraintsAsync(application, entity.Name, constraints));
        }

        /// <summary>
        /// Appends the rules the application does not have to <paramref name="rules"/>, the stored
        /// ones. The rules were checked before the first step (<see cref="ThrowIfNotValid"/>) and
        /// are stored as sent but for the name of their item (see ToStored), not
        /// cleaned up as PUT security/rules does (the action is not turned to lower case, for one);
        /// the stored rules are kept exactly as they are.
        /// </summary>
        private async Task ImportSecurityAsync(DBWS_Application application, DBWS_Application imported, List<DBWS_Security> rules, List<SchemaImportSecurityRule> items, List<string> warnings)
        {
            if (items.Count == 0)
            {
                return;
            }

            var anyAdded = false;

            for (var i = 0; i < items.Count; i++)
            {
                var item = ToStored(imported, items[i]);

                var existing = rules.FirstOrDefault(x => x.ToUniqueStringShort().Equals(item.ToUniqueStringShort(), StringComparison.OrdinalIgnoreCase));

                if (existing is null)
                {
                    rules.Add(item);
                    anyAdded = true;
                    continue;
                }

                if (!existing.ToUniqueStringLong().Equals(item.ToUniqueStringLong(), StringComparison.OrdinalIgnoreCase))
                {
                    throw Stopped(
                        $"{nameof(SchemaImportRequest.Security)}[{i}]",
                        $"Security item '{item.NameDescriptive()}' already exists with different configuration (existing: '{existing.ToUniqueStringLong()}', import: '{item.ToUniqueStringLong()}').");
                }

                warnings.Add($"Security item '{item.NameDescriptive()}' already exists — skipped.");
            }

            if (!anyAdded)
            {
                return;
            }

            // The same class and serializer as SecurityRulesService, so the stored JSON is the same.
            application.Security = JsonSerializer.Serialize(rules);

            await _dbContext.SaveChangesAsync();
        }

        // The API server learns about a custom endpoint through its cache reset: nothing to call.
        private async Task ImportCustomEndpointsAsync(DBWS_Application application, List<SchemaImportCustomEndpoint> items, List<string> warnings)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var name = Utils.GetString(items[i].Name);

                if (HasCustomEndpoint(application, name))
                {
                    warnings.Add($"Custom endpoint '{name}' already exists — skipped creation.");
                    continue;
                }

                var endpoint = new DBWS_CustomEndpoint
                {
                    AppID = application.ID,
                    Name = name,
                    Description = items[i].Description,
                    Query = items[i].Query
                };

                _dbContext.CustomEndpoints.Add(endpoint);

                await _dbContext.SaveChangesAsync();

                if (!application.CustomEndpoints.Contains(endpoint))
                {
                    application.CustomEndpoints.Add(endpoint);
                }
            }
        }

        // ---------- Helpers ----------

        private static DBWS_Entity? FindEntity(DBWS_Application application, string name)
        {
            return application.Entities.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        private static DBWS_EntityProperty? FindProperty(DBWS_Entity entity, string name)
        {
            return entity.Properties.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // The name is matched without the spaces around it, as it is stored.
        private static bool HasCustomEndpoint(DBWS_Application application, string name)
        {
            return application.CustomEndpoints.Any(x => string.Equals(Utils.GetString(x.Name), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Whether the API server gives a new entity a property of this name by itself. A payload
        /// that lists one is compared with it at its step, like any property that exists.
        /// </summary>
        private static bool ComesWithNewEntity(DBWS_Application application, SchemaImportEntity entity, string propertyName)
        {
            return SystemPropertyNames(application, entity).Contains(propertyName, StringComparer.OrdinalIgnoreCase);
        }

        // The properties the API server gives a new entity by itself.
        private static List<string> SystemPropertyNames(DBWS_Application application, SchemaImportEntity entity)
        {
            var names = new List<string> { Globals.PrimaryKeyColumn, Globals.OwnerColumn, Globals.CreatedColumn };

            if (entity.HasDifferentiationProperty && !string.IsNullOrWhiteSpace(application.DifferentiationEntity))
            {
                names.Add(application.DifferentiationEntity.GetDifferentiationPropertyName());
            }

            return names;
        }

        /// <summary>
        /// A copy of the application as the import leaves it, for the checks of the security rules
        /// to read: its entities, properties and custom endpoints, and the ones of the payload it
        /// does not have yet, matched as the steps match them. A copy, because the context tracks
        /// the application and must not see an entity that no step has created.
        /// </summary>
        private static DBWS_Application ApplicationAfterImport(
            DBWS_Application application,
            List<SchemaImportEntity> entities,
            List<SchemaImportCustomEndpoint> customEndpoints)
        {
            var imported = new DBWS_Application
            {
                DifferentiationEntity = application.DifferentiationEntity,
                Entities = application.Entities
                    .Select(x => new DBWS_Entity
                    {
                        Name = x.Name,
                        IsReadOnly = x.IsReadOnly,
                        IsSystem = x.IsSystem,
                        HasDifferentiationProperty = x.HasDifferentiationProperty,
                        Properties = x.Properties
                            .Select(p => new DBWS_EntityProperty { ID = p.ID, Name = p.Name, IsSystem = p.IsSystem, IsPrimaryKey = p.IsPrimaryKey })
                            .ToList()
                    })
                    .ToList(),
                CustomEndpoints = application.CustomEndpoints
                    .Select(x => new DBWS_CustomEndpoint { Name = Utils.GetString(x.Name) })
                    .ToList()
            };

            // A null item is reported by its own check.
            foreach (var item in entities.Where(x => x is not null))
            {
                var entity = FindEntity(imported, item.Name);

                if (entity is null)
                {
                    entity = new DBWS_Entity
                    {
                        Name = item.Name,
                        HasDifferentiationProperty = item.HasDifferentiationProperty,
                        Properties = SystemPropertyNames(imported, item)
                            .Select(x => new DBWS_EntityProperty { Name = x, IsSystem = true, IsPrimaryKey = x == Globals.PrimaryKeyColumn })
                            .ToList()
                    };

                    imported.Entities.Add(entity);
                }

                foreach (var property in (item.Properties ?? new List<SchemaImportProperty>()).Where(x => x is not null))
                {
                    if (FindProperty(entity, property.Name) is null)
                    {
                        entity.Properties.Add(new DBWS_EntityProperty { Name = property.Name });
                    }
                }
            }

            foreach (var item in customEndpoints.Where(x => x is not null))
            {
                var name = Utils.GetString(item.Name);

                if (!HasCustomEndpoint(imported, name))
                {
                    imported.CustomEndpoints.Add(new DBWS_CustomEndpoint { Name = name });
                }
            }

            return imported;
        }

        // What POST entities says about the name of a new entity; null when it is fine.
        private static string? EntityNameError(string name)
        {
            if (!IsMatch(name, EntityNameRules.Pattern))
            {
                return EntityNameRules.PatternMessage;
            }

            if (name.Length < EntityNameRules.MinLength || name.Length > EntityNameRules.MaxLength)
            {
                return EntityNameRules.LengthMessage;
            }

            return null;
        }

        // What POST properties says about the name of a new property; null when it is fine.
        private static string? PropertyNameError(string name)
        {
            if (!IsMatch(name, PropertyRules.Pattern))
            {
                return PropertyRules.PatternMessage;
            }

            if (name.Length < PropertyRules.MinLength || name.Length > PropertyRules.MaxLength)
            {
                return PropertyRules.LengthMessage;
            }

            if (name.EndsWith(PropertyRules.ForbiddenSuffix, StringComparison.Ordinal))
            {
                return PropertyRules.ForbiddenSuffixMessage;
            }

            return null;
        }

        private static void AddIfRefused(List<ErrorDetail> errors, string property, string? message)
        {
            if (message is not null)
            {
                errors.Add(Error(property, message));
            }
        }

        private static EntityConstraint ToStored(SchemaImportConstraint item)
        {
            return new EntityConstraint { IsSystem = item.IsSystem, TypeID = item.TypeID, Properties = item.Properties };
        }

        /// <summary>
        /// The rule as it is stored, for a rule that passed its checks: under the spelling its item
        /// has in <paramref name="imported"/>. The API server matches the name of a rule whatever its
        /// letter case, but the Security tab shows only the rules that spell it as the item does, and
        /// its next save deletes the others: a rule it does not show is a grant nobody sees.
        /// </summary>
        private static DBWS_Security ToStored(DBWS_Application imported, SchemaImportSecurityRule item)
        {
            var name = (SecurityTypes)item.TypeID switch
            {
                SecurityTypes.Entity => SecurityRuleChecks.FindEntity(imported, item.Name, StringComparison.OrdinalIgnoreCase)?.Name,
                SecurityTypes.CustomEndpoint => SecurityRuleChecks.FindCustomEndpoint(imported, item.Name, StringComparison.OrdinalIgnoreCase)?.Name,
                _ => Globals.SCHEMA
            };

            return new DBWS_Security
            {
                Name = name ?? item.Name,
                TypeID = item.TypeID,
                RoleID = item.RoleID,
                Action = item.Action,
                Record = item.Record,
                Properties = item.Properties,
                RateLimit = item.RateLimit is null
                    ? null
                    : new DBWS_Security.RateLimitItem { MaxRequests = item.RateLimit.MaxRequests, TimeWindowType = item.RateLimit.TimeWindowType }
            };
        }

        // The model throws for a foreign key text it cannot read; OverflowException is an
        // on-delete action written as a number that is too large.
        private static bool IsForeignKey(EntityConstraint constraint)
        {
            try
            {
                constraint.GetForeignKeyProperties();
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException || exception is OverflowException)
            {
                return false;
            }
        }

        // The whole value must match, as with [RegularExpression] on a request contract.
        private static bool IsMatch(string value, string pattern)
        {
            var match = Regex.Match(value, pattern);

            return match.Success && match.Index == 0 && match.Length == value.Length;
        }

        private static void ThrowIfDifferent<T>(string path, string name, string field, T existing, T import)
        {
            if (!EqualityComparer<T>.Default.Equals(existing, import))
            {
                throw Stopped($"{path}.{field}", $"Property '{name}': '{field}' mismatch (existing: {existing}, import: {import}).");
            }
        }

        private static ErrorDetail Error(string property, string message)
        {
            return new ErrorDetail { Property = property, Message = message };
        }

        /// <summary>
        /// A 400 VALIDATION that names the item of the payload the import stopped at. The message
        /// says what is wrong with it, and that the steps before it stay applied.
        /// </summary>
        private static PortalException Stopped(string property, string message)
        {
            return new PortalException(
                StatusCodes.Status400BadRequest,
                PortalErrorCode.Validation,
                message + StoppedNote,
                property: property,
                errors: new List<ErrorDetail> { Error(property, message) });
        }

        /// <summary>
        /// A failed call to the API server, with the step it was made for in the message: a
        /// refusal (400) names the item of the payload, anything else stays an UPSTREAM_ERROR.
        /// </summary>
        private async Task<T> CallApiServerAsync<T>(string path, string step, Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (PortalException exception)
            {
                var message = $"{step}: {exception.Message}";

                throw exception.StatusCode == StatusCodes.Status400BadRequest
                    ? Stopped(path, message)
                    : new PortalException(exception.StatusCode, exception.Code, message + StoppedNote);
            }
        }

        // The order of every write to an application: the API server first, then the Portal database.
        private async Task CallApiServerThenSaveAsync(string path, string step, Func<Task> call)
        {
            await CallApiServerAsync(path, step, async () =>
            {
                await call();
                return true;
            });

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException exception)
            {
                _logger.LogError(exception, "A change was made on the API server but not saved in the Portal | {Step}", step);

                throw new PortalException(
                    StatusCodes.Status500InternalServerError,
                    PortalErrorCode.Error,
                    $"{step}: the change was made there but could not be saved in the Portal.{StoppedNote}");
            }
        }
    }
}
