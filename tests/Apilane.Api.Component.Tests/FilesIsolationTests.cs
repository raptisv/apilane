using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Enums;
using Apilane.Net.Models.Files;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class FilesIsolationTests : AppicationTestsBase
    {
        public FilesIsolationTests(SuiteContext suiteContext) : base(suiteContext)
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
        public async Task UploadFile_ToApp_FileIdReturned(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var (userId, authToken) = await RegisterAndLoginAsync("uploader@test.com", "password");

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                var fileId = await PostFile_ShouldSucceed(authToken);
                Assert.True(fileId > 0);
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task GetFileRecord_AfterUpload_ReturnsCorrectData(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var (ownerUserId, ownerToken) = await RegisterAndLoginAsync("owner@test.com", "password");
            var (_, nonOwnerToken) = await RegisterAndLoginAsync("other@test.com", "password");

            long fileId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                fileId = await PostFile_ShouldSucceed(ownerToken);
                Assert.True(fileId > 0);
            }

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                properties: new() { nameof(FileItem.ID), nameof(FileItem.Owner), nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) },
                recordAccess: EndpointRecordAuthorization.Owned))
            {
                await GetFileByID_NonOwner_ShouldFail<FileItem>(nonOwnerToken, fileId);

                var fileItem = await GetFileByID_ShouldSucceed<FileItem>(ownerToken, fileId);
                Assert.Equal(fileId, fileItem.ID);
                Assert.Equal(ownerUserId, fileItem.Owner);
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task FileRecord_BelongsToUploadingUser(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var (ownerUserId, ownerToken) = await RegisterAndLoginAsync("owner@test.com", "password");

            long fileId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                fileId = await PostFile_ShouldSucceed(ownerToken);
                Assert.True(fileId > 0);
            }

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                properties: new() { nameof(FileItem.ID), nameof(FileItem.Owner), nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) },
                recordAccess: EndpointRecordAuthorization.Owned))
            {
                var fileItem = await GetFileByID_ShouldSucceed<FileItem>(ownerToken, fileId);
                Assert.Equal(ownerUserId, fileItem.Owner);
            }
        }

        private async Task<(long UserId, string AuthToken)> RegisterAndLoginAsync(string email, string password)
        {
            var registerResult = await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem()
            {
                Username = email,
                Email = email,
                Password = password
            }));

            var userId = registerResult.Match(newUserId => newUserId,
                error => throw new Exception($"Register failed | {error.Code} | {error.Message} | {error.Property}"));

            var loginResult = await ApilaneService.AccountLoginAsync<UserItem>(AccountLoginRequest.New(new LoginItem()
            {
                Email = email,
                Password = password
            }));

            var authToken = loginResult.Match(success =>
            {
                Assert.NotNull(success);
                Assert.NotNull(success.AuthToken);
                Assert.NotNull(success.User);
                Assert.Equal(email, success.User.Email);
                return success.AuthToken;
            },
            error => throw new Exception($"Login failed | {error.Code} | {error.Message} | {error.Property}"));

            return (userId, authToken);
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

        private async Task GetFileByID_NonOwner_ShouldFail<T>(string? authToken, long id)
        {
            var request = FileGetByIdRequest.New(id);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getFile = await ApilaneService.GetFileByIdAsync<T>(request);

            getFile.Match(response => throw new Exception("We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.NOT_FOUND, error.Code);
            });
        }
    }
}
