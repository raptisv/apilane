using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// A change of an entity's foreign keys, and the rename of an entity, must keep the data and the other
    /// constraints. SQLite cannot add or drop a foreign key, so it rebuilds the table: these tests pin that
    /// the rows of the table and of the tables referencing it survive, and that the unique indexes and the
    /// ID sequence carry over. They run on every database: the outcome must be the same everywhere.
    /// </summary>
    public class SchemaRebuildTests : AppicationTestsBase
    {
        public SchemaRebuildTests() : base(SuiteContext.Shared)
        {
        }

        private const string RegionEntity = "RebuildRegion";
        private const string CustomerEntity = "RebuildCustomer";
        private const string CascadeOrderEntity = "RebuildCascadeOrder";
        private const string SetNullOrderEntity = "RebuildSetNullOrder";
        private const string NoActionOrderEntity = "RebuildNoActionOrder";
        private const string CategoryEntity = "RebuildCategory";
        private const string RenameSourceEntity = "RebuildRenameSource";
        private const string RenameTargetEntity = "RebuildRenameTarget";

        private class NamedItem : DataItem
        {
            public string? Name { get; set; }
        }

        private class OrderItem : DataItem
        {
            public string? Name { get; set; }
            public long? CustomerRef { get; set; }
        }

        private class CategoryItem : DataItem
        {
            public string? Name { get; set; }
            public long? ParentRef { get; set; }
        }

        // ─── Foreign key change on a referenced table ─────────────────────────────

        /// <summary>
        /// A foreign key added to Customers (which three tables reference, with CASCADE, SET NULL and NO
        /// ACTION) must keep every row of those tables and their references, and the three foreign keys must
        /// still act on the rebuilt Customers.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task SetConstraints_ForeignKeyChange_On_Referenced_Table_Should_Keep_Child_Rows(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(RegionEntity);
            await AddStringPropertyAsync(RegionEntity, nameof(NamedItem.Name));

            await AddEntityAsync(CustomerEntity);
            await AddStringPropertyAsync(CustomerEntity, nameof(NamedItem.Name));
            await AddNumberPropertyAsync(CustomerEntity, "RegionRef");

            foreach (var (entity, logic) in new[]
            {
                (CascadeOrderEntity, ForeignKeyLogic.ON_DELETE_CASCADE),
                (SetNullOrderEntity, ForeignKeyLogic.ON_DELETE_SET_NULL),
                (NoActionOrderEntity, ForeignKeyLogic.ON_DELETE_NO_ACTION)
            })
            {
                await AddEntityAsync(entity);
                await AddStringPropertyAsync(entity, nameof(OrderItem.Name));
                await AddNumberPropertyAsync(entity, nameof(OrderItem.CustomerRef));
                await AddConstraintsAsync(entity, ForeignKey(nameof(OrderItem.CustomerRef), CustomerEntity, logic));
            }

            var firstCustomer = await PostAsync(CustomerEntity, new { Name = "first" }, nameof(NamedItem.Name));
            var secondCustomer = await PostAsync(CustomerEntity, new { Name = "second" }, nameof(NamedItem.Name));

            var cascadeOrder = await PostAsync(CascadeOrderEntity, new { Name = "cascade", CustomerRef = firstCustomer }, nameof(OrderItem.Name), nameof(OrderItem.CustomerRef));
            var setNullOrder = await PostAsync(SetNullOrderEntity, new { Name = "set null", CustomerRef = firstCustomer }, nameof(OrderItem.Name), nameof(OrderItem.CustomerRef));
            var noActionOrder = await PostAsync(NoActionOrderEntity, new { Name = "no action", CustomerRef = secondCustomer }, nameof(OrderItem.Name), nameof(OrderItem.CustomerRef));

            // The change: SQLite rebuilds Customers
            await AddConstraintsAsync(CustomerEntity, ForeignKey("RegionRef", RegionEntity, ForeignKeyLogic.ON_DELETE_NO_ACTION));

            Assert.Equal(firstCustomer, (await GetOrderAsync(CascadeOrderEntity, cascadeOrder))?.CustomerRef);
            Assert.Equal(firstCustomer, (await GetOrderAsync(SetNullOrderEntity, setNullOrder))?.CustomerRef);
            Assert.Equal(secondCustomer, (await GetOrderAsync(NoActionOrderEntity, noActionOrder))?.CustomerRef);

            // The foreign keys of the three tables still point to the rebuilt table
            Assert.True(await DeleteAsync(CustomerEntity, firstCustomer), "Expected the first customer to be deleted");
            Assert.Null(await GetOrderAsync(CascadeOrderEntity, cascadeOrder));

            var setNullAfterDelete = await GetOrderAsync(SetNullOrderEntity, setNullOrder);
            Assert.NotNull(setNullAfterDelete);
            Assert.Null(setNullAfterDelete.CustomerRef);

            Assert.False(await DeleteAsync(CustomerEntity, secondCustomer), "Expected NO ACTION to keep refusing the delete of a referenced customer");
            Assert.Equal(secondCustomer, (await GetOrderAsync(NoActionOrderEntity, noActionOrder))?.CustomerRef);
        }

        // ─── Foreign key change on a self-referencing table ───────────────────────

        /// <summary>
        /// A foreign key added to an entity that references itself must keep its rows, those with a parent
        /// included, and the self-reference must still be enforced.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task SetConstraints_ForeignKeyChange_On_Self_Referencing_Table_Should_Keep_Rows(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(RegionEntity);
            await AddStringPropertyAsync(RegionEntity, nameof(NamedItem.Name));

            await AddEntityAsync(CategoryEntity);
            await AddStringPropertyAsync(CategoryEntity, nameof(CategoryItem.Name));
            await AddNumberPropertyAsync(CategoryEntity, nameof(CategoryItem.ParentRef));
            await AddNumberPropertyAsync(CategoryEntity, "RegionRef");

            // SQL Server refuses a cascading self-reference ("may cause cycles")
            var selfReferenceLogic = dbType == DatabaseType.SQLServer ? ForeignKeyLogic.ON_DELETE_NO_ACTION : ForeignKeyLogic.ON_DELETE_CASCADE;
            await AddConstraintsAsync(CategoryEntity, ForeignKey(nameof(CategoryItem.ParentRef), CategoryEntity, selfReferenceLogic));

            var root = await PostAsync(CategoryEntity, new { Name = "root" }, nameof(CategoryItem.Name));
            var child = await PostAsync(CategoryEntity, new { Name = "child", ParentRef = root }, nameof(CategoryItem.Name), nameof(CategoryItem.ParentRef));

            // The change: SQLite rebuilds Categories, whose copy references Categories
            await AddConstraintsAsync(CategoryEntity, ForeignKey("RegionRef", RegionEntity, ForeignKeyLogic.ON_DELETE_NO_ACTION));

            Assert.NotNull(await GetCategoryAsync(root));
            Assert.Equal(root, (await GetCategoryAsync(child))?.ParentRef);

            // The self-reference is still enforced
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CategoryEntity,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(CategoryItem.Name), nameof(CategoryItem.ParentRef) }))
            {
                var orphan = await ApilaneService.PostDataAsync(DataPostRequest.New(CategoryEntity), new { Name = "orphan", ParentRef = 999999 });

                var orphanWasRefused = false;
                orphan.Match(_ => { }, _ => { orphanWasRefused = true; });
                Assert.True(orphanWasRefused, "Expected a category with a missing parent to be refused");
            }
        }

        // ─── Unique constraints survive a rebuild ─────────────────────────────────

        /// <summary>
        /// A unique constraint that exists before a foreign key change must still refuse duplicates after it.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task SetConstraints_ForeignKeyChange_Should_Keep_Unique_Constraints_Enforced(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(RegionEntity);
            await AddStringPropertyAsync(RegionEntity, nameof(NamedItem.Name));

            await AddEntityAsync(CustomerEntity);
            await AddStringPropertyAsync(CustomerEntity, nameof(NamedItem.Name), maximum: 200);
            await AddNumberPropertyAsync(CustomerEntity, "RegionRef");

            await AddConstraintsAsync(CustomerEntity, Unique(nameof(NamedItem.Name)));
            await PostAsync(CustomerEntity, new { Name = "taken" }, nameof(NamedItem.Name));

            // The change: SQLite rebuilds Customers
            await AddConstraintsAsync(CustomerEntity, ForeignKey("RegionRef", RegionEntity, ForeignKeyLogic.ON_DELETE_NO_ACTION));

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomerEntity,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(NamedItem.Name) }))
            {
                var duplicate = await ApilaneService.PostDataAsync(DataPostRequest.New(CustomerEntity), new { Name = "taken" });

                var duplicateWasRefused = false;
                duplicate.Match(_ => { }, _ => { duplicateWasRefused = true; });
                Assert.True(duplicateWasRefused, "Expected the unique constraint to refuse a duplicate after the foreign key change");

                var other = await ApilaneService.PostDataAsync(DataPostRequest.New(CustomerEntity), new { Name = "free" });
                other.Match(ids => ids.Single(), err => throw new Exception($"Insert of a new value failed | {err.Code} | {err.Message}"));
            }
        }

        // ─── IDs of deleted records are not handed out again ──────────────────────

        /// <summary>
        /// After a foreign key change, a new record must not get the ID of a deleted one: the history of the
        /// deleted record is kept under that ID.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task SetConstraints_ForeignKeyChange_Should_Not_Reuse_Deleted_IDs(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(RegionEntity);
            await AddStringPropertyAsync(RegionEntity, nameof(NamedItem.Name));

            await AddEntityAsync(CustomerEntity);
            await AddStringPropertyAsync(CustomerEntity, nameof(NamedItem.Name));
            await AddNumberPropertyAsync(CustomerEntity, "RegionRef");

            await PostAsync(CustomerEntity, new { Name = "first" }, nameof(NamedItem.Name));
            await PostAsync(CustomerEntity, new { Name = "second" }, nameof(NamedItem.Name));
            var last = await PostAsync(CustomerEntity, new { Name = "third" }, nameof(NamedItem.Name));

            Assert.True(await DeleteAsync(CustomerEntity, last), "Expected the last customer to be deleted");

            // The change: SQLite rebuilds Customers
            await AddConstraintsAsync(CustomerEntity, ForeignKey("RegionRef", RegionEntity, ForeignKeyLogic.ON_DELETE_NO_ACTION));

            var next = await PostAsync(CustomerEntity, new { Name = "fourth" }, nameof(NamedItem.Name));

            Assert.True(next > last, $"Expected a new ID after {last}, got {next}");
        }

        // ─── Constraints follow an entity rename ──────────────────────────────────

        /// <summary>
        /// The constraint names carry the entity name. After a rename, removing a unique constraint must drop
        /// it (duplicates are accepted again), and an entity with the old name can be created.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task RenameEntity_Then_Remove_Unique_Constraint_Should_Allow_Duplicates(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(RenameSourceEntity);
            await AddStringPropertyAsync(RenameSourceEntity, nameof(NamedItem.Name), maximum: 200);

            var unique = Unique(nameof(NamedItem.Name));
            await AddConstraintsAsync(RenameSourceEntity, unique);
            await PostAsync(RenameSourceEntity, new { Name = "same" }, nameof(NamedItem.Name));

            // Rename: the RenameEntity endpoint finds the entity by its ID
            var entity = TestApplication.Entities.Single(x => x.Name == RenameSourceEntity);
            entity.ID = 100;
            MockApplicationService(TestApplication);

            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var renameResponse = await HttpClient.RequestAsync(
                    HttpMethod.Get,
                    $"/api/Application/RenameEntity?appToken={TestApplication.Token}&ID={entity.ID}&NewName={RenameTargetEntity}");

                Assert.True(renameResponse.IsSuccessStatusCode, $"RenameEntity failed: {await renameResponse.Content.ReadAsStringAsync()}");
            }

            // What the Portal stores after the rename
            entity.Name = RenameTargetEntity;
            MockApplicationService(TestApplication);

            // Remove the unique constraint, keep the others (the owner foreign key)
            await SetConstraintsAsync(RenameTargetEntity, entity.Constraints
                .Where(x => x.TypeID != (int)ConstraintType.Unique)
                .ToList());

            var duplicate = await PostAsync(RenameTargetEntity, new { Name = "same" }, nameof(NamedItem.Name));
            Assert.True(duplicate > 0);

            // The old name is free again, its constraint names included
            await AddEntityAsync(RenameSourceEntity);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static EntityConstraint ForeignKey(string property, string referencedEntity, ForeignKeyLogic logic)
        {
            return new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = $"{property},{referencedEntity},{logic}"
            };
        }

        private static EntityConstraint Unique(params string[] properties)
        {
            return new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.Unique,
                Properties = string.Join(",", properties)
            };
        }

        /// <summary>
        /// Saves the entity's constraints with <paramref name="added"/> as well, as the Portal does: the
        /// existing ones (the owner foreign key among them) are sent too.
        /// </summary>
        private Task AddConstraintsAsync(string entityName, params EntityConstraint[] added)
        {
            var entity = TestApplication.Entities.Single(x => x.Name == entityName);

            return SetConstraintsAsync(entityName, entity.Constraints.Concat(added).ToList());
        }

        private async Task SetConstraintsAsync(string entityName, List<EntityConstraint> constraints)
        {
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var response = await HttpClient.RequestAsync(
                    HttpMethod.Post,
                    $"/api/Application/GenerateConstraints?appToken={TestApplication.Token}&Entity={entityName}",
                    constraints);

                Assert.True(response.IsSuccessStatusCode, $"GenerateConstraints on {entityName} failed: {await response.Content.ReadAsStringAsync()}");
            }

            // What the Portal stores: the current constraints of the next save
            var entity = TestApplication.Entities.Single(x => x.Name == entityName);
            entity.EntConstraints = JsonSerializer.Serialize(constraints);
            MockApplicationService(TestApplication);
        }

        private async Task<long> PostAsync(string entityName, object data, params string[] properties)
        {
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, entityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: properties.ToList()))
            {
                var result = await ApilaneService.PostDataAsync(DataPostRequest.New(entityName), data);

                return result.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Insert into {entityName} failed | {err.Code} | {err.Message}"));
            }
        }

        /// <summary>
        /// Deletes a record; false when the delete was refused.
        /// </summary>
        private async Task<bool> DeleteAsync(string entityName, long id)
        {
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, entityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.delete))
            {
                var result = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New(entityName, new List<long> { id }));

                return result.Match<bool>(ids => ids.Contains(id), _ => false);
            }
        }

        /// <summary>
        /// An order, or null when it does not exist.
        /// </summary>
        private async Task<OrderItem?> GetOrderAsync(string entityName, long id)
        {
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, entityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new List<string> { nameof(OrderItem.Name), nameof(OrderItem.CustomerRef) }))
            {
                var result = await ApilaneService.GetDataByIdAsync<OrderItem>(DataGetByIdRequest.New(entityName, id));

                return result.Match<OrderItem?>(item => item, _ => null);
            }
        }

        /// <summary>
        /// A category, or null when it does not exist.
        /// </summary>
        private async Task<CategoryItem?> GetCategoryAsync(long id)
        {
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CategoryEntity,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new List<string> { nameof(CategoryItem.Name), nameof(CategoryItem.ParentRef) }))
            {
                var result = await ApilaneService.GetDataByIdAsync<CategoryItem>(DataGetByIdRequest.New(CategoryEntity, id));

                return result.Match<CategoryItem?>(item => item, _ => null);
            }
        }
    }
}
