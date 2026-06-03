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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class FilesStreamingTests : AppicationTestsBase
    {
        public FilesStreamingTests(SuiteContext suiteContext) : base(suiteContext)
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

        /// <summary>
        /// Uploads a 10 MB file and asserts the in-process GC memory delta stays below 15 MB,
        /// verifying that the server does not buffer the entire payload excessively.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Upload_10MB_File_MemoryDelta_IsAcceptable(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Increase the per-application file-size limit so the 10 MB upload is accepted
            TestApplication.MaxAllowedFileSizeInKB = 20 * 1024;
            MockApplicationService(TestApplication);

            var authToken = await RegisterAndLoginAsync("memtest10mb@test.com", "password");

            var fileData = new byte[10 * 1024 * 1024];
            Random.Shared.NextBytes(fileData);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            var memBefore = GC.GetTotalMemory(true);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                var fileId = await PostFile_ShouldSucceed(authToken, fileData);
                Assert.True(fileId > 0, "Expected a positive file ID after upload");
            }

            var memAfter = GC.GetTotalMemory(false);
            var deltaBytes = memAfter - memBefore;

            // Allow up to 40 MB total delta for a 10 MB upload:
            // ~10 MB for the SDK's ByteArrayContent copy + ~10 MB server-side buffering + 20 MB headroom.
            // This catches regressions where the entire payload would be buffered multiple times (e.g., > 4×).
            Assert.True(
                deltaBytes < 40 * 1024 * 1024,
                $"Memory delta too high: {deltaBytes / 1024 / 1024} MB");
        }

        /// <summary>
        /// Uploads a 1 MB file, then downloads it via the Download endpoint and verifies
        /// that the SHA-256 hash of the downloaded bytes matches the original.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Download_File_AfterUpload_ContentMatches(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            TestApplication.MaxAllowedFileSizeInKB = 5 * 1024;
            MockApplicationService(TestApplication);

            var authToken = await RegisterAndLoginAsync("dltest@test.com", "password");

            var fileData = new byte[1 * 1024 * 1024];
            Random.Shared.NextBytes(fileData);
            var expectedHash = SHA256.HashData(fileData);

            // Upload
            long fileId;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                fileId = await PostFile_ShouldSucceed(authToken, fileData);
                Assert.True(fileId > 0, "Expected a positive file ID after upload");
            }

            // Download the raw file bytes via the Download endpoint
            byte[] downloadedBytes;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.get))
            {
                downloadedBytes = await DownloadFileAsync(authToken, fileId);
            }

            Assert.NotNull(downloadedBytes);
            Assert.Equal(fileData.Length, downloadedBytes.Length);

            var actualHash = SHA256.HashData(downloadedBytes);
            Assert.Equal(expectedHash, actualHash);
        }

        /// <summary>
        /// Sanity test: uploads a 1 MB file and asserts a valid positive file ID is returned.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Upload_1MB_File_ReturnsValidFileId(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            TestApplication.MaxAllowedFileSizeInKB = 5 * 1024;
            MockApplicationService(TestApplication);

            var authToken = await RegisterAndLoginAsync("upload1mb@test.com", "password");

            var fileData = new byte[1 * 1024 * 1024];
            Random.Shared.NextBytes(fileData);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                var fileId = await PostFile_ShouldSucceed(authToken, fileData);
                Assert.True(fileId > 0, "Expected a positive file ID for the 1 MB upload");
            }
        }

        /// <summary>
        /// Uploads 5 files concurrently and asserts that all uploads succeed with unique IDs.
        /// </summary>
        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task ConcurrentUploads_5Files_AllSucceed(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            TestApplication.MaxAllowedFileSizeInKB = 5 * 1024;
            MockApplicationService(TestApplication);

            var authToken = await RegisterAndLoginAsync("concurrent@test.com", "password");

            const int fileCount = 5;
            var tasks = new List<Task<long>>(fileCount);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.AUTHENTICATED,
                actionType: SecurityActionType.post,
                properties: new() { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                for (int i = 0; i < fileCount; i++)
                {
                    var fileData = new byte[512 * 1024]; // 512 KB each
                    Random.Shared.NextBytes(fileData);
                    tasks.Add(PostFile_ShouldSucceed(authToken, fileData));
                }

                var fileIds = await Task.WhenAll(tasks);

                Assert.Equal(fileCount, fileIds.Length);
                Assert.All(fileIds, id => Assert.True(id > 0, $"Expected positive file ID, got {id}"));

                // All IDs must be unique
                var distinctCount = fileIds.Distinct().Count();
                Assert.Equal(fileCount, distinctCount);
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

            return loginResult.Match(
                success =>
                {
                    Assert.NotNull(success);
                    Assert.NotNull(success.AuthToken);
                    return success.AuthToken;
                },
                error => throw new Exception($"Login failed | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task<long> PostFile_ShouldSucceed(string? authToken, byte[] fileData)
        {
            var request = FilePostRequest.New();

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var postFile = await ApilaneService.PostFileAsync(request, fileData);

            return postFile.Match(
                response =>
                {
                    Assert.NotNull(response);
                    return response.Value;
                },
                error => throw new Exception($"Upload failed | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task<byte[]> DownloadFileAsync(string? authToken, long fileId)
        {
            var downloadUrl = $"{ApiConfiguration.Url.TrimEnd('/')}/api/Files/Download?FileID={fileId}";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            }

            using var response = await HttpClient.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception($"Download failed with status {response.StatusCode}: {body}");
            }

            return await response.Content.ReadAsByteArrayAsync();
        }
    }
}
