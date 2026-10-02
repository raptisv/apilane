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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    public class FilesSecurityTests : AppicationTestsBase
    {
        public FilesSecurityTests() : base(SuiteContext.Shared)
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
        public async Task AnonymousUser_CannotUploadFile_Returns401(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Upload without auth token — no security configured → UNAUTHORIZED
            await PostFile_Unauthorized_ShouldFail(authToken: null);
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task AuthenticatedUser_CanUploadFile_Returns200(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var authToken = await RegisterAndLoginAsync("uploader@test.com", "password");

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
        public async Task AnonymousUser_CannotDownloadFile_Returns401(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var authToken = await RegisterAndLoginAsync("owner@test.com", "password");

            // Upload as authenticated user
            long fileId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                fileId = await PostFile_ShouldSucceed(authToken);
                Assert.True(fileId > 0);
            }

            // Download without auth token — no security configured for anonymous → UNAUTHORIZED
            await GetFileByID_Unauthorized_ShouldFail(authToken: null, fileId);
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task AuthenticatedOwner_CanDownloadOwnFile_Returns200(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var authToken = await RegisterAndLoginAsync("owner@test.com", "password");

            // Upload as UserA
            long fileId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                fileId = await PostFile_ShouldSucceed(authToken);
                Assert.True(fileId > 0);
            }

            // Download as UserA (the owner)
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED))
            {
                var fileItem = await GetFileByID_ShouldSucceed<FileItem>(authToken, fileId);
                Assert.NotNull(fileItem);
            }
        }

        /// <summary>
        /// A file's UID is read from the application's Files table, which its owner can write with custom
        /// SQL. A UID that resolves outside the application's own files folder must never be served.
        /// </summary>
        [Theory]
        [InlineData(false)] // relative: "../../{file}"
        [InlineData(true)]  // absolute path
        public async Task Download_WithFileUidPointingOutsideTheApplication_IsNotServed(bool absolutePath)
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            var authToken = await RegisterAndLoginAsync("owner@test.com", "password");

            long fileId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                fileId = await PostFile_ShouldSucceed(authToken);
            }

            // A file outside the application's folder, standing in for another application's data
            var outsideFile = Path.Combine(ApiConfiguration.FilesPath, $"outside-{Guid.NewGuid():N}.txt");
            File.WriteAllText(outsideFile, "other tenant data");

            try
            {
                // The storage path is {FilesPath}/{token}/files/{UID}
                var uid = absolutePath ? outsideFile : $"../../{Path.GetFileName(outsideFile)}";

                AddCustomEndpoint("PointFileOutside", $"UPDATE [Files] SET [UID] = '{uid}' WHERE [ID] = {fileId}");
                using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "PointFileOutside", type: SecurityTypes.CustomEndpoint))
                {
                    (await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New("PointFileOutside")))
                        .Match(r => r, e => throw new Exception($"Update failed | {e.Code} | {e.Message}"));
                }

                using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                    inRole: Globals.AUTHENTICATED))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Files/Download?FileID={fileId}");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);

                    using var response = await HttpClient.SendAsync(request);
                    var body = await response.Content.ReadAsStringAsync();

                    Assert.DoesNotContain("other tenant data", body);
                    Assert.False(response.IsSuccessStatusCode);
                    Assert.Contains("NOT_FOUND", body);
                }
            }
            finally
            {
                File.Delete(outsideFile);
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task AuthenticatedNonOwner_CannotDownloadOtherFile_Returns404(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var userAToken = await RegisterAndLoginAsync("usera@test.com", "password");
            var userBToken = await RegisterAndLoginAsync("userb@test.com", "password");

            // Upload as UserA
            long fileId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                fileId = await PostFile_ShouldSucceed(userAToken);
                Assert.True(fileId > 0);
            }

            // Download as UserB (non-owner) — Owned access means only the record owner can read it
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                recordAccess: EndpointRecordAuthorization.Owned))
            {
                await GetFileByID_NonOwner_ShouldFail<FileItem>(userBToken, fileId);
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private async Task<string> RegisterAndLoginAsync(string email, string password)
        {
            var registerResult = await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem()
            {
                Username = email,
                Email = email,
                Password = password
            }));

            registerResult.Match(
                _ => { },
                error => throw new Exception($"Register failed | {error.Code} | {error.Message} | {error.Property}"));

            var loginResult = await ApilaneService.AccountLoginAsync<UserItem>(AccountLoginRequest.New(new LoginItem()
            {
                Email = email,
                Password = password
            }));

            return loginResult.Match(success =>
            {
                Assert.NotNull(success);
                Assert.NotNull(success.AuthToken);
                return success.AuthToken;
            },
            error => throw new Exception($"Login failed | {error.Code} | {error.Message} | {error.Property}"));
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

        private async Task PostFile_Unauthorized_ShouldFail(string? authToken)
        {
            var request = FilePostRequest.New();

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var postFile = await ApilaneService.PostFileAsync(request, new byte[1] { 0 });

            postFile.Match(
                response => throw new Exception("We should not be here — upload succeeded unexpectedly"),
                error =>
                {
                    Assert.NotNull(error);
                    Assert.Equal(ValidationError.UNAUTHORIZED, error.Code);
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

        private async Task GetFileByID_Unauthorized_ShouldFail(string? authToken, long id)
        {
            var request = FileGetByIdRequest.New(id);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getFile = await ApilaneService.GetFileByIdAsync<FileItem>(request);

            getFile.Match(
                response => throw new Exception("We should not be here — download succeeded unexpectedly"),
                error =>
                {
                    Assert.NotNull(error);
                    Assert.Equal(ValidationError.UNAUTHORIZED, error.Code);
                });
        }

        private async Task GetFileByID_NonOwner_ShouldFail<T>(string? authToken, long id)
        {
            var request = FileGetByIdRequest.New(id);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getFile = await ApilaneService.GetFileByIdAsync<T>(request);

            getFile.Match(
                response => throw new Exception("We should not be here — non-owner download succeeded unexpectedly"),
                error =>
                {
                    Assert.NotNull(error);
                    Assert.Equal(ValidationError.NOT_FOUND, error.Code);
                });
        }
    }
}
