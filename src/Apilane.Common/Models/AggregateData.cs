using System.Collections.Generic;

namespace Apilane.Common.Models
{
    public class AggregateData
    {
        public required List<AggregateProperty> Properties { get; set; } = null!;

        public class AggregateProperty
        {
            public required string Name { get; set; } = null!;
            public required string Alias { get; set; } = null!;
            public required DataAggregates Aggregate { get; set; }
        }

        public enum DataAggregates
        {
            Count,
            Min,
            Max,
            Sum,
            Avg
        }

        public static DataAggregates ConvertToType(string text)
        {
            return text.ToLower().Trim() switch
            {
                "min" => DataAggregates.Min,
                "max" => DataAggregates.Max,
                "sum" => DataAggregates.Sum,
                "avg" => DataAggregates.Avg,
                _ => DataAggregates.Count
            };
        }
    }
}
