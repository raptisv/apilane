using Apilane.Common.Attributes;
using Apilane.Common.Enums;
using System.Collections.Generic;
using System.Text.Json;
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

        /// <summary>
        /// Builds the Stats/Aggregate URL for this series, using its own entity / group-by and the
        /// panel's shared page size. Series are aligned on the x-axis by their group value.
        /// <paramref name="filterOverride"/>, when provided, replaces this series' own filter
        /// (used to inject a time-range window for date group-bys).
        /// </summary>
        public string GetApiUrl(DBWS_ReportPanel panel, string? filterOverride = null)
        {
            var filter = filterOverride ?? Filter;
            return $"Stats/Aggregate?Entity={Entity}&Properties={Property}&Filter={filter}&Sort=Desc&GroupBy={GroupBy}&PageIndex=1&PageSize={panel.MaxRecords}".Replace(" ", "");
        }

        /// <summary>
        /// Returns this series' filter AND-combined with a [start, end] window on the given date
        /// property (values are unix-ms; the server resolves the property type from the schema).
        /// </summary>
        public string BuildWindowedFilter(string dateProperty, long startMs, long endMs)
        {
            var gte = new FilterData(dateProperty, FilterData.FilterOperators.greaterorequal, startMs, PropertyType.Date);
            var lte = new FilterData(dateProperty, FilterData.FilterOperators.lessorequal, endMs, PropertyType.Date);

            var existing = FilterData.Parse(Filter);

            var combined = existing is null
                ? new FilterData(FilterData.FilterLogic.AND, new List<FilterData> { gte, lte })
                : new FilterData(FilterData.FilterLogic.AND, new List<FilterData> { existing, gte, lte });

            return JsonSerializer.Serialize(combined);
        }
    }
}
