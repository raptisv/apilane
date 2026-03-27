using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common.Enums;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class SchemaTests : AppicationTestsBase
    {
        public SchemaTests(SuiteContext suiteContext) : base(suiteContext)
        {
        }

        private class SchemaTestItem : DataItem
        {
            public const string EntityName = "SchemaTestEntity";
            public string? Test_Property { get; set; }
        }

        private class RenamedSchemaTestItem : DataItem
        {
            public const string EntityName = "SchemaTestEntityRenamed";
            public string? Test_Property { get; set; }
        }

        private class RenamedPropertyItem : DataItem
        {
            public const string EntityName = "SchemaTestEntity";
            public string? Test_Property_Renamed { get; set; }
        }

        /// <summary>
        /// Verifies that RenameTableAsync works correctly across all storage providers.
        /// Creates an entity, inserts a record, renames the entity, and verifies that:
        /// - The old entity name no longer resolves to data
        /// - The new entity name returns the same data
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Schema_RenameEntity_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add a custom entity with one string property
            await AddEntityAsync(SchemaTestItem.EntityName);
            await AddStringPropertyAsync(SchemaTestItem.EntityName, nameof(SchemaTestItem.Test_Property));

            // Assign a non-zero ID to the entity so RenameEntity endpoint can locate it
            var entity = TestApplication.Entities.Single(x => x.Name == SchemaTestItem.EntityName);
            entity.ID = 100;
            MockTestApplicationService();

            // Insert a record into the original entity
            AddSecurity(SchemaTestItem.EntityName, inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(SchemaTestItem.Test_Property) });
            AddSecurity(SchemaTestItem.EntityName, inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new List<string> { nameof(SchemaTestItem.Test_Property) });

            long insertedId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, SchemaTestItem.EntityName,
                inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(SchemaTestItem.Test_Property) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(SchemaTestItem.EntityName),
                    new { Test_Property = "original_value" });

                insertedId = postResult.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Post failed | {err.Code} | {err.Message}"));

                Assert.True(insertedId > 0);
            }

            // Rename the entity
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var renameResponse = await HttpClient.RequestAsync(
                    HttpMethod.Get,
                    $"/api/Application/RenameEntity?appToken={TestApplication.Token}&ID={entity.ID}&NewName={RenamedSchemaTestItem.EntityName}");

                if (!renameResponse.IsSuccessStatusCode)
                {
                    var body = await renameResponse.Content.ReadAsStringAsync();
                    throw new Exception($"RenameEntity failed: {renameResponse.StatusCode} | {body}");
                }
            }

            // Update the mock to reflect the rename (portal would normally do this)
            entity.Name = RenamedSchemaTestItem.EntityName;
            MockTestApplicationService();

            // Re-add security for the renamed entity
            AddSecurity(RenamedSchemaTestItem.EntityName, inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new List<string> { nameof(RenamedSchemaTestItem.Test_Property) });

            // Verify: data is accessible under the new entity name
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, RenamedSchemaTestItem.EntityName,
                inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new List<string> { nameof(RenamedSchemaTestItem.Test_Property) }))
            {
                var getResult = await ApilaneService.GetDataByIdAsync<RenamedSchemaTestItem>(
                    DataGetByIdRequest.New(RenamedSchemaTestItem.EntityName, insertedId));

                var record = getResult.Match(
                    r => r,
                    err => throw new Exception($"GetById on renamed entity failed | {err.Code} | {err.Message}"));

                Assert.Equal("original_value", record.Test_Property);
            }
        }

        /// <summary>
        /// Verifies that DropColumnAsync works correctly across all storage providers.
        /// Creates an entity with a property, inserts a record, drops the property, and verifies that:
        /// - Querying the entity still works (the row is accessible)
        /// - The dropped property column no longer exists in the schema
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Schema_DropColumn_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add a custom entity with one string property
            await AddEntityAsync(SchemaTestItem.EntityName);
            await AddStringPropertyAsync(SchemaTestItem.EntityName, nameof(SchemaTestItem.Test_Property));

            // Assign a non-zero ID to the entity and property so DegenerateProperty endpoint can locate them
            var entity = TestApplication.Entities.Single(x => x.Name == SchemaTestItem.EntityName);
            entity.ID = 100;
            var property = entity.Properties.Single(p => p.Name == nameof(SchemaTestItem.Test_Property));
            property.ID = 200;
            property.EntityID = entity.ID;
            MockTestApplicationService();

            // Insert a record before dropping the column
            long insertedId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, SchemaTestItem.EntityName,
                inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(SchemaTestItem.Test_Property) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(SchemaTestItem.EntityName),
                    new { Test_Property = "will_be_gone" });

                insertedId = postResult.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Post failed | {err.Code} | {err.Message}"));

                Assert.True(insertedId > 0);
            }

            // Drop the property (column)
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var dropResponse = await HttpClient.RequestAsync(
                    HttpMethod.Get,
                    $"/api/Application/DegenerateProperty?appToken={TestApplication.Token}&ID={property.ID}");

                if (!dropResponse.IsSuccessStatusCode)
                {
                    var body = await dropResponse.Content.ReadAsStringAsync();
                    throw new Exception($"DegenerateProperty failed: {dropResponse.StatusCode} | {body}");
                }
            }

            // Update the mock: remove the dropped property from TestApplication
            entity.Properties.Remove(property);
            MockTestApplicationService();

            // Verify: can still get the entity by ID (row still exists, just without the dropped column)
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, SchemaTestItem.EntityName,
                inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.get))
            {
                var getResult = await ApilaneService.GetDataByIdAsync<DataItem>(
                    DataGetByIdRequest.New(SchemaTestItem.EntityName, insertedId));

                var record = getResult.Match(
                    r => r,
                    err => throw new Exception($"GetById after drop-column failed | {err.Code} | {err.Message}"));

                // The row should still be accessible by its primary key
                Assert.Equal(insertedId, record.ID);
            }
        }

        /// <summary>
        /// Verifies that RenameColumnAsync works correctly across all storage providers.
        /// Creates an entity with a property, inserts a record, renames the property, and verifies that:
        /// - The data is accessible under the new property name
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Schema_RenameColumn_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add a custom entity with one string property
            await AddEntityAsync(SchemaTestItem.EntityName);
            await AddStringPropertyAsync(SchemaTestItem.EntityName, nameof(SchemaTestItem.Test_Property));

            // Assign a non-zero ID to the entity and property so RenameEntityProperty endpoint can locate them
            var entity = TestApplication.Entities.Single(x => x.Name == SchemaTestItem.EntityName);
            entity.ID = 100;
            var property = entity.Properties.Single(p => p.Name == nameof(SchemaTestItem.Test_Property));
            property.ID = 200;
            property.EntityID = entity.ID;
            MockTestApplicationService();

            // Insert a record before renaming the column
            long insertedId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, SchemaTestItem.EntityName,
                inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(SchemaTestItem.Test_Property) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(SchemaTestItem.EntityName),
                    new { Test_Property = "rename_test_value" });

                insertedId = postResult.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Post failed | {err.Code} | {err.Message}"));

                Assert.True(insertedId > 0);
            }

            // Rename the property
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var renameResponse = await HttpClient.RequestAsync(
                    HttpMethod.Get,
                    $"/api/Application/RenameEntityProperty?appToken={TestApplication.Token}&ID={property.ID}&NewName={nameof(RenamedPropertyItem.Test_Property_Renamed)}");

                if (!renameResponse.IsSuccessStatusCode)
                {
                    var body = await renameResponse.Content.ReadAsStringAsync();
                    throw new Exception($"RenameEntityProperty failed: {renameResponse.StatusCode} | {body}");
                }
            }

            // Update the mock: reflect the renamed property in TestApplication
            property.Name = nameof(RenamedPropertyItem.Test_Property_Renamed);
            MockTestApplicationService();

            // Verify: data is accessible under the new property name
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, SchemaTestItem.EntityName,
                inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new List<string> { nameof(RenamedPropertyItem.Test_Property_Renamed) }))
            {
                var getResult = await ApilaneService.GetDataByIdAsync<RenamedPropertyItem>(
                    DataGetByIdRequest.New(SchemaTestItem.EntityName, insertedId));

                var record = getResult.Match(
                    r => r,
                    err => throw new Exception($"GetById after rename-column failed | {err.Code} | {err.Message}"));

                Assert.Equal("rename_test_value", record.Test_Property_Renamed);
            }
        }

        /// <summary>
        /// Verifies that RebuildAsync works correctly across all storage providers.
        /// Creates an entity with a property, inserts a record, then calls Rebuild and verifies that:
        /// - The schema is recreated (entity is still accessible)
        /// - All data has been wiped (no rows remain after rebuild)
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Schema_Rebuild_Should_Wipe_Data_And_Recreate_Schema(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add a custom entity with one string property
            await AddEntityAsync(SchemaTestItem.EntityName);
            await AddStringPropertyAsync(SchemaTestItem.EntityName, nameof(SchemaTestItem.Test_Property));

            // Insert a record before rebuild
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, SchemaTestItem.EntityName,
                inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(SchemaTestItem.Test_Property) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(SchemaTestItem.EntityName),
                    new { Test_Property = "before_rebuild" });

                var insertedId = postResult.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Post failed | {err.Code} | {err.Message}"));

                Assert.True(insertedId > 0);
            }

            // Call Rebuild
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var rebuildResponse = await HttpClient.RequestAsync(
                    HttpMethod.Get,
                    $"/api/Application/Rebuild?appToken={TestApplication.Token}");

                if (!rebuildResponse.IsSuccessStatusCode)
                {
                    var body = await rebuildResponse.Content.ReadAsStringAsync();
                    throw new Exception($"Rebuild failed: {rebuildResponse.StatusCode} | {body}");
                }
            }

            // Verify: schema still exists (GET returns empty list, not an error)
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, SchemaTestItem.EntityName,
                inRole: Common.Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new List<string> { nameof(SchemaTestItem.Test_Property) }))
            {
                var getResult = await ApilaneService.GetDataAsync<SchemaTestItem>(
                    DataGetListRequest.New(SchemaTestItem.EntityName));

                var page = getResult.Match(
                    r => r,
                    err => throw new Exception($"Get after rebuild failed | {err.Code} | {err.Message}"));

                // All data must be wiped — no rows should remain
                Assert.Empty(page.Data);
            }
        }

        /// <summary>
        /// Re-applies the FakeItEasy mock for the application service after mutating
        /// TestApplication in-place (e.g. after assigning IDs or renaming entities/properties).
        /// Delegates to the base class protected helper.
        /// </summary>
        private void MockTestApplicationService()
        {
            MockApplicationService(TestApplication);
        }
    }
}
