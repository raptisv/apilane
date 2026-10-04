using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Api.V1.Mapping;
using Apilane.Portal.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class EntityManagementService : IEntityManagementService
    {
        private const string EntityName = "Entity";

        private readonly ApplicationDbContext _dbContext;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IApiServerClient _apiServerClient;
        private readonly IApplicationWriteScope _applicationWriteScope;

        public EntityManagementService(
            ApplicationDbContext dbContext,
            IApplicationAccessService applicationAccessService,
            IApiServerClient apiServerClient,
            IApplicationWriteScope applicationWriteScope)
        {
            _dbContext = dbContext;
            _applicationAccessService = applicationAccessService;
            _apiServerClient = apiServerClient;
            _applicationWriteScope = applicationWriteScope;
        }

        public async Task<List<EntityResponse>> GetAllAsync(string appToken, bool includeProperties)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            // Sorted in memory, as the Razor page does. The properties are loaded either way.
            return application.Entities
                .OrderBy(x => x.Name)
                .Select(x => x.ToResponse(application, withProperties: includeProperties))
                .ToList();
        }

        public async Task<EntityResponse> GetAsync(string appToken, string entityName)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            return entity.ToResponse(application, withProperties: true);
        }

        public async Task<EntityResponse> CreateAsync(string appToken, CreateEntityRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var name = Utils.GetString(request.Name);

            if (request.HasDifferentiationProperty && string.IsNullOrWhiteSpace(application.DifferentiationEntity))
            {
                throw PortalException.Validation(
                    nameof(CreateEntityRequest.HasDifferentiationProperty),
                    "The application has no differentiation entity");
            }

            ThrowIfNameIsTaken(application, name, except: null);

            var initial = await _apiServerClient.GetSystemPropertiesAndConstraintsAsync(application, request.HasDifferentiationProperty);

            // The API server numbers what it returns; the Portal database gives the real IDs.
            initial.Properties.ForEach(x => x.ID = 0);

            var entity = new DBWS_Entity
            {
                AppID = application.ID,
                Name = name,
                Description = EmptyToNull(request.Description),
                RequireChangeTracking = request.RequireChangeTracking,
                HasDifferentiationProperty = request.HasDifferentiationProperty,
                IsReadOnly = false,
                IsSystem = false,
                Properties = initial.Properties,
                EntConstraints = JsonSerializer.Serialize(initial.Constraints)
            };

            _dbContext.Entities.Add(entity);

            await _applicationWriteScope.SaveAsync(application, () => _apiServerClient.GenerateEntityAsync(application, entity));

            return entity.ToResponse(application, withProperties: true);
        }

        public async Task<EntityResponse> UpdateAsync(string appToken, string entityName, UpdateEntityRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            // [Required] on the contract already answers 400 for a missing value.
            var requireChangeTracking = request.RequireChangeTracking
                ?? throw PortalException.Validation(nameof(UpdateEntityRequest.RequireChangeTracking), "Required");

            // The Razor form does not offer the switch for such an entity.
            if (requireChangeTracking && !entity.AllowPut())
            {
                throw PortalException.Validation(
                    nameof(UpdateEntityRequest.RequireChangeTracking),
                    "Changes cannot be tracked for this entity: its records cannot be updated");
            }

            entity.Description = EmptyToNull(request.Description);
            entity.RequireChangeTracking = requireChangeTracking;

            // Nothing to call: the cache reset is how the API server learns about the change.
            await _applicationWriteScope.SaveAsync(application);

            return entity.ToResponse(application, withProperties: true);
        }

        public async Task<EntityResponse> RenameAsync(string appToken, string entityName, RenameEntityRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);
            var newName = Utils.GetString(request.NewName);

            if (entity.IsSystem)
            {
                throw PortalException.Conflict("Cannot rename system Entities", EntityName);
            }

            ThrowIfReferenced(application, entity, "rename");
            ThrowIfNameIsTaken(application, newName, except: entity);

            entity.Name = newName;

            await _applicationWriteScope.SaveAsync(application, () => _apiServerClient.RenameEntityAsync(application, entity.ID, newName));

            return entity.ToResponse(application, withProperties: true);
        }

        public async Task DeleteAsync(string appToken, string entityName)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            if (entity.IsSystem)
            {
                throw PortalException.Conflict("Cannot delete system Entities", EntityName);
            }

            ThrowIfReferenced(application, entity, "delete");

            // Its properties are loaded, so they are removed and audited with it.
            _dbContext.Entities.Remove(entity);

            await _applicationWriteScope.SaveAsync(application, () => _apiServerClient.DegenerateEntityAsync(application, entity.Name));
        }

        // Case-insensitive: the API server and some databases do not tell 'Orders' from 'orders'.
        private static void ThrowIfNameIsTaken(DBWS_Application application, string name, DBWS_Entity? except)
        {
            if (application.Entities.Any(x => x != except && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw PortalException.Conflict($"Entity '{name}' already exists", EntityName);
            }
        }

        // A foreign key of any entity of the application, of the entity itself included.
        private static void ThrowIfReferenced(DBWS_Application application, DBWS_Entity entity, string action)
        {
            var referencedBy = application.Entities.FirstOrDefault(x => References(x, entity.Name));

            if (referencedBy is not null)
            {
                throw PortalException.Conflict($"Cannot {action} entity as it is referenced by '{referencedBy.Name}'", EntityName);
            }
        }

        // Reads the stored constraints itself: the mapper of the responses leaves out a foreign key
        // it cannot show (an unknown on-delete choice, too many parts), and this check must not.
        private static bool References(DBWS_Entity other, string entityName)
        {
            foreach (var constraint in other.Constraints ?? new List<EntityConstraint>())
            {
                if (constraint is null
                    || constraint.TypeID != (int)ConstraintType.ForeignKey
                    || string.IsNullOrWhiteSpace(constraint.Properties))
                {
                    continue;
                }

                var parts = constraint.Properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (parts.Length >= 2 && string.Equals(parts[1], entityName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // What the Razor form does with a field left empty.
        private static string? EmptyToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
