using System.Collections.Generic;

namespace Apilane.Common.Models
{
    public class GroupData
    {
        public required List<GroupProperty> Properties { get; set; } = null!;

        public class GroupProperty
        {
            public required string Name { get; set; } = null!;
            public required string Alias { get; set; } = null!;
            public required GroupByType Type { get; set; } = GroupByType.None;
        }

        public enum GroupByType
        {
            None,
            Date_Year,
            Date_Month,
            Date_Day,
            Date_Hour,
            Date_Minute,
            Date_Second
        }

        /// <summary>
        /// Strictly parses a group-by suffix (the part after the '.' in e.g. "Created.year").
        /// Accepts only an empty/whitespace suffix (<see cref="GroupByType.None"/>) or one of
        /// year, month, day, hour, minute, second (case-insensitive, surrounding whitespace ignored).
        /// Any other text returns false.
        /// </summary>
        public static bool TryParseType(string? text, out GroupByType type)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                    type = GroupByType.None;
                    return true;
                case "year":
                    type = GroupByType.Date_Year;
                    return true;
                case "month":
                    type = GroupByType.Date_Month;
                    return true;
                case "day":
                    type = GroupByType.Date_Day;
                    return true;
                case "hour":
                    type = GroupByType.Date_Hour;
                    return true;
                case "minute":
                    type = GroupByType.Date_Minute;
                    return true;
                case "second":
                    type = GroupByType.Date_Second;
                    return true;
                default:
                    type = GroupByType.None;
                    return false;
            }
        }

        /// <summary>
        /// Lenient conversion kept for compatibility: unknown text maps to <see cref="GroupByType.None"/>.
        /// Do not use it to validate user input; use <see cref="TryParseType"/> instead.
        /// </summary>
        public static GroupByType ConvertToType(string text)
        {
            return TryParseType(text, out var type) ? type : GroupByType.None;
        }

        /// <summary>
        /// The canonical lowercase suffix for a group-by type ("year", "month", ...), or an empty string for <see cref="GroupByType.None"/>.
        /// </summary>
        public static string GetTypeSuffix(GroupByType type)
        {
            return type switch
            {
                GroupByType.Date_Year => "year",
                GroupByType.Date_Month => "month",
                GroupByType.Date_Day => "day",
                GroupByType.Date_Hour => "hour",
                GroupByType.Date_Minute => "minute",
                GroupByType.Date_Second => "second",
                _ => string.Empty
            };
        }

        /// <summary>
        /// Builds the result-column alias for a group-by property, e.g. "Created_year", or the bare
        /// property name when there is no suffix. It is built only from the property name and the
        /// canonical suffix of the parsed type, never from raw request text.
        /// </summary>
        public static string GetAlias(string propertyName, GroupByType type)
        {
            var suffix = GetTypeSuffix(type);
            return string.IsNullOrEmpty(suffix)
                ? propertyName
                : $"{propertyName}_{suffix}";
        }
    }
}
