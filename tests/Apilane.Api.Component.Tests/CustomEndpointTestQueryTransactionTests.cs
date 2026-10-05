using Apilane.Common.Enums;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The owner can run a custom endpoint's query from the Portal before saving it ("Test"). The API runs it
    /// in a transaction scope that it never completes, so a Test must not change anything, whatever the
    /// query does. The first request of an application after the API restarted reaches the scope with the
    /// store's connection already open.
    /// </summary>
    public class CustomEndpointTestQueryTransactionTests : TransactionScopeTestsBase
    {
        [Theory]
        [ClassData(typeof(FirstCallTestData))]
        public async Task TestQuery_Insert_Should_Not_Persist(DatabaseType dbType, bool firstCallAfterRestart)
        {
            await PrepareItemEntityAsync(dbType);
            ArrangeApiProcess(firstCallAfterRestart);

            var (succeeded, body) = await TestQueryAsync($"{InsertItemSql(dbType, "'tested'")} {CountItemsSql(dbType)}");

            Assert.True(succeeded, $"The Test failed | {body}");
            AssertApiProcessKnowsTheApplication();

            // The insert ran inside the request: its own SELECT saw the row
            Assert.Equal(1, ReadCount(body));

            // A Test must never commit
            Assert.Equal(0, await CountRowsAsync(ItemEntity));
        }

        [Theory]
        [ClassData(typeof(FirstCallTestData))]
        public async Task TestQuery_Update_And_Delete_Should_Not_Persist(DatabaseType dbType, bool firstCallAfterRestart)
        {
            await PrepareItemEntityAsync(dbType);

            // Committed by an independent connection, not through the API (a request would register the
            // application token and the first-call case would no longer be the first call)
            await SeedItemAsync("seed1");
            await SeedItemAsync("seed2");

            ArrangeApiProcess(firstCallAfterRestart);

            var query = $"UPDATE {Quote(dbType, ItemEntity)} SET {Quote(dbType, "Name")} = 'changed' WHERE {Quote(dbType, "Name")} = 'seed1'; "
                + $"DELETE FROM {Quote(dbType, ItemEntity)} WHERE {Quote(dbType, "Name")} = 'seed2'; "
                + CountItemsSql(dbType);

            var (succeeded, body) = await TestQueryAsync(query);

            Assert.True(succeeded, $"The Test failed | {body}");
            AssertApiProcessKnowsTheApplication();

            // The statements ran inside the request: one row was left when it counted
            Assert.Equal(1, ReadCount(body));

            // A Test must never commit: both rows are as they were
            Assert.Equal(new[] { "seed1", "seed2" }, await ReadNamesAsync(ItemEntity));
        }
    }
}
