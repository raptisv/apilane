using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api.V1.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Portal.Services
{
    /// <summary>
    /// What a security rule has to satisfy, in one place for PUT security/rules and the schema
    /// import, so the two cannot disagree about it. A check answers the first problem it finds,
    /// or null, and the caller decides what to do with it: the editor stops at it, the import
    /// lists it. The place of a problem is the path of the rule and the field (Rules[3].Action,
    /// Security[3].Action): the rule of the request and the rule of the import call these fields alike.
    /// </summary>
    internal static class SecurityRuleChecks
    {
        private const string FilesEntityName = "Files";

        // Read only: what it holds is what both the rules editor and the import accept.
        public static readonly IReadOnlyList<string> Actions = Array.AsReadOnly(new[] { "get", "post", "put", "delete" });

        /// <summary>
        /// The item of the rule, its role and its action, in the order the rules editor reports
        /// them. <paramref name="type"/> must be a defined type and <paramref name="action"/>
        /// lower case. <paramref name="entity"/> is the entity of an Entity rule, for the check of
        /// its properties. The application must have its entities with their properties and its
        /// custom endpoints loaded; the names of the items are compared as
        /// <paramref name="nameComparison"/> says.
        /// </summary>
        public static ErrorDetail? CheckItem(
            DBWS_Application application,
            SecurityTypes type,
            string name,
            string roleId,
            string action,
            string path,
            StringComparison nameComparison,
            out DBWS_Entity? entity)
        {
            entity = null;

            var namePath = $"{path}.{nameof(SecurityRuleRequest.Name)}";

            if (string.IsNullOrWhiteSpace(name))
            {
                return Error(namePath, "Required");
            }

            switch (type)
            {
                case SecurityTypes.Entity:
                    entity = FindEntity(application, name, nameComparison);

                    if (entity is null)
                    {
                        return Error(namePath, $"The application has no entity '{name}'");
                    }

                    break;
                case SecurityTypes.CustomEndpoint:
                    if (!HasCustomEndpoint(application, name, nameComparison))
                    {
                        return Error(namePath, $"The application has no custom endpoint '{name}'");
                    }

                    break;
                default:
                    if (!string.Equals(name, Globals.SCHEMA, nameComparison))
                    {
                        return Error(namePath, $"Must be {Globals.SCHEMA}");
                    }

                    break;
            }

            if (string.IsNullOrWhiteSpace(roleId))
            {
                return Error($"{path}.{nameof(SecurityRuleRequest.RoleID)}", "Required");
            }

            var actionPath = $"{path}.{nameof(SecurityRuleRequest.Action)}";

            if (!Actions.Contains(action))
            {
                return Error(actionPath, "Must be get, post, put or delete");
            }

            if (type == SecurityTypes.Schema && action != "get")
            {
                return Error(actionPath, "Schema rules allow only get");
            }

            if (!ItemAllows(type, entity, action))
            {
                return Error(actionPath, $"{name} does not allow {action}");
            }

            return null;
        }

        /// <summary>
        /// The properties of the rule. <paramref name="entity"/> is null for a rule that is not an
        /// Entity rule, which has none. The names are compared exactly, as the Security tab does
        /// when it shows a rule. The API server ignores their letter case, so a name spelled
        /// otherwise would work there, but the tab would not list it.
        /// </summary>
        public static ErrorDetail? CheckProperties(DBWS_Application application, DBWS_Entity? entity, string action, IEnumerable<string?> properties, string path)
        {
            var allowed = entity is null ? new List<string>() : AllowedProperties(application, entity, action);

            foreach (var property in properties)
            {
                if (property is null || !allowed.Contains(property, StringComparer.Ordinal))
                {
                    return Error(
                        $"{path}.{nameof(SecurityRuleRequest.Properties)}",
                        entity is null
                            ? "Only entity rules have properties"
                            : $"'{property}' is not a property a {action} rule of {entity.Name} can list");
                }
            }

            return null;
        }

        /// <summary>
        /// The properties a rule may name for an action of an entity: never the primary key,
        /// only editable ones for post and put, none for delete and none for a post to Files.
        /// </summary>
        public static List<string> AllowedProperties(DBWS_Application application, DBWS_Entity entity, string action)
        {
            var properties = entity.Properties.OrderBy(x => x.ID).Where(x => !x.IsPrimaryKey);

            return action switch
            {
                "get" => properties.Select(x => x.Name).ToList(),
                // Put is never allowed on Files, and its post takes a file, not properties.
                "post" or "put" when entity.Name != FilesEntityName => properties
                    .Where(x => x.AllowEdit(application.DifferentiationEntity, entity.HasDifferentiationProperty))
                    .Select(x => x.Name)
                    .ToList(),
                _ => new List<string>()
            };
        }

        /// <summary>
        /// Whether the item has the action: every item has get;
        /// only entities have post, put and delete, and only when the entity allows them.
        /// </summary>
        public static bool ItemAllows(SecurityTypes type, DBWS_Entity? entity, string action)
        {
            if (action == "get")
            {
                return true;
            }

            if (type != SecurityTypes.Entity || entity is null)
            {
                return false;
            }

            return action switch
            {
                "post" => entity.AllowPost(),
                "put" => entity.AllowPut(),
                "delete" => entity.AllowDelete(),
                _ => false
            };
        }

        public static DBWS_Entity? FindEntity(DBWS_Application application, string name, StringComparison comparison)
        {
            return application.Entities.FirstOrDefault(x => string.Equals(x.Name, name, comparison));
        }

        public static DBWS_CustomEndpoint? FindCustomEndpoint(DBWS_Application application, string name, StringComparison comparison)
        {
            return (application.CustomEndpoints ?? new List<DBWS_CustomEndpoint>())
                .FirstOrDefault(x => string.Equals(x.Name, name, comparison));
        }

        public static bool HasCustomEndpoint(DBWS_Application application, string name, StringComparison comparison)
        {
            return FindCustomEndpoint(application, name, comparison) is not null;
        }

        private static ErrorDetail Error(string property, string message)
        {
            return new ErrorDetail { Property = property, Message = message };
        }
    }
}
