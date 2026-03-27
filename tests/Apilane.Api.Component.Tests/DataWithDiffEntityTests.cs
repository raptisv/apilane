using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Net.Extensions;
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
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class DataWithDiffEntityTests : AppicationTestsBase
    {
        public DataWithDiffEntityTests(SuiteContext suiteContext) : base(suiteContext)
        {

        }

        private class UserItem : RegisterItem, IApiUser
        {
            public long ID { get; set; }
            public long Created { get; set; }
            public bool EmailConfirmed { get; set; }
            public long? LastLogin { get; set; }
            public string Roles { get; set; } = null!;

            public long? Company_ID { get; set; } = null;

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

            public long? Company_ID { get; set; } = null;
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Entity_Get_Post_Put_Delete_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            if (!useDiffEntity)
            {
                // Only diff entities
                return;
            }

            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add custom entity

            await AddEntityAsync(CustomEntityLight.EntityName, hasDifferentiationProperty: useDiffEntity);

            // Add custom properties

            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            // Assert authotized access  

            // User 1 company 1
            var userEmailCompany_1 = "test@company1.com"; var userPassword = "password";
            var userIdCompany_1 = await RegisterUserAsync(userEmailCompany_1, userPassword); // Register 1  
            var updateDiffPropertyEndpointName_1 = "UpdateDiffProperty1";

            var customEndpointQuery_1 = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                                INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES ({1}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                                UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = {1} WHERE [ID] = {userIdCompany_1};
                                                SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES ({1}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = {1} WHERE `ID` = {userIdCompany_1};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES ({1}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = {1} WHERE ""ID"" = {userIdCompany_1};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES ({1}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = {1} WHERE [ID] = {userIdCompany_1};",
            };

            AddCustomEndpoint(updateDiffPropertyEndpointName_1, customEndpointQuery_1); // Set company 1
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, updateDiffPropertyEndpointName_1, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(updateDiffPropertyEndpointName_1));
            }
            var loginResponseUserIdCompany_1 = await LoginUserAsync(userEmailCompany_1, userPassword); // Login 1 

            // User 2 company 2
            var userEmailCompany_2 = "test@company2.com";
            var userIdCompany_2 = await RegisterUserAsync(userEmailCompany_2, userPassword); // Register 2 
            var updateDiffPropertyEndpointName_2 = "UpdateDiffProperty2";
            var customEndpointQuery_2 = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                                INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES ({2}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                                UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = {2} WHERE [ID] = {userIdCompany_2};
                                                SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES ({2}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = {2} WHERE `ID` = {userIdCompany_2};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES ({2}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = {2} WHERE ""ID"" = {userIdCompany_2};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES ({2}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = {2} WHERE [ID] = {userIdCompany_2};",
            };

            AddCustomEndpoint(updateDiffPropertyEndpointName_2, customEndpointQuery_2); // Set company 2
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, updateDiffPropertyEndpointName_2, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(updateDiffPropertyEndpointName_2));
            }
            var loginResponseUserIdCompany_2 = await LoginUserAsync(userEmailCompany_2, userPassword); // Login 2 

            await Assert_CRUD_With_AuthToken_Security_Async(loginResponseUserIdCompany_1, loginResponseUserIdCompany_2, Globals.AUTHENTICATED);
        }

        private async Task Assert_CRUD_With_AuthToken_Security_Async(
            AccountLoginResponse<UserItem> loginResponseUserIdCompany_1,
            AccountLoginResponse<UserItem> loginResponseUserIdCompany_2,
            string securityRole)
        {
            Assert.NotNull(loginResponseUserIdCompany_1.User.Company_ID);
            Assert.NotNull(loginResponseUserIdCompany_2.User.Company_ID);

            // Post
            await PostData_Unauthorized_ShouldFail(loginResponseUserIdCompany_1.AuthToken, new CustomEntityLight());
            await PostData_Unauthorized_ShouldFail(loginResponseUserIdCompany_2.AuthToken, new CustomEntityLight());

            long postedDataId_Company_1 = 0;
            long postedDataId_Company_2 = 0;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                postedDataId_Company_1 = await PostData_ShouldSucceed(loginResponseUserIdCompany_1.AuthToken, new CustomEntityLight());
                Assert.True(postedDataId_Company_1 > 0);

                postedDataId_Company_2 = await PostData_ShouldSucceed(loginResponseUserIdCompany_2.AuthToken, new CustomEntityLight());
                Assert.True(postedDataId_Company_2 > 0);
            }

            // Get
            await GetData_Unauthorized_ShouldFail<CustomEntityLight>(loginResponseUserIdCompany_1.AuthToken);
            await GetData_Unauthorized_ShouldFail<CustomEntityLight>(loginResponseUserIdCompany_2.AuthToken);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Company_ID), nameof(CustomEntityLight.Owner), nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var dataCompany_1 = await GetData_ShouldSucceed<CustomEntityLight>(loginResponseUserIdCompany_1.AuthToken);
                Assert.Single(dataCompany_1);
                Assert.Equal(dataCompany_1.Single().Company_ID, loginResponseUserIdCompany_1.User.Company_ID);
                Assert.Equal(dataCompany_1.Single().ID, postedDataId_Company_1);

                var dataCompany_2 = await GetData_ShouldSucceed<CustomEntityLight>(loginResponseUserIdCompany_2.AuthToken);
                Assert.Single(dataCompany_2);
                Assert.Equal(dataCompany_2.Single().Company_ID, loginResponseUserIdCompany_2.User.Company_ID);
                Assert.Equal(dataCompany_2.Single().ID, postedDataId_Company_2);
            }

            // Get by id
            await GetData_Unauthorized_ShouldFail<CustomEntityLight>(loginResponseUserIdCompany_1.AuthToken);
            await GetData_Unauthorized_ShouldFail<CustomEntityLight>(loginResponseUserIdCompany_2.AuthToken);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Company_ID), nameof(CustomEntityLight.Owner), nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var dataCompany_1 = await GetDataByID_ShouldSucceed<CustomEntityLight>(loginResponseUserIdCompany_1.AuthToken, postedDataId_Company_1);
                Assert.NotNull(dataCompany_1);
                Assert.Equal(dataCompany_1.Company_ID, loginResponseUserIdCompany_1.User.Company_ID);
                Assert.Equal(dataCompany_1.ID, postedDataId_Company_1);

                var dataCompany_2 = await GetDataByID_ShouldSucceed<CustomEntityLight>(loginResponseUserIdCompany_2.AuthToken, postedDataId_Company_2);
                Assert.NotNull(dataCompany_2);
                Assert.Equal(dataCompany_2.Company_ID, loginResponseUserIdCompany_2.User.Company_ID);
                Assert.Equal(dataCompany_2.ID, postedDataId_Company_2);
            }

            // Put
            await PutData_Unauthorized_ShouldFail(loginResponseUserIdCompany_1.AuthToken, new CustomEntityLight());
            await PutData_Unauthorized_ShouldFail(loginResponseUserIdCompany_2.AuthToken, new CustomEntityLight());

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                // Valid updates
                var updatedValues_1 = await PutData_ShouldSucceed(loginResponseUserIdCompany_1.AuthToken, new CustomEntityLight() { ID = postedDataId_Company_1 });
                Assert.Equal(1, updatedValues_1);

                var updatedValues_2 = await PutData_ShouldSucceed(loginResponseUserIdCompany_2.AuthToken, new CustomEntityLight() { ID = postedDataId_Company_2 });
                Assert.Equal(1, updatedValues_2);

                // Invalid updates
                var updatedValues_1_Invalid = await PutData_ShouldSucceed(loginResponseUserIdCompany_1.AuthToken, new CustomEntityLight() { ID = postedDataId_Company_2 });
                Assert.Equal(0, updatedValues_1_Invalid);

                var updatedValues_2_Invalid = await PutData_ShouldSucceed(loginResponseUserIdCompany_2.AuthToken, new CustomEntityLight() { ID = postedDataId_Company_1 });
                Assert.Equal(0, updatedValues_2_Invalid);
            }

            // Delete
            await DeleteData_Unauthorized_ShouldFail(loginResponseUserIdCompany_1.AuthToken, new List<long>() { postedDataId_Company_1 });
            await DeleteData_Unauthorized_ShouldFail(loginResponseUserIdCompany_2.AuthToken, new List<long>() { postedDataId_Company_2 });

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.delete))
            {
                // Invalid deletes
                var deletedIds_1_Invalid = await DeleteData_ShouldSucceed(loginResponseUserIdCompany_1.AuthToken, new List<long>() { postedDataId_Company_2 });
                Assert.NotNull(deletedIds_1_Invalid);
                Assert.Empty(deletedIds_1_Invalid);

                var deletedIds_2_Invalid = await DeleteData_ShouldSucceed(loginResponseUserIdCompany_2.AuthToken, new List<long>() { postedDataId_Company_1 });
                Assert.NotNull(deletedIds_2_Invalid);
                Assert.Empty(deletedIds_2_Invalid);

                // Valid deletes
                var deletedIds_1 = await DeleteData_ShouldSucceed(loginResponseUserIdCompany_1.AuthToken, new List<long>() { postedDataId_Company_1 });
                Assert.NotNull(deletedIds_1);
                Assert.NotEmpty(deletedIds_1);
                Assert.Single(deletedIds_1);
                Assert.Equal(postedDataId_Company_1, deletedIds_1.First());

                var deletedIds_2 = await DeleteData_ShouldSucceed(loginResponseUserIdCompany_2.AuthToken, new List<long>() { postedDataId_Company_2 });
                Assert.NotNull(deletedIds_2);
                Assert.NotEmpty(deletedIds_2);
                Assert.Single(deletedIds_2);
                Assert.Equal(postedDataId_Company_2, deletedIds_2.First());
            }
        }

        /// <summary>
        /// Verifies that anonymous access is blocked when differentiation entity is enabled.
        /// Even if GET security is open to anonymous, the diff filter is applied and
        /// anonymous requests have no Company_ID — so they should be blocked/return nothing.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task DiffEntity_AnonymousAccess_ShouldBeBlocked(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            if (!useDiffEntity)
            {
                return;
            }

            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityLight.EntityName, hasDifferentiationProperty: true);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            // Register + assign company to a real user and insert a record
            var userEmail = "test@company1.com"; var userPassword = "password";
            var userId = await RegisterUserAsync(userEmail, userPassword);
            var setCompanyEndpointName = "SetCompanyAnon";
            var setCompanyQuery = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId};
                                             SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 1 WHERE `ID` = {userId};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 1 WHERE ""ID"" = {userId};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId};",
            };
            AddCustomEndpoint(setCompanyEndpointName, setCompanyQuery);
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, setCompanyEndpointName, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(setCompanyEndpointName));
            }
            var authToken = await LoginUserAsync(userEmail, userPassword);

            // Insert a record as that authenticated user
            long insertedId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(CustomEntityLight.EntityName).WithAuthToken(authToken.AuthToken),
                    new { Custom_String_Required = "sensitive" });

                insertedId = postResult.Match(
                    ids => ids.Single(),
                    err => throw new Exception($"Post failed | {err.Code} | {err.Message}"));

                Assert.True(insertedId > 0);
            }

            // Now open GET to anonymous — anonymous has no Company_ID, should get 0 rows (not an error, just empty)
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS, actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Company_ID), nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var getData = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName));

                var result = getData.Match(
                    r => r,
                    err => throw new Exception($"Get failed | {err.Code} | {err.Message}"));

                // Anonymous user has NULL Company_ID — diff filter returns no rows
                Assert.Empty(result.Data);
            }
        }

        /// <summary>
        /// Verifies that stats aggregations respect the differentiation filter.
        /// User from company 1 should only see aggregate results over their own company's data.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task DiffEntity_Stats_ShouldRespectDiffFilter(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            if (!useDiffEntity)
            {
                return;
            }

            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityLight.EntityName, hasDifferentiationProperty: true);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            var userPassword = "password";

            // User 1 → Company 1
            var userId1 = await RegisterUserAsync("stats@company1.com", userPassword);
            var setCompany1 = "SetCompanyStats1";
            var setCompanyQuery1 = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};
                                             SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 1 WHERE `ID` = {userId1};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 1 WHERE ""ID"" = {userId1};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};",
            };
            AddCustomEndpoint(setCompany1, setCompanyQuery1);
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, setCompany1, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(setCompany1));
            }
            var login1 = await LoginUserAsync("stats@company1.com", userPassword);

            // User 2 → Company 2
            var userId2 = await RegisterUserAsync("stats@company2.com", userPassword);
            var setCompany2 = "SetCompanyStats2";
            var setCompanyQuery2 = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 2 WHERE [ID] = {userId2};
                                             SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 2 WHERE `ID` = {userId2};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 2 WHERE ""ID"" = {userId2};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 2 WHERE [ID] = {userId2};",
            };
            AddCustomEndpoint(setCompany2, setCompanyQuery2);
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, setCompany2, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(setCompany2));
            }
            var login2 = await LoginUserAsync("stats@company2.com", userPassword);

            // Both users insert one record each
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var p1 = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(CustomEntityLight.EntityName).WithAuthToken(login1.AuthToken),
                    new { Custom_String_Required = "company1_data" });
                p1.Match(r => r.Single(), e => throw new Exception($"Post1 failed | {e.Code} | {e.Message}"));

                var p2 = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(CustomEntityLight.EntityName).WithAuthToken(login2.AuthToken),
                    new { Custom_String_Required = "company2_data" });
                p2.Match(r => r.Single(), e => throw new Exception($"Post2 failed | {e.Code} | {e.Message}"));
            }

            // Each user's aggregate COUNT must be 1 (not 2 — which would indicate a cross-tenant leak)
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Company_ID), nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var statsRequest1 = StatsAggregateRequest.New(CustomEntityLight.EntityName)
                    .WithProperty("ID", StatsAggregateRequest.DataAggregates.Count)
                    .WithAuthToken(login1.AuthToken);

                var stats1Raw = await ApilaneService.GetStatsAggregateAsync(statsRequest1);
                var stats1Json = stats1Raw.Match(r => r, e => throw new Exception($"Stats1 failed | {e.Code} | {e.Message}"));

                // Parse the count from the raw JSON string — format: [{"ID_count":1}]
                using var doc1 = System.Text.Json.JsonDocument.Parse(stats1Json);
                var count1 = doc1.RootElement[0].GetProperty("ID_count").GetInt64();
                Assert.Equal(1, count1); // Company 1 should only see its own 1 record

                var statsRequest2 = StatsAggregateRequest.New(CustomEntityLight.EntityName)
                    .WithProperty("ID", StatsAggregateRequest.DataAggregates.Count)
                    .WithAuthToken(login2.AuthToken);

                var stats2Raw = await ApilaneService.GetStatsAggregateAsync(statsRequest2);
                var stats2Json = stats2Raw.Match(r => r, e => throw new Exception($"Stats2 failed | {e.Code} | {e.Message}"));

                using var doc2 = System.Text.Json.JsonDocument.Parse(stats2Json);
                var count2 = doc2.RootElement[0].GetProperty("ID_count").GetInt64();
                Assert.Equal(1, count2); // Company 2 should only see its own 1 record
            }
        }

        /// <summary>
        /// Verifies that the Users entity does not expose cross-tenant user rows.
        /// User from company 1 should not be able to see users belonging to company 2.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task DiffEntity_UsersEntity_ShouldNotLeakCrossTenant(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            if (!useDiffEntity)
            {
                return;
            }

            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var userPassword = "password";

            // Register two users and assign them to different companies
            var userId1 = await RegisterUserAsync("users@company1.com", userPassword);
            var userId2 = await RegisterUserAsync("users@company2.com", userPassword);

            var setCompaniesEndpointName = "SetCompaniesUsers";
            var setCompaniesQuery = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 2 WHERE [ID] = {userId2};
                                             SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 1 WHERE `ID` = {userId1};
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 2 WHERE `ID` = {userId2};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 1 WHERE ""ID"" = {userId1};
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 2 WHERE ""ID"" = {userId2};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 2 WHERE [ID] = {userId2};",
            };

            AddCustomEndpoint(setCompaniesEndpointName, setCompaniesQuery);
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, setCompaniesEndpointName, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(setCompaniesEndpointName));
            }

            var login1 = await LoginUserAsync("users@company1.com", userPassword);
            var login2 = await LoginUserAsync("users@company2.com", userPassword);

            // Open GET on Users entity for authenticated users
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Users",
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.get,
                properties: new() { nameof(UserItem.Company_ID) }))
            {
                // User 1 should only see themselves (company 1)
                var getData1 = await ApilaneService.GetDataAsync<UserItem>(
                    DataGetListRequest.New("Users").WithAuthToken(login1.AuthToken));

                var result1 = getData1.Match(
                    r => r,
                    err => throw new Exception($"GetUsers1 failed | {err.Code} | {err.Message}"));

                Assert.All(result1.Data, u => Assert.Equal(login1.User.Company_ID, u.Company_ID));
                Assert.DoesNotContain(result1.Data, u => u.ID == userId2);

                // User 2 should only see themselves (company 2)
                var getData2 = await ApilaneService.GetDataAsync<UserItem>(
                    DataGetListRequest.New("Users").WithAuthToken(login2.AuthToken));

                var result2 = getData2.Match(
                    r => r,
                    err => throw new Exception($"GetUsers2 failed | {err.Code} | {err.Message}"));

                Assert.All(result2.Data, u => Assert.Equal(login2.User.Company_ID, u.Company_ID));
                Assert.DoesNotContain(result2.Data, u => u.ID == userId1);
            }
        }

        /// <summary>
        /// Verifies that transaction POST operations inside a diff-entity context
        /// stamp Company_ID on the inserted records, and that those records are
        /// only visible to the correct company.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task DiffEntity_Transaction_ShouldRespectDiffFilter(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            if (!useDiffEntity)
            {
                return;
            }

            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityLight.EntityName, hasDifferentiationProperty: true);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            var userPassword = "password";

            // Register two users in two different companies
            var userId1 = await RegisterUserAsync("txn@company1.com", userPassword);
            var userId2 = await RegisterUserAsync("txn@company2.com", userPassword);

            var setCompaniesEndpointName = "SetCompaniesTxn";
            var setCompaniesQuery = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 2 WHERE [ID] = {userId2};
                                             SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 1 WHERE `ID` = {userId1};
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 2 WHERE `ID` = {userId2};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 1 WHERE ""ID"" = {userId1};
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 2 WHERE ""ID"" = {userId2};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 2 WHERE [ID] = {userId2};",
            };
            AddCustomEndpoint(setCompaniesEndpointName, setCompaniesQuery);
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, setCompaniesEndpointName, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(setCompaniesEndpointName));
            }

            var login1 = await LoginUserAsync("txn@company1.com", userPassword);
            var login2 = await LoginUserAsync("txn@company2.com", userPassword);

            // Both users POST via transaction
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Company_ID), nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var txnData1 = new InTransactionData()
                {
                    Post = new List<InTransactionData.InTransactionSet>()
                    {
                        new InTransactionData.InTransactionSet() { Entity = CustomEntityLight.EntityName, Data = new { Custom_String_Required = "txn_company1" } }
                    }
                };

                var txnResult1 = await ApilaneService.TransactionDataAsync(
                    DataTransactionRequest.New().WithAuthToken(login1.AuthToken), txnData1);
                var txnResponse1 = txnResult1.Match(r => r, e => throw new Exception($"Txn1 failed | {e.Code} | {e.Message}"));
                Assert.Single(txnResponse1.Post!);
                var newId1 = txnResponse1.Post!.Single();

                var txnData2 = new InTransactionData()
                {
                    Post = new List<InTransactionData.InTransactionSet>()
                    {
                        new InTransactionData.InTransactionSet() { Entity = CustomEntityLight.EntityName, Data = new { Custom_String_Required = "txn_company2" } }
                    }
                };

                var txnResult2 = await ApilaneService.TransactionDataAsync(
                    DataTransactionRequest.New().WithAuthToken(login2.AuthToken), txnData2);
                var txnResponse2 = txnResult2.Match(r => r, e => throw new Exception($"Txn2 failed | {e.Code} | {e.Message}"));
                Assert.Single(txnResponse2.Post!);
                var newId2 = txnResponse2.Post!.Single();

                // Each user should only see their own record
                var get1 = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName).WithAuthToken(login1.AuthToken));
                var data1 = get1.Match(r => r.Data, e => throw new Exception($"Get1 failed | {e.Code} | {e.Message}"));
                Assert.Single(data1);
                Assert.Equal(newId1, data1.Single().ID);
                Assert.Equal(login1.User.Company_ID, data1.Single().Company_ID);

                var get2 = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName).WithAuthToken(login2.AuthToken));
                var data2 = get2.Match(r => r.Data, e => throw new Exception($"Get2 failed | {e.Code} | {e.Message}"));
                Assert.Single(data2);
                Assert.Equal(newId2, data2.Single().ID);
                Assert.Equal(login2.User.Company_ID, data2.Single().Company_ID);
            }
        }

        /// <summary>
        /// Verifies that a user with NULL Company_ID (no company assigned) cannot see any data
        /// from a diff-entity-enabled entity, even when GET security is open.
        /// The diff filter becomes "WHERE Company_ID = NULL" which never matches.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task DiffEntity_NullCompanyId_ShouldSeeNoData(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            if (!useDiffEntity)
            {
                return;
            }

            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityLight.EntityName, hasDifferentiationProperty: true);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            var userPassword = "password";

            // User with a company — inserts data
            var userId1 = await RegisterUserAsync("null@company1.com", userPassword);
            var setCompanyEndpointName = "SetCompanyNull";
            var setCompanyQuery = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};
                                             SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 1 WHERE `ID` = {userId1};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 1 WHERE ""ID"" = {userId1};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};",
            };
            AddCustomEndpoint(setCompanyEndpointName, setCompanyQuery);
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, setCompanyEndpointName, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(setCompanyEndpointName));
            }
            var login1 = await LoginUserAsync("null@company1.com", userPassword);

            // User with NULL Company_ID (never assigned to any company)
            await RegisterUserAsync("null@nocompany.com", userPassword);
            var loginNull = await LoginUserAsync("null@nocompany.com", userPassword);
            Assert.Null(loginNull.User.Company_ID);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Company_ID), nameof(CustomEntityLight.Custom_String_Required) }))
            {
                // Company 1 user inserts a record
                var postResult = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(CustomEntityLight.EntityName).WithAuthToken(login1.AuthToken),
                    new { Custom_String_Required = "company1_record" });
                postResult.Match(r => r.Single(), e => throw new Exception($"Post failed | {e.Code} | {e.Message}"));

                // NULL Company_ID user should see nothing
                var getData = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName).WithAuthToken(loginNull.AuthToken));

                var result = getData.Match(
                    r => r,
                    err => throw new Exception($"Get failed | {err.Code} | {err.Message}"));

                Assert.Empty(result.Data);
            }
        }

        /// <summary>
        /// Verifies that a user cannot override the differentiation filter by passing
        /// an explicit filter on Company_ID (e.g. trying to query another company's data).
        /// The diff filter is AND-ed with any user-supplied filter, so cross-tenant filtering must fail.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task DiffEntity_ExplicitFilterOverride_ShouldNotLeakData(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            if (!useDiffEntity)
            {
                return;
            }

            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityLight.EntityName, hasDifferentiationProperty: true);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            var userPassword = "password";

            // Two users in two companies
            var userId1 = await RegisterUserAsync("filter@company1.com", userPassword);
            var userId2 = await RegisterUserAsync("filter@company2.com", userPassword);

            var setCompaniesEndpointName = "SetCompaniesFilter";
            var setCompaniesQuery = dbType switch
            {
                DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};
                                             UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 2 WHERE [ID] = {userId2};
                                             SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 1 WHERE `ID` = {userId1};
                                         UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = 2 WHERE `ID` = {userId2};",
                DatabaseType.PostgreSQL => $@"INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             INSERT INTO ""{DiffEntityName}"" (""ID"", ""Created"") VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 1 WHERE ""ID"" = {userId1};
                                             UPDATE ""Users"" SET ""{DiffEntityName.GetDifferentiationPropertyName()}"" = 2 WHERE ""ID"" = {userId2};",
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (1, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES (2, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 1 WHERE [ID] = {userId1};
                        UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = 2 WHERE [ID] = {userId2};",
            };
            AddCustomEndpoint(setCompaniesEndpointName, setCompaniesQuery);
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, setCompaniesEndpointName, type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(setCompaniesEndpointName));
            }

            var login1 = await LoginUserAsync("filter@company1.com", userPassword);
            var login2 = await LoginUserAsync("filter@company2.com", userPassword);

            long idFromCompany1;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var post1 = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(CustomEntityLight.EntityName).WithAuthToken(login1.AuthToken),
                    new { Custom_String_Required = "c1_secret" });
                idFromCompany1 = post1.Match(r => r.Single(), e => throw new Exception($"Post1 failed | {e.Code} | {e.Message}"));

                var post2 = await ApilaneService.PostDataAsync(
                    DataPostRequest.New(CustomEntityLight.EntityName).WithAuthToken(login2.AuthToken),
                    new { Custom_String_Required = "c2_secret" });
                post2.Match(r => r.Single(), e => throw new Exception($"Post2 failed | {e.Code} | {e.Message}"));
            }

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.AUTHENTICATED, actionType: SecurityActionType.get,
                properties: new() { nameof(CustomEntityLight.Company_ID), nameof(CustomEntityLight.Custom_String_Required) }))
            {
                // User 2 explicitly filters for company 1's data — must NOT return any results
                var diffPropertyName = DiffEntityName.GetDifferentiationPropertyName();
                var maliciousFilter = new FilterItem(diffPropertyName, FilterOperator.equal, login1.User.Company_ID!.Value);

                var getData = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName)
                        .WithAuthToken(login2.AuthToken)
                        .WithFilter(maliciousFilter));

                var result = getData.Match(
                    r => r,
                    err => throw new Exception($"Get with malicious filter failed | {err.Code} | {err.Message}"));

                // The diff filter (Company_ID = 2) AND-ed with malicious filter (Company_ID = 1) = no results
                Assert.Empty(result.Data);

                // Sanity: user 1 can still see their own record normally
                var getData1 = await ApilaneService.GetDataAsync<CustomEntityLight>(
                    DataGetListRequest.New(CustomEntityLight.EntityName).WithAuthToken(login1.AuthToken));
                var result1 = getData1.Match(r => r, err => throw new Exception($"Get1 failed | {err.Code} | {err.Message}"));
                Assert.Single(result1.Data);
                Assert.Equal(idFromCompany1, result1.Data.Single().ID);
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

        private async Task<T> GetDataByID_ShouldSucceed<T>(string? authToken, long id)
        {
            var request = DataGetByIdRequest.New(CustomEntityLight.EntityName, id);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getData = await ApilaneService.GetDataByIdAsync<T>(request);

            return getData.Match(response =>
            {
                Assert.NotNull(response);
                return response;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task GetDataByID_Unauthorized_ShouldFail<T>(string? authToken, long id)
        {
            var request = DataGetByIdRequest.New(CustomEntityLight.EntityName, id);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getData = await ApilaneService.GetDataByIdAsync<T>(request);

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

        private async Task<AccountLoginResponse<UserItem>> LoginUserAsync(string userEmail, string userPassword)
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

                return success;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }
    }
}