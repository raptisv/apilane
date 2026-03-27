using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class ConstraintTests : AppicationTestsBase
    {
        public ConstraintTests(SuiteContext suiteContext) : base(suiteContext)
        {
        }

        // ─── Model helpers ────────────────────────────────────────────────────────

        private class ParentItem : DataItem
        {
            public const string EntityName = "ConstraintParent";
            public string? Name { get; set; }
        }

        private class ChildItem : DataItem
        {
            public const string EntityName = "ConstraintChild";
            public string? Name { get; set; }
            public long ParentRef { get; set; }
        }

        private class UniqueItem : DataItem
        {
            public const string EntityName = "ConstraintUnique";
            public string? UniqueField { get; set; }
            public string? OtherField { get; set; }
        }

        // ─── Helper: call GenerateConstraints endpoint ────────────────────────────

        private async Task<HttpResponseMessage> SetConstraintsAsync(
            string entityName,
            List<EntityConstraint> constraints)
        {
            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                return await HttpClient.RequestAsync(
                    HttpMethod.Post,
                    $"/api/Application/GenerateConstraints?appToken={TestApplication.Token}&Entity={entityName}",
                    constraints);
            }
        }

        // Updates entity's EntConstraints in the mock so subsequent calls use correct "current" state.
        private void SyncConstraints(string entityName, List<EntityConstraint> constraints)
        {
            var entity = TestApplication.Entities.Single(x => x.Name == entityName);
            entity.EntConstraints = System.Text.Json.JsonSerializer.Serialize(constraints);
            MockApplicationService(TestApplication);
        }

        // ─── Unique constraint: add ───────────────────────────────────────────────

        /// <summary>
        /// Adds a unique constraint on UniqueField and verifies that a duplicate insert is rejected.
        /// Runs against all configured storage providers.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_Unique_Add_Rejects_Duplicate(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(UniqueItem.EntityName);
            await AddStringPropertyAsync(UniqueItem.EntityName, nameof(UniqueItem.UniqueField), maximum: 450);
            await AddStringPropertyAsync(UniqueItem.EntityName, nameof(UniqueItem.OtherField), maximum: 450);

            // Set a UNIQUE constraint on UniqueField
            var uniqueConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.Unique,
                Properties = nameof(UniqueItem.UniqueField)
            };

            var response = await SetConstraintsAsync(UniqueItem.EntityName, new List<EntityConstraint> { uniqueConstraint });
            Assert.True(response.IsSuccessStatusCode, $"GenerateConstraints failed: {await response.Content.ReadAsStringAsync()}");

            SyncConstraints(UniqueItem.EntityName, new List<EntityConstraint> { uniqueConstraint });

            // Insert first record — must succeed
            long firstId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, UniqueItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(UniqueItem.UniqueField), nameof(UniqueItem.OtherField) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(UniqueItem.EntityName),
                    new { UniqueField = "value_A", OtherField = "x" });

                firstId = postResult.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"First insert failed | {err.Code} | {err.Message}"));

                Assert.True(firstId > 0);
            }

            // Insert duplicate — must fail (HTTP error or domain error)
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, UniqueItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(UniqueItem.UniqueField), nameof(UniqueItem.OtherField) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(UniqueItem.EntityName),
                    new { UniqueField = "value_A", OtherField = "y" });

                // Must be an error — unique constraint violation
                var uniqueDupWasError = false;
                postResult.Match(_ => { }, _ => { uniqueDupWasError = true; });
                Assert.True(uniqueDupWasError, "Expected duplicate insert to be rejected by unique constraint");
            }

            // Different value — must still succeed
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, UniqueItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(UniqueItem.UniqueField), nameof(UniqueItem.OtherField) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(UniqueItem.EntityName),
                    new { UniqueField = "value_B", OtherField = "z" });

                var newId = postResult.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Non-duplicate insert failed | {err.Code} | {err.Message}"));

                Assert.True(newId > firstId);
            }
        }

        // ─── Unique constraint: remove ────────────────────────────────────────────

        /// <summary>
        /// Verifies that removing a unique constraint allows duplicates to be inserted again.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_Unique_Remove_Allows_Duplicate(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(UniqueItem.EntityName);
            await AddStringPropertyAsync(UniqueItem.EntityName, nameof(UniqueItem.UniqueField), maximum: 450);

            var uniqueConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.Unique,
                Properties = nameof(UniqueItem.UniqueField)
            };

            // Add the constraint
            var addResp = await SetConstraintsAsync(UniqueItem.EntityName, new List<EntityConstraint> { uniqueConstraint });
            Assert.True(addResp.IsSuccessStatusCode, $"Add constraint failed: {await addResp.Content.ReadAsStringAsync()}");
            SyncConstraints(UniqueItem.EntityName, new List<EntityConstraint> { uniqueConstraint });

            // Insert one record with value_X
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, UniqueItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(UniqueItem.UniqueField) }))
            {
                var r = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(UniqueItem.EntityName),
                    new { UniqueField = "value_X" });
                r.Match(ids => ids.Single(), err => throw new Exception($"Insert failed | {err.Code} | {err.Message}"));
            }

            // Remove the constraint (pass empty list)
            var removeResp = await SetConstraintsAsync(UniqueItem.EntityName, new List<EntityConstraint>());
            Assert.True(removeResp.IsSuccessStatusCode, $"Remove constraint failed: {await removeResp.Content.ReadAsStringAsync()}");
            SyncConstraints(UniqueItem.EntityName, new List<EntityConstraint>());

            // Duplicate insert must now succeed
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, UniqueItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(UniqueItem.UniqueField) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(UniqueItem.EntityName),
                    new { UniqueField = "value_X" });

                var newId = postResult.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Expected duplicate to succeed after constraint removal, but got error | {err.Code} | {err.Message}"));

                Assert.True(newId > 0);
            }
        }

        // ─── Unique constraint: idempotent re-apply ───────────────────────────────

        /// <summary>
        /// Verifies that calling GenerateConstraints with the same constraint twice is idempotent.
        /// The second call must succeed (not throw "constraint already exists").
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_Unique_ReApply_Same_Is_Idempotent(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(UniqueItem.EntityName);
            await AddStringPropertyAsync(UniqueItem.EntityName, nameof(UniqueItem.UniqueField), maximum: 450);

            var uniqueConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.Unique,
                Properties = nameof(UniqueItem.UniqueField)
            };

            var resp1 = await SetConstraintsAsync(UniqueItem.EntityName, new List<EntityConstraint> { uniqueConstraint });
            Assert.True(resp1.IsSuccessStatusCode, $"First apply failed: {await resp1.Content.ReadAsStringAsync()}");
            SyncConstraints(UniqueItem.EntityName, new List<EntityConstraint> { uniqueConstraint });

            // Second apply of identical constraint — should succeed (no-op)
            var resp2 = await SetConstraintsAsync(UniqueItem.EntityName, new List<EntityConstraint> { uniqueConstraint });
            Assert.True(resp2.IsSuccessStatusCode, $"Idempotent re-apply failed: {await resp2.Content.ReadAsStringAsync()}");
        }

        // ─── Unique constraint: composite (two columns) ───────────────────────────

        /// <summary>
        /// Verifies that a composite unique constraint (two columns) allows same values in each column
        /// individually, but rejects the same combination.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_Unique_Composite_Enforced(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(UniqueItem.EntityName);
            // Use maximum: 200 to stay within MySQL InnoDB's 3072-byte composite key limit.
            // Two VARCHAR(450) utf8mb4 columns = 2 × 450 × 4 = 3600 bytes > 3072 limit.
            // Two VARCHAR(200) utf8mb4 columns = 2 × 200 × 4 = 1600 bytes — safely within limit.
            await AddStringPropertyAsync(UniqueItem.EntityName, nameof(UniqueItem.UniqueField), maximum: 200);
            await AddStringPropertyAsync(UniqueItem.EntityName, nameof(UniqueItem.OtherField), maximum: 200);

            // Composite unique on (UniqueField, OtherField)
            var compositeConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.Unique,
                Properties = $"{nameof(UniqueItem.UniqueField)},{nameof(UniqueItem.OtherField)}"
            };

            var addResp = await SetConstraintsAsync(UniqueItem.EntityName, new List<EntityConstraint> { compositeConstraint });
            Assert.True(addResp.IsSuccessStatusCode, $"Add composite constraint failed: {await addResp.Content.ReadAsStringAsync()}");
            SyncConstraints(UniqueItem.EntityName, new List<EntityConstraint> { compositeConstraint });

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, UniqueItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(UniqueItem.UniqueField), nameof(UniqueItem.OtherField) }))
            {
                // (A, 1) — first insert, succeeds
                var r1 = await ApilaneService.PostDataAsync(DataPostRequest.New(UniqueItem.EntityName), new { UniqueField = "A", OtherField = "1" });
                r1.Match(ids => ids.Single(), err => throw new Exception($"Insert (A,1) failed | {err.Code} | {err.Message}"));

                // (A, 2) — same UniqueField, different OtherField — must succeed (composite, not single-column)
                var r2 = await ApilaneService.PostDataAsync(DataPostRequest.New(UniqueItem.EntityName), new { UniqueField = "A", OtherField = "2" });
                r2.Match(ids => ids.Single(), err => throw new Exception($"Insert (A,2) failed — composite should allow | {err.Code} | {err.Message}"));

                // (B, 1) — different UniqueField, same OtherField — must succeed
                var r3 = await ApilaneService.PostDataAsync(DataPostRequest.New(UniqueItem.EntityName), new { UniqueField = "B", OtherField = "1" });
                r3.Match(ids => ids.Single(), err => throw new Exception($"Insert (B,1) failed | {err.Code} | {err.Message}"));

                // (A, 1) — exact duplicate — must fail
                var r4 = await ApilaneService.PostDataAsync(DataPostRequest.New(UniqueItem.EntityName), new { UniqueField = "A", OtherField = "1" });
                var compositeDupWasError = false;
                r4.Match(_ => { }, _ => { compositeDupWasError = true; });
                Assert.True(compositeDupWasError, "Expected exact duplicate (A,1) to be rejected by composite unique constraint");
            }
        }

        // ─── FK constraint: add (ON DELETE NO ACTION) ─────────────────────────────

        /// <summary>
        /// Adds a FK from ChildItem.ParentRef → ParentItem.ID (ON DELETE NO ACTION) and verifies:
        /// - Insert child with valid parent succeeds
        /// - Insert child with non-existent parent is rejected
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_FK_Add_Rejects_Invalid_Reference(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(ParentItem.EntityName);
            await AddStringPropertyAsync(ParentItem.EntityName, nameof(ParentItem.Name));

            await AddEntityAsync(ChildItem.EntityName);
            await AddStringPropertyAsync(ChildItem.EntityName, nameof(ChildItem.Name));
            await AddNumberPropertyAsync(ChildItem.EntityName, nameof(ChildItem.ParentRef));

            // Insert a parent record first
            long parentId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ParentItem.Name) }))
            {
                var r = await ApilaneService.PostDataAsync(DataPostRequest.New(ParentItem.EntityName), new { Name = "Parent1" });
                parentId = r.Match(ids => ids.Single(), err => throw new Exception($"Parent insert failed | {err.Code} | {err.Message}"));
                Assert.True(parentId > 0);
            }

            // Add FK: ChildItem.ParentRef → ParentItem.ID (ON DELETE NO ACTION)
            var fkConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = $"{nameof(ChildItem.ParentRef)},{ParentItem.EntityName},{ForeignKeyLogic.ON_DELETE_NO_ACTION}"
            };

            var response = await SetConstraintsAsync(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });
            Assert.True(response.IsSuccessStatusCode, $"GenerateConstraints FK failed: {await response.Content.ReadAsStringAsync()}");
            SyncConstraints(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ChildItem.Name), nameof(ChildItem.ParentRef) }))
            {
                // Valid parent reference — must succeed
                var validInsert = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(ChildItem.EntityName),
                    new { Name = "Child1", ParentRef = parentId });

                validInsert.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Valid FK insert failed | {err.Code} | {err.Message}"));

                // Non-existent parent reference — must fail
                var invalidInsert = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(ChildItem.EntityName),
                    new { Name = "Child2", ParentRef = 999999 });

                var fkViolationWasError = false;
                invalidInsert.Match(_ => { }, _ => { fkViolationWasError = true; });
                Assert.True(fkViolationWasError, "Expected FK violation to be rejected (non-existent parent)");
            }
        }

        // ─── FK constraint: ON DELETE CASCADE ─────────────────────────────────────

        /// <summary>
        /// Verifies that ON DELETE CASCADE removes child rows when the parent is deleted.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_FK_OnDeleteCascade_Removes_Children(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(ParentItem.EntityName);
            await AddStringPropertyAsync(ParentItem.EntityName, nameof(ParentItem.Name));

            await AddEntityAsync(ChildItem.EntityName);
            await AddStringPropertyAsync(ChildItem.EntityName, nameof(ChildItem.Name));
            await AddNumberPropertyAsync(ChildItem.EntityName, nameof(ChildItem.ParentRef));

            // Insert parent
            long parentId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ParentItem.Name) }))
            {
                var r = await ApilaneService.PostDataAsync(DataPostRequest.New(ParentItem.EntityName), new { Name = "CascadeParent" });
                parentId = r.Match(ids => ids.Single(), err => throw new Exception($"Parent insert failed | {err.Code} | {err.Message}"));
            }

            // Add FK with CASCADE
            var fkConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = $"{nameof(ChildItem.ParentRef)},{ParentItem.EntityName},{ForeignKeyLogic.ON_DELETE_CASCADE}"
            };

            var fkResp = await SetConstraintsAsync(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });
            Assert.True(fkResp.IsSuccessStatusCode, $"FK constraint failed: {await fkResp.Content.ReadAsStringAsync()}");
            SyncConstraints(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });

            // Insert two children referencing the parent
            long childId1, childId2;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ChildItem.Name), nameof(ChildItem.ParentRef) }))
            {
                var r1 = await ApilaneService.PostDataAsync(DataPostRequest.New(ChildItem.EntityName), new { Name = "C1", ParentRef = parentId });
                childId1 = r1.Match(ids => ids.Single(), err => throw new Exception($"Child1 insert failed | {err.Code} | {err.Message}"));

                var r2 = await ApilaneService.PostDataAsync(DataPostRequest.New(ChildItem.EntityName), new { Name = "C2", ParentRef = parentId });
                childId2 = r2.Match(ids => ids.Single(), err => throw new Exception($"Child2 insert failed | {err.Code} | {err.Message}"));
            }

            // Delete parent
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.delete))
            {
                var delResult = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New(ParentItem.EntityName, new List<long> { parentId }));
                delResult.Match(
                    count => count,
                    err => throw new Exception($"Parent delete failed | {err.Code} | {err.Message}"));
            }

            // Children must be gone
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.get))
            {
                var c1 = await ApilaneService.GetDataByIdAsync<ChildItem>(DataGetByIdRequest.New(ChildItem.EntityName, childId1));
                var c1WasError = false;
                c1.Match(_ => { }, _ => { c1WasError = true; });
                Assert.True(c1WasError, "Expected child1 to be cascade-deleted");

                var c2 = await ApilaneService.GetDataByIdAsync<ChildItem>(DataGetByIdRequest.New(ChildItem.EntityName, childId2));
                var c2WasError = false;
                c2.Match(_ => { }, _ => { c2WasError = true; });
                Assert.True(c2WasError, "Expected child2 to be cascade-deleted");
            }
        }

        // ─── FK constraint: ON DELETE SET NULL ────────────────────────────────────

        /// <summary>
        /// Verifies that ON DELETE SET NULL nullifies the FK column on child rows when the parent is deleted.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_FK_OnDeleteSetNull_Nullifies_Children(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(ParentItem.EntityName);
            await AddStringPropertyAsync(ParentItem.EntityName, nameof(ParentItem.Name));

            await AddEntityAsync(ChildItem.EntityName);
            await AddStringPropertyAsync(ChildItem.EntityName, nameof(ChildItem.Name));
            // ParentRef must be nullable (not required) for SET NULL to work
            await AddNumberPropertyAsync(ChildItem.EntityName, nameof(ChildItem.ParentRef), required: false);

            // Insert parent
            long parentId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ParentItem.Name) }))
            {
                var r = await ApilaneService.PostDataAsync(DataPostRequest.New(ParentItem.EntityName), new { Name = "NullParent" });
                parentId = r.Match(ids => ids.Single(), err => throw new Exception($"Parent insert failed | {err.Code} | {err.Message}"));
            }

            // Add FK with SET NULL
            var fkConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = $"{nameof(ChildItem.ParentRef)},{ParentItem.EntityName},{ForeignKeyLogic.ON_DELETE_SET_NULL}"
            };

            var fkResp = await SetConstraintsAsync(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });
            Assert.True(fkResp.IsSuccessStatusCode, $"FK SET NULL constraint failed: {await fkResp.Content.ReadAsStringAsync()}");
            SyncConstraints(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });

            // Insert child
            long childId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ChildItem.Name), nameof(ChildItem.ParentRef) }))
            {
                var r = await ApilaneService.PostDataAsync(DataPostRequest.New(ChildItem.EntityName), new { Name = "NullChild", ParentRef = parentId });
                childId = r.Match(ids => ids.Single(), err => throw new Exception($"Child insert failed | {err.Code} | {err.Message}"));
            }

            // Delete parent
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.delete))
            {
                var del = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New(ParentItem.EntityName, new List<long> { parentId }));
                del.Match(count => count, err => throw new Exception($"Parent delete failed | {err.Code} | {err.Message}"));
            }

            // Child must still exist, but ParentRef must be null
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new List<string> { nameof(ChildItem.Name), nameof(ChildItem.ParentRef) }))
            {
                var getResult = await ApilaneService.GetDataByIdAsync<ChildItem>(DataGetByIdRequest.New(ChildItem.EntityName, childId));

                var child = getResult.Match(
                    r => r,
                    err => throw new Exception($"Expected child to still exist after SET NULL delete | {err.Code} | {err.Message}"));

                Assert.Equal(0, child.ParentRef); // ParentRef is long; SET NULL maps to 0/default for value types
            }
        }

        // ─── FK constraint: ON DELETE NO ACTION prevents parent delete ─────────────

        /// <summary>
        /// Verifies that ON DELETE NO ACTION prevents deleting a parent that has children.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_FK_OnDeleteNoAction_Prevents_Parent_Delete(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(ParentItem.EntityName);
            await AddStringPropertyAsync(ParentItem.EntityName, nameof(ParentItem.Name));

            await AddEntityAsync(ChildItem.EntityName);
            await AddStringPropertyAsync(ChildItem.EntityName, nameof(ChildItem.Name));
            await AddNumberPropertyAsync(ChildItem.EntityName, nameof(ChildItem.ParentRef));

            // Insert parent
            long parentId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ParentItem.Name) }))
            {
                var r = await ApilaneService.PostDataAsync(DataPostRequest.New(ParentItem.EntityName), new { Name = "NoActionParent" });
                parentId = r.Match(ids => ids.Single(), err => throw new Exception($"Parent insert failed | {err.Code} | {err.Message}"));
            }

            // Add FK with NO ACTION
            var fkConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = $"{nameof(ChildItem.ParentRef)},{ParentItem.EntityName},{ForeignKeyLogic.ON_DELETE_NO_ACTION}"
            };

            var fkResp = await SetConstraintsAsync(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });
            Assert.True(fkResp.IsSuccessStatusCode, $"FK NO ACTION constraint failed: {await fkResp.Content.ReadAsStringAsync()}");
            SyncConstraints(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });

            // Insert child referencing parent
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ChildItem.Name), nameof(ChildItem.ParentRef) }))
            {
                var r = await ApilaneService.PostDataAsync(DataPostRequest.New(ChildItem.EntityName), new { Name = "BlockedChild", ParentRef = parentId });
                r.Match(ids => ids.Single(), err => throw new Exception($"Child insert failed | {err.Code} | {err.Message}"));
            }

            // Attempt to delete the parent — must fail due to FK (NO ACTION)
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ParentItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.delete))
            {
                var delResult = await ApilaneService.DeleteDataAsync(DataDeleteRequest.New(ParentItem.EntityName, new List<long> { parentId }));
                var noActionBlockedDelete = false;
                delResult.Match(_ => { }, _ => { noActionBlockedDelete = true; });
                Assert.True(noActionBlockedDelete, "Expected FK NO ACTION to prevent parent deletion while children exist");
            }
        }

        // ─── FK constraint: remove allows orphan inserts ──────────────────────────

        /// <summary>
        /// Verifies that after removing a FK constraint, inserts with non-existent parent references succeed.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_FK_Remove_Allows_Orphan_Insert(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(ParentItem.EntityName);
            await AddStringPropertyAsync(ParentItem.EntityName, nameof(ParentItem.Name));

            await AddEntityAsync(ChildItem.EntityName);
            await AddStringPropertyAsync(ChildItem.EntityName, nameof(ChildItem.Name));
            await AddNumberPropertyAsync(ChildItem.EntityName, nameof(ChildItem.ParentRef));

            // Add FK
            var fkConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.ForeignKey,
                Properties = $"{nameof(ChildItem.ParentRef)},{ParentItem.EntityName},{ForeignKeyLogic.ON_DELETE_NO_ACTION}"
            };

            var addResp = await SetConstraintsAsync(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });
            Assert.True(addResp.IsSuccessStatusCode, $"Add FK failed: {await addResp.Content.ReadAsStringAsync()}");
            SyncConstraints(ChildItem.EntityName, new List<EntityConstraint> { fkConstraint });

            // Verify FK is enforced before removal
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ChildItem.Name), nameof(ChildItem.ParentRef) }))
            {
                var r = await ApilaneService.PostDataAsync(DataPostRequest.New(ChildItem.EntityName), new { Name = "OrphanBlocked", ParentRef = 999999 });
                var orphanWasBlocked = false;
                r.Match(_ => { }, _ => { orphanWasBlocked = true; });
                Assert.True(orphanWasBlocked, "Expected orphan insert to fail before FK removal");
            }

            // Remove FK
            var removeResp = await SetConstraintsAsync(ChildItem.EntityName, new List<EntityConstraint>());
            Assert.True(removeResp.IsSuccessStatusCode, $"Remove FK failed: {await removeResp.Content.ReadAsStringAsync()}");
            SyncConstraints(ChildItem.EntityName, new List<EntityConstraint>());

            // Orphan insert must now succeed
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, ChildItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(ChildItem.Name), nameof(ChildItem.ParentRef) }))
            {
                var r = await ApilaneService.PostDataAsync(DataPostRequest.New(ChildItem.EntityName), new { Name = "OrphanAllowed", ParentRef = 999999 });
                var id = r.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Expected orphan insert to succeed after FK removal | {err.Code} | {err.Message}"));
                Assert.True(id > 0);
            }
        }

        // ─── GenerateConstraints: endpoint requires owner access ──────────────────

        /// <summary>
        /// Verifies that calling GenerateConstraints without application-owner credentials returns 401/403.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_GenerateConstraints_Requires_Owner_Access(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(UniqueItem.EntityName);
            await AddStringPropertyAsync(UniqueItem.EntityName, nameof(UniqueItem.UniqueField), maximum: 450);

            var uniqueConstraint = new EntityConstraint
            {
                IsSystem = false,
                TypeID = (int)ConstraintType.Unique,
                Properties = nameof(UniqueItem.UniqueField)
            };

            // Call WITHOUT WithApplicationOwnerAccess — PortalInfoService mock returns false
            var response = await HttpClient.RequestAsync(
                HttpMethod.Post,
                $"/api/Application/GenerateConstraints?appToken={TestApplication.Token}&Entity={UniqueItem.EntityName}",
                new List<EntityConstraint> { uniqueConstraint });

            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden,
                $"Expected 401/403 but got {response.StatusCode}");
        }

        // ─── GenerateConstraints: empty list is a no-op when no constraints exist ──

        /// <summary>
        /// Verifies that calling GenerateConstraints with an empty list when no constraints exist is a clean no-op.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Constraint_Empty_List_When_None_Exist_Is_NoOp(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(UniqueItem.EntityName);
            await AddStringPropertyAsync(UniqueItem.EntityName, nameof(UniqueItem.UniqueField), maximum: 450);

            var response = await SetConstraintsAsync(UniqueItem.EntityName, new List<EntityConstraint>());
            Assert.True(response.IsSuccessStatusCode, $"Empty no-op failed: {await response.Content.ReadAsStringAsync()}");

            // Data operations still work after the no-op call
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, UniqueItem.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.post,
                properties: new List<string> { nameof(UniqueItem.UniqueField) }))
            {
                var r1 = await ApilaneService.PostDataAsync(DataPostRequest.New(UniqueItem.EntityName), new { UniqueField = "a" });
                var r2 = await ApilaneService.PostDataAsync(DataPostRequest.New(UniqueItem.EntityName), new { UniqueField = "a" }); // duplicate allowed — no constraint
                r1.Match(ids => ids.Single(), err => throw new Exception($"Insert 1 failed | {err.Code} | {err.Message}"));
                r2.Match(ids => ids.Single(), err => throw new Exception($"Insert 2 (no-constraint duplicate) failed | {err.Code} | {err.Message}"));
            }
        }
    }
}
