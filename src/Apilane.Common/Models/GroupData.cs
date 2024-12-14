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

        public static GroupByType ConvertToType(string text)
        {
            return text.ToLower().Trim() switch
            {
                "year" => GroupByType.Date_Year,
                "month" => GroupByType.Date_Month,
                "day" => GroupByType.Date_Day,
                "hour" => GroupByType.Date_Hour,
                "minute" => GroupByType.Date_Minute,
                "second" => GroupByType.Date_Second,
                _ => GroupByType.None
            };
        }
    }
}
