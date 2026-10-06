using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Data.Helper.Models;
using Apilane.Data.Repository.Factory;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Files;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The page size of the list endpoints (Data/Get, Files/Get, Data/GetHistoryByID and Stats/Aggregate) is 1 to
    /// 1000: a smaller or larger value is replaced by 1000, and a page size of 0 is no exception. The repositories
    /// add no LIMIT for a page size of 0 or less, so a request for 0 used to return every record.
    /// More records than one page can hold are written straight to the application's database (not through the
    /// API), then every page size is asked for through the API.
    /// </summary>
    public class PageSizeTests : AppicationTestsBase
    {
        private const string ItemEntity = "PagedItem";
        private const int MaxPageSize = 1000;

        // More than one page of 1000, so that a page cut at 1000 is told from "every record"
        private const int SeededRecords = MaxPageSize + 5;

        public PageSizeTests() : base(SuiteContext.Shared)
        {
        }

        private class PagedItem : DataItem
        {
            public int Seq { get; set; }
        }

        private class PagedItemHistory
        {
            public int Seq { get; set; }
            public long History_Record_Created { get; set; }
        }

        /// <summary>The records of one page, or the reason it could not be read.</summary>
        private record PageResult(int? Rows, long? Total, string? Error);

        // ─── Data/Get ─────────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task DataGet_PageSizeZero_Should_Return_A_Page_Of_At_Most_1000_Records(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType, SeededRecords);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(PagedItem.Seq)))
            {
                await AssertPagesAsync("Data/Get", GetDataPageAsync, SeededRecords, (0, MaxPageSize));
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task DataGet_PageSize_Should_Be_Held_To_The_Range_1_To_1000(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType, SeededRecords);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(PagedItem.Seq)))
            {
                await AssertPagesAsync(
                    "Data/Get",
                    GetDataPageAsync,
                    SeededRecords,
                    (1, 1), (20, 20), (MaxPageSize, MaxPageSize),
                    // Larger and negative values are replaced by 1000, as documented
                    (MaxPageSize + 1, MaxPageSize), (100000, MaxPageSize), (-1, MaxPageSize));
            }
        }

        // The documented way to read more than 1000 records: the next page. A page size of 0 is read as 1000, so
        // the second page starts after the first 1000 records and holds the rest
        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task DataGet_PageSizeZero_SecondPage_Should_Return_The_Remaining_Records(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType, SeededRecords);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(PagedItem.Seq)))
            {
                var result = await ApilaneService.GetDataTotalAsync<PagedItem>(
                    DataGetListRequest.New(ItemEntity).WithPageSize(0).WithPageIndex(2));

                var page = result.Match(
                    response => new PageResult(response.Data.Count, response.Total, null),
                    error => new PageResult(null, null, $"{error.Code} {error.Message}"));

                Assert.Null(page.Error);
                Assert.Equal(SeededRecords - MaxPageSize, page.Rows);
                Assert.Equal(SeededRecords, page.Total);
            }
        }

        // ─── Files/Get ────────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task FilesGet_PageSizeZero_Should_Return_A_Page_Of_At_Most_1000_Records(DatabaseType dbType)
        {
            await PrepareFilesAsync(dbType, SeededRecords);

            using (GrantAccess("Files", SecurityActionType.get, nameof(FileItem.Name), nameof(FileItem.UID), nameof(FileItem.Size)))
            {
                await AssertPagesAsync("Files/Get", GetFilesPageAsync, null, (0, MaxPageSize));
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task FilesGet_PageSize_Should_Be_Held_To_The_Range_1_To_1000(DatabaseType dbType)
        {
            await PrepareFilesAsync(dbType, SeededRecords);

            using (GrantAccess("Files", SecurityActionType.get, nameof(FileItem.Name), nameof(FileItem.UID), nameof(FileItem.Size)))
            {
                await AssertPagesAsync(
                    "Files/Get",
                    GetFilesPageAsync,
                    null,
                    (1, 1), (20, 20), (MaxPageSize, MaxPageSize),
                    (MaxPageSize + 1, MaxPageSize), (100000, MaxPageSize), (-1, MaxPageSize));
            }
        }

        // ─── Data/GetHistoryByID ──────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task GetHistoryById_PageSizeZeroOrLess_Should_Return_A_Page_Of_At_Most_1000_Records(DatabaseType dbType)
        {
            var recordId = await PrepareHistoryAsync(dbType, SeededRecords);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(PagedItem.Seq)))
            {
                await AssertPagesAsync(
                    "Data/GetHistoryByID",
                    pageSize => GetHistoryPageAsync(recordId, pageSize),
                    SeededRecords,
                    (0, MaxPageSize), (-1, MaxPageSize));
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task GetHistoryById_PageSize_Should_Be_Held_To_The_Range_1_To_1000(DatabaseType dbType)
        {
            var recordId = await PrepareHistoryAsync(dbType, SeededRecords);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(PagedItem.Seq)))
            {
                await AssertPagesAsync(
                    "Data/GetHistoryByID",
                    pageSize => GetHistoryPageAsync(recordId, pageSize),
                    SeededRecords,
                    (1, 1), (10, 10), (MaxPageSize, MaxPageSize),
                    (MaxPageSize + 1, MaxPageSize), (100000, MaxPageSize));
            }
        }

        // The Portal reads the history of a record from this owner-only endpoint, which shares the code of the one above
        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task EntityHistoryGet_PageSize_Should_Be_Held_To_The_Range_1_To_1000(DatabaseType dbType)
        {
            var recordId = await PrepareHistoryAsync(dbType, SeededRecords);

            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                await AssertPagesAsync(
                    "EntityHistory/Get",
                    pageSize => GetOwnerHistoryPageAsync(recordId, pageSize),
                    SeededRecords,
                    (1, 1), (100, 100), (MaxPageSize, MaxPageSize),
                    (MaxPageSize + 1, MaxPageSize), (100000, MaxPageSize),
                    (0, MaxPageSize), (-1, MaxPageSize));
            }
        }

        // ─── Stats/Aggregate ──────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task StatsAggregate_PageSizeZero_Should_Return_A_Page_Of_At_Most_1000_Groups(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType, SeededRecords);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(PagedItem.Seq)))
            {
                // Every record has its own Seq, so there are as many groups as records
                await AssertPagesAsync("Stats/Aggregate", GetAggregatePageAsync, null, (0, MaxPageSize));
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task StatsAggregate_PageSize_Should_Be_Held_To_The_Range_1_To_1000(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType, SeededRecords);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(PagedItem.Seq)))
            {
                await AssertPagesAsync(
                    "Stats/Aggregate",
                    GetAggregatePageAsync,
                    null,
                    (1, 1), (20, 20), (MaxPageSize, MaxPageSize),
                    (MaxPageSize + 1, MaxPageSize), (100000, MaxPageSize), (-1, MaxPageSize));
            }
        }

        // ─── Defaults ─────────────────────────────────────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task List_Without_PageSize_Should_Return_20_Records_And_The_History_10(DatabaseType dbType)
        {
            // Data/Get takes 20 when the page size is left out; the history of a record takes 10
            var recordId = await PrepareHistoryAsync(dbType, 30);
            await SeedAsync(ItemEntity, 30, i => new Dictionary<string, object?> { ["Created"] = 1L, ["Seq"] = (long)i });

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(PagedItem.Seq)))
            {
                Assert.Equal(20, await CountRowsOfRawGetAsync($"/api/Data/Get?entity={ItemEntity}"));
                Assert.Equal(10, await CountRowsOfRawGetAsync($"/api/Data/GetHistoryByID?entity={ItemEntity}&id={recordId}"));
            }
        }

        // ─── Pages of the endpoints ───────────────────────────────────────────────

        private async Task<PageResult> GetDataPageAsync(int pageSize)
        {
            var result = await ApilaneService.GetDataTotalAsync<PagedItem>(DataGetListRequest.New(ItemEntity).WithPageSize(pageSize));

            return result.Match(
                response => new PageResult(response.Data.Count, response.Total, null),
                error => new PageResult(null, null, $"{error.Code} {error.Message}"));
        }

        private async Task<PageResult> GetFilesPageAsync(int pageSize)
        {
            var result = await ApilaneService.GetFilesAsync<FileItem>(FileGetListRequest.New().WithPageSize(pageSize));

            return result.Match(
                response => new PageResult(response.Data.Count, null, null),
                error => new PageResult(null, null, $"{error.Code} {error.Message}"));
        }

        private async Task<PageResult> GetHistoryPageAsync(long recordId, int pageSize)
        {
            var result = await ApilaneService.GetHistoryByIdAsync<PagedItemHistory>(DataGetHistoryByIdRequest.New(ItemEntity, recordId).WithPageSize(pageSize));

            return result.Match(
                response => new PageResult(response.Data.Count, response.Total, null),
                error => new PageResult(null, null, $"{error.Code} {error.Message}"));
        }

        private async Task<PageResult> GetOwnerHistoryPageAsync(long recordId, int pageSize)
        {
            var response = await HttpClient.GetAsync($"/api/EntityHistory/Get?entity={ItemEntity}&recordID={recordId}&pageIndex=1&pageSize={pageSize}");
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new PageResult(null, null, $"{(int)response.StatusCode} {body}");
            }

            using var document = JsonDocument.Parse(body);
            var data = document.RootElement.EnumerateObject().Single(x => x.Name.Equals("Data", StringComparison.OrdinalIgnoreCase));
            var total = document.RootElement.EnumerateObject().Single(x => x.Name.Equals("Total", StringComparison.OrdinalIgnoreCase));

            return new PageResult(data.Value.GetArrayLength(), total.Value.GetInt64(), null);
        }

        private async Task<PageResult> GetAggregatePageAsync(int pageSize)
        {
            var result = await ApilaneService.GetStatsAggregateAsync(StatsAggregateRequest.New(ItemEntity)
                .WithGroupBy(nameof(PagedItem.Seq))
                .WithProperty(nameof(PagedItem.Seq), StatsAggregateRequest.DataAggregates.Count)
                .WithPageSize(pageSize));

            return result.Match(
                response =>
                {
                    using var document = JsonDocument.Parse(response);
                    return new PageResult(document.RootElement.GetArrayLength(), null, null);
                },
                error => new PageResult(null, null, $"{error.Code} {error.Message}"));
        }

        /// <summary>
        /// Asks for each page size and collects what does not match, so that one failure names every page size
        /// that is wrong. <paramref name="expectedTotal"/>: the total that the endpoint reports, when it reports one.
        /// </summary>
        private static async Task AssertPagesAsync(
            string endpoint,
            Func<int, Task<PageResult>> getPage,
            long? expectedTotal,
            params (int PageSize, int ExpectedRows)[] cases)
        {
            var mismatches = new List<string>();

            foreach (var (pageSize, expectedRows) in cases)
            {
                var page = await getPage(pageSize);

                if (page.Error is not null)
                {
                    mismatches.Add($"pageSize={pageSize}: expected {expectedRows} records, got the error {page.Error}");
                }
                else if (page.Rows != expectedRows)
                {
                    mismatches.Add($"pageSize={pageSize}: expected {expectedRows} records, got {page.Rows}");
                }
                else if (expectedTotal.HasValue && page.Total != expectedTotal)
                {
                    mismatches.Add($"pageSize={pageSize}: expected the total {expectedTotal}, got {page.Total}");
                }
            }

            Assert.True(mismatches.Count == 0, $"{endpoint} | {string.Join(" | ", mismatches)}");
        }

        /// <summary>A request that leaves the page size out: the records of the page, counted from the raw answer.</summary>
        private async Task<int> CountRowsOfRawGetAsync(string pathAndQuery)
        {
            // The SDK client carries the application token of the test
            var response = await HttpClient.GetAsync(pathAndQuery);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{pathAndQuery} | {response.StatusCode} | {body}");

            using var document = JsonDocument.Parse(body);
            var data = document.RootElement.EnumerateObject().Single(x => x.Name.Equals("Data", StringComparison.OrdinalIgnoreCase));

            return data.Value.GetArrayLength();
        }

        // ─── Application and data ─────────────────────────────────────────────────

        private WithSecurityAccess GrantAccess(string entity, SecurityActionType action, params string[] properties)
        {
            return new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, entity,
                inRole: Globals.ANONYMOUS,
                actionType: action,
                properties: properties.ToList());
        }

        private async Task PrepareItemsAsync(DatabaseType dbType, int records)
        {
            await InitializeApplicationAsync(dbType, null, false);
            await AddEntityAsync(ItemEntity);
            await AddNumberPropertyAsync(ItemEntity, nameof(PagedItem.Seq), required: false);

            await SeedAsync(ItemEntity, records, i => new Dictionary<string, object?> { ["Created"] = 1L, ["Seq"] = (long)i });
        }

        private async Task PrepareFilesAsync(DatabaseType dbType, int records)
        {
            await InitializeApplicationAsync(dbType, null, false);

            // Rows of the Files entity are made by an upload, one file at a time: the rows are written
            // directly, there is no stored file behind them (the list does not read it)
            await SeedAsync("Files", records, i => new Dictionary<string, object?>
            {
                ["Name"] = $"file{i}.txt",
                ["UID"] = Guid.NewGuid().ToString("N"),
                ["Size"] = 1m,
                ["Created"] = 1L
            });
        }

        /// <summary>A record with <paramref name="historyRows"/> snapshots in its history. Returns the record id.</summary>
        private async Task<long> PrepareHistoryAsync(DatabaseType dbType, int historyRows)
        {
            await InitializeApplicationAsync(dbType, null, false);
            await AddEntityAsync(ItemEntity, requireChangeTracking: true);
            await AddNumberPropertyAsync(ItemEntity, nameof(PagedItem.Seq), required: false);

            var recordId = await SeedAsync(ItemEntity, 1, i => new Dictionary<string, object?> { ["Created"] = 1L, ["Seq"] = 0L });

            await SeedAsync(nameof(H_Entity_Change_Tracking), historyRows, i => new Dictionary<string, object?>
            {
                [nameof(H_Entity_Change_Tracking.Entity)] = ItemEntity,
                [nameof(H_Entity_Change_Tracking.RecordID)] = recordId,
                [nameof(H_Entity_Change_Tracking.Data)] = JsonSerializer.Serialize(new Dictionary<string, object?> { ["Seq"] = i }),
                [nameof(H_Entity_Change_Tracking.Created)] = (long)i
            });

            return recordId;
        }

        /// <summary>
        /// Writes rows to a table of the application's database through a connection of its own, outside the API.
        /// Returns the id of the first row.
        /// </summary>
        private async Task<long> SeedAsync(string table, int count, Func<int, Dictionary<string, object?>> row)
        {
            await using var store = OpenStore();

            long? firstId = null;

            for (var i = 1; i <= count; i++)
            {
                var id = await store.CreateDataAsync(table, row(i), allowInsertIdentity: false);
                firstId ??= id;
            }

            return firstId ?? throw new Exception($"No row was written to {table}");
        }

        private ApplicationDataStoreFactory OpenStore()
        {
            return new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));
        }
    }
}
