using Apilane.Common.Enums;
using Apilane.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Apilane.Common.Extensions
{
    public static class EntityConstraintExtensions
    {
        public static List<string> GetUniqueProperties(this EntityConstraint constraint)
        {
            if ((ConstraintType)constraint.TypeID != ConstraintType.Unique)
            {
                throw new InvalidOperationException($"Invalid use of {nameof(GetUniqueProperties)}");
            }

            if (!string.IsNullOrWhiteSpace(constraint.Properties))
            {
                return constraint.Properties.Split(',', StringSplitOptions.TrimEntries)
                    .Select(ValidateIdentifier).OrderBy(x => x).Distinct().ToList();
            }

            return new List<string>();
        }

        public static List<string> GetForeignKeyPropertiesAsList(this EntityConstraint constraint)
        {
            var props = constraint.GetForeignKeyProperties();
            if (props.Property is not null && props.FKEntity is not null)
            {
                return new List<string>() { props.Property, props.FKEntity };
            }

            return new List<string>();
        }

        public static (string Property, string FKEntity, ForeignKeyLogic FKLogic) GetForeignKeyProperties(this EntityConstraint constraint)
        {
            if ((ConstraintType)constraint.TypeID != ConstraintType.ForeignKey)
            {
                throw new InvalidOperationException($"Invalid use of {nameof(GetForeignKeyProperties)}");
            }

            if (!string.IsNullOrWhiteSpace(constraint.Properties))
            {
                var list = constraint.Properties.Split(',', StringSplitOptions.TrimEntries).ToList();
                if (list.Count == 2)
                {
                    return (ValidateIdentifier(list[0]), ValidateIdentifier(list[1]), ForeignKeyLogic.ON_DELETE_NO_ACTION); // Default is no action
                }
                else if (list.Count == 3)
                {
                    if (!Enum.TryParse<ForeignKeyLogic>(list[2], out var logic) || !Enum.IsDefined(logic))
                    {
                        throw new InvalidOperationException("Invalid foreign key on-delete action");
                    }
                    return (ValidateIdentifier(list[0]), ValidateIdentifier(list[1]), logic);
                }
                else
                {
                    throw new InvalidOperationException($"Invalid number of elements on FK contraint | Properties '{constraint.Properties}'");
                }
            }

            throw new InvalidOperationException($"Invalid properties on FK contraint | Properties '{constraint.Properties}'");
        }

        // SQL builders compose constraint names from these parts as well as quoting columns.
        // Keep fragments, delimiters and empty names out before any database command is built.
        // Digits after the first character allow safe legacy identifiers in addition to the
        // letters/underscores accepted by today's entity and property creation endpoints.
        private static string ValidateIdentifier(string identifier)
        {
            if (!Regex.IsMatch(identifier, @"\A[A-Za-z_][A-Za-z0-9_]*\z"))
            {
                throw new InvalidOperationException("Invalid constraint identifier");
            }
            return identifier;
        }
    }
}
