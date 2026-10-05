using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Helper.Models;
using Apilane.Data.Repository.Factory;
using Apilane.Data.Utilities;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// Base of the tests that look at what a request leaves in the database when the transaction scope it
    /// opened is not completed (a custom endpoint Test, a failing batch, a failing history write).
    /// The tests read the database through a second, independent connection, never through the API, so
    /// what they see is what was really committed.
    /// </summary>
    public abstract class TransactionScopeTestsBase : AppicationTestsBase, IAsyncLifetime
    {
        protected const string ItemEntity = "ScopeItem";
        protected const string HistoryTable = nameof(H_Entity_Change_Tracking);

        protected TransactionScopeTestsBase() : base(SuiteContext.Shared)
        {
        }

        /// <summary>
        /// Every database type, once as a request that reaches an API process which has already seen the
        /// application token (false) and once as the first request after the API restarted (true).
        /// </summary>
        protected class FirstCallTestData : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                foreach (var dbType in new[] { DatabaseType.SQLLite, DatabaseType.SQLServer, DatabaseType.MySQL, DatabaseType.PostgreSQL })
                {
                    yield return new object[] { dbType, false };
                    yield return new object[] { dbType, true };
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>Removes the application's tables and files, so a test leaves nothing behind.</summary>
        public async Task DisposeAsync()
        {
            if (TestApplication is null)
            {
                return;
            }

            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var response = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/Application/Degenerate?appToken={TestApplication.Token}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Could not remove the test application | {response.StatusCode} | {await response.Content.ReadAsStringAsync()}");
                }
            }
        }

        // ─── Application ──────────────────────────────────────────────────────────

        /// <summary>
        /// An application whose entity <see cref="ItemEntity"/> has a required string property Name.
        /// Custom statements fill the Created column themselves: it is a required BIGINT that has no
        /// default on MySQL.
        /// </summary>
        protected async Task PrepareItemEntityAsync(DatabaseType dbType, bool requireChangeTracking = false)
        {
            await InitializeApplicationAsync(dbType, null, false);
            await AddEntityAsync(ItemEntity, requireChangeTracking: requireChangeTracking);
            await AddStringPropertyAsync(ItemEntity, "Name", required: true);
        }

        /// <summary>
        /// Puts the API process in the state the test is about, right before the request under test.
        /// <paramref name="firstCallAfterRestart"/>: the API has not seen the application token yet, so the
        /// request finds the store's connection already open when its transaction scope starts. Otherwise
        /// the token is known (the requests that built the application registered it) and the connection
        /// is opened inside the scope.
        /// </summary>
        protected void ArrangeApiProcess(bool firstCallAfterRestart)
        {
            if (firstCallAfterRestart)
            {
                ApiProcessState.SimulateApiRestart(TestApplication.Token);
            }

            Assert.Equal(!firstCallAfterRestart, ApiProcessState.HasMigratedSystemTables(TestApplication.Token));
        }

        /// <summary>Proof that the request under test went through the first-request check.</summary>
        protected void AssertApiProcessKnowsTheApplication()
        {
            Assert.True(ApiProcessState.HasMigratedSystemTables(TestApplication.Token), "The request did not reach the application's first-request check");
        }

        // ─── SQL ──────────────────────────────────────────────────────────────────

        protected static string Quote(DatabaseType dbType, string identifier)
        {
            return SqlUtilis.QuoteIdentifier(identifier, dbType);
        }

        /// <summary>An INSERT into the item entity; <paramref name="nameLiteral"/> is a SQL literal ('abc' or NULL).</summary>
        protected static string InsertItemSql(DatabaseType dbType, string nameLiteral)
        {
            return $"INSERT INTO {Quote(dbType, ItemEntity)} ({Quote(dbType, "Created")}, {Quote(dbType, "Name")}) VALUES (1, {nameLiteral});";
        }

        protected static string CountItemsSql(DatabaseType dbType, string alias = "Cnt")
        {
            return $"SELECT COUNT(*) AS {Quote(dbType, alias)} FROM {Quote(dbType, ItemEntity)};";
        }

        // ─── Independent connection to the application's database ────────────────

        private ApplicationDataStoreFactory OpenStore()
        {
            return new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));
        }

        protected async Task<long> CountRowsAsync(string table, FilterData? filter = null)
        {
            await using var store = OpenStore();
            return await store.GetDataCountAsync(table, filter);
        }

        protected async Task<List<string>> ReadNamesAsync(string entity)
        {
            await using var store = OpenStore();
            var rows = await store.GetPagedDataAsync(entity, null, null, null, 1, 1000);

            return rows
                .Select(row => row["Name"]?.ToString() ?? string.Empty)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }

        protected async Task DropTableAsync(string table)
        {
            await using var store = OpenStore();
            await store.DropTableAsync(table);
        }

        /// <summary>Inserts an item and commits it at once, outside the API.</summary>
        protected async Task SeedItemAsync(string name)
        {
            await using var store = OpenStore();
            await store.CreateDataAsync(
                ItemEntity,
                new Dictionary<string, object?> { ["Created"] = 1L, ["Name"] = name },
                allowInsertIdentity: false);
        }

        // ─── Custom endpoint Test (the owner's "run this query" feature) ─────────

        protected async Task<(bool Succeeded, string Body)> TestQueryAsync(string query)
        {
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var response = await HttpClient.RequestAsync(
                    HttpMethod.Post,
                    $"/api/Custom/test?appToken={TestApplication.Token}",
                    new DBWS_CustomEndpoint { Name = "test", Query = query, AppID = TestApplication.ID });

                return (response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            }
        }

        /// <summary>The number a statement of the query selected as "Cnt", read from the result sets of a custom call.</summary>
        protected static long ReadCount(string resultSetsJson)
        {
            using var document = JsonDocument.Parse(resultSetsJson);

            foreach (var table in document.RootElement.EnumerateArray())
            {
                foreach (var row in table.EnumerateArray())
                {
                    foreach (var column in row.EnumerateObject())
                    {
                        if (column.Name.Equals("Cnt", StringComparison.OrdinalIgnoreCase))
                        {
                            return column.Value.GetInt64();
                        }
                    }
                }
            }

            throw new Exception($"No Cnt column in the result | {resultSetsJson}");
        }
    }
}
