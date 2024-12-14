using Apilane.Common.Enums;
using Apilane.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;

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
                return constraint.Properties.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).Distinct().ToList();
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
                var list = constraint.Properties.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (list.Count == 2)
                {
                    return (list[0], list[1], ForeignKeyLogic.ON_DELETE_NO_ACTION); // Default is no action
                }
                else if(list.Count == 3)
                {
                    return (list[0], list[1], (ForeignKeyLogic)Enum.Parse(typeof(ForeignKeyLogic), list[2]));
                }
                else
                {
                    throw new InvalidOperationException($"Invalid number of elements on FK contraint | Properties '{constraint.Properties}'");
                }
            }

            throw new InvalidOperationException($"Invalid properties on FK contraint | Properties '{constraint.Properties}'");
        }
    }
}
