using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api.V1.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Portal.Api.V1.Mapping
{
    public static class EntityMapper
    {
        /// <summary>
        /// The entity as the API shows it. The flags come from the model's own methods, so the
        /// API and the API server always agree. Properties must be loaded.
        /// </summary>
        public static EntityResponse ToResponse(this DBWS_Entity entity, DBWS_Application application, bool withProperties)
        {
            return new EntityResponse
            {
                Name = entity.Name,
                Description = entity.Description,
                IsSystem = entity.IsSystem,
                IsReadOnly = entity.IsReadOnly,
                RequireChangeTracking = entity.RequireChangeTracking,
                HasDifferentiationProperty = entity.HasDifferentiationProperty,
                AllowPost = entity.AllowPost(),
                AllowPut = entity.AllowPut(),
                AllowDelete = entity.AllowDelete(),
                AllowAddProperties = entity.AllowAddProperties(),
                Constraints = entity.ToConstraintResponses(),
                Properties = withProperties
                    ? entity.Properties
                        // The primary key first, then the custom properties, then the system ones, each by name.
                        .OrderByDescending(x => x.IsPrimaryKey)
                        .ThenBy(x => x.IsSystem)
                        .ThenBy(x => x.Name)
                        .Select(x => x.ToResponse(application, entity))
                        .ToList()
                    : null
            };
        }

        public static PropertyResponse ToResponse(this DBWS_EntityProperty property, DBWS_Application application, DBWS_Entity entity)
        {
            return new PropertyResponse
            {
                Name = property.Name,
                Description = property.Description,
                Type = property.TypeID_Enum.ToString(),
                IsPrimaryKey = property.IsPrimaryKey,
                IsSystem = property.IsSystem,
                // Counting lower ids also works for a new property EF has not added to the list yet.
                Position = entity.Properties.Count(x => x.ID < property.ID),
                Required = property.Required,
                Encrypted = property.Encrypted,
                ValidationRegex = property.ValidationRegex,
                DecimalPlaces = property.DecimalPlaces,
                Minimum = property.Minimum,
                Maximum = property.Maximum,
                AllowEdit = property.AllowEdit(application.DifferentiationEntity, entity.HasDifferentiationProperty),
                IsUtc = property.IsOnUTC(),
                AllowMin = property.AllowMin(),
                AllowMaxEdit = property.AllowMaxEdit(),
                AllowValidationRegex = property.AllowValidationRegex()
            };
        }

        /// <summary>
        /// The stored constraints as a structured list: a unique constraint is stored as
        /// 'PropA,PropB', a foreign key as 'Property,Entity' or 'Property,Entity,ON_DELETE_x'.
        /// Tolerant: a constraint without properties, of an unknown type or with a foreign key
        /// that does not have that shape is left out instead of failing the whole answer.
        /// </summary>
        public static List<ConstraintResponse> ToConstraintResponses(this DBWS_Entity entity)
        {
            var result = new List<ConstraintResponse>();

            // The model answers null when the stored JSON is the word 'null'.
            foreach (var constraint in entity.Constraints ?? new List<EntityConstraint>())
            {
                if (constraint is null || string.IsNullOrWhiteSpace(constraint.Properties))
                {
                    continue;
                }

                var parts = constraint.Properties
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                if (parts.Count == 0)
                {
                    continue;
                }

                if (constraint.TypeID == (int)ConstraintType.Unique)
                {
                    result.Add(new ConstraintResponse
                    {
                        Type = nameof(ConstraintType.Unique),
                        IsSystem = constraint.IsSystem,
                        Properties = parts
                    });
                }
                else if (constraint.TypeID == (int)ConstraintType.ForeignKey && TryGetOnDelete(parts, out var onDelete))
                {
                    result.Add(new ConstraintResponse
                    {
                        Type = nameof(ConstraintType.ForeignKey),
                        IsSystem = constraint.IsSystem,
                        Property = parts[0],
                        ForeignEntity = parts[1],
                        OnDelete = onDelete.ToString()
                    });
                }
            }

            return result;
        }

        private static bool TryGetOnDelete(List<string> parts, out ForeignKeyLogic onDelete)
        {
            // Two parts is the older format, from before the on-delete choice existed.
            onDelete = ForeignKeyLogic.ON_DELETE_NO_ACTION;

            return parts.Count == 2
                || (parts.Count == 3 && Enum.TryParse(parts[2], out onDelete) && Enum.IsDefined(onDelete));
        }
    }
}
