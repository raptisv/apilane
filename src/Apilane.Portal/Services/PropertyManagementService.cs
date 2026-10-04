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
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class PropertyManagementService : IPropertyManagementService
    {
        private const string EntityName = "Entity";
        private const string PropertyName = "Property";

        private readonly ApplicationDbContext _dbContext;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IApiServerClient _apiServerClient;
        private readonly IApplicationWriteScope _applicationWriteScope;

        public PropertyManagementService(
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

        public async Task<List<PropertyResponse>> GetAllAsync(string appToken, string entityName)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            // The mapper sorts them (EntityMapper).
            return entity.ToResponse(application, withProperties: true).Properties ?? new List<PropertyResponse>();
        }

        public async Task<PropertyResponse> GetAsync(string appToken, string entityName, string propertyName)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);
            var property = _applicationAccessService.GetProperty(entity, propertyName);

            return property.ToResponse(application, entity);
        }

        public async Task<PropertyResponse> CreateAsync(string appToken, string entityName, CreatePropertyRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            // Some system entities take no new properties.
            if (!entity.AllowAddProperties())
            {
                throw PortalException.Conflict("Properties cannot be added to this entity", EntityName);
            }

            var name = Utils.GetString(request.Name);
            var errors = ValidateName(name, nameof(CreatePropertyRequest.Name));

            if (!TryParseType(request.Type, out var type))
            {
                errors.Add(Error(nameof(CreatePropertyRequest.Type), $"Must be one of: {string.Join(", ", Enum.GetNames<PropertyType>())}"));

                // The other rules depend on the type, so they are not checked without one.
                throw PortalException.Validation(errors);
            }

            var property = new DBWS_EntityProperty
            {
                EntityID = entity.ID,
                Name = name,
                Description = EmptyToNull(request.Description),
                TypeID = (int)type,
                IsPrimaryKey = false,
                IsSystem = false,
                Required = request.Required,
                Encrypted = request.Encrypted,
                ValidationRegex = EmptyToNull(request.ValidationRegex),
                DecimalPlaces = request.DecimalPlaces,
                Minimum = request.Minimum,
                Maximum = request.Maximum
            };

            errors.AddRange(NormaliseAndValidate(property, isNew: true));
            ThrowIfAny(errors);

            ThrowIfNameIsTaken(entity, name, except: null);

            _dbContext.EntityProperties.Add(property);

            await _applicationWriteScope.SaveAsync(application, () => _apiServerClient.GeneratePropertyAsync(application, entity.Name, property));

            return property.ToResponse(application, entity);
        }

        public async Task<PropertyResponse> UpdateAsync(string appToken, string entityName, string propertyName, UpdatePropertyRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);
            var property = _applicationAccessService.GetProperty(entity, propertyName);

            if (property.IsSystem)
            {
                throw PortalException.Conflict("Cannot edit system Properties", PropertyName);
            }

            var allowMaxEdit = property.AllowMaxEdit();

            // A copy with the stored type, so the rules are those of the property and not of
            // whatever the request claims it is. Nothing of the stored property changes before
            // the values were checked.
            var candidate = (DBWS_EntityProperty)property.Clone();
            candidate.Description = EmptyToNull(request.Description);
            candidate.ValidationRegex = EmptyToNull(request.ValidationRegex);
            candidate.Minimum = request.Minimum;

            if (allowMaxEdit)
            {
                candidate.Maximum = request.Maximum;
            }

            ThrowIfAny(NormaliseAndValidate(candidate, isNew: false));

            property.Description = candidate.Description;
            property.ValidationRegex = candidate.ValidationRegex;
            property.Minimum = candidate.Minimum;

            if (allowMaxEdit)
            {
                property.Maximum = candidate.Maximum;
            }

            // Nothing to call: the cache reset is how the API server learns about the change.
            await _applicationWriteScope.SaveAsync(application);

            return property.ToResponse(application, entity);
        }

        public async Task<PropertyResponse> RenameAsync(string appToken, string entityName, string propertyName, RenamePropertyRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);
            var property = _applicationAccessService.GetProperty(entity, propertyName);
            var newName = Utils.GetString(request.NewName);

            if (property.IsSystem)
            {
                throw PortalException.Conflict("Cannot rename system Properties", PropertyName);
            }

            ThrowIfAny(ValidateName(newName, nameof(RenamePropertyRequest.NewName)));
            ThrowIfInConstraint(entity, property, "rename");
            ThrowIfNameIsTaken(entity, newName, except: property);

            property.Name = newName;

            await _applicationWriteScope.SaveAsync(application, () => _apiServerClient.RenameEntityPropertyAsync(application, property.ID, newName));

            return property.ToResponse(application, entity);
        }

        public async Task DeleteAsync(string appToken, string entityName, string propertyName)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);
            var property = _applicationAccessService.GetProperty(entity, propertyName);

            if (property.IsSystem)
            {
                throw PortalException.Conflict("This property is part of a module and cannot be deleted.", PropertyName);
            }

            ThrowIfInConstraint(entity, property, "delete");

            _dbContext.EntityProperties.Remove(property);

            await _applicationWriteScope.SaveAsync(application, () => _apiServerClient.DegeneratePropertyAsync(application, property.ID));
        }

        /// <summary>
        /// The one set of type-dependent rules, for a new property and for a changed one. First
        /// drops the values the type has no use for, then checks the rest.
        /// The errors name the request properties, which are called the same in both requests.
        /// </summary>
        private static List<ErrorDetail> NormaliseAndValidate(DBWS_EntityProperty property, bool isNew)
        {
            var errors = new List<ErrorDetail>();

            if (!property.AllowMin())
            {
                property.Minimum = null;
            }

            if (!property.AllowMax())
            {
                property.Maximum = null;
            }

            if (!property.AllowDecimalPlaces())
            {
                property.DecimalPlaces = null;
            }

            if (!property.AllowValidationRegex())
            {
                property.ValidationRegex = null;
            }

            if (!property.AllowEncrypted())
            {
                property.Encrypted = false;
            }

            // Decimal places are set once, when the column is created.
            if (isNew && property.AllowDecimalPlaces())
            {
                if (property.DecimalPlaces is null)
                {
                    errors.Add(Error(nameof(CreatePropertyRequest.DecimalPlaces), "Required"));
                }
                else if (property.DecimalPlaces < 0 || property.DecimalPlaces > PropertyRules.MaxDecimalPlaces)
                {
                    errors.Add(Error(nameof(CreatePropertyRequest.DecimalPlaces), $"Must be between 0 and {PropertyRules.MaxDecimalPlaces}"));
                }
            }

            if (!string.IsNullOrWhiteSpace(property.ValidationRegex) && !Utils.IsValidRegex(property.ValidationRegex))
            {
                errors.Add(Error(nameof(CreatePropertyRequest.ValidationRegex), "Invalid regex"));
            }

            // Minimum and Maximum travel as JSON numbers, which a browser reads exactly only within
            // this range: beyond it the number that arrives is already a rounded one, so it is
            // refused instead of stored. Only what the request writes is checked: on a change the
            // Maximum of a String is the stored one, and a stored one beyond the range is left alone.
            if (PropertyRules.IsOutsideLimitRange(property.Minimum))
            {
                errors.Add(Error(nameof(CreatePropertyRequest.Minimum), PropertyRules.LimitRangeMessage));
            }

            if ((isNew || property.AllowMaxEdit()) && PropertyRules.IsOutsideLimitRange(property.Maximum))
            {
                errors.Add(Error(nameof(CreatePropertyRequest.Maximum), PropertyRules.LimitRangeMessage));
            }

            // For a String both are lengths, and its Maximum is fixed once the column exists: a
            // value no text can meet could never be put right. A stored one is left alone.
            if (property.TypeID_Enum == PropertyType.String)
            {
                if (property.Minimum.HasValue && property.Minimum.Value < 0)
                {
                    errors.Add(Error(nameof(CreatePropertyRequest.Minimum), "Cannot be negative"));
                }

                if (isNew && property.Maximum.HasValue && property.Maximum.Value < 1)
                {
                    errors.Add(Error(nameof(CreatePropertyRequest.Maximum), "Must be at least 1"));
                }
            }

            if (property.Minimum.HasValue && property.Maximum.HasValue && property.Minimum.Value > property.Maximum.Value)
            {
                errors.Add(Error(nameof(CreatePropertyRequest.Minimum), $"Min ({property.Minimum.Value}) cannot be greater than Max ({property.Maximum.Value})"));
            }

            return errors;
        }

        // Letters, underscore and length are checked by the attributes of the request.
        private static List<ErrorDetail> ValidateName(string name, string requestProperty)
        {
            var errors = new List<ErrorDetail>();

            if (name.EndsWith(PropertyRules.ForbiddenSuffix, StringComparison.Ordinal))
            {
                errors.Add(Error(requestProperty, PropertyRules.ForbiddenSuffixMessage));
            }

            return errors;
        }

        private static bool TryParseType(string value, out PropertyType type)
        {
            type = default;

            // By name only: Enum.TryParse would take a number too.
            return Enum.GetNames<PropertyType>().Contains(value) && Enum.TryParse(value, out type);
        }

        private static ErrorDetail Error(string property, string message)
        {
            return new ErrorDetail { Property = property, Message = message };
        }

        private static void ThrowIfAny(List<ErrorDetail> errors)
        {
            if (errors.Count > 0)
            {
                throw PortalException.Validation(errors);
            }
        }

        // Case-insensitive: the API server and some databases do not tell 'Price' from 'price'.
        private static void ThrowIfNameIsTaken(DBWS_Entity entity, string name, DBWS_EntityProperty? except)
        {
            if (entity.Properties.Any(x => x != except && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw PortalException.Conflict($"Property name '{name}' already exists", PropertyName);
            }
        }

        // The stored constraints name properties as text, and nothing rewrites that text: after a
        // rename or a delete the constraint would name a property that is not there.
        private static void ThrowIfInConstraint(DBWS_Entity entity, DBWS_EntityProperty property, string action)
        {
            if (IsInConstraint(entity, property.Name))
            {
                throw PortalException.Conflict($"Cannot {action} property as it is part of a constraint of the entity", PropertyName);
            }
        }

        // Reads the stored constraints itself, as EntityManagementService does: a unique
        // constraint is 'PropA,PropB', a foreign key 'Property,Entity[,ON_DELETE_x]'.
        private static bool IsInConstraint(DBWS_Entity entity, string propertyName)
        {
            foreach (var constraint in entity.Constraints ?? new List<EntityConstraint>())
            {
                if (constraint is null || string.IsNullOrWhiteSpace(constraint.Properties))
                {
                    continue;
                }

                var parts = constraint.Properties.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // Only the first part of a foreign key is a property of this entity.
                var names = constraint.TypeID == (int)ConstraintType.ForeignKey ? parts.Take(1) : parts;

                if (names.Any(x => string.Equals(x, propertyName, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        // A field left empty is stored as null.
        private static string? EmptyToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
