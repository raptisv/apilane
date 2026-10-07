using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api.V1.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Apilane.Portal.Services
{
    internal static class SchemaImportConstraintChecks
    {
        internal const string ForeignKeyFormat = "A foreign key is 'Property,Entity' or 'Property,Entity,ON_DELETE_NO_ACTION' (or ON_DELETE_SET_NULL, ON_DELETE_CASCADE)";

        // Check the complete schema before importing any entity. Only canonical names from that
        // schema are stored or sent to the API server, never caller-supplied SQL fragments.
        internal static string? ValidateAndCanonicalize(DBWS_Application application, DBWS_Entity entity, SchemaImportConstraint item)
        {
            if (item.TypeID == (int)ConstraintType.Unique && string.IsNullOrWhiteSpace(item.Properties))
            {
                // Retain the documented no-op for an omitted/blank unique constraint.
                return null;
            }

            if (!IsIdentifier(entity.Name))
            {
                return "The entity name cannot be used in a constraint";
            }

            var parts = (item.Properties ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
            if (item.TypeID == (int)ConstraintType.Unique)
            {
                var names = new List<string>();
                foreach (var name in parts)
                {
                    var property = FindProperty(entity, name);
                    if (!IsIdentifier(name) || property is null || !IsIdentifier(property.Name))
                    {
                        return $"Property '{name}' does not exist or is not a valid constraint identifier";
                    }
                    if (property.Encrypted)
                    {
                        return $"Property '{property.Name}' is encrypted and cannot be unique";
                    }
                    if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        return "A property can be listed only once";
                    }
                    names.Add(property.Name);
                }

                item.Properties = string.Join(",", names);
                return null;
            }

            if (parts.Length is not (2 or 3) || !IsIdentifier(parts[0]) || !IsIdentifier(parts[1]))
            {
                return ForeignKeyFormat;
            }

            var action = ForeignKeyLogic.ON_DELETE_NO_ACTION;
            if (parts.Length == 3 && (!Enum.TryParse(parts[2], out action) || !Enum.IsDefined(action)))
            {
                return ForeignKeyFormat;
            }

            var local = FindProperty(entity, parts[0]);
            if (local is null || !IsIdentifier(local.Name))
            {
                return $"Property '{parts[0]}' does not exist or is not a valid constraint identifier";
            }
            if (local.IsSystem || local.TypeID_Enum != PropertyType.Number || local.DecimalPlaces != 0)
            {
                return "A foreign key must use a custom Number property with 0 decimal places";
            }

            var foreign = application.Entities.FirstOrDefault(x => string.Equals(x.Name, parts[1], StringComparison.OrdinalIgnoreCase));
            if (foreign is null || !IsIdentifier(foreign.Name))
            {
                return $"Entity '{parts[1]}' does not exist or is not a valid constraint identifier";
            }
            if (foreign.Name.Equals("Files", StringComparison.OrdinalIgnoreCase))
            {
                return "A foreign key cannot point to Files";
            }

            item.Properties = parts.Length == 2
                ? $"{local.Name},{foreign.Name}"
                : $"{local.Name},{foreign.Name},{action}";
            return null;
        }

        private static DBWS_EntityProperty? FindProperty(DBWS_Entity entity, string name)
        {
            return entity.Properties.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsIdentifier(string value)
        {
            return Regex.IsMatch(value, @"\A[A-Za-z_][A-Za-z0-9_]*\z");
        }
    }
}
