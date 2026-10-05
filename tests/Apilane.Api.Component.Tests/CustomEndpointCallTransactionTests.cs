using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common.Enums;
using Apilane.Net.Request;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// A call of a custom endpoint runs in a transaction scope that is completed only when the whole query
    /// succeeded: a query that fails after its first statement must leave nothing behind, and one that
    /// succeeds must commit. The first request of an application after the API restarted reaches the scope
    /// with the store's connection already open.
    /// </summary>
    public class CustomEndpointCallTransactionTests : TransactionScopeTestsBase
    {
        [Theory]
        [ClassData(typeof(FirstCallTestData))]
        public async Task Call_Failing_After_A_First_Statement_Should_Leave_No_Change(DatabaseType dbType, bool firstCallAfterRestart)
        {
            await PrepareItemEntityAsync(dbType);

            // The second statement breaks the NOT NULL rule of the Name column
            const string endpoint = "InsertThenFail";
            AddCustomEndpoint(endpoint, $"{InsertItemSql(dbType, "'first'")} {InsertItemSql(dbType, "NULL")}");

            ArrangeApiProcess(firstCallAfterRestart);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, endpoint, type: SecurityTypes.CustomEndpoint))
            {
                var result = await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(endpoint));

                Assert.True(result.Match(_ => false, _ => true), "The call should have failed");
            }

            AssertApiProcessKnowsTheApplication();

            // The call failed, so the first statement must have been undone. (PostgreSQL runs the two
            // statements of one command as one implicit transaction, so it passes here even when the
            // scope does not cover the connection: the other tests of this suite cover it.)
            Assert.Equal(0, await CountRowsAsync(ItemEntity));
        }

        [Theory]
        [ClassData(typeof(FirstCallTestData))]
        public async Task Call_Succeeding_Should_Commit(DatabaseType dbType, bool firstCallAfterRestart)
        {
            await PrepareItemEntityAsync(dbType);

            const string endpoint = "InsertTwice";
            AddCustomEndpoint(endpoint, $"{InsertItemSql(dbType, "'a'")} {InsertItemSql(dbType, "'b'")}");

            ArrangeApiProcess(firstCallAfterRestart);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, endpoint, type: SecurityTypes.CustomEndpoint))
            {
                var result = await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(endpoint));

                Assert.True(result.Match(_ => true, error => false), "The call should have succeeded");
            }

            AssertApiProcessKnowsTheApplication();

            Assert.Equal(new[] { "a", "b" }, await ReadNamesAsync(ItemEntity));
        }
    }
}
