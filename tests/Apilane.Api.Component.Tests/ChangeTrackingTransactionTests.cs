using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Helper.Models;
using Apilane.Net.Request;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// An entity with change tracking writes a history row next to every update and delete, in a transaction
    /// scope of its own. When the second write fails, the first must be undone: the record may not change
    /// without its history, and a history snapshot may not stay for a record that was not deleted.
    /// These requests read the record before they open the scope, on every request, not only the first one
    /// after the API restarted.
    /// </summary>
    public class ChangeTrackingTransactionTests : TransactionScopeTestsBase
    {
        private const string ParentEntity = "TrackedParent";
        private const string ChildEntity = "TrackedChild";

        private static FilterData HistoryOf(string entity)
        {
            return new FilterData(nameof(H_Entity_Change_Tracking.Entity), FilterData.FilterOperators.equal, entity, PropertyType.String);
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Put_When_The_History_Write_Fails_Should_Roll_Back_The_Update(DatabaseType dbType)
        {
            await PrepareItemEntityAsync(dbType, requireChangeTracking: true);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { "Name" }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ItemEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.put,
                properties: new() { "Name" }))
            {
                var postResult = await ApilaneService.PostDataAsync(DataPostRequest.New(ItemEntity), new { Name = "v1" });
                var id = postResult.Match(r => r.Single(), e => throw new Exception($"Post failed | {e.Code} | {e.Message}"));

                // A put that works writes its history row. This is also what proves the put reaches the
                // history code at all, so that the failure below is the one this test is about.
                var firstPut = await ApilaneService.PutDataAsync(DataPutRequest.New(ItemEntity), new { ID = id, Name = "v2" });
                firstPut.Match(r => r, e => throw new Exception($"Put failed | {e.Code} | {e.Message}"));

                Assert.Equal(1, await CountRowsAsync(HistoryTable, HistoryOf(ItemEntity)));

                // The API has seen the application, so its first-request check cannot recreate the table below
                AssertApiProcessKnowsTheApplication();

                // Make the history write fail: the update comes first, the history row second
                await DropTableAsync(HistoryTable);

                var failingPut = await ApilaneService.PutDataAsync(DataPutRequest.New(ItemEntity), new { ID = id, Name = "v3" });

                Assert.True(failingPut.Match(_ => false, _ => true), "The put should have failed: its history row cannot be written");
            }

            // The update came before the failing history write, so it must have been undone
            Assert.Equal(new[] { "v2" }, await ReadNamesAsync(ItemEntity));
        }

        [Theory]
        [ClassData(typeof(AllDatabasesTestData))]
        public async Task Delete_When_The_Delete_Fails_Should_Not_Keep_A_History_Snapshot(DatabaseType dbType)
        {
            await InitializeApplicationAsync(dbType, null, false);

            await AddEntityAsync(ParentEntity, requireChangeTracking: true);
            await AddStringPropertyAsync(ParentEntity, "Name");

            await AddEntityAsync(ChildEntity);
            await AddStringPropertyAsync(ChildEntity, "Name");
            await AddNumberPropertyAsync(ChildEntity, "ParentRef");

            // A child that refers to its parent blocks the delete of the parent (NO ACTION)
            var foreignKey = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = $"ParentRef,{ParentEntity},{ForeignKeyLogic.ON_DELETE_NO_ACTION}"
            };

            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var response = await HttpClient.RequestAsync(
                    HttpMethod.Post,
                    $"/api/Application/GenerateConstraints?appToken={TestApplication.Token}&Entity={ChildEntity}",
                    new List<EntityConstraint> { foreignKey });

                Assert.True(response.IsSuccessStatusCode, $"GenerateConstraints failed: {await response.Content.ReadAsStringAsync()}");
            }

            TestApplication.Entities.Single(x => x.Name == ChildEntity).EntConstraints = JsonSerializer.Serialize(new List<EntityConstraint> { foreignKey });
            MockApplicationService(TestApplication);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { "Name" }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.delete))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildEntity,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { "Name", "ParentRef" }))
            {
                var parentWithChild = (await ApilaneService.PostDataAsync(DataPostRequest.New(ParentEntity), new { Name = "withChild" }))
                    .Match(r => r.Single(), e => throw new Exception($"Post parent failed | {e.Code} | {e.Message}"));

                var parentWithoutChild = (await ApilaneService.PostDataAsync(DataPostRequest.New(ParentEntity), new { Name = "withoutChild" }))
                    .Match(r => r.Single(), e => throw new Exception($"Post parent failed | {e.Code} | {e.Message}"));

                (await ApilaneService.PostDataAsync(DataPostRequest.New(ChildEntity), new { Name = "child", ParentRef = parentWithChild }))
                    .Match(r => r.Single(), e => throw new Exception($"Post child failed | {e.Code} | {e.Message}"));

                // A delete that works keeps one snapshot of the deleted record. This is also what proves the
                // delete reaches the snapshot code at all, so that the failure below is the one this test is about.
                var firstDelete = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New(ParentEntity, new List<long> { parentWithoutChild }));
                firstDelete.Match(r => r, e => throw new Exception($"Delete failed | {e.Code} | {e.Message}"));

                Assert.Equal(1, await CountRowsAsync(HistoryTable, HistoryOf(ParentEntity)));

                // The delete of the parent that has a child fails after its snapshot was written
                var failingDelete = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New(ParentEntity, new List<long> { parentWithChild }));

                Assert.True(failingDelete.Match(_ => false, _ => true), "The delete should have failed: the parent has a child");
            }

            // The record was not deleted, so no snapshot of it may stay
            Assert.Equal(1, await CountRowsAsync(ParentEntity));
            Assert.Equal(1, await CountRowsAsync(HistoryTable, HistoryOf(ParentEntity)));
        }
    }
}
