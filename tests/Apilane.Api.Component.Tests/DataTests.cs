using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Apilane.Net.Services;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    public class DataTests : AppicationTestsBase
    {
        public DataTests() : base(SuiteContext.Shared)
        {

        }

        private class UserItem : RegisterItem, IApiUser
        {
            public long ID { get; set; }
            public long Created { get; set; }
            public bool EmailConfirmed { get; set; }
            public long? LastLogin { get; set; }
            public string Roles { get; set; } = null!;

            public bool IsInRole(string role)
            {
                return !string.IsNullOrWhiteSpace(Roles)
                    && Roles.Split(',').Contains(role);
            }
        }

        private class RegisterItem : IRegisterItem
        {
            public string Email { get; set; } = null!;
            public string Username { get; set; } = null!;
            public string Password { get; set; } = null!;
        }

        private class CustomEntityLight : DataItem
        {
            public CustomEntityLight(string custom_String_Required = "test")
            {
                Custom_String_Required = custom_String_Required;
            }

            public const string EntityName = "CustomEntityLight";
            public string Custom_String_Required { get; set; } = null!;
        }

        private class CustomEntityChild : DataItem
        {
            public const string EntityName = "CustomEntityChild";
            public string Custom_String_Required { get; set; } = null!;
            public long ParentId { get; set; }
        }

        // Shape returned by the GetHistoryByID endpoint: the entity's previous property
        // values plus the change-tracking metadata columns.
        private class CustomEntityLightHistory
        {
            public string? Custom_String_Required { get; set; }
            public long History_Record_Created { get; set; }
            public long? History_Record_Owner { get; set; }
        }

        // Used to count rows in the H_Entity_Change_Tracking system table via a custom endpoint.
        private class HistoryCountRow
        {
            public long HistoryCount { get; set; }
        }

        private async Task<long> GetEntityHistoryRowCountAsync(string countEndpointName)
        {
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, countEndpointName, type: SecurityTypes.CustomEndpoint))
            {
                var result = await ApilaneService.GetCustomEndpointAsync<List<HistoryCountRow>>(CustomEndpointRequest.New(countEndpointName));
                return result.Match(
                    r => r.Single().HistoryCount,
                    e => throw new Exception($"Count history failed | {e.Code} | {e.Message}"));
            }
        }

        //private class CustomEntityFull : DataItem
        //{
        //    public const string EntityName = "CustomEntityFull";
        //    public string Custom_String_Required { get; set; } = null!;
        //    public int Custom_Integer_Required { get; set; }
        //    public decimal Custom_Decimal_Required { get; set; }
        //    public bool Custom_Bool_Required { get; set; }
        //    public long Custom_Date_Required { get; set; }
        //    public string? Custom_String_Not_Required { get; set; }
        //    public int? Custom_Integer_Not_Required { get; set; }
        //    public decimal? Custom_Decimal_Not_Required { get; set; }
        //    public bool? Custom_Bool_Not_Required { get; set; }
        //    public long? Custom_Date_Not_Required { get; set; }
        //}
        //await AddStringPropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_String_Required), required: true);
        //await AddNumberPropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_Integer_Required), required: true);
        //await AddNumberPropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_Decimal_Required), required: true, decimalPlaces: 2);
        //await AddBooleanPropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_Bool_Required), required: true);
        //await AddDatePropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_Date_Required), required: true);
        //await AddStringPropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_String_Not_Required), required: false);
        //await AddNumberPropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_Integer_Not_Required), required: false);
        //await AddNumberPropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_Decimal_Not_Required), required: false, decimalPlaces: 2);
        //await AddBooleanPropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_Bool_Not_Required), required: false);
        //await AddDatePropertyAsync(CustomEntity.EntityName, nameof(CustomEntity.Custom_Date_Not_Required), required: false);

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Entity_Get_Post_Put_Delete_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add custom entity

            await AddEntityAsync(CustomEntityLight.EntityName);

            // Add custom properties

            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            // Assert anonymous access

            await Assert_CRUD_With_AuthToken_Security_Async(null, Globals.ANONYMOUS);

            // Assert authotized access

            var userPassword = "password";
            var userEmail = "test@test.com";
            var userId = await RegisterUserAsync(userEmail, userPassword); // Register 
            var authToken = await LoginUserAsync(userEmail, userPassword); // Login
            await Assert_CRUD_With_AuthToken_Security_Async(authToken, Globals.AUTHENTICATED);

            // Assert role access

            var roles = new List<string>() { "role1", "role2" };
            await SetUserRolesAsync(userId, roles, dbType);
            authToken = await LoginUserAsync(userEmail, userPassword); // Login again to reload the user grain
            foreach (var role in roles)
            {
                await Assert_CRUD_With_AuthToken_Security_Async(authToken, role);
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task StringFilters_Should_Match_Pattern_Characters_Literally(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityLight.EntityName);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            // Values holding LIKE pattern syntax ('%' and '_' everywhere, '[' on SQL Server, '\' on MySQL/PostgreSQL,
            // '!' which is the escape character) next to the near-misses they used to match as wildcards.
            var allValues = new List<string>() { "cust_1", "CUST_1", "custA1", "cust-1", "50%", "5000", @"a\b", "ab", "[ab]", "a", "x!y", "xy", "O'Brien" };

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                foreach (var value in allValues)
                {
                    var postResult = await ApilaneService.PostDataAsync(
                        DataPostRequest.New(CustomEntityLight.EntityName),
                        new { Custom_String_Required = value });
                    postResult.Match(r => r.Single(), e => throw new Exception($"Post failed | {value} | {e.Code} | {e.Message}"));
                }

                async Task AssertMatchesAsync(FilterOperator filterOperator, string filterValue, params string[] expectedValues)
                {
                    var getResult = await ApilaneService.GetDataTotalAsync<CustomEntityLight>(
                        DataGetListRequest.New(CustomEntityLight.EntityName)
                            .WithPageSize(100)
                            .WithFilter(new FilterItem(nameof(CustomEntityLight.Custom_String_Required), filterOperator, filterValue)));

                    var response = getResult.Match(r => r, e => throw new Exception($"Get failed | {filterOperator} '{filterValue}' | {e.Code} | {e.Message}"));

                    var expected = expectedValues.OrderBy(x => x, StringComparer.Ordinal).ToList();
                    var actual = response.Data.Select(x => x.Custom_String_Required).OrderBy(x => x, StringComparer.Ordinal).ToList();
                    Assert.True(expected.SequenceEqual(actual), $"{filterOperator} '{filterValue}' | expected [{string.Join(", ", expected)}] | actual [{string.Join(", ", actual)}]");

                    // Data and Total are two queries built from the same filter instance
                    Assert.Equal(expected.Count, response.Total);
                }

                string[] AllExcept(params string[] excluded) => allValues.Except(excluded).ToArray();

                // '_' is not a single-character wildcard (case-insensitivity is kept)
                await AssertMatchesAsync(FilterOperator.equal, "cust_1", "cust_1", "CUST_1");
                await AssertMatchesAsync(FilterOperator.notequal, "cust_1", AllExcept("cust_1", "CUST_1"));
                await AssertMatchesAsync(FilterOperator.startswith, "cust_", "cust_1", "CUST_1");
                await AssertMatchesAsync(FilterOperator.endswith, "_1", "cust_1", "CUST_1");
                await AssertMatchesAsync(FilterOperator.contains, "_", "cust_1", "CUST_1");
                await AssertMatchesAsync(FilterOperator.notcontains, "_", AllExcept("cust_1", "CUST_1"));
                await AssertMatchesAsync(FilterOperator.equal, "_");

                // '%' is not a multi-character wildcard
                await AssertMatchesAsync(FilterOperator.equal, "%");
                await AssertMatchesAsync(FilterOperator.contains, "%", "50%");
                await AssertMatchesAsync(FilterOperator.contains, "0%", "50%");
                await AssertMatchesAsync(FilterOperator.notcontains, "%", AllExcept("50%"));

                // '[' is not a character class (SQL Server), '\' is not an escape character (MySQL/PostgreSQL)
                await AssertMatchesAsync(FilterOperator.equal, "[ab]", "[ab]");
                await AssertMatchesAsync(FilterOperator.contains, "[ab]", "[ab]");
                await AssertMatchesAsync(FilterOperator.startswith, "[", "[ab]");
                await AssertMatchesAsync(FilterOperator.equal, @"a\b", @"a\b");
                await AssertMatchesAsync(FilterOperator.endswith, @"\b", @"a\b");

                // The escape character itself
                await AssertMatchesAsync(FilterOperator.equal, "x!y", "x!y");
                await AssertMatchesAsync(FilterOperator.contains, "!", "x!y");

                // Plain values are unaffected
                await AssertMatchesAsync(FilterOperator.equal, "ab", "ab");
                await AssertMatchesAsync(FilterOperator.startswith, "cust", "cust_1", "CUST_1", "custA1", "cust-1");
                await AssertMatchesAsync(FilterOperator.equal, "O'Brien", "O'Brien");
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task StringValues_Should_Roundtrip_And_Filter_Unicode(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityLight.EntityName);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            // Outside the SQL Server code page (lost without N'' on write) and outside utf8mb3 (MySQL N'' literals cannot hold emoji)
            var allValues = new List<string>() { "Dvořák", "Łukasz", "Σωκράτης", "Иван", "あいう", "x😀", "é", "Straße" };

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var ids = new Dictionary<string, long>();
                foreach (var value in allValues)
                {
                    // Post a placeholder and update it, to cover both the insert and the update path
                    var postResult = await ApilaneService.PostDataAsync(
                        DataPostRequest.New(CustomEntityLight.EntityName),
                        new { Custom_String_Required = "placeholder" });
                    var id = postResult.Match(r => r.Single(), e => throw new Exception($"Post failed | {value} | {e.Code} | {e.Message}"));

                    var putResult = await ApilaneService.PutDataAsync(
                        DataPutRequest.New(CustomEntityLight.EntityName),
                        new { ID = id, Custom_String_Required = value });
                    putResult.Match(r => r, e => throw new Exception($"Put failed | {value} | {e.Code} | {e.Message}"));

                    ids.Add(value, id);
                }

                var getAll = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName).WithPageSize(100));
                var stored = getAll.Match(r => r.Data, e => throw new Exception($"Get failed | {e.Code} | {e.Message}"));

                // Stored exactly as sent
                foreach (var value in allValues)
                {
                    Assert.Equal(value, stored.Single(x => x.ID == ids[value]).Custom_String_Required);
                }

                // Each value finds exactly its own row
                foreach (var value in allValues)
                {
                    var getResult = await ApilaneService.GetDataAsync<CustomEntityLight>(
                        DataGetListRequest.New(CustomEntityLight.EntityName)
                            .WithPageSize(100)
                            .WithFilter(new FilterItem(nameof(CustomEntityLight.Custom_String_Required), FilterOperator.equal, value)));

                    var data = getResult.Match(r => r.Data, e => throw new Exception($"Get failed | equal '{value}' | {e.Code} | {e.Message}"));
                    Assert.True(data.Count == 1 && data.Single().ID == ids[value], $"equal '{value}' | expected id {ids[value]} | actual [{string.Join(", ", data.Select(x => $"{x.ID}:{x.Custom_String_Required}"))}]");
                }
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task FilterAndSort_Should_Enforce_Property_Access_And_Ignore_Name_Casing(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            const string hiddenProperty = "Custom_String_Hidden";

            await AddEntityAsync(CustomEntityLight.EntityName);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, hiddenProperty, required: false);

            // Callers may write both properties but may only read Custom_String_Required
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required), hiddenProperty }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                foreach (var (visible, hidden) in new[] { ("a", "secret-1"), ("b", "secret-2"), ("c", "other") })
                {
                    var postResult = await ApilaneService.PostDataAsync(
                        DataPostRequest.New(CustomEntityLight.EntityName),
                        new { Custom_String_Required = visible, Custom_String_Hidden = hidden });
                    postResult.Match(r => r.Single(), e => throw new Exception($"Post failed | {visible} | {e.Code} | {e.Message}"));
                }

                // Property names match case-insensitively on every database, including PostgreSQL where
                // identifiers are quoted (and therefore case-sensitive) in the generated SQL.
                var filtered = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName)
                        .WithFilter(new FilterItem("custom_string_REQUIRED", FilterOperator.equal, "b")));
                var filteredData = filtered.Match(r => r.Data, e => throw new Exception($"Get with filter failed | {e.Code} | {e.Message}"));
                Assert.Equal("b", Assert.Single(filteredData).Custom_String_Required);

                var sorted = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName)
                        .WithSort(new SortItem() { Property = "CUSTOM_string_required", Direction = "desc" }));
                var sortedData = sorted.Match(r => r.Data, e => throw new Exception($"Get with sort failed | {e.Code} | {e.Message}"));
                Assert.Equal(new[] { "c", "b", "a" }, sortedData.Select(x => x.Custom_String_Required));

                // A property the caller cannot read is rejected wherever it appears in the filter tree, in any casing
                var hiddenFilters = new List<FilterItem>()
                {
                    new FilterItem(hiddenProperty, FilterOperator.startswith, "secret"),
                    new FilterItem(FilterLogic.AND, new List<FilterItem>()
                    {
                        new FilterItem(hiddenProperty.ToLowerInvariant(), FilterOperator.startswith, "secret"),
                        new FilterItem(nameof(CustomEntityLight.Custom_String_Required), FilterOperator.notequal, "zzz")
                    }),
                    new FilterItem(FilterLogic.OR, new List<FilterItem>()
                    {
                        new FilterItem(FilterLogic.AND, new List<FilterItem>()
                        {
                            new FilterItem(hiddenProperty.ToUpperInvariant(), FilterOperator.contains, "1"),
                            new FilterItem(nameof(CustomEntityLight.Custom_String_Required), FilterOperator.equal, "a")
                        }),
                        new FilterItem(nameof(CustomEntityLight.Custom_String_Required), FilterOperator.equal, "zzz")
                    })
                };

                foreach (var hiddenFilter in hiddenFilters)
                {
                    var result = await ApilaneService.GetDataAsync<CustomEntityLight>(
                        DataGetListRequest.New(CustomEntityLight.EntityName).WithFilter(hiddenFilter));
                    result.Match(
                        r => throw new Exception($"A filter on '{hiddenProperty}' should be rejected, got {r.Data.Count} rows"),
                        e => Assert.Equal(ValidationError.INVALID_FILTER_PARAMETER, e.Code));
                }

                var hiddenSort = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName)
                        .WithSort(new SortItem() { Property = hiddenProperty.ToLowerInvariant(), Direction = "asc" }));
                hiddenSort.Match(
                    r => throw new Exception($"A sort on '{hiddenProperty}' should be rejected, got {r.Data.Count} rows"),
                    e => Assert.Equal(ValidationError.INVALID_SORT_PARAMETER, e.Code));
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task GetHistoryById_Should_Return_Record_Change_History(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add custom entity WITH change tracking enabled, plus a property
            await AddEntityAsync(CustomEntityLight.EntityName, requireChangeTracking: true);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: true);

            // Register a custom endpoint that counts the rows kept in the change-tracking table for this entity.
            const string countEndpoint = "CountCustomEntityLightHistory";
            const string historyTable = "H_Entity_Change_Tracking";
            var countQuery = dbType switch
            {
                DatabaseType.MySQL => $"SELECT COUNT(*) AS `HistoryCount` FROM `{historyTable}` WHERE `Entity` = '{CustomEntityLight.EntityName}'",
                DatabaseType.PostgreSQL => $"SELECT COUNT(*) AS \"HistoryCount\" FROM \"{historyTable}\" WHERE \"Entity\" = '{CustomEntityLight.EntityName}'",
                _ => $"SELECT COUNT(*) AS [HistoryCount] FROM [{historyTable}] WHERE [Entity] = '{CustomEntityLight.EntityName}'"
            };
            AddCustomEndpoint(countEndpoint, countQuery);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.delete))
            {
                // POST a record
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(CustomEntityLight.EntityName),
                    new { Custom_String_Required = "v1" });
                var id = postResult.Match(r => r.Single(), e => throw new Exception($"Post failed | {e.Code} | {e.Message}"));

                // No history exists before any update
                var initialHistory = await ApilaneService.GetHistoryByIdAsync<CustomEntityLightHistory>(
                    DataGetHistoryByIdRequest.New(CustomEntityLight.EntityName, id));
                var initialResponse = initialHistory.Match(r => r, e => throw new Exception($"GetHistory failed | {e.Code} | {e.Message}"));
                Assert.Equal(0, initialResponse.Total);
                Assert.Empty(initialResponse.Data);

                // Two updates -> two history snapshots holding the previous values
                var put1 = await ApilaneService.PutDataAsync(
                    DataPutRequest.New(CustomEntityLight.EntityName),
                    new { ID = id, Custom_String_Required = "v2" });
                put1.Match(r => r, e => throw new Exception($"Put1 failed | {e.Code} | {e.Message}"));

                var put2 = await ApilaneService.PutDataAsync(
                    DataPutRequest.New(CustomEntityLight.EntityName),
                    new { ID = id, Custom_String_Required = "v3" });
                put2.Match(r => r, e => throw new Exception($"Put2 failed | {e.Code} | {e.Message}"));

                // History now holds the two previous values ("v1" before the first update, "v2" before the second)
                var historyResult = await ApilaneService.GetHistoryByIdAsync<CustomEntityLightHistory>(
                    DataGetHistoryByIdRequest.New(CustomEntityLight.EntityName, id)
                        .WithPageIndex(1)
                        .WithPageSize(20));
                var historyResponse = historyResult.Match(r => r, e => throw new Exception($"GetHistory failed | {e.Code} | {e.Message}"));

                Assert.Equal(2, historyResponse.Total);
                Assert.Equal(2, historyResponse.Data.Count);
                // Each entry carries the change-tracking metadata
                Assert.All(historyResponse.Data, h => Assert.True(h.History_Record_Created > 0));
                // The snapshots contain the previous values (order-independent assertion to avoid timestamp ties)
                var historicalValues = historyResponse.Data.Select(h => h.Custom_String_Required).ToList();
                Assert.Contains("v1", historicalValues);
                Assert.Contains("v2", historicalValues);

                // Paging: page size 1 returns a single entry but reports the full total
                var pagedResult = await ApilaneService.GetHistoryByIdAsync<CustomEntityLightHistory>(
                    DataGetHistoryByIdRequest.New(CustomEntityLight.EntityName, id)
                        .WithPageIndex(1)
                        .WithPageSize(1));
                var pagedResponse = pagedResult.Match(r => r, e => throw new Exception($"GetHistory paged failed | {e.Code} | {e.Message}"));
                Assert.Single(pagedResponse.Data);
                Assert.Equal(2, pagedResponse.Total);

                // Before deletion there are two history rows (one per update)
                Assert.Equal(2, await GetEntityHistoryRowCountAsync(countEndpoint));

                // Delete the record. Because change tracking is enabled, a final snapshot of the
                // record is captured before it is removed (and prior history is retained).
                var deleteResult = await ApilaneService.DeleteDataAsync(
                    DataDeleteRequest.New(CustomEntityLight.EntityName).AddIdToDelete(id));
                deleteResult.Match(r => r, e => throw new Exception($"Delete failed | {e.Code} | {e.Message}"));

                // The deletion snapshot was added on top of the two update snapshots
                Assert.Equal(3, await GetEntityHistoryRowCountAsync(countEndpoint));

                // The GetHistoryByID endpoint resolves history through the live record: once the
                // record is deleted it can no longer be located, so the endpoint returns NOT_FOUND
                // (even though the snapshots remain in the database).
                var historyAfterDelete = await ApilaneService.GetHistoryByIdAsync<CustomEntityLightHistory>(
                    DataGetHistoryByIdRequest.New(CustomEntityLight.EntityName, id));
                historyAfterDelete.Match(
                    r => throw new Exception("History should not be returned for a deleted record"),
                    e => Assert.Equal(ValidationError.NOT_FOUND, e.Code));
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task TransactionDelete_WithChangeTracking_Captures_Snapshot_Atomically(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add custom entity WITH change tracking enabled, plus a property
            await AddEntityAsync(CustomEntityLight.EntityName, requireChangeTracking: true);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: true);

            // Register a custom endpoint that counts the rows kept in the change-tracking table for this entity.
            const string countEndpoint = "CountCustomEntityLightHistory";
            const string historyTable = "H_Entity_Change_Tracking";
            var countQuery = dbType switch
            {
                DatabaseType.MySQL => $"SELECT COUNT(*) AS `HistoryCount` FROM `{historyTable}` WHERE `Entity` = '{CustomEntityLight.EntityName}'",
                DatabaseType.PostgreSQL => $"SELECT COUNT(*) AS \"HistoryCount\" FROM \"{historyTable}\" WHERE \"Entity\" = '{CustomEntityLight.EntityName}'",
                _ => $"SELECT COUNT(*) AS [HistoryCount] FROM [{historyTable}] WHERE [Entity] = '{CustomEntityLight.EntityName}'"
            };
            AddCustomEndpoint(countEndpoint, countQuery);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.delete))
            {
                // POST a record, then update it once to create one history snapshot
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(CustomEntityLight.EntityName),
                    new { Custom_String_Required = "v1" });
                var id = postResult.Match(r => r.Single(), e => throw new Exception($"Post failed | {e.Code} | {e.Message}"));

                var put = await ApilaneService.PutDataAsync(
                    DataPutRequest.New(CustomEntityLight.EntityName),
                    new { ID = id, Custom_String_Required = "v2" });
                put.Match(r => r, e => throw new Exception($"Put failed | {e.Code} | {e.Message}"));

                Assert.Equal(1, await GetEntityHistoryRowCountAsync(countEndpoint));

                // Delete the record inside a BATCH TRANSACTION (which opens an ambient transaction scope).
                // The delete's own Required scope joins that ambient transaction, so the final snapshot
                // and the delete commit together.
                var transactionData = new InTransactionData()
                {
                    Delete = new List<InTransactionData.InTransactionDelete>()
                    {
                        new InTransactionData.InTransactionDelete() { Entity = CustomEntityLight.EntityName, Ids = id.ToString() }
                    }
                };

                var transResult = await ApilaneService.TransactionDataAsync(DataTransactionRequest.New(), transactionData);
                var response = transResult.Match(r => r, e => throw new Exception($"Transaction failed | {e.Code} | {e.Message}"));

                Assert.NotNull(response.Delete);
                Assert.Single(response.Delete);
                Assert.Equal(id, response.Delete.Single());

                // The deletion snapshot was committed together with the delete: 1 update + 1 deletion = 2 rows
                Assert.Equal(2, await GetEntityHistoryRowCountAsync(countEndpoint));

                // The record itself is gone
                var getResult = await ApilaneService.GetDataByIdAsync<CustomEntityLight>(
                    DataGetByIdRequest.New(CustomEntityLight.EntityName, id));
                getResult.Match(
                    r => throw new Exception("Record should have been deleted"),
                    e => Assert.Equal(ValidationError.NOT_FOUND, e.Code));
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Transaction_Legacy_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add custom entity and property
            await AddEntityAsync(CustomEntityLight.EntityName);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: true);

            // Set up security for all CRUD operations
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.delete))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) },
                actionType: SecurityActionType.get))
            {
                // POST 2 records outside the transaction to get known IDs
                var postResult1 = await ApilaneService.PostDataAsync(DataPostRequest.New(CustomEntityLight.EntityName), new { Custom_String_Required = "record1" });
                var id1 = postResult1.Match(r => r.Single(), e => throw new Exception($"Post1 failed | {e.Code} | {e.Message}"));

                var postResult2 = await ApilaneService.PostDataAsync(DataPostRequest.New(CustomEntityLight.EntityName), new { Custom_String_Required = "record2" });
                var id2 = postResult2.Match(r => r.Single(), e => throw new Exception($"Post2 failed | {e.Code} | {e.Message}"));

                // Call Transaction: POST 1 new record, PUT record #1, DELETE record #2
                var transactionData = new InTransactionData()
                {
                    Post = new List<InTransactionData.InTransactionSet>()
                    {
                        new InTransactionData.InTransactionSet() { Entity = CustomEntityLight.EntityName, Data = new { Custom_String_Required = "newrecord" } }
                    },
                    Put = new List<InTransactionData.InTransactionSet>()
                    {
                        new InTransactionData.InTransactionSet() { Entity = CustomEntityLight.EntityName, Data = new { ID = id1, Custom_String_Required = "updated1" } }
                    },
                    Delete = new List<InTransactionData.InTransactionDelete>()
                    {
                        new InTransactionData.InTransactionDelete() { Entity = CustomEntityLight.EntityName, Ids = id2.ToString() }
                    }
                };

                var transResult = await ApilaneService.TransactionDataAsync(DataTransactionRequest.New(), transactionData);
                var response = transResult.Match(
                    r => r,
                    e => throw new Exception($"Transaction failed | {e.Code} | {e.Message}"));

                // Assert transaction results
                Assert.NotNull(response.Post);
                Assert.Single(response.Post);
                var newId = response.Post.Single();
                Assert.True(newId > 0);
                Assert.Equal(1, response.Put);
                Assert.NotNull(response.Delete);
                Assert.Single(response.Delete);
                Assert.Equal(id2, response.Delete.Single());

                // Verify: record #1 was updated
                var getResult1 = await ApilaneService.GetDataByIdAsync<CustomEntityLight>(DataGetByIdRequest.New(CustomEntityLight.EntityName, id1));
                var record1 = getResult1.Match(r => r, e => throw new Exception($"GetById1 failed | {e.Code} | {e.Message}"));
                Assert.Equal("updated1", record1.Custom_String_Required);

                // Verify: new record exists
                var getResultNew = await ApilaneService.GetDataByIdAsync<CustomEntityLight>(DataGetByIdRequest.New(CustomEntityLight.EntityName, newId));
                var recordNew = getResultNew.Match(r => r, e => throw new Exception($"GetByIdNew failed | {e.Code} | {e.Message}"));
                Assert.Equal("newrecord", recordNew.Custom_String_Required);

                // Verify: record #2 was deleted (should fail to get)
                var getResult2 = await ApilaneService.GetDataByIdAsync<CustomEntityLight>(DataGetByIdRequest.New(CustomEntityLight.EntityName, id2));
                getResult2.Match(
                    r => throw new Exception("Record should have been deleted"),
                    e => Assert.NotNull(e));
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task TransactionOperations_WithRefs_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add parent entity and properties
            await AddEntityAsync(CustomEntityLight.EntityName);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: true);

            // Add child entity and properties
            await AddEntityAsync(CustomEntityChild.EntityName);
            await AddStringPropertyAsync(CustomEntityChild.EntityName, nameof(CustomEntityChild.Custom_String_Required), required: true);
            await AddNumberPropertyAsync(CustomEntityChild.EntityName, nameof(CustomEntityChild.ParentId), required: true);

            // Set up security for post and get on both entities, put on parent
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) },
                actionType: SecurityActionType.get))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityChild.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityChild.Custom_String_Required), nameof(CustomEntityChild.ParentId) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityChild.EntityName,
                inRole: Globals.ANONYMOUS,
                properties: new() { nameof(CustomEntityChild.Custom_String_Required), nameof(CustomEntityChild.ParentId) },
                actionType: SecurityActionType.get))
            {
                // Build transaction with $ref: cross-references
                var transaction = new TransactionBuilder()
                    .Post(CustomEntityLight.EntityName, new { Custom_String_Required = "parent" }, out var parentRef)
                    .Post(CustomEntityChild.EntityName, new { Custom_String_Required = "child", ParentId = parentRef.Id() })
                    .Put(CustomEntityLight.EntityName, new { ID = parentRef.Id(), Custom_String_Required = "updated" })
                    .Build();

                var transResult = await ApilaneService.TransactionOperationsAsync(DataTransactionOperationsRequest.New(), transaction);
                var response = transResult.Match(
                    r => r,
                    e => throw new Exception($"TransactionOperations failed | {e.Code} | {e.Message}"));

                // Assert: 3 results
                Assert.NotNull(response.Results);
                Assert.Equal(3, response.Results.Count);

                // Result[0]: Post parent
                Assert.Equal(TransactionAction.Post, response.Results[0].Action);
                Assert.Equal(CustomEntityLight.EntityName, response.Results[0].Entity);
                Assert.NotNull(response.Results[0].Created);
                Assert.Single(response.Results[0].Created!);
                var parentId = response.Results[0].Created!.Single();
                Assert.True(parentId > 0);

                // Result[1]: Post child
                Assert.Equal(TransactionAction.Post, response.Results[1].Action);
                Assert.Equal(CustomEntityChild.EntityName, response.Results[1].Entity);
                Assert.NotNull(response.Results[1].Created);
                Assert.Single(response.Results[1].Created!);
                var childId = response.Results[1].Created!.Single();
                Assert.True(childId > 0);

                // Result[2]: Put parent
                Assert.Equal(TransactionAction.Put, response.Results[2].Action);
                Assert.Equal(CustomEntityLight.EntityName, response.Results[2].Entity);
                Assert.Equal(1, response.Results[2].Affected);

                // Verify: child's ParentId matches parent's ID ($ref: was resolved)
                var getChildResult = await ApilaneService.GetDataByIdAsync<CustomEntityChild>(DataGetByIdRequest.New(CustomEntityChild.EntityName, childId));
                var childRecord = getChildResult.Match(r => r, e => throw new Exception($"GetChild failed | {e.Code} | {e.Message}"));
                Assert.Equal(parentId, childRecord.ParentId);
                Assert.Equal("child", childRecord.Custom_String_Required);

                // Verify: parent was updated
                var getParentResult = await ApilaneService.GetDataByIdAsync<CustomEntityLight>(DataGetByIdRequest.New(CustomEntityLight.EntityName, parentId));
                var parentRecord = getParentResult.Match(r => r, e => throw new Exception($"GetParent failed | {e.Code} | {e.Message}"));
                Assert.Equal("updated", parentRecord.Custom_String_Required);
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task TransactionOperations_WithCustomEndpoint_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add entity and properties
            await AddEntityAsync(CustomEntityLight.EntityName);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: true);

            // Add a custom endpoint that selects a record by ID
            var selectQuery = dbType switch
            {
                DatabaseType.MySQL => "SELECT `ID`, `Custom_String_Required` FROM `CustomEntityLight` WHERE `ID` = {entityId}",
                DatabaseType.PostgreSQL => "SELECT \"ID\", \"Custom_String_Required\" FROM \"CustomEntityLight\" WHERE \"ID\" = {entityId}",
                _ => "SELECT [ID], [Custom_String_Required] FROM [CustomEntityLight] WHERE [ID] = {entityId}"
            };
            AddCustomEndpoint("GetEntityById", selectQuery);

            // Set up security for post on entity and get on custom endpoint
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) },
                actionType: SecurityActionType.get))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "GetEntityById",
                inRole: Globals.ANONYMOUS,
                type: SecurityTypes.CustomEndpoint))
            {
                // Build transaction: Post a new entity, then call custom endpoint with the created ID
                var transaction = new TransactionBuilder()
                    .Post(CustomEntityLight.EntityName, new { Custom_String_Required = "hello" }, out var entityRef)
                    .Custom("GetEntityById", new { entityId = entityRef.Id() })
                    .Build();

                var transResult = await ApilaneService.TransactionOperationsAsync(DataTransactionOperationsRequest.New(), transaction);
                var response = transResult.Match(
                    r => r,
                    e => throw new Exception($"TransactionOperations with Custom failed | {e.Code} | {e.Message}"));

                // Assert: 2 results
                Assert.NotNull(response.Results);
                Assert.Equal(2, response.Results.Count);

                // Result[0]: Post entity
                Assert.Equal(TransactionAction.Post, response.Results[0].Action);
                Assert.Equal(CustomEntityLight.EntityName, response.Results[0].Entity);
                Assert.NotNull(response.Results[0].Created);
                Assert.Single(response.Results[0].Created!);
                var createdId = response.Results[0].Created!.Single();
                Assert.True(createdId > 0);

                // Result[1]: Custom endpoint
                Assert.Equal(TransactionAction.Custom, response.Results[1].Action);
                Assert.Equal("GetEntityById", response.Results[1].Entity);
                Assert.NotNull(response.Results[1].CustomResult);
                Assert.Single(response.Results[1].CustomResult!);

                // The custom endpoint returns a single result set with one row
                var resultSet = response.Results[1].CustomResult![0];
                Assert.Single(resultSet);
                var row = resultSet[0];
                Assert.Equal("hello", row["Custom_String_Required"]?.ToString());
            }
        }

        private async Task Assert_CRUD_With_AuthToken_Security_Async(string? authtoken, string securityRole)
        {
            // Get
            await GetData_Unauthorized_ShouldFail<CustomEntityLight>(authtoken);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole))
            {
                await GetData_ShouldSucceed<CustomEntityLight>(authtoken);
            }

            // Post
            await PostData_Unauthorized_ShouldFail(authtoken, new CustomEntityLight());

            long postedDataId = 0;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                postedDataId = await PostData_ShouldSucceed(authtoken, new CustomEntityLight());
                Assert.True(postedDataId > 0);
            }

            // Put
            await PutData_Unauthorized_ShouldFail(authtoken, new CustomEntityLight());

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var updatedValues = await PutData_ShouldSucceed(authtoken, new CustomEntityLight() { ID = postedDataId });
                Assert.Equal(1, updatedValues);
            }

            // Delete
            await DeleteData_Unauthorized_ShouldFail(authtoken, new List<long>() { postedDataId });

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.delete))
            {
                var deletedIds = await DeleteData_ShouldSucceed(authtoken, new List<long>() { postedDataId });
                Assert.NotNull(deletedIds);
                Assert.NotEmpty(deletedIds);
                Assert.Single(deletedIds);
                Assert.Equal(postedDataId, deletedIds.First());
            }
        }

        private async Task<List<T>> GetData_ShouldSucceed<T>(string? authToken)
        {
            var request = DataGetListRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getData = await ApilaneService.GetDataAsync<T>(request);

            return getData.Match(response =>
            {
                Assert.NotNull(response);
                Assert.NotNull(response.Data);
                return response.Data;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task GetData_Unauthorized_ShouldFail<T>(string? authToken)
        {
            var request = DataGetListRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getData = await ApilaneService.GetDataAsync<T>(request);

            getData.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<long> PostData_ShouldSucceed(string? authToken, object data)
        {
            var request = DataPostRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var postData = await ApilaneService.PostDataAsync(request, data);

            return postData.Match(response =>
            {
                Assert.NotNull(response);
                Assert.Single(response);
                return response.Single();
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task PostData_Unauthorized_ShouldFail(string? authToken, object data)
        {
            var request = DataPostRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var postData = await ApilaneService.PostDataAsync(request, data);

            postData.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<int> PutData_ShouldSucceed(string? authToken, object data)
        {
            var request = DataPutRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var putData = await ApilaneService.PutDataAsync(request, data);

            return putData.Match(response => response,
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task PutData_Unauthorized_ShouldFail(string? authToken, object data)
        {
            var request = DataPutRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var putData = await ApilaneService.PutDataAsync(request, data);

            putData.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<long[]> DeleteData_ShouldSucceed(string? authToken, List<long> Ids)
        {
            var request = DataDeleteRequest.New(CustomEntityLight.EntityName, Ids);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var deleteData = await ApilaneService.DeleteDataAsync(request);

            return deleteData.Match(response => response,
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task DeleteData_Unauthorized_ShouldFail(string? authToken, List<long> Ids)
        {
            var request = DataDeleteRequest.New(CustomEntityLight.EntityName, Ids);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var deleteData = await ApilaneService.DeleteDataAsync(request);

            deleteData.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<long> RegisterUserAsync(string userEmail, string userPassword)
        {
            var registerResult = await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem()
            {
                Username = userEmail,
                Email = userEmail,
                Password = userPassword
            }));

            return registerResult.Match(newUserId =>
            {
                return newUserId;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task<string> LoginUserAsync(string userEmail, string userPassword)
        {
            var loginResult = await ApilaneService.AccountLoginAsync<UserItem>(AccountLoginRequest.New(new LoginItem()
            {
                Email = userEmail,
                Password = userPassword
            }));

            return loginResult.Match(success =>
            {
                Assert.NotNull(success);
                Assert.NotNull(success.AuthToken);
                Assert.NotNull(success.User);
                Assert.NotEmpty(success.AuthToken);
                Assert.Equal(userEmail, success.User.Email);
                Assert.False(success.User.EmailConfirmed);

                return success.AuthToken;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }
    }
}