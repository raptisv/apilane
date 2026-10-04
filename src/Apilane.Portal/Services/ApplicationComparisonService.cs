using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApplicationComparisonService : IApplicationComparisonService
    {
        private readonly IApplicationAccessService _applicationAccessService;

        public ApplicationComparisonService(IApplicationAccessService applicationAccessService)
        {
            _applicationAccessService = applicationAccessService;
        }

        public async Task<ApplicationComparisonResponse> CompareAsync(string appToken, string targetAppToken)
        {
            var source = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            if (string.Equals(targetAppToken, source.Token, StringComparison.Ordinal))
            {
                throw PortalException.Validation("Target", "Cannot compare to self");
            }

            var target = await _applicationAccessService.GetApplicationWithEntitiesAsync(targetAppToken);

            return new ApplicationComparisonResponse
            {
                ApplicationSource = source.Name,
                ApplicationTarget = target.Name,
                Entities = CompareEntities(source, target),
                CustomEndpoints = CompareCustomEndpoints(source, target),
                Security = CompareSecurity(source, target)
            };
        }

        // Entities are matched by their exact name.
        private static ComparisonEntities CompareEntities(DBWS_Application source, DBWS_Application target)
        {
            var sourceEntities = SchemaDiff.Entities(source);
            var targetEntities = SchemaDiff.Entities(target);

            var result = new ComparisonEntities
            {
                Added = targetEntities.Where(x => !sourceEntities.Any(s => s.Name.Equals(x.Name))).Select(ToEntity).ToList(),
                Removed = sourceEntities.Where(x => !targetEntities.Any(t => t.Name.Equals(x.Name))).Select(ToEntity).ToList()
            };

            foreach (var sourceEntity in sourceEntities)
            {
                var targetEntity = targetEntities.FirstOrDefault(x => x.Name.Equals(sourceEntity.Name));

                if (targetEntity is null)
                {
                    continue;
                }

                var changed = CompareEntity(sourceEntity, targetEntity);

                if (changed.MetadataChanges.Count > 0
                    || changed.PropertiesAdded.Count > 0
                    || changed.PropertiesChanged.Count > 0
                    || changed.PropertiesRemoved.Count > 0
                    || changed.ConstraintsAdded.Count > 0
                    || changed.ConstraintsRemoved.Count > 0)
                {
                    result.Changed.Add(changed);
                }
            }

            return result;
        }

        private static ComparisonChangedEntity CompareEntity(DBWS_Entity source, DBWS_Entity target)
        {
            var sourceProperties = SchemaDiff.CustomProperties(source);
            var targetProperties = SchemaDiff.CustomProperties(target);

            var changed = new ComparisonChangedEntity
            {
                Name = source.Name,
                // Added and removed look at the exact name among all properties, system ones included...
                PropertiesAdded = targetProperties
                    .Where(x => !source.Properties.Any(p => p.Name.Equals(x.Name)))
                    .Select(ToProperty)
                    .ToList(),
                PropertiesRemoved = sourceProperties
                    .Where(x => !target.Properties.Any(p => p.Name.Equals(x.Name)))
                    .Select(x => new ComparisonRemovedProperty { Name = x.Name, TypeLabel = x.TypeID_Enum.ToString() })
                    .ToList()
            };

            // ...while changed pairs the custom properties whatever their letter case.
            foreach (var sourceProperty in sourceProperties)
            {
                var targetProperty = targetProperties.FirstOrDefault(x => x.Name.Equals(sourceProperty.Name, StringComparison.OrdinalIgnoreCase));

                if (targetProperty is null)
                {
                    continue;
                }

                var changes = CompareProperty(sourceProperty, targetProperty);

                if (changes.Count > 0)
                {
                    changed.PropertiesChanged.Add(new ComparisonChangedProperty { Name = sourceProperty.Name, Changes = changes });
                }
            }

            AddIfDifferent(changed.MetadataChanges, "Description", source.Description, target.Description);
            AddIfDifferent(changed.MetadataChanges, "RequireChangeTracking", source.RequireChangeTracking.ToString(), target.RequireChangeTracking.ToString());
            AddIfDifferent(changed.MetadataChanges, "HasDifferentiationProperty", source.HasDifferentiationProperty.ToString(), target.HasDifferentiationProperty.ToString());

            // A custom constraint counts as present when the other entity has it at all, as a system one too.
            var sourceKeys = SchemaDiff.Constraints(source).Select(SchemaDiff.ConstraintKey).ToHashSet();
            var targetKeys = SchemaDiff.Constraints(target).Select(SchemaDiff.ConstraintKey).ToHashSet();

            changed.ConstraintsAdded = SchemaDiff.CustomConstraints(target)
                .Where(x => !sourceKeys.Contains(SchemaDiff.ConstraintKey(x)))
                .Select(ToConstraint)
                .ToList();

            changed.ConstraintsRemoved = SchemaDiff.CustomConstraints(source)
                .Where(x => !targetKeys.Contains(SchemaDiff.ConstraintKey(x)))
                .Select(ToConstraint)
                .ToList();

            return changed;
        }

        private static List<ComparisonFieldChange> CompareProperty(DBWS_EntityProperty source, DBWS_EntityProperty target)
        {
            var changes = new List<ComparisonFieldChange>();

            AddIfDifferent(changes, "Type", source.TypeID_Enum.ToString(), target.TypeID_Enum.ToString());
            AddIfDifferent(changes, "Required", source.Required.ToString(), target.Required.ToString());
            AddIfDifferent(changes, "Minimum", source.Minimum?.ToString(), target.Minimum?.ToString());
            AddIfDifferent(changes, "Maximum", source.Maximum?.ToString(), target.Maximum?.ToString());
            AddIfDifferent(changes, "DecimalPlaces", source.DecimalPlaces?.ToString(), target.DecimalPlaces?.ToString());
            AddIfDifferent(changes, "Encrypted", source.Encrypted.ToString(), target.Encrypted.ToString());
            AddIfDifferent(changes, "ValidationRegex", source.ValidationRegex, target.ValidationRegex);
            AddIfDifferent(changes, "Description", source.Description, target.Description);

            return changes;
        }

        private static void AddIfDifferent(List<ComparisonFieldChange> changes, string field, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changes.Add(new ComparisonFieldChange { Field = field, Before = before, After = after });
            }
        }

        private static ComparisonCustomEndpoints CompareCustomEndpoints(DBWS_Application source, DBWS_Application target)
        {
            var sourceEndpoints = source.CustomEndpoints.OrderBy(x => x.ID).ToList();
            var targetEndpoints = target.CustomEndpoints.OrderBy(x => x.ID).ToList();

            var result = new ComparisonCustomEndpoints
            {
                Added = targetEndpoints.Where(x => !sourceEndpoints.Any(s => s.Name.Equals(x.Name))).Select(ToCustomEndpoint).ToList(),
                Removed = sourceEndpoints.Where(x => !targetEndpoints.Any(t => t.Name.Equals(x.Name))).Select(ToCustomEndpoint).ToList()
            };

            foreach (var sourceEndpoint in sourceEndpoints)
            {
                var targetEndpoint = targetEndpoints.FirstOrDefault(x => x.Name.Equals(sourceEndpoint.Name));

                if (targetEndpoint is null)
                {
                    continue;
                }

                if (!string.Equals(sourceEndpoint.Query, targetEndpoint.Query, StringComparison.Ordinal)
                    || !string.Equals(sourceEndpoint.Description, targetEndpoint.Description, StringComparison.Ordinal))
                {
                    result.Changed.Add(new ComparisonChangedCustomEndpoint
                    {
                        Name = sourceEndpoint.Name,
                        DescriptionBefore = sourceEndpoint.Description,
                        DescriptionAfter = targetEndpoint.Description,
                        QueryBefore = sourceEndpoint.Query,
                        QueryAfter = targetEndpoint.Query
                    });
                }
            }

            return result;
        }

        // Rules are matched by type, name, role and action, exactly.
        private static ComparisonSecurity CompareSecurity(DBWS_Application source, DBWS_Application target)
        {
            var sourceRules = SchemaDiff.SecurityRules(source)
                .Where(x => SchemaDiff.SecurityItemExists(source, x, StringComparison.Ordinal))
                .ToList();

            var targetRules = SchemaDiff.SecurityRules(target)
                .Where(x => SchemaDiff.SecurityItemExists(target, x, StringComparison.Ordinal))
                .ToList();

            var result = new ComparisonSecurity
            {
                Added = targetRules.Where(x => !sourceRules.Any(s => s.ToUniqueStringShort().Equals(x.ToUniqueStringShort()))).Select(ToSecurityRule).ToList(),
                Removed = sourceRules.Where(x => !targetRules.Any(t => t.ToUniqueStringShort().Equals(x.ToUniqueStringShort()))).Select(ToSecurityRule).ToList()
            };

            foreach (var sourceRule in sourceRules)
            {
                var targetRule = targetRules.FirstOrDefault(x => x.ToUniqueStringShort().Equals(sourceRule.ToUniqueStringShort()));

                if (targetRule is not null && !sourceRule.ToUniqueStringLong().Equals(targetRule.ToUniqueStringLong()))
                {
                    result.Changed.Add(new ComparisonChangedSecurityRule
                    {
                        Name = sourceRule.NameDescriptive(),
                        SecurityBefore = ToSecurityRule(sourceRule),
                        SecurityAfter = ToSecurityRule(targetRule)
                    });
                }
            }

            return result;
        }

        private static ComparisonEntity ToEntity(DBWS_Entity entity)
        {
            return new ComparisonEntity
            {
                Name = entity.Name,
                Description = entity.Description,
                RequireChangeTracking = entity.RequireChangeTracking,
                HasDifferentiationProperty = entity.HasDifferentiationProperty,
                Properties = SchemaDiff.CustomProperties(entity).Select(ToProperty).ToList(),
                Constraints = SchemaDiff.CustomConstraints(entity).Select(ToConstraint).ToList()
            };
        }

        private static ComparisonProperty ToProperty(DBWS_EntityProperty property)
        {
            return new ComparisonProperty
            {
                Name = property.Name,
                TypeLabel = property.TypeID_Enum.ToString(),
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

        private static ComparisonConstraint ToConstraint(EntityConstraint constraint)
        {
            return new ComparisonConstraint { TypeID = constraint.TypeID, Properties = constraint.Properties };
        }

        private static ComparisonCustomEndpoint ToCustomEndpoint(DBWS_CustomEndpoint endpoint)
        {
            return new ComparisonCustomEndpoint { Name = endpoint.Name, Description = endpoint.Description, Query = endpoint.Query };
        }

        private static ComparisonSecurityRule ToSecurityRule(DBWS_Security rule)
        {
            return new ComparisonSecurityRule
            {
                Name = rule.NameDescriptive(),
                Role = rule.RoleID,
                Type = rule.TypeID_Enum.ToString(),
                RateLimit = rule.RateLimit?.ToUniqueString(),
                Action = rule.Action,
                Record = ((EndpointRecordAuthorization)rule.Record).ToString(),
                Properties = string.Join(",", rule.GetProperties().OrderBy(x => x))
            };
        }
    }
}
