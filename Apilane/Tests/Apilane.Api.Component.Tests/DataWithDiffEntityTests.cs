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
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES ({1}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = {1} WHERE [ID] = {userIdCompany_1};",
            };

            AddCustomEndpoint(updateDiffPropertyEndpointName_1, customEndpointQuery_1); // Set company 1
            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, updateDiffPropertyEndpointName_1, type: SecurityTypes.CustomEndpoint))
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
                _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES ({2}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = {2} WHERE [ID] = {userIdCompany_2};",
            };

            AddCustomEndpoint(updateDiffPropertyEndpointName_2, customEndpointQuery_2); // Set company 2
            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, updateDiffPropertyEndpointName_2, type: SecurityTypes.CustomEndpoint))
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
            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
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

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
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

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
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

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
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

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
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
            var loginResult = await ApilaneService.AccountLoginAsync<UserItem>(new LoginItem()
            {
                Email = userEmail,
                Password = userPassword
            });

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