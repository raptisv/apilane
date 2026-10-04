using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// A report of an application: one panel of its dashboard, a table or a chart drawn from one
    /// or more series. In this API it is identified by its numeric ID. The numbers themselves are
    /// not part of it: each series is one call to the API server (see ReportSeriesResponse).
    /// </summary>
    public class ReportResponse
    {
        [Required]
        public long ID { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Grid (a table), Pie, Line, Bar, Radar or StackedBar. A pie shows the first series only.
        /// A stored type that is none of these comes as its stored number.
        /// </summary>
        [Required]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The column the panel starts at on the 12-column dashboard grid, 0 to 11.
        /// </summary>
        [Required]
        public int X { get; set; }

        /// <summary>
        /// The row the panel starts at, 0 being the top.
        /// </summary>
        [Required]
        public int Y { get; set; }

        /// <summary>
        /// How many of the 12 columns the panel covers.
        /// </summary>
        [Required]
        public int Width { get; set; }

        /// <summary>
        /// How many rows the panel covers.
        /// </summary>
        [Required]
        public int Height { get; set; }

        /// <summary>
        /// The most points one series shows ('Top N'): the page size of its call. 1 to 1000.
        /// </summary>
        [Required]
        public int MaxRecords { get; set; }

        /// <summary>
        /// The time window of the series that are grouped by a date first, as a number and a unit:
        /// h (hours), d (days), m (months) or y (years), such as 24h, 7d, 6m or 2y. Null when the
        /// report has none.
        /// </summary>
        public string? TimeRange { get; set; }

        /// <summary>
        /// TimeRange in words, such as 'Last 7 days'. Null when the report has no time range, and
        /// when the stored one cannot be read: no series is windowed then.
        /// </summary>
        public string? TimeRangeLabel { get; set; }

        /// <summary>
        /// When the report was created through this API or last changed (UTC). A report created by
        /// the older portal pages and never changed holds 0001-01-01T00:00:00. Moving or resizing
        /// the panel does not change it.
        /// </summary>
        [Required]
        public DateTime DateModified { get; set; }

        /// <summary>
        /// The series, in the order they are drawn.
        /// </summary>
        [Required]
        public List<ReportSeriesResponse> Series { get; set; } = new List<ReportSeriesResponse>();
    }

    /// <summary>
    /// One series of a report: one query, with what is needed to run it and to read its answer.
    /// The caller runs it on the API server with the token of GET /api/v1/session/api-token:
    /// GET {ServerUrl}/api/Stats/Aggregate?Entity={Entity}&amp;Properties={Property}&amp;GroupBy={GroupBy}&amp;Filter={Filter}&amp;PageIndex=1&amp;PageSize={MaxRecords of the report}.
    /// When Windowed is true the filter to send is {"Logic":"AND","Filters":[...]} holding the
    /// stored filter (when there is one), {"Property":WindowProperty,"Operator":"greaterorequal","Value":start}
    /// and {"Property":WindowProperty,"Operator":"lessorequal","Value":now}, both in Unix
    /// milliseconds, start being now minus the TimeRange of the report.
    /// Each row of the answer has one value per column named in Groups and the one named in
    /// Aggregate; compare column names ignoring letter case.
    /// </summary>
    public class ReportSeriesResponse
    {
        /// <summary>
        /// The name of the series in the legend.
        /// </summary>
        [Required]
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// The entity the series reads, as stored.
        /// </summary>
        [Required]
        public string Entity { get; set; } = string.Empty;

        /// <summary>
        /// What the records are grouped by, as stored: names from the Groupings of report-fields
        /// joined with commas, such as 'Created.Year,Created.Month'.
        /// </summary>
        [Required]
        public string GroupBy { get; set; } = string.Empty;

        /// <summary>
        /// The value of each group, as stored: a property and an aggregate, such as 'ID.Count'.
        /// </summary>
        [Required]
        public string Property { get; set; } = string.Empty;

        /// <summary>
        /// The filter of the series, exactly as stored: the JSON text the data API takes as its
        /// Filter. Null when the series has none.
        /// </summary>
        public string? Filter { get; set; }

        /// <summary>
        /// Property, resolved against the entity. Null when Error is set.
        /// </summary>
        public ReportSeriesAggregateResponse? Aggregate { get; set; }

        /// <summary>
        /// GroupBy, resolved against the entity, in the same order. Empty when Error is set.
        /// </summary>
        [Required]
        public List<ReportSeriesGroupResponse> Groups { get; set; } = new List<ReportSeriesGroupResponse>();

        /// <summary>
        /// True when the time range of the report applies to this series: the report has one and
        /// the first group is a Date property. Such a series shows the time window; every other
        /// series shows its top MaxRecords groups.
        /// </summary>
        [Required]
        public bool Windowed { get; set; }

        /// <summary>
        /// The Date property the time window is put on. Null unless Windowed is true.
        /// </summary>
        public string? WindowProperty { get; set; }

        /// <summary>
        /// Why the series cannot be run: its entity or one of its properties no longer exists, or
        /// its stored text cannot be read. Null for a series that can be run. The other series of
        /// the report are not affected.
        /// </summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// The value a series computes for each group.
    /// </summary>
    public class ReportSeriesAggregateResponse
    {
        /// <summary>
        /// The name of the property, as the entity has it.
        /// </summary>
        [Required]
        public string Property { get; set; } = string.Empty;

        /// <summary>
        /// Count, Min, Max, Sum or Avg.
        /// </summary>
        [Required]
        public string Function { get; set; } = string.Empty;

        /// <summary>
        /// The column of the answer that holds the value, such as 'ID_count'.
        /// </summary>
        [Required]
        public string Column { get; set; } = string.Empty;
    }

    /// <summary>
    /// One thing a series is grouped by.
    /// </summary>
    public class ReportSeriesGroupResponse
    {
        /// <summary>
        /// The name of the property, as the entity has it.
        /// </summary>
        [Required]
        public string Property { get; set; } = string.Empty;

        /// <summary>
        /// The type of the property: String, Number, Boolean or Date.
        /// </summary>
        [Required]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The part of a date the records are grouped by: year, month, day, hour, minute or
        /// second. Null when they are grouped by the value itself.
        /// </summary>
        public string? Part { get; set; }

        /// <summary>
        /// The column of the answer that holds the group value, such as 'Created_year' or 'Status'.
        /// </summary>
        [Required]
        public string Column { get; set; } = string.Empty;
    }

    /// <summary>
    /// The values of a report, for creating one and for changing one. Where the panel sits on the
    /// dashboard is not part of it (PUT reports/layout).
    /// </summary>
    public class ReportRequest
    {
        /// <summary>
        /// The title of the panel. Stored as sent.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Grid, Pie, Line, Bar, Radar or StackedBar.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// The most points one series shows, 1 to 1000.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [Range(1, 1000, ErrorMessage = "Must be between 1 and 1000")]
        public int? MaxRecords { get; set; }

        /// <summary>
        /// A number from 1 to 1000 followed by h (hours), d (days), m (months) or y (years), such
        /// as 24h, 7d, 6m or 2y. Empty or left out for no time range. It applies only to series
        /// whose first group is a Date property.
        /// </summary>
        public string? TimeRange { get; set; }

        /// <summary>
        /// The complete list of series, at least one, in the order they are drawn. It replaces
        /// the stored list.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public List<ReportSeriesRequest>? Series { get; set; }
    }

    /// <summary>
    /// One series of a report. What Property and GroupBy may hold depends on the entity and on
    /// the type of the report: GET entities/{entity}/report-fields?Type= lists it. Spaces before
    /// and after each value are removed.
    /// </summary>
    public class ReportSeriesRequest
    {
        /// <summary>
        /// The name of the series in the legend.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// The name of an entity of the application (case-sensitive).
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Entity { get; set; } = string.Empty;

        /// <summary>
        /// One or more of the Groupings of report-fields, joined with commas and without spaces:
        /// Name for a grouping without Subs, Name.Sub for one with them, such as
        /// 'Created.Year,Created.Month'.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string GroupBy { get; set; } = string.Empty;

        /// <summary>
        /// One of the Properties of report-fields as Name.Sub, such as 'ID.Count'.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Property { get; set; } = string.Empty;

        /// <summary>
        /// The filter of the series: the JSON text the data API takes as its Filter, such as
        /// {"Logic":"AND","Filters":[{"Property":"Code","Operator":"equal","Value":"A1"}]}.
        /// Stored as sent. Empty or left out for no filter.
        /// </summary>
        public string? Filter { get; set; }
    }

    /// <summary>
    /// Where panels sit on the dashboard.
    /// </summary>
    public class ReportLayoutRequest
    {
        /// <summary>
        /// The panels that moved or changed size, each report once. Reports left out stay where
        /// they are; an empty list changes nothing.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public List<ReportLayoutItem>? Items { get; set; }
    }

    /// <summary>
    /// The place and size of one panel on the 12-column dashboard grid. X + Width is 12 at most.
    /// </summary>
    public class ReportLayoutItem
    {
        /// <summary>
        /// The ID of a report of the application.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public long? ID { get; set; }

        /// <summary>
        /// The column the panel starts at, 0 to 11.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [Range(0, 11, ErrorMessage = "Must be between 0 and 11")]
        public int? X { get; set; }

        /// <summary>
        /// The row the panel starts at, 0 to 10000.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [Range(0, 10000, ErrorMessage = "Must be between 0 and 10000")]
        public int? Y { get; set; }

        /// <summary>
        /// How many columns the panel covers, 1 to 12.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [Range(1, 12, ErrorMessage = "Must be between 1 and 12")]
        public int? Width { get; set; }

        /// <summary>
        /// How many rows the panel covers, 1 to 100.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [Range(1, 100, ErrorMessage = "Must be between 1 and 100")]
        public int? Height { get; set; }
    }

    /// <summary>
    /// What a series of a report of one type can show for one entity.
    /// </summary>
    public class ReportFieldsResponse
    {
        /// <summary>
        /// The values a series can show, each sent as Name.Sub ('Amount.Sum'). The primary key
        /// with Count comes first.
        /// </summary>
        [Required]
        public List<ReportFieldResponse> Properties { get; set; } = new List<ReportFieldResponse>();

        /// <summary>
        /// What a series can be grouped by: sent as Name when Subs is empty, as Name.Sub
        /// ('Created.Year') when it is not.
        /// </summary>
        [Required]
        public List<ReportFieldResponse> Groupings { get; set; } = new List<ReportFieldResponse>();
    }

    /// <summary>
    /// A property of the entity and what can follow its name.
    /// </summary>
    public class ReportFieldResponse
    {
        /// <summary>
        /// The name of the property.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// In Properties the aggregates: Count, Max, Min, Sum, Avg. In Groupings the parts of a
        /// date (Year, Month, Day, Hour, Minute, Second), or nothing for a property that is
        /// grouped by its value.
        /// </summary>
        [Required]
        public List<string> Subs { get; set; } = new List<string>();
    }
}
