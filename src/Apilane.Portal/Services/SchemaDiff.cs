using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Apilane.Portal.Services
{
    /// <summary>
    /// What the schema-import diff and the application comparison have in common: which parts of
    /// an application are compared at all. How names are matched is not in here, because the two
    /// differ on purpose: the import matches whatever the letter case, the comparison exactly.
    /// </summary>
    public static class SchemaDiff
    {
        /// <summary>
        /// The entities in the order they were created.
        /// </summary>
        public static List<DBWS_Entity> Entities(DBWS_Application application)
        {
            return application.Entities.OrderBy(x => x.ID).ToList();
        }

        /// <summary>
        /// The properties somebody added, in the order they were created: not the primary key and
        /// not the system properties, which every entity gets from the API server.
        /// </summary>
        public static List<DBWS_EntityProperty> CustomProperties(DBWS_Entity entity)
        {
            return entity.Properties
                .Where(x => !x.IsSystem && !x.IsPrimaryKey)
                .OrderBy(x => x.ID)
                .ToList();
        }

        /// <summary>
        /// Every stored constraint. The model answers null when the stored JSON is the word 'null'.
        /// </summary>
        public static List<EntityConstraint> Constraints(DBWS_Entity entity)
        {
            return (entity.Constraints ?? new List<EntityConstraint>())
                .Where(x => x is not null)
                .ToList();
        }

        /// <summary>
        /// The constraints somebody added: not the system ones and not the ones without properties.
        /// </summary>
        public static List<EntityConstraint> CustomConstraints(DBWS_Entity entity)
        {
            return Constraints(entity)
                .Where(x => !x.IsSystem && !string.IsNullOrWhiteSpace(x.Properties))
                .ToList();
        }

        /// <summary>
        /// Two constraints are the same when they have the same type and the same properties text,
        /// whatever its letter case.
        /// </summary>
        public static string ConstraintKey(EntityConstraint constraint)
        {
            return $"{constraint.TypeID}|{constraint.Properties?.Trim().ToLowerInvariant()}";
        }

        /// <summary>
        /// The stored security rules, as they are stored. Answers 409 CONFLICT when the stored text
        /// is not a list of rules: nothing can be compared with it or added to it.
        /// </summary>
        public static List<DBWS_Security> SecurityRules(DBWS_Application application)
        {
            if (string.IsNullOrWhiteSpace(application.Security))
            {
                return new List<DBWS_Security>();
            }

            try
            {
                return (JsonSerializer.Deserialize<List<DBWS_Security>>(application.Security) ?? new List<DBWS_Security>())
                    .Where(x => x is not null)
                    .ToList();
            }
            catch (JsonException)
            {
                throw PortalException.Conflict($"The stored security rules of application '{application.Name}' cannot be read.", "Application");
            }
        }

        /// <summary>
        /// Rules stay behind when their entity or custom endpoint is renamed or deleted. Only a
        /// rule whose item the application still has counts; a Schema rule never does.
        /// </summary>
        public static bool SecurityItemExists(DBWS_Application application, DBWS_Security rule, StringComparison nameComparison)
        {
            return rule.TypeID_Enum switch
            {
                SecurityTypes.Entity => application.Entities.Any(x => string.Equals(x.Name, rule.Name, nameComparison)),
                SecurityTypes.CustomEndpoint => application.CustomEndpoints.Any(x => string.Equals(x.Name, rule.Name, nameComparison)),
                _ => false
            };
        }
    }
}
