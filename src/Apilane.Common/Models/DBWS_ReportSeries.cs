using Apilane.Common.Attributes;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    /// <summary>
    /// A single series (query target) within a report panel. All series of a panel share the
    /// panel's Entity, GroupBy (the x-axis) and MaxRecords; each series contributes one aggregate
    /// property and may apply its own filter, so several lines/slices can be overlaid in one panel.
    /// </summary>
    public class DBWS_ReportSeries : DBWS_MainModel
    {
        public long PanelID { get; set; }

        [JsonIgnore]
        public DBWS_ReportPanel Panel { get; set; } = null!;

        [AttrRequired]
        public string Label { get; set; } = null!;

        /// <summary>The entity this series queries (series in a panel may target different entities).</summary>
        [AttrRequired]
        public string Entity { get; set; } = null!;

        /// <summary>This series' x-axis grouping (entity-specific). Series align by group value.</summary>
        [AttrRequired]
        public string GroupBy { get; set; } = null!;

        /// <summary>Single aggregate property, e.g. "ID.Count" or "Total.Sum".</summary>
        [AttrRequired]
        public string Property { get; set; } = null!;

        public string? Filter { get; set; }

        public int Order { get; set; }
    }
}
