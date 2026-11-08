using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Data.Repository.Factory;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Models.Files;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The read endpoints that take a filter (Data/Get, Files/Get, Stats/Aggregate, Stats/Distinct) also accept the
    /// HTTP QUERY method (RFC 10008), its parameters in a JSON body. The SDK sends QUERY when the address of the GET
    /// would be longer than <see cref="DataGetListRequest.MaxGetUrlLength"/>, so a filter too long for a URL works.
    /// </summary>
    public class QueryMethodTests : AppicationTestsBase
    {
        private const string ItemEntity = "QueriedItem";
        private const int SeededRecords = 10;

        // The API refuses request lines above 8 KB
        private const int MaxRequestLine = 8192;

        private static readonly HttpMethod Query = new("QUERY");

        public QueryMethodTests() : base(SuiteContext.Shared)
        {
        }

        private class QueriedItem : DataItem
        {
            public int Seq { get; set; }
        }

        /// <summary>
        /// The odd numbers up to 4001 as an "IN" filter on <paramref name="property"/>: about 10 KB, too long for a
        /// URL. Of the seeded records 1 to 10 it matches 1, 3, 5, 7 and 9.
        /// </summary>
        private static FilterItem LongOddFilter(string property)
        {
            var odd = Enumerable.Range(0, 2001).Select(i => (2 * i) + 1);
            return new FilterItem(property, FilterOperator.contains, string.Join(",", odd));
        }

        private static readonly int[] OddSeq = { 1, 3, 5, 7, 9 };

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task DataGet_With_A_Filter_Too_Long_For_A_Url_Should_Be_Sent_As_Query(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(QueriedItem.Seq)))
            {
                var request = DataGetListRequest.New(ItemEntity)
                    .WithFilter(LongOddFilter(nameof(QueriedItem.Seq)))
                    .WithSort(new SortItem { Property = nameof(QueriedItem.Seq), Direction = "desc" })
                    .WithProperties(nameof(QueriedItem.Seq));

                // As a GET this request could not be sent
                Assert.True(request.GetUrl("http://localhost").Length > MaxRequestLine);

                var result = await ApilaneService.GetDataTotalAsync<QueriedItem>(request);
                var response = result.Match(r => r, e => throw new Exception($"{e.Code} {e.Message}"));

                Assert.Equal(OddSeq.Length, response.Total);
                Assert.Equal(OddSeq.Reverse(), response.Data.Select(x => x.Seq));
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task DataGet_Short_Request_Should_Stay_Get(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(QueriedItem.Seq)))
            {
                var request = DataGetListRequest.New(ItemEntity)
                    .WithFilter(new FilterItem(nameof(QueriedItem.Seq), FilterOperator.contains, "1,2"));

                Assert.True(request.GetUrl("http://localhost").Length <= DataGetListRequest.MaxGetUrlLength);

                var result = await ApilaneService.GetDataTotalAsync<QueriedItem>(request);
                Assert.Equal(2, result.Match(r => r.Total, e => throw new Exception($"{e.Code} {e.Message}")));
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task DataGet_Query_Body_Should_Take_Json_Or_Strings(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(QueriedItem.Seq)))
            {
                var filter = new { Property = nameof(QueriedItem.Seq), Operator = "greater", Value = 7 };
                var sort = new { Property = nameof(QueriedItem.Seq), Direction = "asc" };

                // The filter and the sort as JSON, the properties as an array
                var asJson = await SendQueryAsync($"/api/Data/Get?entity={ItemEntity}", new
                {
                    filter,
                    sort,
                    properties = new[] { nameof(QueriedItem.Seq) },
                    getTotal = true,
                    pageSize = 2
                });

                // The same as the strings of the query string
                var asStrings = await SendQueryAsync($"/api/Data/Get?entity={ItemEntity}", new
                {
                    filter = JsonSerializer.Serialize(filter),
                    sort = JsonSerializer.Serialize(sort),
                    properties = nameof(QueriedItem.Seq),
                    getTotal = true,
                    pageSize = 2
                });

                foreach (var body in new[] { asJson, asStrings })
                {
                    using var document = JsonDocument.Parse(body);
                    Assert.Equal(3, Property(document.RootElement, "Total").GetInt64());
                    Assert.Equal(
                        new[] { 8, 9 },
                        Property(document.RootElement, "Data").EnumerateArray().Select(x => Property(x, "Seq").GetInt32()));
                }
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task DataGet_Query_Without_A_Body_Should_Use_The_Defaults(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(QueriedItem.Seq)))
            {
                using var request = new HttpRequestMessage(Query, $"/api/Data/Get?entity={ItemEntity}");
                var response = await HttpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode} | {body}");

                using var document = JsonDocument.Parse(body);
                Assert.Equal(SeededRecords, Property(document.RootElement, "Data").GetArrayLength());
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task DataGet_Query_Should_Apply_The_Security_Of_Get(DatabaseType dbType)
        {
            // No access is granted: QUERY is refused as GET is
            await PrepareItemsAsync(dbType);

            var result = await ApilaneService.GetDataAsync<QueriedItem>(
                DataGetListRequest.New(ItemEntity).WithFilter(LongOddFilter(nameof(QueriedItem.Seq))));

            Assert.True(result.HasError(out var error));
            Assert.Equal(ValidationError.UNAUTHORIZED, error.Code);
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Query_Should_Not_Be_Accepted_By_Other_Endpoints(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using var request = new HttpRequestMessage(Query, $"/api/Data/GetByID?entity={ItemEntity}&id=1");
            var response = await HttpClient.SendAsync(request);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task FilesGet_With_A_Filter_Too_Long_For_A_Url_Should_Be_Sent_As_Query(DatabaseType dbType)
        {
            await InitializeApplicationAsync(dbType, null, false);
            var ids = new List<long>();
            for (var i = 1; i <= SeededRecords; i++)
            {
                ids.Add(await SeedAsync("Files", new Dictionary<string, object?>
                {
                    ["Name"] = $"file{i}.txt",
                    ["UID"] = Guid.NewGuid().ToString("N"),
                    ["Size"] = 1m,
                    ["Created"] = 1L
                }));
            }

            using (GrantAccess("Files", SecurityActionType.get, nameof(FileItem.Name), nameof(FileItem.UID), nameof(FileItem.Size)))
            {
                // The first three files, and ids no file has, enough of them to be too long for a URL
                var wanted = ids.Take(3).Concat(Enumerable.Range(1, 2000).Select(i => ids.Max() + i));
                var request = FileGetListRequest.New()
                    .WithFilter(new FilterItem(Globals.PrimaryKeyColumn, FilterOperator.contains, string.Join(",", wanted)));

                Assert.True(request.GetUrl("http://localhost").Length > MaxRequestLine);

                var result = await ApilaneService.GetFilesAsync<FileItem>(request);
                var files = result.Match(r => r.Data, e => throw new Exception($"{e.Code} {e.Message}"));

                Assert.Equal(ids.Take(3).OrderBy(x => x), files.Select(x => x.ID).OrderBy(x => x));
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Stats_With_A_Filter_Too_Long_For_A_Url_Should_Be_Sent_As_Query(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(QueriedItem.Seq)))
            {
                var aggregate = StatsAggregateRequest.New(ItemEntity)
                    .WithProperty(nameof(QueriedItem.Seq), StatsAggregateRequest.DataAggregates.Sum)
                    .WithFilter(LongOddFilter(nameof(QueriedItem.Seq)));

                Assert.True(aggregate.GetUrl("http://localhost").Length > MaxRequestLine);

                var aggregateResult = await ApilaneService.GetStatsAggregateAsync(aggregate);
                using (var document = JsonDocument.Parse(aggregateResult.Match(r => r, e => throw new Exception($"{e.Code} {e.Message}"))))
                {
                    var row = document.RootElement.EnumerateArray().Single();
                    Assert.Equal(OddSeq.Sum(), row.EnumerateObject().Single().Value.GetDecimal());
                }

                var distinct = StatsDistinctRequest.New(ItemEntity, nameof(QueriedItem.Seq))
                    .WithFilter(LongOddFilter(nameof(QueriedItem.Seq)));

                var distinctResult = await ApilaneService.GetStatsDistinctAsync(distinct);
                using (var document = JsonDocument.Parse(distinctResult.Match(r => r, e => throw new Exception($"{e.Code} {e.Message}"))))
                {
                    var values = document.RootElement.EnumerateArray().Select(x => x.EnumerateObject().Single().Value.GetInt32());
                    Assert.Equal(OddSeq, values.OrderBy(x => x));
                }
            }
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Stats_Query_Should_Require_Its_Properties(DatabaseType dbType)
        {
            await PrepareItemsAsync(dbType);

            using (GrantAccess(ItemEntity, SecurityActionType.get, nameof(QueriedItem.Seq)))
            {
                foreach (var path in new[] { "Aggregate", "Distinct" })
                {
                    using var request = new HttpRequestMessage(Query, $"/api/Stats/{path}?entity={ItemEntity}")
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };

                    var response = await HttpClient.SendAsync(request);
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                }
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private async Task<string> SendQueryAsync(string pathAndQuery, object body)
        {
            using var request = new HttpRequestMessage(Query, pathAndQuery)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };

            // The SDK client carries the application token of the test
            var response = await HttpClient.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"QUERY {pathAndQuery} | {response.StatusCode} | {text}");
            return text;
        }

        private static JsonElement Property(JsonElement element, string name)
        {
            return element.EnumerateObject().Single(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        private WithSecurityAccess GrantAccess(string entity, SecurityActionType action, params string[] properties)
        {
            return new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, entity,
                inRole: Globals.ANONYMOUS,
                actionType: action,
                properties: properties.ToList());
        }

        private async Task PrepareItemsAsync(DatabaseType dbType)
        {
            await InitializeApplicationAsync(dbType, null, false);
            await AddEntityAsync(ItemEntity);
            await AddNumberPropertyAsync(ItemEntity, nameof(QueriedItem.Seq), required: false);

            for (var i = 1; i <= SeededRecords; i++)
            {
                await SeedAsync(ItemEntity, new Dictionary<string, object?> { ["Created"] = 1L, ["Seq"] = (long)i });
            }
        }

        /// <summary>Writes a row to a table of the application's database outside the API. Returns its id.</summary>
        private async Task<long> SeedAsync(string table, Dictionary<string, object?> row)
        {
            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));
            return await store.CreateDataAsync(table, row, allowInsertIdentity: false)
                ?? throw new Exception($"No row was written to {table}");
        }
    }
}
