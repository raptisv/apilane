using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// The batch endpoints (Transaction and TransactionOperations) are all-or-nothing: when an operation
    /// fails, the operations before it must be undone. The first request of an application after the API
    /// restarted reaches the batch's transaction scope with the store's connection already open.
    /// </summary>
    public class BatchTransactionScopeTests : TransactionScopeTestsBase
    {
        /// <summary>
        /// The control of the tests below: a batch whose operations all succeed is kept, so that "no rows" after
        /// a failing batch is the rollback and not a request that never reached the database.
        /// </summary>
        [Theory]
        [ClassData(typeof(FirstCallTestData))]
        public async Task Transaction_With_All_Operations_Succeeding_Should_Commit(DatabaseType dbType, bool firstCallAfterRestart)
        {
            await PrepareItemEntityAsync(dbType);
            ArrangeApiProcess(firstCallAfterRestart);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { "Name" }))
            {
                var data = new InTransactionData()
                {
                    Post = new List<InTransactionData.InTransactionSet>()
                    {
                        new InTransactionData.InTransactionSet() { Entity = ItemEntity, Data = new { Name = "first" } },
                        new InTransactionData.InTransactionSet() { Entity = ItemEntity, Data = new { Name = "second" } }
                    }
                };

                var result = await ApilaneService.TransactionDataAsync(DataTransactionRequest.New(), data);

                Assert.True(result.Match(_ => true, _ => false), "The transaction should have succeeded");
            }

            AssertApiProcessKnowsTheApplication();

            Assert.Equal(2, await CountRowsAsync(ItemEntity));
        }

        [Theory]
        [ClassData(typeof(FirstCallTestData))]
        public async Task Transaction_With_A_Failing_Later_Operation_Should_Roll_Back_The_Earlier_Ones(DatabaseType dbType, bool firstCallAfterRestart)
        {
            await PrepareItemEntityAsync(dbType);
            ArrangeApiProcess(firstCallAfterRestart);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { "Name" }))
            {
                var data = new InTransactionData()
                {
                    Post = new List<InTransactionData.InTransactionSet>()
                    {
                        new InTransactionData.InTransactionSet() { Entity = ItemEntity, Data = new { Name = "first" } }
                    },
                    // Fails after the post: the entity does not exist
                    Put = new List<InTransactionData.InTransactionSet>()
                    {
                        new InTransactionData.InTransactionSet() { Entity = "NoSuchEntity", Data = new { ID = 1, Name = "x" } }
                    }
                };

                var result = await ApilaneService.TransactionDataAsync(DataTransactionRequest.New(), data);

                Assert.True(result.Match(_ => false, _ => true), "The transaction should have failed");
            }

            AssertApiProcessKnowsTheApplication();

            Assert.Equal(0, await CountRowsAsync(ItemEntity));
        }

        [Theory]
        [ClassData(typeof(FirstCallTestData))]
        public async Task TransactionOperations_With_A_Failing_Custom_Operation_Should_Roll_Back_The_Post(DatabaseType dbType, bool firstCallAfterRestart)
        {
            await PrepareItemEntityAsync(dbType);

            // Breaks the NOT NULL rule of the Name column
            const string endpoint = "FailingInsert";
            AddCustomEndpoint(endpoint, InsertItemSql(dbType, "NULL"));

            ArrangeApiProcess(firstCallAfterRestart);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { "Name" }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, endpoint,
                inRole: Globals.ANONYMOUS,
                type: SecurityTypes.CustomEndpoint))
            {
                var transaction = new TransactionBuilder()
                    .Post(ItemEntity, new { Name = "first" })
                    .Custom(endpoint, new { unused = 1 })
                    .Build();

                var result = await ApilaneService.TransactionOperationsAsync(DataTransactionOperationsRequest.New(), transaction);

                Assert.True(result.Match(_ => false, _ => true), "The transaction should have failed");
            }

            AssertApiProcessKnowsTheApplication();

            Assert.Equal(0, await CountRowsAsync(ItemEntity));
        }
    }
}
