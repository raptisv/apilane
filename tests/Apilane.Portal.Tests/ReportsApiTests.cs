using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Models;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// The reports of an application and the report-fields of an entity. The entities are the
    /// ones of <see cref="EntityScene"/>: 'Orders' has a property of every type.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class ReportsApiTests
    {
        private const string Omit = "<omit>";
        private const string TypeMessage = "Must be one of: Grid, Pie, Line, Bar, Radar, StackedBar";
        private const string TimeRangeMessage = "Must be a number from 1 to 1000 followed by h, d, m or y, such as 7d";
        private const string OrdersFilter = "{\"Logic\":\"AND\",\"Filters\":[{\"Property\":\"Code\",\"Operator\":\"equal\",\"Value\":\"A 1\"}]}";

        private static readonly DateTime _seeded = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        private readonly PortalFactory _portal;

        public ReportsApiTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        // ---------- List ----------

        [Fact]
        public async Task List_Should_Return_The_Reports_Of_The_Application_In_Dashboard_Order()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // Seeded bottom first: the answer is by row, then by column, not by ID.
            var bottom = await SeedAsync(scene.AppId, "Bottom", ReportType.Grid, 0, 8, 12, 3, null, 1000, Series("Rows", "Customers", "Name", "ID.Count"));
            var topRight = await SeedAsync(scene.AppId, "Top right", ReportType.Pie, 6, 0, 6, 4, "7d", 5, Series("Paid", "Orders", "Paid", "Amount.Sum"));
            var topLeft = await SeedAsync(scene.AppId, "Top left", ReportType.Line, 0, 0, 6, 4, "24h", 20,
                Series("Second", "Orders", "Created.Year,Created.Month", "Amount.Avg", OrdersFilter, order: 1),
                Series("First", "Orders", "Created.Year,Created.Month", "ID.Count", order: 0));

            // A report of another application of the same owner is not listed.
            var other = await _portal.CreateApplicationAsync((await _portal.CreateServerAsync()).ID, scene.OwnerEmail, "other");
            await SeedAsync(other.Application.ID, "Elsewhere", ReportType.Bar, 0, 0, 6, 4, null, 10, Series("X", "Orders", "Code", "ID.Count"));

            var response = await scene.Owner.GetAsync(ReportsUrl(scene));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var list = await response.ReadJsonAsync<ListResponse<ReportResponse>>();

            Assert.Equal(new[] { "Top left", "Top right", "Bottom" }, list.Data.Select(x => x.Title));
            Assert.Equal(new[] { topLeft.ID, topRight.ID, bottom.ID }, list.Data.Select(x => x.ID));
            Assert.Equal(3, list.Total);

            var first = list.Data[0];
            Assert.Equal("Line", first.Type);
            Assert.Equal("0,0,6,4", $"{first.X},{first.Y},{first.Width},{first.Height}");
            Assert.Equal(20, first.MaxRecords);
            Assert.Equal("24h", first.TimeRange);
            Assert.Equal("Last 24 hours", first.TimeRangeLabel);
            Assert.Equal(_seeded, first.DateModified);

            // The series come in their stored order, with their stored texts.
            Assert.Equal(new[] { "First", "Second" }, first.Series.Select(x => x.Label));
            Assert.Null(first.Series[0].Filter);
            Assert.Equal(OrdersFilter, first.Series[1].Filter);
            Assert.Equal("Orders | Created.Year,Created.Month | Amount.Avg", $"{first.Series[1].Entity} | {first.Series[1].GroupBy} | {first.Series[1].Property}");

            Assert.Equal("6,0,6,4", $"{list.Data[1].X},{list.Data[1].Y},{list.Data[1].Width},{list.Data[1].Height}");
            Assert.Equal("0,8,12,3", $"{list.Data[2].X},{list.Data[2].Y},{list.Data[2].Width},{list.Data[2].Height}");
            Assert.Null(list.Data[2].TimeRange);
            Assert.Null(list.Data[2].TimeRangeLabel);

            // Reading does not involve the API server.
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task List_Of_An_Application_Without_Reports_Should_Be_Empty()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var list = await (await scene.Owner.GetAsync(ReportsUrl(scene))).ReadJsonAsync<ListResponse<ReportResponse>>();

            Assert.Empty(list.Data);
            Assert.Equal(0, list.Total);
        }

        [Theory]
        [InlineData(ReportType.Grid, "Grid")]
        [InlineData(ReportType.Pie, "Pie")]
        [InlineData(ReportType.Line, "Line")]
        [InlineData(ReportType.Bar, "Bar")]
        [InlineData(ReportType.Radar, "Radar")]
        [InlineData(ReportType.StackedBar, "StackedBar")]
        public async Task List_Should_Resolve_The_Series_Of_A_Report_Of_Each_Type(ReportType type, string expectedType)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            await SeedAsync(scene.AppId, "Resolved", type, 0, 0, 6, 4, null, 30,
                Series("Amounts", "Orders", "Created.Year,Created.Month,Paid", "Amount.Sum", OrdersFilter),
                Series("Customers", "Customers", "Name", "ID.Count"));

            var report = Assert.Single((await (await scene.Owner.GetAsync(ReportsUrl(scene))).ReadJsonAsync<ListResponse<ReportResponse>>()).Data);

            Assert.Equal(expectedType, report.Type);

            var amounts = report.Series[0];
            Assert.Null(amounts.Error);
            Assert.Equal("Amount | Sum | Amount_sum", Describe(amounts.Aggregate));
            Assert.Equal(
                new[] { "Created | Date | year | Created_year", "Created | Date | month | Created_month", "Paid | Boolean |  | Paid" },
                amounts.Groups.Select(Describe));

            var customers = report.Series[1];
            Assert.Null(customers.Error);
            Assert.Equal("ID | Count | ID_count", Describe(customers.Aggregate));
            Assert.Equal(new[] { "Name | String |  | Name" }, customers.Groups.Select(Describe));

            // No time range, so nothing is windowed.
            Assert.All(report.Series, x => Assert.False(x.Windowed));
            Assert.All(report.Series, x => Assert.Null(x.WindowProperty));
        }

        [Fact]
        public async Task A_Time_Range_Should_Window_Only_The_Series_Grouped_By_A_Date_First()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            await SeedAsync(scene.AppId, "Windowed", ReportType.Bar, 0, 0, 6, 4, "6m", 30,
                Series("By month", "Orders", "Created.Year,Created.Month", "ID.Count"),
                Series("By code", "Orders", "Code", "ID.Count"),
                Series("Date second", "Orders", "Code,Created.Year", "ID.Count"));

            var report = Assert.Single((await (await scene.Owner.GetAsync(ReportsUrl(scene))).ReadJsonAsync<ListResponse<ReportResponse>>()).Data);

            Assert.Equal("Last 6 months", report.TimeRangeLabel);
            Assert.Equal(new[] { true, false, false }, report.Series.Select(x => x.Windowed));
            Assert.Equal(new[] { "Created", null, null }, report.Series.Select(x => x.WindowProperty));
            Assert.All(report.Series, x => Assert.Null(x.Error));
        }

        [Theory]
        [InlineData("soon")]
        [InlineData("7w")]
        [InlineData("0d")]
        [InlineData("d")]
        public async Task A_Stored_Time_Range_That_Cannot_Be_Read_Should_Be_No_Time_Range(string stored)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            await SeedAsync(scene.AppId, "Odd", ReportType.Line, 0, 0, 6, 4, stored, 30, Series("By month", "Orders", "Created.Year", "ID.Count"));

            var response = await scene.Owner.GetAsync(ReportsUrl(scene));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var report = Assert.Single((await response.ReadJsonAsync<ListResponse<ReportResponse>>()).Data);

            // The stored text is shown as it is; the label says there is no window.
            Assert.Equal(stored, report.TimeRange);
            Assert.Null(report.TimeRangeLabel);
            Assert.False(report.Series[0].Windowed);
            Assert.Null(report.Series[0].Error);
        }

        [Theory]
        // A custom range has no upper limit.
        [InlineData("5000d", "Last 5000 days")]
        [InlineData("7D", "Last 7 days")]
        [InlineData(" 7d ", "Last 7 days")]
        public async Task A_Stored_Time_Range_That_Reads_But_Could_Not_Be_Saved_Here_Should_Still_Be_A_Time_Range(string stored, string expectedLabel)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            await SeedAsync(scene.AppId, "Odd", ReportType.Line, 0, 0, 6, 4, stored, 30, Series("By year", "Orders", "Created.Year", "ID.Count"));

            var report = Assert.Single((await (await scene.Owner.GetAsync(ReportsUrl(scene))).ReadJsonAsync<ListResponse<ReportResponse>>()).Data);

            Assert.Equal(stored, report.TimeRange);
            Assert.Equal(expectedLabel, report.TimeRangeLabel);
            Assert.True(report.Series[0].Windowed);
            Assert.Equal("Created", report.Series[0].WindowProperty);
        }

        [Fact]
        public async Task Stored_Texts_With_Spaces_And_An_Empty_Group_By_Should_Resolve_As_The_Call_Of_The_Dashboard_Runs_Them()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // The dashboard strips every space before it calls, and the API server drops an empty group-by.
            await SeedAsync(scene.AppId, "Spaced", ReportType.Grid, 0, 0, 6, 4, null, 30, Series("Spaced", "Orders", "Created.Year, Code,", "ID. Count"));

            var series = Assert.Single(Assert.Single((await (await scene.Owner.GetAsync(ReportsUrl(scene))).ReadJsonAsync<ListResponse<ReportResponse>>()).Data).Series);

            Assert.Null(series.Error);
            Assert.Equal("Created.Year, Code, | ID. Count", $"{series.GroupBy} | {series.Property}");
            Assert.Equal("ID | Count | ID_count", Describe(series.Aggregate));
            Assert.Equal(new[] { "Created | Date | year | Created_year", "Code | String |  | Code" }, series.Groups.Select(Describe));
        }

        [Fact]
        public async Task Stored_Names_In_Another_Letter_Case_Should_Resolve_As_The_Api_Server_Resolves_Them()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            await SeedAsync(scene.AppId, "Casing", ReportType.Line, 0, 0, 6, 4, "1y", 30, Series("Loose", "orders", "created.YEAR,code", "id.count"));

            var series = Assert.Single(Assert.Single((await (await scene.Owner.GetAsync(ReportsUrl(scene))).ReadJsonAsync<ListResponse<ReportResponse>>()).Data).Series);

            Assert.Null(series.Error);

            // The stored texts stay; the resolved names are the ones the entity has.
            Assert.Equal("orders | created.YEAR,code | id.count", $"{series.Entity} | {series.GroupBy} | {series.Property}");
            Assert.Equal("ID | Count | ID_count", Describe(series.Aggregate));
            Assert.Equal(new[] { "Created | Date | year | Created_year", "Code | String |  | Code" }, series.Groups.Select(Describe));
            Assert.True(series.Windowed);
            Assert.Equal("Created", series.WindowProperty);
        }

        [Fact]
        public async Task A_Series_Whose_Property_Was_Deleted_Should_Get_An_Error_And_The_List_Should_Still_Answer()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var report = await SeedAsync(scene.AppId, "Broken", ReportType.Line, 0, 0, 6, 4, "7d", 30,
                Series("Sum of amounts", "Orders", "Created.Year", "Amount.Sum"),
                Series("Grouped by amount", "Orders", "Created.Year,Amount", "ID.Count"),
                Series("Fine", "Orders", "Created.Year", "ID.Count"));

            // Both read fine while the property exists.
            var before = await (await scene.Owner.GetAsync(ReportUrl(scene, report.ID))).ReadJsonAsync<ReportResponse>();
            Assert.All(before.Series, x => Assert.Null(x.Error));

            var appId = scene.AppId;

            await _portal.WithDbContextAsync(async db =>
            {
                var orders = await db.Entities.Include(x => x.Properties).SingleAsync(x => x.AppID == appId && x.Name == "Orders");

                db.EntityProperties.Remove(orders.Properties.Single(x => x.Name == "Amount"));
                return await db.SaveChangesAsync();
            });

            var response = await scene.Owner.GetAsync(ReportsUrl(scene));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var series = Assert.Single((await response.ReadJsonAsync<ListResponse<ReportResponse>>()).Data).Series;

            Assert.Equal(
                new[] { "Property 'Amount' of entity 'Orders' does not exist", "Property 'Amount' of entity 'Orders' does not exist", null },
                series.Select(x => x.Error));

            // A series with an error has its stored texts and nothing resolved.
            Assert.Equal("Orders | Created.Year | Amount.Sum", $"{series[0].Entity} | {series[0].GroupBy} | {series[0].Property}");
            Assert.All(series.Take(2), x => Assert.Null(x.Aggregate));
            Assert.All(series.Take(2), x => Assert.Empty(x.Groups));
            Assert.All(series.Take(2), x => Assert.False(x.Windowed));
            Assert.All(series.Take(2), x => Assert.Null(x.WindowProperty));

            // The series next to them is not affected.
            Assert.Equal("ID | Count | ID_count", Describe(series[2].Aggregate));
            Assert.True(series[2].Windowed);

            // The single read answers too.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync(ReportUrl(scene, report.ID))).StatusCode);
        }

        [Theory]
        [InlineData("Nothing", "Created.Year", "ID.Count", "Entity 'Nothing' does not exist")]
        [InlineData("Orders", "Created.Year", "Gone.Sum", "Property 'Gone' of entity 'Orders' does not exist")]
        [InlineData("Orders", "Created.Year,Gone", "ID.Count", "Property 'Gone' of entity 'Orders' does not exist")]
        [InlineData("Orders", "Created.Year", "ID", "'ID' is not a property and an aggregate, such as ID.Count")]
        [InlineData("Orders", "Created.Year", "ID.Count,Amount.Sum", "'ID.Count,Amount.Sum' is not a property and an aggregate, such as ID.Count")]
        [InlineData("Orders", "Created.Year", "Amount.Median", "'Median' is not an aggregate")]
        [InlineData("Orders", "Created.Week", "ID.Count", "'Created.Week' is not something records can be grouped by")]
        [InlineData("Orders", "Created.Year.Month", "ID.Count", "'Created.Year.Month' is not something records can be grouped by")]
        public async Task A_Stored_Series_That_Cannot_Be_Run_Should_Get_An_Error_And_The_List_Should_Still_Answer(string entity, string groupBy, string property, string expected)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            await SeedAsync(scene.AppId, "Broken", ReportType.Grid, 0, 0, 6, 4, "7d", 30,
                Series("Broken", entity, groupBy, property),
                Series("Fine", "Orders", "Code", "ID.Count"));

            var response = await scene.Owner.GetAsync(ReportsUrl(scene));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var series = Assert.Single((await response.ReadJsonAsync<ListResponse<ReportResponse>>()).Data).Series;

            Assert.Equal(expected, series[0].Error);
            Assert.Null(series[0].Aggregate);
            Assert.Empty(series[0].Groups);
            Assert.False(series[0].Windowed);

            Assert.Null(series[1].Error);
            Assert.Equal("ID | Count | ID_count", Describe(series[1].Aggregate));
        }

        [Fact]
        public async Task A_Stored_Type_That_Does_Not_Exist_Should_Come_As_Its_Number()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // Older versions stored whatever number was posted.
            await SeedAsync(scene.AppId, "Odd type", (ReportType)9, 0, 0, 6, 4, null, 30, Series("Fine", "Orders", "Code", "ID.Count"));

            var response = await scene.Owner.GetAsync(ReportsUrl(scene));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("9", Assert.Single((await response.ReadJsonAsync<ListResponse<ReportResponse>>()).Data).Type);
        }

        // ---------- Get ----------

        [Fact]
        public async Task Get_Should_Return_The_Report_As_The_List_Does()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var report = await SeedAsync(scene.AppId, "One", ReportType.Radar, 3, 2, 5, 6, "2y", 12,
                Series("A", "Orders", "Created.Year", "Amount.Max", OrdersFilter),
                Series("B", "Nothing", "Code", "ID.Count"));

            var response = await scene.Owner.GetAsync(ReportUrl(scene, report.ID));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var one = await response.ReadJsonAsync<ReportResponse>();
            var listed = Assert.Single((await (await scene.Owner.GetAsync(ReportsUrl(scene))).ReadJsonAsync<ListResponse<ReportResponse>>()).Data);

            Assert.Equal(report.ID, one.ID);
            Assert.Equal("Radar", one.Type);
            Assert.Equal("3,2,5,6", $"{one.X},{one.Y},{one.Width},{one.Height}");
            Assert.Equal("Last 2 years", one.TimeRangeLabel);
            Assert.Equal("Entity 'Nothing' does not exist", one.Series[1].Error);
            Assert.Equal(JsonSerializer.Serialize(listed), JsonSerializer.Serialize(one));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task An_Id_Of_Another_Application_Or_Unknown_Should_Return_404_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await SeedAsync(scene.AppId, "Mine", ReportType.Line, 0, 0, 6, 4, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));

            // Another application of the same owner: the ID exists, but not under this token.
            var other = await _portal.CreateApplicationAsync((await _portal.CreateServerAsync()).ID, scene.OwnerEmail, "other");
            var foreign = await SeedAsync(other.Application.ID, "Elsewhere", ReportType.Bar, 0, 0, 6, 4, null, 10, Series("X", "Orders", "Code", "ID.Count"));

            var before = await SnapshotAsync(scene.AppId, other.Application.ID);

            foreach (var id in new[] { foreign.ID, 987654321L, 0L, -1L })
            {
                await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(ReportUrl(scene, id)), "Report");
                await EntityScene.AssertNotFoundAsync(await scene.Owner.PutAsync(ReportUrl(scene, id), Body().ToJsonContent()), "Report");

                // Also with a body the rules of a report refuse: the report is looked up first.
                await EntityScene.AssertNotFoundAsync(await scene.Owner.PutAsync(ReportUrl(scene, id), Body("Donut").ToJsonContent()), "Report");
                await EntityScene.AssertNotFoundAsync(await scene.Owner.DeleteAsync(ReportUrl(scene, id)), "Report");
            }

            Assert.Equal(before, await SnapshotAsync(scene.AppId, other.Application.ID));
            await scene.AssertNothingChangedAsync();
        }

        [Theory]
        [InlineData("GET", "layout")]
        [InlineData("DELETE", "layout")]
        [InlineData("POST", "layout")]
        [InlineData("GET", "first")]
        [InlineData("PUT", "first")]
        [InlineData("DELETE", "1.5")]
        public async Task A_Report_Id_That_Is_Not_A_Whole_Number_Should_Return_The_Api_404(string method, string id)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await SeedAsync(scene.AppId, "first", ReportType.Line, 0, 0, 6, 4, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var before = await SnapshotAsync(scene.AppId);

            var request = new HttpRequestMessage(new HttpMethod(method), $"{ReportsUrl(scene)}/{id}");

            if (method != "GET" && method != "DELETE")
            {
                request.Content = Body().ToJsonContent();
            }

            var response = await scene.Owner.SendAsync(request);

            // No route matches ('layout' only answers PUT), so the API fallback answers: NOT_FOUND without an Entity.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.NotFound, error.Code);
            Assert.Null(error.Entity);

            Assert.Equal(before, await SnapshotAsync(scene.AppId));
        }

        [Fact]
        public async Task Put_To_Layout_Should_Never_Be_Taken_For_A_Report()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // The body of a report: the layout action answers, and it misses its Items.
            var response = await scene.Owner.PutAsync($"{ReportsUrl(scene)}/layout", Body().ToJsonContent());

            await EntityScene.AssertValidationAsync(response, "Items: Required");
        }

        // ---------- Create ----------

        [Fact]
        public async Task Create_Should_Store_The_Report_Below_The_Existing_Panels()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // The lowest panel ends at row 7 (2 + 5), whatever its column.
            await SeedAsync(scene.AppId, "High", ReportType.Line, 0, 0, 12, 3, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            await SeedAsync(scene.AppId, "Low", ReportType.Line, 8, 2, 4, 5, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));

            var body = Body("Bar", "7d", 25, "  Orders per month ",
                SeriesBody("  Paid  ", " Orders ", " Created.Year,Created.Month ", " Amount.Sum ", OrdersFilter),
                SeriesBody("All", "Customers", "Name", "ID.Count", "   "));

            var response = await scene.Owner.PostAsync(ReportsUrl(scene), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<ReportResponse>();

            Assert.Equal($"/api/v1/applications/{scene.Token}/reports/{created.ID}", response.Headers.Location?.ToString());
            Assert.Equal("Bar", created.Type);
            Assert.Equal("0,7,6,4", $"{created.X},{created.Y},{created.Width},{created.Height}");
            Assert.Equal("Last 7 days", created.TimeRangeLabel);
            Assert.Equal(new[] { "Paid", "All" }, created.Series.Select(x => x.Label));
            Assert.True(created.Series[0].Windowed);
            Assert.False(created.Series[1].Windowed);

            var stored = Assert.Single(await LoadAsync(scene.AppId), x => x.ID == created.ID);

            // The title is stored as sent; the texts of a series are trimmed and an empty filter is none.
            Assert.Equal(
                $"3 |   Orders per month  | 25 | 7d | 0,7,6,4 | 0:Paid|Orders|Created.Year,Created.Month|Amount.Sum|{OrdersFilter} ; 1:All|Customers|Name|ID.Count|<null>",
                Describe(stored));
            Assert.InRange(stored.DateModified, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(stored.DateModified, created.DateModified);

            // The answer is what GET answers.
            var read = await (await scene.Owner.GetAsync(ReportUrl(scene, created.ID))).ReadJsonAsync<ReportResponse>();
            Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(read));

            // One audit row, for the report. The API server is not told: reports live in the Portal only.
            Assert.Equal(
                new[] { "Report | Created |   Orders per month  | AppID,DateModified,H,MaxRecords,TimeRange,Title,TypeID,W,X,Y" },
                (await scene.AuditRowsAsync(scene.OwnerEmail)).Select(x => AuditShape(x, scene.AppId)));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Create_First_Report_Should_Start_At_The_Top()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // A tall panel of another application of the same owner does not push it down.
            var other = await _portal.CreateApplicationAsync((await _portal.CreateServerAsync()).ID, scene.OwnerEmail, "other");
            await SeedAsync(other.Application.ID, "Elsewhere", ReportType.Bar, 0, 40, 6, 9, null, 10, Series("X", "Orders", "Code", "ID.Count"));

            var created = await (await scene.Owner.PostAsync(ReportsUrl(scene), Body().ToJsonContent())).ReadJsonAsync<ReportResponse>();

            Assert.Equal("0,0,6,4", $"{created.X},{created.Y},{created.Width},{created.Height}");
            Assert.Null(created.TimeRange);
            Assert.Null(created.TimeRangeLabel);
        }

        [Theory]
        // Older versions stored any row and height; the new panel stays on the grid of this API.
        [InlineData(int.MaxValue, 1, 10000)]
        [InlineData(20000, 4, 10000)]
        [InlineData(-50, 4, 0)]
        public async Task Create_Below_A_Panel_Stored_Outside_The_Grid_Should_Stay_On_The_Grid(int storedY, int storedHeight, int expectedY)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await SeedAsync(scene.AppId, "Far away", ReportType.Line, 0, storedY, 6, storedHeight, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));

            var response = await scene.Owner.PostAsync(ReportsUrl(scene), Body().ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(expectedY, (await response.ReadJsonAsync<ReportResponse>()).Y);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1000)]
        public async Task Create_Should_Accept_The_Smallest_And_The_Largest_Number_Of_Records(int maxRecords)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.PostAsync(ReportsUrl(scene), Body(maxRecords: maxRecords).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(maxRecords, Assert.Single(await LoadAsync(scene.AppId)).MaxRecords);
        }

        [Theory]
        [InlineData("1h", "1h", "Last 1 hour")]
        [InlineData("24h", "24h", "Last 24 hours")]
        [InlineData("90d", "90d", "Last 90 days")]
        [InlineData("6m", "6m", "Last 6 months")]
        [InlineData("3y", "3y", "Last 3 years")]
        [InlineData("1000d", "1000d", "Last 1000 days")]
        [InlineData("  7d ", "7d", "Last 7 days")]
        [InlineData("", null, null)]
        [InlineData("   ", null, null)]
        [InlineData(null, null, null)]
        public async Task Create_Should_Store_The_Time_Range_Trimmed_And_A_Blank_One_As_None(string? sent, string? expectedStored, string? expectedLabel)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.PostAsync(ReportsUrl(scene), Body(timeRange: sent).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<ReportResponse>();

            Assert.Equal(expectedStored, created.TimeRange);
            Assert.Equal(expectedLabel, created.TimeRangeLabel);
            Assert.Equal(expectedStored, Assert.Single(await LoadAsync(scene.AppId)).TimeRange);
        }

        [Theory]
        [InlineData("Grid")]
        [InlineData("Pie")]
        [InlineData("Line")]
        [InlineData("Bar")]
        [InlineData("Radar")]
        [InlineData("StackedBar")]
        public async Task Create_Should_Accept_And_Resolve_Everything_Report_Fields_Offers_For_The_Type(string type)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var fields = await (await scene.Owner.GetAsync(FieldsUrl(scene, "Orders", type))).ReadJsonAsync<ReportFieldsResponse>();

            var properties = fields.Properties.SelectMany(x => x.Subs.Select(sub => $"{x.Name}.{sub}")).ToList();
            var groupBy = string.Join(",", fields.Groupings.SelectMany(x => x.Subs.Count == 0 ? new[] { x.Name } : x.Subs.Select(sub => $"{x.Name}.{sub}")));

            // One series per value a series can show, each grouped by everything it can be grouped by.
            var body = Body(type, null, 100, $"Everything of {type}", properties.Select(x => (object)SeriesBody(x, "Orders", groupBy, x)).ToArray());

            var response = await scene.Owner.PostAsync(ReportsUrl(scene), body.ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var created = await response.ReadJsonAsync<ReportResponse>();

            Assert.Equal(type, created.Type);
            Assert.Equal(properties, created.Series.Select(x => x.Property));
            Assert.All(created.Series, x => Assert.Null(x.Error));
            Assert.All(created.Series, x => Assert.Equal(groupBy.Split(',').Length, x.Groups.Count));

            // The column of each value is the name the API server gives it.
            Assert.Equal(properties.Select(x => x.Replace('.', '_').ToLowerInvariant()), created.Series.Select(x => x.Aggregate?.Column.ToLowerInvariant()));
            Assert.Equal((int)Enum.Parse<ReportType>(type), Assert.Single(await LoadAsync(scene.AppId)).TypeID);
        }

        [Fact]
        public async Task The_Same_Series_Should_Be_Accepted_Or_Refused_Depending_On_The_Type()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // The largest text of a group is a value only a Grid can show; a Line cannot be grouped by a text.
            var series = SeriesBody("Codes", "Orders", "Code", "Code.Max");

            Assert.Equal(HttpStatusCode.Created, (await scene.Owner.PostAsync(ReportsUrl(scene), Body("Grid", series: series).ToJsonContent())).StatusCode);

            await EntityScene.AssertValidationAsync(
                await scene.Owner.PostAsync(ReportsUrl(scene), Body("Pie", series: series).ToJsonContent()),
                "Series[0].Property: 'Code.Max' is not one of the Properties of report-fields for entity 'Orders' and type Pie");

            await EntityScene.AssertValidationAsync(
                await scene.Owner.PostAsync(ReportsUrl(scene), Body("Line", series: series).ToJsonContent()),
                "Series[0].Property: 'Code.Max' is not one of the Properties of report-fields for entity 'Orders' and type Line",
                "Series[0].GroupBy: 'Code' is not one of the Groupings of report-fields for entity 'Orders' and type Line");

            Assert.Single(await LoadAsync(scene.AppId));
        }

        // ---------- Update ----------

        [Fact]
        public async Task Update_Should_Change_The_Values_Replace_The_Series_And_Leave_The_Panel_Where_It_Is()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var report = await SeedAsync(scene.AppId, "Before", ReportType.Line, 3, 9, 7, 2, "24h", 30,
                Series("Old A", "Orders", "Created.Year", "ID.Count"),
                Series("Old B", "Orders", "Created.Year", "Amount.Sum"));
            var untouched = await SeedAsync(scene.AppId, "Untouched", ReportType.Pie, 0, 0, 6, 4, null, 10, Series("X", "Orders", "Code", "ID.Count"));

            var oldSeriesIds = report.Series.Select(x => x.ID).ToList();
            var untouchedBefore = await SnapshotOfAsync(scene.AppId, untouched.ID);

            var response = await scene.Owner.PutAsync(
                ReportUrl(scene, report.ID),
                Body("Grid", null, 75, "After", SeriesBody("New", "Customers", "Name", "Name.Max", OrdersFilter)).ToJsonContent());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var updated = await response.ReadJsonAsync<ReportResponse>();
            Assert.Equal(report.ID, updated.ID);
            Assert.Equal("Grid", updated.Type);
            Assert.Equal("3,9,7,2", $"{updated.X},{updated.Y},{updated.Width},{updated.Height}");
            Assert.Null(updated.TimeRange);
            Assert.Equal("Name | Max | Name_max", Describe(Assert.Single(updated.Series).Aggregate));

            var stored = Assert.Single(await LoadAsync(scene.AppId), x => x.ID == report.ID);

            Assert.Equal($"0 | After | 75 | <null> | 3,9,7,2 | 0:New|Customers|Name|Name.Max|{OrdersFilter}", Describe(stored));
            Assert.InRange(stored.DateModified, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));

            // The old series rows are gone, not left behind without a report.
            Assert.DoesNotContain(Assert.Single(stored.Series).ID, oldSeriesIds);
            Assert.False(await _portal.WithDbContextAsync(db => db.ReportSeries.AnyAsync(x => oldSeriesIds.Contains(x.ID))));

            Assert.Equal(untouchedBefore, await SnapshotOfAsync(scene.AppId, untouched.ID));

            // The answer is what GET answers.
            var read = await (await scene.Owner.GetAsync(ReportUrl(scene, report.ID))).ReadJsonAsync<ReportResponse>();
            Assert.Equal(JsonSerializer.Serialize(updated), JsonSerializer.Serialize(read));

            Assert.Equal(
                new[] { "Report | Modified | After | DateModified,MaxRecords,TimeRange,Title,TypeID" },
                (await scene.AuditRowsAsync(scene.OwnerEmail)).Select(x => AuditShape(x, scene.AppId)));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        // ---------- Delete ----------

        [Fact]
        public async Task Delete_Should_Remove_The_Report_With_Its_Series_And_Leave_The_Others()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var report = await SeedAsync(scene.AppId, "Going", ReportType.Line, 0, 0, 6, 4, null, 30,
                Series("A", "Orders", "Created.Year", "ID.Count"),
                Series("B", "Orders", "Created.Year", "Amount.Sum"));
            var staying = await SeedAsync(scene.AppId, "Staying", ReportType.Pie, 0, 4, 6, 4, null, 10, Series("X", "Orders", "Code", "ID.Count"));

            var seriesIds = report.Series.Select(x => x.ID).ToList();
            var stayingBefore = await SnapshotOfAsync(scene.AppId, staying.ID);

            var response = await scene.Owner.DeleteAsync(ReportUrl(scene, report.ID));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(new[] { staying.ID }, (await LoadAsync(scene.AppId)).Select(x => x.ID));
            Assert.False(await _portal.WithDbContextAsync(db => db.ReportSeries.AnyAsync(x => seriesIds.Contains(x.ID))));

            // The other panel stays where it was.
            Assert.Equal(stayingBefore, await SnapshotOfAsync(scene.AppId, staying.ID));

            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(ReportUrl(scene, report.ID)), "Report");
            await EntityScene.AssertNotFoundAsync(await scene.Owner.DeleteAsync(ReportUrl(scene, report.ID)), "Report");

            var audit = Assert.Single(await scene.AuditRowsAsync(scene.OwnerEmail));
            Assert.Equal("Report | Deleted | Going", $"{audit.EntityType} | {audit.Action} | {audit.EntityIdentifier}");
            Assert.Equal(scene.AppId, audit.AppID);
            Assert.Empty(_portal.ApiServer.Requests);
        }

        // ---------- Rules of create and update ----------

        [Theory]
        // The values of the report.
        [InlineData("Title", Omit, "Required")]
        [InlineData("Title", "null", "Required")]
        [InlineData("Title", "\"\"", "Required")]
        [InlineData("Title", "\"   \"", "Required")]
        [InlineData("Type", Omit, "Required")]
        [InlineData("Type", "\"\"", "Required")]
        [InlineData("Type", "\"Donut\"", TypeMessage)]
        [InlineData("Type", "\"line\"", TypeMessage)]
        [InlineData("Type", "\"2\"", TypeMessage)]
        [InlineData("Type", "\" Line\"", TypeMessage)]
        [InlineData("MaxRecords", Omit, "Required")]
        [InlineData("MaxRecords", "null", "Required")]
        [InlineData("MaxRecords", "0", "Must be between 1 and 1000")]
        [InlineData("MaxRecords", "-5", "Must be between 1 and 1000")]
        [InlineData("MaxRecords", "1001", "Must be between 1 and 1000")]
        [InlineData("TimeRange", "\"7\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"d\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"0d\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"07d\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"-1d\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"1001d\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"7w\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"7D\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"7 d\"", TimeRangeMessage)]
        [InlineData("TimeRange", "\"1.5d\"", TimeRangeMessage)]
        [InlineData("Series", Omit, "Required")]
        [InlineData("Series", "null", "Required")]
        [InlineData("Series", "[]", "Add at least one series")]
        // The texts of a series.
        [InlineData("Series[0].Label", Omit, "Required")]
        [InlineData("Series[0].Label", "\"  \"", "Required")]
        [InlineData("Series[0].Entity", "null", "Required")]
        [InlineData("Series[0].Entity", "\"\"", "Required")]
        [InlineData("Series[0].GroupBy", Omit, "Required")]
        [InlineData("Series[0].GroupBy", "\" \"", "Required")]
        [InlineData("Series[0].Property", "null", "Required")]
        [InlineData("Series[0].Property", "\"\"", "Required")]
        // The entity must exist, in this spelling.
        [InlineData("Series[0].Entity", "\"Nothing\"", "Unknown entity 'Nothing'.")]
        [InlineData("Series[0].Entity", "\"orders\"", "Unknown entity 'orders'.")]
        // The property must be one a Line can show for 'Orders'.
        [InlineData("Series[0].Property", "\"Gone.Sum\"", "'Gone.Sum' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].Property", "\"Amount.Median\"", "'Amount.Median' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].Property", "\"Amount\"", "'Amount' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].Property", "\"amount.sum\"", "'amount.sum' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].Property", "\"ID.Sum\"", "'ID.Sum' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].Property", "\"Code.Max\"", "'Code.Max' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].Property", "\"Created.Max\"", "'Created.Max' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].Property", "\"Paid.Count\"", "'Paid.Count' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].Property", "\"ID.Count,Amount.Sum\"", "'ID.Count,Amount.Sum' is not one of the Properties of report-fields for entity 'Orders' and type Line")]
        // Every group-by must be one a Line can be grouped by for 'Orders'.
        [InlineData("Series[0].GroupBy", "\"Gone\"", "'Gone' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Code\"", "'Code' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Paid\"", "'Paid' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Created\"", "'Created' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Created.Week\"", "'Created.Week' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"created.year\"", "'created.year' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Amount.Year\"", "'Amount.Year' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"ID\"", "'ID' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Created.Year,Gone\"", "'Gone' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Created.Year, Created.Month\"", "' Created.Month' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Created.Year,,Amount\"", "'' is not one of the Groupings of report-fields for entity 'Orders' and type Line")]
        [InlineData("Series[0].GroupBy", "\"Created.Year,Created.Year\"", "A grouping can be listed only once")]
        // The filter must read as a filter of the data API.
        [InlineData("Series[0].Filter", "\"Code = 1\"", "Not a filter the data API can read")]
        [InlineData("Series[0].Filter", "\"null\"", "Not a filter the data API can read")]
        [InlineData("Series[0].Filter", "\"[]\"", "Not a filter the data API can read")]
        [InlineData("Series[0].Filter", "\"{\\\"Property\\\":\\\"Code\\\",\\\"Operator\\\":\\\"between\\\",\\\"Value\\\":1}\"", "Not a filter the data API can read")]
        [InlineData("Series[0].Filter", "\"{\\\"Logic\\\":\\\"XOR\\\",\\\"Filters\\\":[]}\"", "Not a filter the data API can read")]
        public async Task A_Value_That_Breaks_A_Rule_Should_Return_400_With_Its_Path_And_Change_Nothing(string path, string json, string message)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var report = await SeedAsync(scene.AppId, "Existing", ReportType.Line, 0, 0, 6, 4, "24h", 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var before = await SnapshotAsync(scene.AppId);

            var body = BodyWith(path, json);

            await EntityScene.AssertValidationAsync(await scene.Owner.PostAsync(ReportsUrl(scene), EntityScene.Json(body)), $"{path}: {message}");
            await EntityScene.AssertValidationAsync(await scene.Owner.PutAsync(ReportUrl(scene, report.ID), EntityScene.Json(body)), $"{path}: {message}");

            Assert.Equal(before, await SnapshotAsync(scene.AppId));
            await scene.AssertNothingChangedAsync();
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task A_Series_That_Is_Null_Should_Return_400_With_Its_Place()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.PostAsync(ReportsUrl(scene), EntityScene.Json(BodyWith("Series", "[null]")));

            await EntityScene.AssertValidationAsync(response, "Series[0]: Required");
            Assert.Empty(await LoadAsync(scene.AppId));
        }

        [Fact]
        public async Task Every_Problem_Should_Be_Reported_With_The_Place_Of_Its_Series()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var report = await SeedAsync(scene.AppId, "Existing", ReportType.Line, 0, 0, 6, 4, "24h", 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var before = await SnapshotAsync(scene.AppId);

            var body = Body("Pie", "sometimes", 10, "Several problems",
                SeriesBody("Fine", "Orders", "Code", "Amount.Sum"),
                SeriesBody("Property and filter", "Orders", "Paid,Created.Year", "Code.Max", "nope"),
                SeriesBody("Entity", "Nothing", "Code", "ID.Count"),
                SeriesBody("Group", "Customers", "Name,Gone", "ID.Count"));

            var expected = new[]
            {
                $"TimeRange: {TimeRangeMessage}",
                "Series[1].Property: 'Code.Max' is not one of the Properties of report-fields for entity 'Orders' and type Pie",
                "Series[1].Filter: Not a filter the data API can read",
                "Series[2].Entity: Unknown entity 'Nothing'.",
                "Series[3].GroupBy: 'Gone' is not one of the Groupings of report-fields for entity 'Customers' and type Pie"
            };

            await EntityScene.AssertValidationAsync(await scene.Owner.PostAsync(ReportsUrl(scene), body.ToJsonContent()), expected);
            await EntityScene.AssertValidationAsync(await scene.Owner.PutAsync(ReportUrl(scene, report.ID), body.ToJsonContent()), expected);

            Assert.Equal(before, await SnapshotAsync(scene.AppId));
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task An_Unknown_Type_Should_Still_Report_The_Entity_And_The_Filter_Of_A_Series()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // What a series may show depends on the type, so that is not checked without one.
            var body = Body("Donut", null, 10, "No type",
                SeriesBody("A", "Orders", "Whatever", "Whatever", "nope"),
                SeriesBody("B", "Nothing", "Code", "ID.Count"));

            await EntityScene.AssertValidationAsync(
                await scene.Owner.PostAsync(ReportsUrl(scene), body.ToJsonContent()),
                $"Type: {TypeMessage}",
                "Series[0].Filter: Not a filter the data API can read",
                "Series[1].Entity: Unknown entity 'Nothing'.");

            Assert.Empty(await LoadAsync(scene.AppId));
        }

        [Fact]
        public async Task A_Filter_Should_Be_Stored_As_Sent()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // Nested, lower-case logic, an operator by its short name, odd spacing: whatever the data API reads.
            const string filter = "{ \"logic\": \"or\",  \"Filters\": [ { \"Property\": \"Code\", \"Operator\": \"equal\", \"Value\": null }, { \"Logic\": \"AND\", \"Filters\": [ { \"Property\": \"Amount\", \"Operator\": \"greater\", \"Value\": 5 } ] } ] }";

            var response = await scene.Owner.PostAsync(ReportsUrl(scene), Body(series: SeriesBody(filter: filter)).ToJsonContent());

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(filter, Assert.Single((await response.ReadJsonAsync<ReportResponse>()).Series).Filter);
            Assert.Equal(filter, Assert.Single(Assert.Single(await LoadAsync(scene.AppId)).Series).Filter);
        }

        // ---------- Layout ----------

        [Fact]
        public async Task Layout_Should_Move_And_Resize_Only_The_Reports_Listed()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var first = await SeedAsync(scene.AppId, "First", ReportType.Line, 0, 0, 6, 4, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var second = await SeedAsync(scene.AppId, "Second", ReportType.Pie, 6, 0, 6, 4, null, 30, Series("A", "Orders", "Code", "ID.Count"));
            var third = await SeedAsync(scene.AppId, "Third", ReportType.Grid, 0, 4, 12, 4, null, 30, Series("A", "Orders", "Code", "ID.Count"));

            // The second is listed where it already is; the third is left out.
            var response = await scene.Owner.PutAsync(LayoutUrl(scene), Layout((first.ID, 0, 4, 12, 9), (second.ID, 6, 0, 6, 4)).ToJsonContent());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());

            var stored = await LoadAsync(scene.AppId);

            Assert.Equal(
                new[] { "First 0,4,12,9", "Second 6,0,6,4", "Third 0,4,12,4" },
                stored.Select(x => $"{x.Title} {x.X},{x.Y},{x.W},{x.H}"));

            // Nothing else of a report changes, its date included.
            Assert.All(stored, x => Assert.Equal(_seeded, x.DateModified));
            Assert.All(stored, x => Assert.Single(x.Series));
            Assert.Equal(new[] { first.ID, second.ID, third.ID }, stored.Select(x => x.ID));

            // One audit row, for the panel that moved.
            Assert.Equal(
                new[] { "Report | Modified | First | H,W,Y" },
                (await scene.AuditRowsAsync(scene.OwnerEmail)).Select(x => AuditShape(x, scene.AppId)));
            Assert.Empty(_portal.ApiServer.Requests);

            // The list follows the new places.
            var list = await (await scene.Owner.GetAsync(ReportsUrl(scene))).ReadJsonAsync<ListResponse<ReportResponse>>();
            Assert.Equal(new[] { "Second", "First", "Third" }, list.Data.Select(x => x.Title));
        }

        [Theory]
        [InlineData(0, 0, 12, 1)]
        [InlineData(11, 0, 1, 1)]
        [InlineData(5, 250, 7, 40)]
        [InlineData(0, 10000, 12, 100)]
        public async Task Layout_At_The_Edges_Of_The_Grid_Should_Be_Saved(int x, int y, int width, int height)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var report = await SeedAsync(scene.AppId, "Panel", ReportType.Line, 2, 2, 2, 2, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));

            var response = await scene.Owner.PutAsync(LayoutUrl(scene), Layout((report.ID, x, y, width, height)).ToJsonContent());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            var stored = Assert.Single(await LoadAsync(scene.AppId));
            Assert.Equal($"{x},{y},{width},{height}", $"{stored.X},{stored.Y},{stored.W},{stored.H}");
        }

        [Fact]
        public async Task Layout_With_An_Empty_List_Should_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await SeedAsync(scene.AppId, "Panel", ReportType.Line, 2, 2, 2, 2, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var before = await SnapshotAsync(scene.AppId);

            var response = await scene.Owner.PutAsync(LayoutUrl(scene), Layout().ToJsonContent());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(before, await SnapshotAsync(scene.AppId));
            await scene.AssertNothingChangedAsync();
        }

        [Theory]
        [InlineData(-1, 0, 6, 4, "X: Must be between 0 and 11")]
        [InlineData(12, 0, 1, 4, "X: Must be between 0 and 11")]
        [InlineData(0, -1, 6, 4, "Y: Must be between 0 and 10000")]
        [InlineData(0, 10001, 6, 4, "Y: Must be between 0 and 10000")]
        [InlineData(0, 0, 0, 4, "Width: Must be between 1 and 12")]
        [InlineData(0, 0, -3, 4, "Width: Must be between 1 and 12")]
        [InlineData(0, 0, 13, 4, "Width: Must be between 1 and 12")]
        [InlineData(0, 0, 6, 0, "Height: Must be between 1 and 100")]
        [InlineData(0, 0, 6, -2, "Height: Must be between 1 and 100")]
        [InlineData(0, 0, 6, 101, "Height: Must be between 1 and 100")]
        [InlineData(7, 0, 6, 4, "Width: X + Width must be 12 or less")]
        [InlineData(1, 0, 12, 4, "Width: X + Width must be 12 or less")]
        [InlineData(11, 0, 2, 4, "Width: X + Width must be 12 or less")]
        public async Task Layout_Outside_The_Grid_Should_Return_400_And_Change_Nothing(int x, int y, int width, int height, string expected)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var first = await SeedAsync(scene.AppId, "First", ReportType.Line, 2, 2, 2, 2, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var second = await SeedAsync(scene.AppId, "Second", ReportType.Line, 4, 2, 2, 2, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var before = await SnapshotAsync(scene.AppId);

            // The first item is fine: it is not saved either.
            var response = await scene.Owner.PutAsync(LayoutUrl(scene), Layout((first.ID, 0, 0, 6, 4), (second.ID, x, y, width, height)).ToJsonContent());

            await EntityScene.AssertValidationAsync(response, $"Items[1].{expected}");
            Assert.Equal(before, await SnapshotAsync(scene.AppId));
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Layout_With_An_Id_That_Is_Not_A_Report_Of_The_Application_Should_Return_400_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var mine = await SeedAsync(scene.AppId, "Mine", ReportType.Line, 2, 2, 2, 2, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));

            // Another application of the same owner.
            var other = await _portal.CreateApplicationAsync((await _portal.CreateServerAsync()).ID, scene.OwnerEmail, "other");
            var foreign = await SeedAsync(other.Application.ID, "Elsewhere", ReportType.Bar, 1, 1, 1, 1, null, 10, Series("X", "Orders", "Code", "ID.Count"));

            var before = await SnapshotAsync(scene.AppId, other.Application.ID);

            var response = await scene.Owner.PutAsync(
                LayoutUrl(scene),
                Layout((mine.ID, 0, 0, 6, 4), (foreign.ID, 0, 0, 6, 4), (987654321L, 0, 0, 6, 4), (mine.ID, 6, 0, 6, 4)).ToJsonContent());

            await EntityScene.AssertValidationAsync(
                response,
                "Items[1].ID: Not a report of this application",
                "Items[2].ID: Not a report of this application",
                "Items[3].ID: A report can be listed only once");

            Assert.Equal(before, await SnapshotAsync(scene.AppId, other.Application.ID));
            await scene.AssertNothingChangedAsync();
        }

        [Theory]
        [InlineData("{}", "Items: Required")]
        [InlineData("{\"Items\":null}", "Items: Required")]
        [InlineData("{\"Items\":[null]}", "Items[0]: Required")]
        [InlineData("{\"Items\":[{}]}", "Items[0].ID: Required|Items[0].X: Required|Items[0].Y: Required|Items[0].Width: Required|Items[0].Height: Required")]
        [InlineData("{\"Items\":[{\"ID\":1,\"X\":0,\"Width\":6}]}", "Items[0].Y: Required|Items[0].Height: Required")]
        public async Task Layout_Without_A_Usable_List_Should_Return_400(string body, string expected)
        {
            var scene = await EntityScene.CreateAsync(_portal);
            await SeedAsync(scene.AppId, "Mine", ReportType.Line, 2, 2, 2, 2, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var before = await SnapshotAsync(scene.AppId);

            var response = await scene.Owner.PutAsync(LayoutUrl(scene), EntityScene.Json(body));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var error = await response.ReadJsonAsync<ErrorResponse>();
            Assert.Equal(PortalErrorCode.Validation, error.Code);
            Assert.Equal(
                expected.Split('|').OrderBy(x => x, StringComparer.Ordinal),
                (error.Errors ?? new List<ErrorDetail>()).Select(x => $"{x.Property}: {x.Message}").OrderBy(x => x, StringComparer.Ordinal));

            Assert.Equal(before, await SnapshotAsync(scene.AppId));
        }

        // ---------- Report fields ----------

        [Fact]
        public async Task Report_Fields_Of_A_Grid_Should_Offer_Max_And_Min_Of_Texts_And_Dates()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var fields = await (await scene.Owner.GetAsync(FieldsUrl(scene, "Orders", "Grid"))).ReadJsonAsync<ReportFieldsResponse>();

            // The primary key first, then the properties in the order they were created; a Boolean has no value to show.
            Assert.Equal(
                new[]
                {
                    "ID: Count", "Owner: Max,Min,Sum,Avg", "Created: Max,Min", "Customer_ID: Max,Min,Sum,Avg", "Agent_ID: Max,Min,Sum,Avg",
                    "Amount: Max,Min,Sum,Avg", "Code: Max,Min", "Secret: Max,Min"
                },
                fields.Properties.Select(Describe));

            Assert.Equal(
                new[]
                {
                    "Owner: ", "Created: Year,Month,Day,Hour,Minute,Second", "Customer_ID: ", "Agent_ID: ", "Amount: ", "Code: ", "Secret: ", "Paid: "
                },
                fields.Groupings.Select(Describe));
        }

        [Fact]
        public async Task Report_Fields_Of_A_Line_Should_Offer_Numbers_And_Group_By_Numbers_And_Dates()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var fields = await (await scene.Owner.GetAsync(FieldsUrl(scene, "Orders", "Line"))).ReadJsonAsync<ReportFieldsResponse>();

            Assert.Equal(
                new[] { "ID: Count", "Owner: Max,Min,Sum,Avg", "Customer_ID: Max,Min,Sum,Avg", "Agent_ID: Max,Min,Sum,Avg", "Amount: Max,Min,Sum,Avg" },
                fields.Properties.Select(Describe));

            Assert.Equal(
                new[] { "Owner: ", "Created: Year,Month,Day,Hour,Minute,Second", "Customer_ID: ", "Agent_ID: ", "Amount: " },
                fields.Groupings.Select(Describe));
        }

        [Theory]
        [InlineData("Pie")]
        [InlineData("Bar")]
        [InlineData("Radar")]
        [InlineData("StackedBar")]
        public async Task Report_Fields_Of_The_Other_Charts_Should_Offer_Numbers_And_Group_By_Anything(string type)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var fields = await (await scene.Owner.GetAsync(FieldsUrl(scene, "Orders", type))).ReadJsonAsync<ReportFieldsResponse>();

            Assert.Equal(
                new[] { "ID: Count", "Owner: Max,Min,Sum,Avg", "Customer_ID: Max,Min,Sum,Avg", "Agent_ID: Max,Min,Sum,Avg", "Amount: Max,Min,Sum,Avg" },
                fields.Properties.Select(Describe));

            Assert.Equal(
                new[]
                {
                    "Owner: ", "Created: Year,Month,Day,Hour,Minute,Second", "Customer_ID: ", "Agent_ID: ", "Amount: ", "Code: ", "Secret: ", "Paid: "
                },
                fields.Groupings.Select(Describe));
        }

        [Theory]
        [InlineData("Grid", "Users", "{\"Properties\":[{\"Name\":\"ID\",\"Subs\":[\"Count\"]},{\"Name\":\"Email\",\"Subs\":[\"Max\",\"Min\"]},{\"Name\":\"Nickname\",\"Subs\":[\"Max\",\"Min\"]}],\"Groupings\":[{\"Name\":\"Email\",\"Subs\":[]},{\"Name\":\"Nickname\",\"Subs\":[]}]}")]
        [InlineData("Line", "Users", "{\"Properties\":[{\"Name\":\"ID\",\"Subs\":[\"Count\"]}],\"Groupings\":[]}")]
        [InlineData("Pie", "Invoices", "{\"Properties\":[{\"Name\":\"ID\",\"Subs\":[\"Count\"]}],\"Groupings\":[]}")]
        public async Task Report_Fields_Of_Entities_With_Texts_Only_Or_Nothing_But_A_Key_Should_Offer_What_The_Type_Can_Show(string type, string entity, string expected)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.GetAsync(FieldsUrl(scene, entity, type));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expected, await response.Content.ReadAsStringAsync());

            // Reading does not involve the API server.
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Report_Fields_Of_An_Entity_Without_A_Primary_Key_Should_Offer_No_Count()
        {
            var scene = await EntityScene.CreateAsync(_portal);

            // Only an imported application file can hold such an entity.
            await _portal.WithDbContextAsync(db =>
            {
                db.Entities.Add(EntityScene.Entity(scene.AppId, "Keyless", false, EntityScene.Property("Name", PropertyType.String)));
                return db.SaveChangesAsync();
            });

            var response = await scene.Owner.GetAsync(FieldsUrl(scene, "Keyless", "Grid"));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var fields = await response.ReadJsonAsync<ReportFieldsResponse>();

            Assert.Equal(new[] { "Name: Max,Min" }, fields.Properties.Select(Describe));
            Assert.Equal(new[] { "Name: " }, fields.Groupings.Select(Describe));

            // Saving a report goes through the same lists: a count is refused, not a failure.
            await EntityScene.AssertValidationAsync(
                await scene.Owner.PostAsync(ReportsUrl(scene), Body("Grid", series: SeriesBody("A", "Keyless", "Name", "ID.Count")).ToJsonContent()),
                "Series[0].Property: 'ID.Count' is not one of the Properties of report-fields for entity 'Keyless' and type Grid");
        }

        [Theory]
        [InlineData("")]
        [InlineData("?Type=")]
        [InlineData("?Type=Donut")]
        [InlineData("?Type=grid")]
        [InlineData("?Type=0")]
        public async Task Report_Fields_Without_A_Known_Type_Should_Return_400(string query)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            var response = await scene.Owner.GetAsync($"{scene.AppUrl}/entities/Orders/report-fields{query}");

            await EntityScene.AssertValidationAsync(response, $"Type: {TypeMessage}");
        }

        [Theory]
        [InlineData("Nothing")]
        [InlineData("orders")]
        public async Task Report_Fields_Of_An_Unknown_Or_Differently_Cased_Entity_Should_Return_404(string entity)
        {
            var scene = await EntityScene.CreateAsync(_portal);

            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(FieldsUrl(scene, entity, "Grid")), "Entity");

            // The entity is looked up before the type is read.
            await EntityScene.AssertNotFoundAsync(await scene.Owner.GetAsync(FieldsUrl(scene, entity, "Donut")), "Entity");
        }

        // ---------- Access ----------

        [Fact]
        public async Task Collaborator_Should_Be_Able_To_Do_Everything_The_Owner_Can()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var seeded = await SeedAsync(scene.AppId, "Seeded", ReportType.Line, 0, 0, 6, 4, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var client = scene.Collaborator;

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ReportsUrl(scene))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ReportUrl(scene, seeded.ID))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(FieldsUrl(scene, "Orders", "Grid"))).StatusCode);

            var created = await client.PostAsync(ReportsUrl(scene), Body().ToJsonContent());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.ReadJsonAsync<ReportResponse>()).ID;

            Assert.Equal(HttpStatusCode.OK, (await client.PutAsync(ReportUrl(scene, id), Body(title: "Renamed").ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync(LayoutUrl(scene), Layout((id, 6, 0, 6, 4)).ToJsonContent())).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(ReportUrl(scene, id))).StatusCode);

            // The changes are audited under the collaborator's name.
            Assert.Equal(
                new[] { "Created", "Modified", "Modified", "Deleted" },
                (await scene.AuditRowsAsync(scene.CollaboratorEmail)).Select(x => x.Action));
            Assert.Empty(await scene.AuditRowsAsync(scene.OwnerEmail));
            Assert.Empty(_portal.ApiServer.Requests);
        }

        [Fact]
        public async Task Stranger_Admin_And_Unknown_Token_Should_Return_404_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var report = await SeedAsync(scene.AppId, "Seeded", ReportType.Line, 0, 0, 6, 4, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var admin = await _portal.CreateAdminClientAsync();
            var before = await SnapshotAsync(scene.AppId);

            foreach (var send in AllRequests(report.ID))
            {
                await EntityScene.AssertNotFoundAsync(await send(scene.Stranger, scene.AppUrl), "Application");
                await EntityScene.AssertNotFoundAsync(await send(admin, scene.AppUrl), "Application");
                await EntityScene.AssertNotFoundAsync(await send(scene.Owner, $"/api/v1/applications/{Guid.NewGuid()}"), "Application");
            }

            Assert.Equal(before, await SnapshotAsync(scene.AppId));
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Anonymous_Should_Return_401()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var report = await SeedAsync(scene.AppId, "Seeded", ReportType.Line, 0, 0, 6, 4, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var anonymous = _portal.CreateAnonymousClient();
            var before = await SnapshotAsync(scene.AppId);

            foreach (var send in AllRequests(report.ID))
            {
                await EntityScene.AssertErrorAsync(await send(anonymous, scene.AppUrl), HttpStatusCode.Unauthorized, PortalErrorCode.Unauthorized);
            }

            Assert.Equal(before, await SnapshotAsync(scene.AppId));
            await scene.AssertNothingChangedAsync();
        }

        [Fact]
        public async Task Writes_Without_The_Csrf_Header_Should_Return_403_And_Change_Nothing()
        {
            var scene = await EntityScene.CreateAsync(_portal);
            var report = await SeedAsync(scene.AppId, "Seeded", ReportType.Line, 0, 0, 6, 4, null, 30, Series("A", "Orders", "Created.Year", "ID.Count"));
            var before = await SnapshotAsync(scene.AppId);

            scene.Owner.DefaultRequestHeaders.Remove(PortalCsrfFilter.HeaderName);

            var writes = new[]
            {
                await scene.Owner.PostAsync(ReportsUrl(scene), Body().ToJsonContent()),
                await scene.Owner.PutAsync(ReportUrl(scene, report.ID), Body(title: "Renamed").ToJsonContent()),
                await scene.Owner.PutAsync(LayoutUrl(scene), Layout((report.ID, 6, 0, 6, 4)).ToJsonContent()),
                await scene.Owner.DeleteAsync(ReportUrl(scene, report.ID))
            };

            foreach (var response in writes)
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

                var error = await response.ReadJsonAsync<ErrorResponse>();
                Assert.Equal(PortalErrorCode.Forbidden, error.Code);
                Assert.Contains(PortalCsrfFilter.HeaderName, error.Message);
            }

            // Reads need no header.
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync(ReportsUrl(scene))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await scene.Owner.GetAsync(FieldsUrl(scene, "Orders", "Grid"))).StatusCode);

            Assert.Equal(before, await SnapshotAsync(scene.AppId));
            await scene.AssertNothingChangedAsync();
        }

        // ---------- Helpers ----------

        private static string ReportsUrl(EntityScene scene)
        {
            return $"{scene.AppUrl}/reports";
        }

        private static string ReportUrl(EntityScene scene, long id)
        {
            return $"{scene.AppUrl}/reports/{id}";
        }

        private static string LayoutUrl(EntityScene scene)
        {
            return $"{scene.AppUrl}/reports/layout";
        }

        private static string FieldsUrl(EntityScene scene, string entity, string type)
        {
            return $"{scene.AppUrl}/entities/{entity}/report-fields?Type={type}";
        }

        private static DBWS_ReportSeries Series(string label, string entity, string groupBy, string property, string? filter = null, int? order = null)
        {
            return new DBWS_ReportSeries
            {
                Label = label,
                Entity = entity,
                GroupBy = groupBy,
                Property = property,
                Filter = filter,
                // -1: the position in the list, set when the report is seeded.
                Order = order ?? -1,
                DateModified = _seeded
            };
        }

        /// <summary>
        /// Adds a report straight to the database, so no audit row is written.
        /// </summary>
        private async Task<DBWS_ReportPanel> SeedAsync(long appId, string title, ReportType type, int x, int y, int w, int h, string? timeRange, int maxRecords, params DBWS_ReportSeries[] series)
        {
            for (var i = 0; i < series.Length; i++)
            {
                if (series[i].Order < 0)
                {
                    series[i].Order = i;
                }
            }

            var report = new DBWS_ReportPanel
            {
                AppID = appId,
                Title = title,
                TypeID = (int)type,
                X = x,
                Y = y,
                W = w,
                H = h,
                TimeRange = timeRange,
                MaxRecords = maxRecords,
                DateModified = _seeded,
                Series = series.ToList()
            };

            await _portal.WithDbContextAsync(db =>
            {
                db.Reports.Add(report);
                return db.SaveChangesAsync();
            });

            return report;
        }

        /// <summary>
        /// The reports of an application with their series, in the order they were added.
        /// </summary>
        private async Task<List<DBWS_ReportPanel>> LoadAsync(long appId)
        {
            var reports = await _portal.WithDbContextAsync(db => db.Reports
                .AsNoTracking()
                .Include(x => x.Series)
                .Where(x => x.AppID == appId)
                .ToListAsync());

            return reports.OrderBy(x => x.ID).ToList();
        }

        /// <summary>
        /// Everything stored of the reports of these applications, IDs and dates included, to
        /// compare before and after a request that must change nothing.
        /// </summary>
        private async Task<List<string>> SnapshotAsync(params long[] appIds)
        {
            var result = new List<string>();

            foreach (var appId in appIds)
            {
                result.AddRange((await LoadAsync(appId)).Select(Snapshot));
            }

            return result;
        }

        private async Task<List<string>> SnapshotOfAsync(long appId, long reportId)
        {
            return (await LoadAsync(appId)).Where(x => x.ID == reportId).Select(Snapshot).ToList();
        }

        private static string Snapshot(DBWS_ReportPanel report)
        {
            var series = report.Series.OrderBy(x => x.ID).Select(x => $"{x.ID}/{x.PanelID}/{x.DateModified.Ticks}");

            return $"{report.ID} | {report.AppID} | {report.DateModified.Ticks} | {Describe(report)} | {string.Join(",", series)}";
        }

        /// <summary>
        /// The stored values of a report and its series, without IDs and dates.
        /// </summary>
        private static string Describe(DBWS_ReportPanel report, bool withPlace = true)
        {
            var place = withPlace ? $" | {report.X},{report.Y},{report.W},{report.H}" : string.Empty;
            var series = report.Series
                .OrderBy(x => x.Order)
                .Select(x => $"{x.Order}:{x.Label}|{x.Entity}|{x.GroupBy}|{x.Property}|{x.Filter ?? "<null>"}");

            return $"{report.TypeID} | {report.Title} | {report.MaxRecords} | {report.TimeRange ?? "<null>"}{place} | {string.Join(" ; ", series)}";
        }

        private static string Describe(ReportSeriesAggregateResponse? aggregate)
        {
            return aggregate is null ? "<null>" : $"{aggregate.Property} | {aggregate.Function} | {aggregate.Column}";
        }

        private static string Describe(ReportSeriesGroupResponse group)
        {
            return $"{group.Property} | {group.Type} | {group.Part} | {group.Column}";
        }

        private static string Describe(ReportFieldResponse field)
        {
            return $"{field.Name}: {string.Join(",", field.Subs)}";
        }

        /// <summary>
        /// What an audit row says, with the names of the values it lists.
        /// </summary>
        private static string AuditShape(PortalAuditLog row, long appId)
        {
            Assert.Equal(appId, row.AppID);

            var changes = JsonSerializer.Deserialize<List<Dictionary<string, string?>>>(row.Changes ?? "[]") ?? new List<Dictionary<string, string?>>();

            return $"{row.EntityType} | {row.Action} | {row.EntityIdentifier} | {string.Join(",", changes.Select(x => x["Property"]).OrderBy(x => x, StringComparer.Ordinal))}";
        }

        private static Dictionary<string, object?> SeriesBody(
            string label = "Count",
            string entity = "Orders",
            string groupBy = "Created.Year,Created.Month",
            string property = "ID.Count",
            string? filter = null)
        {
            return new Dictionary<string, object?>
            {
                ["Label"] = label,
                ["Entity"] = entity,
                ["GroupBy"] = groupBy,
                ["Property"] = property,
                ["Filter"] = filter
            };
        }

        /// <summary>
        /// A report every rule accepts: a Line of the orders per month. Without series it gets one.
        /// </summary>
        private static Dictionary<string, object?> Body(string type = "Line", string? timeRange = null, int maxRecords = 50, string title = "Orders per month", params object[] series)
        {
            return new Dictionary<string, object?>
            {
                ["Title"] = title,
                ["Type"] = type,
                ["MaxRecords"] = maxRecords,
                ["TimeRange"] = timeRange,
                ["Series"] = series.Length == 0 ? new object[] { SeriesBody() } : series
            };
        }

        /// <summary>
        /// The JSON of <see cref="Body"/> with one value replaced by the JSON given, or left out.
        /// The path is the name of a value of the report, or 'Series[0].' and one of the series.
        /// </summary>
        private static string BodyWith(string path, string json)
        {
            const string seriesPrefix = "Series[0].";

            var body = JsonNode.Parse(JsonSerializer.Serialize(Body()))?.AsObject() ?? throw new InvalidOperationException("No body.");
            var target = path.StartsWith(seriesPrefix, StringComparison.Ordinal)
                ? body["Series"]?[0]?.AsObject() ?? throw new InvalidOperationException("No series.")
                : body;
            var name = path.StartsWith(seriesPrefix, StringComparison.Ordinal) ? path.Substring(seriesPrefix.Length) : path;

            if (json == Omit)
            {
                target.Remove(name);
            }
            else
            {
                target[name] = JsonNode.Parse(json);
            }

            return body.ToJsonString();
        }

        private static Dictionary<string, object?> Layout(params (long ID, int X, int Y, int Width, int Height)[] items)
        {
            return new Dictionary<string, object?>
            {
                ["Items"] = items.Select(x => new { x.ID, x.X, x.Y, x.Width, x.Height }).ToList()
            };
        }

        /// <summary>
        /// One request to each endpoint of the reports, valid for the owner of the scene.
        /// </summary>
        private static List<Func<HttpClient, string, Task<HttpResponseMessage>>> AllRequests(long reportId)
        {
            return new List<Func<HttpClient, string, Task<HttpResponseMessage>>>
            {
                (client, appUrl) => client.GetAsync($"{appUrl}/reports"),
                (client, appUrl) => client.GetAsync($"{appUrl}/reports/{reportId}"),
                (client, appUrl) => client.PostAsync($"{appUrl}/reports", Body().ToJsonContent()),
                (client, appUrl) => client.PutAsync($"{appUrl}/reports/{reportId}", Body(title: "Renamed").ToJsonContent()),
                (client, appUrl) => client.PutAsync($"{appUrl}/reports/layout", Layout((reportId, 6, 0, 6, 4)).ToJsonContent()),
                (client, appUrl) => client.DeleteAsync($"{appUrl}/reports/{reportId}"),
                (client, appUrl) => client.GetAsync($"{appUrl}/entities/Orders/report-fields?Type=Grid")
            };
        }
    }
}
