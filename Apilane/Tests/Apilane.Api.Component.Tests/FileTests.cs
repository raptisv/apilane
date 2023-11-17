using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Enums;
using Apilane.Net.Models.Files;
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
    public class FileTests : AppicationTestsBase
    {
        public FileTests(SuiteContext suiteContext) : base(suiteContext)
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

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Entity_Get_Post_Put_Delete_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

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
            await GetFile_Unauthorized_ShouldFail<FileItem>(authtoken);

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, "Files",
                inRole: securityRole))
            {
                await GetFile_ShouldSucceed<FileItem>(authtoken);
            }

            // Post
            await PostFile_Unauthorized_ShouldFail(authtoken, new FileItem());

            long postedFileId = 0;
            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, "Files",
                inRole: securityRole,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.Public), nameof(FileItem.UID) }))
            {
                postedFileId = await PostFile_ShouldSucceed(authtoken);
                Assert.True(postedFileId > 0);
            }

            // Get by id
            await GetFileByID_Unauthorized_ShouldFail<FileItem>(authtoken, postedFileId);

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, "Files",
                inRole: securityRole))
            {
                await GetFileByID_ShouldSucceed<FileItem>(authtoken, postedFileId);
            }

            // Delete
            await DeleteFile_Unauthorized_ShouldFail(authtoken, new List<long>() { postedFileId });

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, "Files",
                inRole: securityRole,
                actionType: SecurityActionType.delete))
            {
                var deletedIds = await DeleteFile_ShouldSucceed(authtoken, new List<long>() { postedFileId });
                Assert.NotNull(deletedIds);
                Assert.NotEmpty(deletedIds);
                Assert.Single(deletedIds);
                Assert.Equal(postedFileId, deletedIds.First());
            }
        }

        private async Task<List<T>> GetFile_ShouldSucceed<T>(string? authToken)
        {
            var request = FileGetListRequest.New();

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getFile = await ApilaneService.GetFilesAsync<T>(request);

            return getFile.Match(response =>
            {
                Assert.NotNull(response);
                Assert.NotNull(response.Data);
                return response.Data;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task GetFile_Unauthorized_ShouldFail<T>(string? authToken)
        {
            var request = FileGetListRequest.New();

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getFile = await ApilaneService.GetFilesAsync<T>(request);

            getFile.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<T> GetFileByID_ShouldSucceed<T>(string? authToken, long id)
        {
            var request = FileGetByIdRequest.New(id);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getFile = await ApilaneService.GetFileByIdAsync<T>(request);

            return getFile.Match(response =>
            {
                Assert.NotNull(response);
                return response;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task GetFileByID_Unauthorized_ShouldFail<T>(string? authToken, long id)
        {
            var request = FileGetByIdRequest.New(id);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getFile = await ApilaneService.GetFileByIdAsync<T>(request);

            getFile.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<long> PostFile_ShouldSucceed(string? authToken)
        {
            var request = FilePostRequest.New();

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var postFile = await ApilaneService.PostFileAsync(request, new byte[1] { 0 });

            return postFile.Match(response =>
            {
                Assert.NotNull(response);
                return response.Value;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task PostFile_Unauthorized_ShouldFail(string? authToken, object data)
        {
            var request = FilePostRequest.New();

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var postFile = await ApilaneService.PostFileAsync(request, new byte[1] { 0 });

            postFile.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<long[]> DeleteFile_ShouldSucceed(string? authToken, List<long> Ids)
        {
            var request = FileDeleteRequest.New(Ids);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var deleteFile = await ApilaneService.DeleteFileAsync(request);

            return deleteFile.Match(response => response,
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task DeleteFile_Unauthorized_ShouldFail(string? authToken, List<long> Ids)
        {
            var request = FileDeleteRequest.New(Ids);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var deleteFile = await ApilaneService.DeleteFileAsync(request);

            deleteFile.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<long> RegisterUserAsync(string userEmail, string userPassword)
        {
            var registerResult = await ApilaneService.AccountRegisterAsync(new RegisterItem()
            {
                Username = userEmail,
                Email = userEmail,
                Password = userPassword
            });

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