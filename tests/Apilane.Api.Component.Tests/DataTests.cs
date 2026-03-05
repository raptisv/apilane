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
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class DataTests : AppicationTestsBase
    {
        public DataTests(SuiteContext suiteContext) : base(suiteContext)
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