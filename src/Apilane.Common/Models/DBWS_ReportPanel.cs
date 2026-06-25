using Apilane.Common.Attributes;
using Apilane.Common.Enums;
using Apilane.Common.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    /// <summary>
    /// A dashboard report panel: a freely placed/resizable grid tile that overlays one or more
    /// series (query targets) sharing this panel's entity, group-by (x-axis) and max records.
    /// Mapped to a dedicated table so it is created fresh by the schema bootstrapper.
    /// </summary>
    public class DBWS_ReportPanel : DBWS_MainModel
    {
        public long AppID { get; set; }

        [JsonIgnore]
        public DBWS_Application Application { get; set; } = null!;

        public int TypeID { get; set; }

        [JsonIgnore]
        public ReportType TypeID_Enum { get { return (ReportType)TypeID; } }

        // Dashboard grid geometry (Gridstack units: 12-column grid, row-based height).
        public int X { get; set; }

        public int Y { get; set; }

        public int W { get; set; }

        public int H { get; set; }

        [AttrRequired]
        [Display(Name = "Title")]
        public string Title { get; set; } = null!;

        [AttrRequired]
        [Range(1, 1000)]
        public int MaxRecords { get; set; }

        /// <summary>
        /// Optional relative time window applied to date-grouped series, encoded as a number plus a
        /// unit: h (hours), d (days), m (months), y (years) — e.g. "24h", "7d", "6m", "2y".
        /// Null/empty means no window — series use the top-N (MaxRecords) behavior.
        /// </summary>
        public string? TimeRange { get; set; }

        private static bool TryParseTimeRange(string? timeRange, out int amount, out char unit)
        {
            amount = 0;
            unit = '\0';

            if (string.IsNullOrWhiteSpace(timeRange) || timeRange.Length < 2)
            {
                return false;
            }

            timeRange = timeRange.Trim().ToLowerInvariant();
            unit = timeRange[timeRange.Length - 1];

            return (unit == 'h' || unit == 'd' || unit == 'm' || unit == 'y')
                && int.TryParse(timeRange.Substring(0, timeRange.Length - 1), out amount)
                && amount > 0;
        }

        // One or more series (query targets) overlaid in this panel. Each series carries its own
        // entity and group-by; they share this panel's MaxRecords and are aligned on the x-axis by value.
        public List<DBWS_ReportSeries> Series { get; set; } = new();

        /// <summary>
        /// Resolves the relative <see cref="TimeRange"/> to an absolute [start, end] window in unix
        /// milliseconds (end = now). Returns null when no time range is set.
        /// </summary>
        public static (long StartMs, long EndMs)? GetTimeWindowMs(string? timeRange)
        {
            if (!TryParseTimeRange(timeRange, out var amount, out var unit))
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var start = unit switch
            {
                'h' => now.AddHours(-amount),
                'd' => now.AddDays(-amount),
                'm' => now.AddMonths(-amount),
                'y' => now.AddYears(-amount),
                _ => now
            };

            return (Utils.GetUnixTimestampMilliseconds(start), Utils.GetUnixTimestampMilliseconds(now));
        }

        /// <summary>
        /// Friendly label for a <see cref="TimeRange"/> code (e.g. "Last 30 days"), or null when no range is set.
        /// </summary>
        public static string? GetTimeRangeDisplay(string? timeRange)
        {
            if (!TryParseTimeRange(timeRange, out var amount, out var unit))
            {
                return null;
            }

            var word = unit switch
            {
                'h' => "hour",
                'd' => "day",
                'm' => "month",
                'y' => "year",
                _ => null
            };

            return word is null ? null : $"Last {amount} {word}{(amount == 1 ? string.Empty : "s")}";
        }
    }
}
