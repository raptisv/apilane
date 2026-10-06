using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Data.Repository.Factory;
using Apilane.Net.Request;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The records that the clone with data and the data import insert with the IDs they come with
    /// (<c>/api/Application/ImportData</c>, which the Portal's clone calls once per page of the source
    /// records). The next record the application creates itself takes its ID from the table's sequence, and
    /// must get a new one on every database: PostgreSQL's BIGSERIAL does not move when an ID is given, so the
    /// first record created after an import used to collide with an imported one. The tests read the
    /// database through an independent connection, never through the API.
    /// </summary>
    public class ImportDataIdSequenceTests : TransactionScopeTestsBase
    {
        // ─── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// One import request, with the rows in the shape of the clone's: every column the source returned,
        /// the ID included.
        /// </summary>
        private async Task<List<long>> ImportAsync(params long[] ids)
        {
            var rows = ids
                .Select(id => new Dictionary<string, object?>
                {
                    ["ID"] = id,
                    [Globals.CreatedColumn] = 1L,
                    ["Name"] = $"imported {id}"
                })
                .ToList();

            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var response = await HttpClient.RequestAsync(
                    HttpMethod.Post,
                    $"/api/Application/ImportData?appToken={TestApplication.Token}&Entity={ItemEntity}",
                    rows);

                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, $"Import failed | {response.StatusCode} | {body}");

                return body.DeserializeTo<List<long>>() ?? throw new Exception($"Invalid response of the import | {body}");
            }
        }

        /// <summary>A record created the normal way, whose ID comes from the table's sequence.</summary>
        private async Task<long> PostAsync(string name)
        {
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { "Name" }))
            {
                var result = await ApilaneService.PostDataAsync(DataPostRequest.New(ItemEntity), new { Name = name });

                return result.Match(
                    ids => ids.Single(),
                    e => throw new Exception($"Post of '{name}' failed | {e.Code} | {e.Message}"));
            }
        }

        private async Task<List<long>> ReadIdsAsync()
        {
            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));
            var rows = await store.GetPagedDataAsync(ItemEntity, null, null, null, 1, 1000);

            return rows
                .Select(row => Convert.ToInt64(row["ID"]))
                .OrderBy(id => id)
                .ToList();
        }

        private async Task DeleteAllAsync()
        {
            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));
            await store.DeleteDataAsync(ItemEntity, null);
        }

        // ─── The sequence moves past the imported IDs ─────────────────────────────

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Post_After_Import_With_Explicit_Ids_Should_Get_A_New_Id(DatabaseType dbType)
        {
            await PrepareItemEntityAsync(dbType);

            // IDs with a gap, as a source application that had deleted records has
            await ImportAsync(1, 2, 5);
            Assert.Equal(new long[] { 1, 2, 5 }, await ReadIdsAsync());

            var first = await PostAsync("first");
            var second = await PostAsync("second");

            Assert.True(first > 5, $"The new record got ID {first}, which is not past the imported IDs");
            Assert.True(second > first, $"The next record got ID {second} after {first}");
            Assert.Equal(new long[] { 1, 2, 5, first, second }, await ReadIdsAsync());
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Post_After_Import_In_Several_Batches_Should_Get_A_New_Id(DatabaseType dbType)
        {
            await PrepareItemEntityAsync(dbType);

            // The clone sends a page of records at a time, and the application is used in between
            await ImportAsync(10, 11, 12);
            var first = await PostAsync("first");
            Assert.True(first > 12, $"The record after the first batch got ID {first}");

            await ImportAsync(100, 101);
            var second = await PostAsync("second");
            Assert.True(second > 101, $"The record after the second batch got ID {second}");

            Assert.Equal(new long[] { 10, 11, 12, first, 100, 101, second }.OrderBy(id => id), await ReadIdsAsync());
        }

        // ─── What the fix must not break ─────────────────────────────────────────

        /// <summary>
        /// Controls for a fix that sets the sequence to the largest ID of the table: it must never move the
        /// sequence backwards. Records 1 to 3 were created and deleted, so the sequence is at 3 and the table
        /// holds no ID that large; importing ID 2 must not make the next record take a deleted record's ID again.
        /// </summary>
        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Post_After_Import_Of_Ids_Below_The_Sequence_Should_Not_Reuse_Deleted_Ids(DatabaseType dbType)
        {
            await PrepareItemEntityAsync(dbType);

            Assert.Equal(1, await PostAsync("a"));
            Assert.Equal(2, await PostAsync("b"));
            Assert.Equal(3, await PostAsync("c"));
            await DeleteAllAsync();

            await ImportAsync(2);
            var id = await PostAsync("after");

            Assert.True(id > 3, $"The new record got ID {id}, the ID of a deleted record");
            Assert.Equal(new long[] { 2, id }, await ReadIdsAsync());
        }

        /// <summary>
        /// Control for a fix that reads the largest ID of the table: an import without rows leaves an empty
        /// table, and the sequence must stay where it was (the first record is still number 1).
        /// </summary>
        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Post_After_Import_Without_Rows_Should_Start_At_One(DatabaseType dbType)
        {
            await PrepareItemEntityAsync(dbType);

            Assert.Empty(await ImportAsync());

            Assert.Equal(1, await PostAsync("first"));
        }

        // ─── The repository method the import calls ───────────────────────────────

        private static Dictionary<string, object?> ItemRow(long? id, string name)
        {
            var row = new Dictionary<string, object?> { [Globals.CreatedColumn] = 1L, ["Name"] = name };

            if (id.HasValue)
            {
                row["ID"] = id.Value;
            }

            return row;
        }

        /// <summary>
        /// The repository on its own, without the import: records inserted with their IDs, then the call the
        /// import makes once per batch, then a record that takes its ID from the generator.
        /// </summary>
        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task SyncIdSequence_After_Explicit_Id_Inserts_Should_Move_Past_The_Largest_Id(DatabaseType dbType)
        {
            await PrepareItemEntityAsync(dbType);

            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));

            foreach (var id in new long[] { 7, 3, 20 })
            {
                await store.CreateDataAsync(ItemEntity, ItemRow(id, $"imported {id}"), allowInsertIdentity: true);
            }

            await store.SyncIdSequenceAsync(ItemEntity);
            await store.SyncIdSequenceAsync(ItemEntity); // calling it again changes nothing

            var first = await store.CreateDataAsync(ItemEntity, ItemRow(null, "first"), allowInsertIdentity: false);
            var second = await store.CreateDataAsync(ItemEntity, ItemRow(null, "second"), allowInsertIdentity: false);

            Assert.Equal(21, first);
            Assert.Equal(22, second);
        }

        /// <summary>An empty table has no largest ID: the generator must stay where it is.</summary>
        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task SyncIdSequence_On_An_Empty_Table_Should_Not_Move_The_Sequence(DatabaseType dbType)
        {
            await PrepareItemEntityAsync(dbType);

            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));

            await store.SyncIdSequenceAsync(ItemEntity);

            Assert.Equal(1, await store.CreateDataAsync(ItemEntity, ItemRow(null, "first"), allowInsertIdentity: false));
        }

        // ─── SQL Server: SET IDENTITY_INSERT is switched back off when the insert fails ───

        /// <summary>
        /// A repository keeps one connection for its life, and SET IDENTITY_INSERT is a setting of that
        /// connection's session: after an explicit-ID insert that failed, the next insert on the same
        /// connection, which gives no ID, must still work. The other databases are the control.
        /// </summary>
        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Insert_After_A_Failed_Explicit_Id_Insert_Should_Work_On_The_Same_Connection(DatabaseType dbType)
        {
            await PrepareItemEntityAsync(dbType);

            await using var store = new ApplicationDataStoreFactory(TestApplication.ToDbInfo(ApiConfiguration.FilesPath));

            // An ID far from the first one the sequence hands out, so that the next insert cannot collide with it
            await store.CreateDataAsync(ItemEntity, ItemRow(100, "imported"), allowInsertIdentity: true);

            // The same ID again: a duplicate key
            await Assert.ThrowsAnyAsync<Exception>(() => store.CreateDataAsync(ItemEntity, ItemRow(100, "duplicate"), allowInsertIdentity: true));

            var id = await store.CreateDataAsync(ItemEntity, ItemRow(null, "normal"), allowInsertIdentity: false);

            Assert.NotNull(id);
        }
    }
}
