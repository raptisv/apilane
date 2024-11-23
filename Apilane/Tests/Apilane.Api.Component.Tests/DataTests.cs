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

        private async Task Assert_CRUD_With_AuthToken_Security_Async(string? authtoken, string securityRole)
        {
            // Get
            await GetData_Unauthorized_ShouldFail<CustomEntityLight>(authtoken);

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole))
            {
                await GetData_ShouldSucceed<CustomEntityLight>(authtoken);
            }

            // Post
            await PostData_Unauthorized_ShouldFail(authtoken, new CustomEntityLight());

            long postedDataId = 0;
            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                postedDataId = await PostData_ShouldSucceed(authtoken, new CustomEntityLight());
                Assert.True(postedDataId > 0);
            }

            // Put
            await PutData_Unauthorized_ShouldFail(authtoken, new CustomEntityLight());

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                var updatedValues = await PutData_ShouldSucceed(authtoken, new CustomEntityLight() { ID = postedDataId });
                Assert.Equal(1, updatedValues);
            }

            // Delete
            await DeleteData_Unauthorized_ShouldFail(authtoken, new List<long>() { postedDataId });

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
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

                return success.AuthToken;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }
    }
}