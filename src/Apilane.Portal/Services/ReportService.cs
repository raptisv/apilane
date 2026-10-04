using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ReportService : IReportService
    {
        private const string EntityName = "Report";

        // The dashboard grid, and the panel a new report gets: half the width, below the others.
        private const int GridColumns = 12;
        private const int MaxRow = 10000;
        private const int NewPanelWidth = 6;
        private const int NewPanelHeight = 4;

        private const string TimeRangeMessage = "Must be a number from 1 to 1000 followed by h, d, m or y, such as 7d";

        // 1 to 1000 and a unit. The limit keeps every date the window is computed from in range.
        private static readonly Regex _timeRangePattern = new Regex("^([1-9][0-9]{0,2}|1000)[hdmy]$", RegexOptions.Compiled);

        private static readonly List<string> _countOnly = new List<string> { nameof(AggregateData.DataAggregates.Count) };

        private static readonly List<string> _maxMin = new List<string>
        {
            nameof(AggregateData.DataAggregates.Max),
            nameof(AggregateData.DataAggregates.Min)
        };

        private static readonly List<string> _maxMinSumAvg = new List<string>
        {
            nameof(AggregateData.DataAggregates.Max),
            nameof(AggregateData.DataAggregates.Min),
            nameof(AggregateData.DataAggregates.Sum),
            nameof(AggregateData.DataAggregates.Avg)
        };

        private static readonly List<string> _dateParts = new List<string> { "Year", "Month", "Day", "Hour", "Minute", "Second" };

        private readonly ApplicationDbContext _dbContext;
        private readonly IApplicationAccessService _applicationAccessService;

        public ReportService(
            ApplicationDbContext dbContext,
            IApplicationAccessService applicationAccessService)
        {
            _dbContext = dbContext;
            _applicationAccessService = applicationAccessService;
        }

        public async Task<List<ReportResponse>> GetAllAsync(string appToken)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            var reports = await _dbContext.Reports
                .Include(x => x.Series)
                .Where(x => x.AppID == application.ID)
                .ToListAsync();

            // The order of the dashboard: top to bottom, then left to right.
            return reports
                .OrderBy(x => x.Y)
                .ThenBy(x => x.X)
                .ThenBy(x => x.ID)
                .Select(x => ToResponse(x, application))
                .ToList();
        }

        public async Task<ReportResponse> GetAsync(string appToken, long reportId)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);

            return ToResponse(await FindAsync(application, reportId), application);
        }

        public async Task<ReportResponse> CreateAsync(string appToken, ReportRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var values = Validate(application, request);

            var panels = await _dbContext.Reports
                .Where(x => x.AppID == application.ID)
                .Select(x => new { x.Y, x.H })
                .ToListAsync();

            var report = new DBWS_ReportPanel
            {
                AppID = application.ID,
                TypeID = (int)values.Type,
                Title = request.Title,
                MaxRecords = values.MaxRecords,
                TimeRange = values.TimeRange,
                X = 0,
                // Summed as long and kept on the grid: rows saved by older versions have no bounds.
                Y = (int)Math.Clamp(panels.Select(x => (long)x.Y + x.H).DefaultIfEmpty(0).Max(), 0, MaxRow),
                W = NewPanelWidth,
                H = NewPanelHeight,
                DateModified = DateTime.UtcNow,
                Series = values.Series
            };

            _dbContext.Reports.Add(report);

            // Reports live in the Portal only: no API server call and no cache reset.
            await _dbContext.SaveChangesAsync();

            return ToResponse(report, application);
        }

        public async Task<ReportResponse> UpdateAsync(string appToken, long reportId, ReportRequest request)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var report = await FindAsync(application, reportId);
            var values = Validate(application, request);

            // The place of the panel stays.
            report.TypeID = (int)values.Type;
            report.Title = request.Title;
            report.MaxRecords = values.MaxRecords;
            report.TimeRange = values.TimeRange;
            report.DateModified = DateTime.UtcNow;

            // The series are replaced as a whole, so they get new IDs.
            _dbContext.ReportSeries.RemoveRange(report.Series);
            report.Series = values.Series;

            await _dbContext.SaveChangesAsync();

            return ToResponse(report, application);
        }

        public async Task DeleteAsync(string appToken, long reportId)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            // The series go with it (cascade).
            _dbContext.Reports.Remove(await FindAsync(application, reportId));

            await _dbContext.SaveChangesAsync();
        }

        public async Task SaveLayoutAsync(string appToken, ReportLayoutRequest request)
        {
            var application = await _applicationAccessService.GetApplicationAsync(appToken);

            // [Required] on the contract already answers 400 for a missing list.
            var items = request.Items
                ?? throw PortalException.Validation(nameof(ReportLayoutRequest.Items), "Required");

            var reports = await _dbContext.Reports
                .Where(x => x.AppID == application.ID)
                .ToListAsync();

            var errors = new List<ErrorDetail>();
            var places = new List<(DBWS_ReportPanel Report, int X, int Y, int Width, int Height)>();

            for (var i = 0; i < items.Count; i++)
            {
                var path = $"{nameof(ReportLayoutRequest.Items)}[{i}]";
                var item = items[i];

                if (item is null)
                {
                    errors.Add(Error(path, "Required"));
                    continue;
                }

                // [Required] and [Range] on the contract already answer 400 for a missing value and
                // for one outside the grid.
                if (item.ID is not long id || item.X is not int x || item.Y is not int y || item.Width is not int width || item.Height is not int height)
                {
                    errors.Add(Error(path, "ID, X, Y, Width and Height are required"));
                    continue;
                }

                // Inside the route application only, so an ID of another application is not found.
                var report = reports.FirstOrDefault(r => r.ID == id);

                if (report is null)
                {
                    errors.Add(Error($"{path}.{nameof(ReportLayoutItem.ID)}", "Not a report of this application"));
                }
                else if (places.Any(p => p.Report == report))
                {
                    errors.Add(Error($"{path}.{nameof(ReportLayoutItem.ID)}", "A report can be listed only once"));
                }
                else
                {
                    places.Add((report, x, y, width, height));
                }

                // The one rule that needs two of the values.
                if ((long)x + width > GridColumns)
                {
                    errors.Add(Error($"{path}.{nameof(ReportLayoutItem.Width)}", $"X + Width must be {GridColumns} or less"));
                }
            }

            if (errors.Count > 0)
            {
                throw PortalException.Validation(errors);
            }

            foreach (var place in places)
            {
                place.Report.X = place.X;
                place.Report.Y = place.Y;
                place.Report.W = place.Width;
                place.Report.H = place.Height;
            }

            // A panel that did not move is not changed, so it gets no audit row. DateModified stays.
            await _dbContext.SaveChangesAsync();
        }

        public async Task<ReportFieldsResponse> GetFieldsAsync(string appToken, string entityName, string? type)
        {
            var application = await _applicationAccessService.GetApplicationWithEntitiesAsync(appToken);
            var entity = _applicationAccessService.GetEntity(application, entityName);

            if (!TryParseType(type, out var reportType))
            {
                throw PortalException.Validation("Type", TypeMessage());
            }

            return GetFields(entity, reportType);
        }

        // Inside the route application only, so an ID of another application is not found.
        private async Task<DBWS_ReportPanel> FindAsync(DBWS_Application application, long reportId)
        {
            return await _dbContext.Reports
                .Include(x => x.Series)
                .FirstOrDefaultAsync(x => x.AppID == application.ID && x.ID == reportId)
                ?? throw PortalException.NotFound(EntityName);
        }

        /// <summary>
        /// The rules of a report: a known type, a time range that can be read, and for every
        /// series an entity that exists, a filter that can be read, and a property and group-bys
        /// that report-fields offers for the entity and the type. Every problem is reported,
        /// each with the path of its value.
        /// </summary>
        private static (ReportType Type, int MaxRecords, string? TimeRange, List<DBWS_ReportSeries> Series) Validate(DBWS_Application application, ReportRequest request)
        {
            var errors = new List<ErrorDetail>();

            // [Required] and [Range] on the contract already answer 400 for a missing or other value.
            var maxRecords = request.MaxRecords
                ?? throw PortalException.Validation(nameof(ReportRequest.MaxRecords), "Required");

            var typeIsKnown = TryParseType(request.Type, out var type);

            if (!typeIsKnown)
            {
                errors.Add(Error(nameof(ReportRequest.Type), TypeMessage()));
            }

            // Stored as the code, or null for none.
            var timeRange = string.IsNullOrWhiteSpace(request.TimeRange) ? null : request.TimeRange.Trim();

            if (timeRange is not null && !_timeRangePattern.IsMatch(timeRange))
            {
                errors.Add(Error(nameof(ReportRequest.TimeRange), TimeRangeMessage));
            }

            // [Required] on the contract already answers 400 for a missing list.
            var requested = request.Series
                ?? throw PortalException.Validation(nameof(ReportRequest.Series), "Required");

            if (requested.Count == 0)
            {
                errors.Add(Error(nameof(ReportRequest.Series), "Add at least one series"));
            }

            var series = new List<DBWS_ReportSeries>();

            for (var i = 0; i < requested.Count; i++)
            {
                var path = $"{nameof(ReportRequest.Series)}[{i}]";
                var item = requested[i];

                if (item is null)
                {
                    errors.Add(Error(path, "Required"));
                    continue;
                }

                // The stored row: the four texts trimmed, an empty filter as null,
                // the position as the order.
                var row = new DBWS_ReportSeries
                {
                    Label = Utils.GetString(item.Label),
                    Entity = Utils.GetString(item.Entity),
                    GroupBy = Utils.GetString(item.GroupBy),
                    Property = Utils.GetString(item.Property),
                    Filter = string.IsNullOrWhiteSpace(item.Filter) ? null : item.Filter,
                    Order = i,
                    DateModified = DateTime.UtcNow
                };

                series.Add(row);

                var entity = application.Entities.FirstOrDefault(x => string.Equals(x.Name, row.Entity, StringComparison.Ordinal));

                if (entity is null)
                {
                    errors.Add(Error($"{path}.{nameof(ReportSeriesRequest.Entity)}", $"Unknown entity '{row.Entity}'."));
                }
                else if (typeIsKnown)
                {
                    // What can be chosen depends on the type, so it is not checked without one.
                    var fields = GetFields(entity, type);

                    if (!NamesOf(fields.Properties).Contains(row.Property))
                    {
                        errors.Add(Error(
                            $"{path}.{nameof(ReportSeriesRequest.Property)}",
                            $"'{row.Property}' is not one of the Properties of report-fields for entity '{entity.Name}' and type {type}"));
                    }

                    var groupings = NamesOf(fields.Groupings);
                    var groups = row.GroupBy.Split(',');
                    var unknownGroup = groups.FirstOrDefault(x => !groupings.Contains(x));

                    if (unknownGroup is not null)
                    {
                        errors.Add(Error(
                            $"{path}.{nameof(ReportSeriesRequest.GroupBy)}",
                            $"'{unknownGroup}' is not one of the Groupings of report-fields for entity '{entity.Name}' and type {type}"));
                    }
                    else if (groups.Distinct().Count() != groups.Length)
                    {
                        // The API server runs a repeated grouping once; Groups would name its column twice.
                        errors.Add(Error($"{path}.{nameof(ReportSeriesRequest.GroupBy)}", "A grouping can be listed only once"));
                    }
                }

                if (!IsReadableFilter(row.Filter))
                {
                    errors.Add(Error($"{path}.{nameof(ReportSeriesRequest.Filter)}", "Not a filter the data API can read"));
                }
            }

            if (errors.Count > 0)
            {
                throw PortalException.Validation(errors);
            }

            return (type, maxRecords, timeRange, series);
        }

        // The text must read as a filter of the data API.
        private static bool IsReadableFilter(string? filter)
        {
            if (filter is null)
            {
                return true;
            }

            try
            {
                return FilterData.Parse(filter) is not null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// What the report editor offers for an entity and a type. The primary key can be
        /// counted. A Number has every aggregate and can be grouped by. A String and a Date have
        /// Max and Min in a Grid only. A Line groups by numbers and dates only; every other type
        /// by any property. A Date is grouped by its parts.
        /// </summary>
        private static ReportFieldsResponse GetFields(DBWS_Entity entity, ReportType reportType)
        {
            var result = new ReportFieldsResponse();
            var properties = entity.Properties.OrderBy(x => x.ID).ToList();
            var primaryKey = properties.FirstOrDefault(x => x.IsPrimaryKey);

            if (primaryKey is not null)
            {
                result.Properties.Add(Field(primaryKey.Name, _countOnly));
            }

            foreach (var property in properties.Where(x => !x.IsPrimaryKey))
            {
                var type = property.TypeID_Enum;
                var isText = type == PropertyType.String;
                var isNumber = type == PropertyType.Number;
                var isDate = type == PropertyType.Date;

                if (isNumber)
                {
                    result.Properties.Add(Field(property.Name, _maxMinSumAvg));
                }
                else if (reportType == ReportType.Grid && (isText || isDate))
                {
                    result.Properties.Add(Field(property.Name, _maxMin));
                }

                var canGroup = reportType == ReportType.Line
                    ? isNumber || isDate
                    : isText || isNumber || isDate || type == PropertyType.Boolean;

                if (canGroup)
                {
                    result.Groupings.Add(Field(property.Name, isDate ? _dateParts : new List<string>()));
                }
            }

            return result;
        }

        private static ReportFieldResponse Field(string name, List<string> subs)
        {
            return new ReportFieldResponse { Name = name, Subs = subs.ToList() };
        }

        // The texts a series may hold: Name, or Name.Sub for a field that has subs.
        private static List<string> NamesOf(List<ReportFieldResponse> fields)
        {
            return fields
                .SelectMany(x => x.Subs.Count == 0 ? new[] { x.Name } : x.Subs.Select(sub => $"{x.Name}.{sub}"))
                .ToList();
        }

        private static ReportResponse ToResponse(DBWS_ReportPanel report, DBWS_Application application)
        {
            // Null also for a stored range that cannot be read: the report then has no time window.
            var timeRangeLabel = DBWS_ReportPanel.GetTimeRangeDisplay(report.TimeRange);

            return new ReportResponse
            {
                ID = report.ID,
                Title = report.Title,
                Type = report.TypeID_Enum.ToString(),
                X = report.X,
                Y = report.Y,
                Width = report.W,
                Height = report.H,
                MaxRecords = report.MaxRecords,
                TimeRange = report.TimeRange,
                TimeRangeLabel = timeRangeLabel,
                // Read back from the database it has no kind; it is always stored as UTC.
                DateModified = DateTime.SpecifyKind(report.DateModified, DateTimeKind.Utc),
                Series = report.Series
                    .OrderBy(x => x.Order)
                    .ThenBy(x => x.ID)
                    .Select(x => ToResponse(x, application, hasTimeRange: timeRangeLabel is not null))
                    .ToList()
            };
        }

        /// <summary>
        /// The series as stored, and what its texts mean for the entity as it is now. Reading is
        /// tolerant: a series whose entity or property is gone gets an Error and the rest is answered.
        /// Names are matched whatever their letter case, as the API server matches them.
        /// </summary>
        private static ReportSeriesResponse ToResponse(DBWS_ReportSeries series, DBWS_Application application, bool hasTimeRange)
        {
            var response = new ReportSeriesResponse
            {
                Label = series.Label,
                Entity = series.Entity,
                GroupBy = series.GroupBy,
                Property = series.Property,
                Filter = series.Filter
            };

            var entity = application.Entities.FirstOrDefault(x => string.Equals(x.Name, series.Entity, StringComparison.OrdinalIgnoreCase));

            if (entity is null)
            {
                response.Error = $"Entity '{series.Entity}' does not exist";
                return response;
            }

            var propertyParts = series.Property.Split('.');

            if (propertyParts.Length != 2)
            {
                response.Error = $"'{series.Property}' is not a property and an aggregate, such as ID.Count";
                return response;
            }

            var property = FindProperty(entity, propertyParts[0]);

            if (property is null)
            {
                response.Error = MissingProperty(entity, propertyParts[0]);
                return response;
            }

            var function = Enum.GetNames<AggregateData.DataAggregates>()
                .FirstOrDefault(x => string.Equals(x, propertyParts[1].Trim(), StringComparison.OrdinalIgnoreCase));

            if (function is null)
            {
                response.Error = $"'{propertyParts[1]}' is not an aggregate";
                return response;
            }

            var groups = new List<ReportSeriesGroupResponse>();

            foreach (var group in series.GroupBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var groupParts = group.Split('.');
                var groupProperty = FindProperty(entity, groupParts[0]);

                if (groupProperty is null)
                {
                    response.Error = MissingProperty(entity, groupParts[0]);
                    return response;
                }

                // The same reading as the API server: no part, or one of the six parts of a date.
                if (groupParts.Length > 2 || !GroupData.TryParseType(groupParts.Length == 2 ? groupParts[1] : string.Empty, out var groupType))
                {
                    response.Error = $"'{group}' is not something records can be grouped by";
                    return response;
                }

                groups.Add(new ReportSeriesGroupResponse
                {
                    Property = groupProperty.Name,
                    Type = groupProperty.TypeID_Enum.ToString(),
                    Part = groupType == GroupData.GroupByType.None ? null : GroupData.GetTypeSuffix(groupType),
                    Column = GroupData.GetAlias(groupProperty.Name, groupType)
                });
            }

            // The column names the API server gives its answer.
            response.Aggregate = new ReportSeriesAggregateResponse
            {
                Property = property.Name,
                Function = function,
                Column = $"{property.Name}_{function.ToLowerInvariant()}"
            };

            response.Groups = groups;

            // The time range is for series grouped by a date first; every other series keeps its top N.
            response.Windowed = hasTimeRange && groups.Count > 0 && groups[0].Type == nameof(PropertyType.Date);
            response.WindowProperty = response.Windowed ? groups[0].Property : null;

            return response;
        }

        private static DBWS_EntityProperty? FindProperty(DBWS_Entity entity, string name)
        {
            return entity.Properties.FirstOrDefault(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static string MissingProperty(DBWS_Entity entity, string name)
        {
            return $"Property '{name.Trim()}' of entity '{entity.Name}' does not exist";
        }

        private static bool TryParseType(string? value, out ReportType type)
        {
            type = default;

            // By name only: Enum.TryParse would take a number too.
            return value is not null && Enum.GetNames<ReportType>().Contains(value) && Enum.TryParse(value, out type);
        }

        private static string TypeMessage()
        {
            return $"Must be one of: {string.Join(", ", Enum.GetNames<ReportType>())}";
        }

        private static ErrorDetail Error(string property, string message)
        {
            return new ErrorDetail { Property = property, Message = message };
        }
    }
}
