using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Api.V1.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class EntityConstraintService : IEntityConstraintService
    {
        private const string FilesEntityName = "Files";

        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IPortalAccessService _portalAccessService;
        private readonly IApiServerClient _apiServerClient;
        private readonly IApplicationWriteScope _applicationWriteScope;

        public EntityConstraintService(
            IApplicationAccessService applicationAccessService,
            IPortalAccessService portalAccessService,
            IApiServerClient apiServerClient,
            IApplicationWriteScope applicationWriteScope)
        {
            _applicationAccessService = applicationAccessService;
            _portalAccessService = portalAccessService;
            _apiServerClient = apiServerClient;
            _applicationWriteScope = applicationWriteScope;
        }

        public async Task<EntityConstraintsResponse> GetAsync(string appToken, string entityName)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            return ToResponse(entity, GetCandidates(application, entity));
        }

        public async Task<EntityConstraintsResponse> ReplaceAsync(string appToken, string entityName, ReplaceConstraintsRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            if (entity.IsSystem && !_portalAccessService.IsAdmin)
            {
                throw PortalException.Forbidden("Only an administrator can change the constraints of a system entity.");
            }

            // [Required] on the contract already answers 400 for a missing list.
            var requested = request.Constraints
                ?? throw PortalException.Validation(nameof(ReplaceConstraintsRequest.Constraints), "Required");

            var candidates = GetCandidates(application, entity);

            // Kept exactly as they are stored, whatever the client sent.
            var constraints = (entity.Constraints ?? new List<EntityConstraint>())
                .Where(x => x is not null && x.IsSystem && !string.IsNullOrWhiteSpace(x.Properties))
                .ToList();

            var seen = new HashSet<string>(constraints.Select(x => DuplicateKey(x.TypeID, x.Properties ?? string.Empty)), StringComparer.Ordinal);
            var errors = new List<ErrorDetail>();

            // The API server compares foreign keys by property and entity only, so it would never
            // apply a new on-delete action to the table. Read tolerantly: a two-part foreign key is
            // NO ACTION, and a stored row that cannot be read is left out.
            var storedForeignKeys = entity.ToConstraintResponses()
                .Where(x => !x.IsSystem && x.Type == nameof(ConstraintType.ForeignKey))
                .Select(x => (Key: DuplicateKey((int)ConstraintType.ForeignKey, $"{x.Property},{x.ForeignEntity}"), x.OnDelete))
                .ToList();

            for (var i = 0; i < requested.Count; i++)
            {
                var path = $"{nameof(ReplaceConstraintsRequest.Constraints)}[{i}]";
                var constraint = ToConstraint(requested[i], path, entity, candidates, errors);

                if (constraint is null)
                {
                    continue;
                }

                var key = DuplicateKey(constraint.TypeID, constraint.Properties ?? string.Empty);

                if (!seen.Add(key))
                {
                    errors.Add(Error(path, "The entity already has this constraint"));
                    continue;
                }

                // Several stored rows for one foreign key come from older versions; keeping any of them is fine.
                var onDelete = requested[i]?.OnDelete;
                if (constraint.TypeID == (int)ConstraintType.ForeignKey
                    && storedForeignKeys.Any(x => x.Key == key)
                    && !storedForeignKeys.Any(x => x.Key == key && x.OnDelete == onDelete))
                {
                    errors.Add(Error($"{path}.{nameof(ConstraintRequest.OnDelete)}", "To change what happens on delete, remove the foreign key and save, then add it again"));
                    continue;
                }

                constraints.Add(constraint);
            }

            if (errors.Count > 0)
            {
                throw PortalException.Validation(errors);
            }

            entity.EntConstraints = JsonSerializer.Serialize(constraints);

            await _applicationWriteScope.SaveAsync(application, () => _apiServerClient.GenerateConstraintsAsync(application, entity.Name, constraints));

            return ToResponse(entity, candidates);
        }

        private static EntityConstraintsResponse ToResponse(DBWS_Entity entity, ConstraintCandidatesResponse candidates)
        {
            return new EntityConstraintsResponse
            {
                Constraints = entity.ToConstraintResponses(),
                Candidates = candidates
            };
        }

        // What a constraint can be made of, in the order the properties and entities were created.
        private static ConstraintCandidatesResponse GetCandidates(DBWS_Application application, DBWS_Entity entity)
        {
            var properties = entity.Properties.OrderBy(x => x.ID).ToList();

            return new ConstraintCandidatesResponse
            {
                // Encrypted values are too long for a unique index.
                UniqueProperties = properties
                    .Where(x => !x.Encrypted)
                    .Select(x => x.Name)
                    .ToList(),
                ForeignKeyProperties = properties
                    .Where(x => !x.IsSystem && x.TypeID_Enum == PropertyType.Number && x.DecimalPlaces == 0)
                    .Select(x => x.Name)
                    .ToList(),
                ForeignEntities = application.Entities
                    .Where(x => !x.Name.Equals(FilesEntityName))
                    .OrderBy(x => x.ID)
                    .Select(x => x.Name)
                    .ToList(),
                OnDeleteActions = Enum.GetNames<ForeignKeyLogic>().ToList()
            };
        }

        /// <summary>
        /// The constraint as it is stored, or null when it is not acceptable: then
        /// <paramref name="errors"/> says why.
        /// </summary>
        private static EntityConstraint? ToConstraint(
            ConstraintRequest? request,
            string path,
            DBWS_Entity entity,
            ConstraintCandidatesResponse candidates,
            List<ErrorDetail> errors)
        {
            if (request is null)
            {
                errors.Add(Error(path, "Required"));
                return null;
            }

            if (request.IsSystem)
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.IsSystem)}", "System constraints cannot be changed: leave them out"));
                return null;
            }

            string? properties;
            ConstraintType type;

            switch (request.Type)
            {
                case nameof(ConstraintType.Unique):
                    type = ConstraintType.Unique;
                    properties = UniqueProperties(request, path, entity, candidates, errors);
                    break;
                case nameof(ConstraintType.ForeignKey):
                    type = ConstraintType.ForeignKey;
                    properties = ForeignKeyProperties(request, path, entity, candidates, errors);
                    break;
                default:
                    errors.Add(Error($"{path}.{nameof(ConstraintRequest.Type)}", $"Must be one of: {string.Join(", ", Enum.GetNames<ConstraintType>())}"));
                    return null;
            }

            return properties is null
                ? null
                : new EntityConstraint { IsSystem = false, TypeID = (int)type, Properties = properties };
        }

        // Stored as 'PropA,PropB', in the order they were sent.
        private static string? UniqueProperties(
            ConstraintRequest request,
            string path,
            DBWS_Entity entity,
            ConstraintCandidatesResponse candidates,
            List<ErrorDetail> errors)
        {
            var property = $"{path}.{nameof(ConstraintRequest.Properties)}";
            var names = request.Properties ?? new List<string>();

            if (names.Count == 0)
            {
                errors.Add(Error(property, "Select at least one property"));
                return null;
            }

            foreach (var name in names)
            {
                if (!entity.Properties.Any(x => string.Equals(x.Name, name, StringComparison.Ordinal)))
                {
                    errors.Add(Error(property, $"Property '{name}' does not exist"));
                    return null;
                }

                if (!candidates.UniqueProperties.Contains(name))
                {
                    errors.Add(Error(property, $"Property '{name}' is encrypted and cannot be unique"));
                    return null;
                }
            }

            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            {
                errors.Add(Error(property, "A property can be listed only once"));
                return null;
            }

            return string.Join(",", names);
        }

        // Stored as 'Property,Entity,ON_DELETE_x'.
        private static string? ForeignKeyProperties(
            ConstraintRequest request,
            string path,
            DBWS_Entity entity,
            ConstraintCandidatesResponse candidates,
            List<ErrorDetail> errors)
        {
            var before = errors.Count;

            if (string.IsNullOrWhiteSpace(request.Property))
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.Property)}", "Required"));
            }
            else if (!entity.Properties.Any(x => string.Equals(x.Name, request.Property, StringComparison.Ordinal)))
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.Property)}", $"Property '{request.Property}' does not exist"));
            }
            else if (!candidates.ForeignKeyProperties.Contains(request.Property))
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.Property)}", "Must be a custom Number property with 0 decimal places"));
            }

            if (string.IsNullOrWhiteSpace(request.ForeignEntity))
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.ForeignEntity)}", "Required"));
            }
            else if (request.ForeignEntity.Equals(FilesEntityName))
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.ForeignEntity)}", "A foreign key cannot point to Files"));
            }
            else if (!candidates.ForeignEntities.Contains(request.ForeignEntity))
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.ForeignEntity)}", $"Entity '{request.ForeignEntity}' does not exist"));
            }

            if (string.IsNullOrWhiteSpace(request.OnDelete))
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.OnDelete)}", "Required"));
            }
            else if (!candidates.OnDeleteActions.Contains(request.OnDelete))
            {
                errors.Add(Error($"{path}.{nameof(ConstraintRequest.OnDelete)}", $"Must be one of: {string.Join(", ", candidates.OnDeleteActions)}"));
            }

            return errors.Count > before
                ? null
                : $"{request.Property},{request.ForeignEntity},{request.OnDelete}";
        }

        // Two unique constraints are the same when they name the same properties in any order; two
        // foreign keys when they join the same property to the same entity, whatever happens on delete.
        private static string DuplicateKey(int typeId, string properties)
        {
            var parts = properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return typeId == (int)ConstraintType.Unique
                ? $"{typeId}:{string.Join(",", parts.OrderBy(x => x, StringComparer.Ordinal))}"
                : $"{typeId}:{string.Join(",", parts.Take(2))}";
        }

        private static ErrorDetail Error(string property, string message)
        {
            return new ErrorDetail { Property = property, Message = message };
        }
    }
}
