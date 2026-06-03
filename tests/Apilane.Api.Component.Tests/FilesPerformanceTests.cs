using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Files;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class FilesPerformanceTests : AppicationTestsBase
    {
        private readonly ITestOutputHelper _output;

        public FilesPerformanceTests(SuiteContext suiteContext, ITestOutputHelper output)
            : base(suiteContext)
        {
            _output = output;
        }

        private class FileSizeAndStorageTestData : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                var fileSizes = new (int Bytes, string Label)[]
                {
                    (1 * 1024,        "1KB"),
                    (10 * 1024,       "10KB"),
                    (1 * 1024 * 1024, "1MB"),
                    (10 * 1024 * 1024,"10MB"),
                };

                foreach (var (bytes, label) in fileSizes)
                {
                    // SQLite only — keeps the matrix manageable; storage config is orthogonal to perf
                    yield return new object[] { DatabaseType.SQLLite, (string?)null!, false, bytes, label };
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Theory]
        [ClassData(typeof(FileSizeAndStorageTestData))]
        public async Task Upload_And_Download_Benchmark(
            DatabaseType dbType,
            string? connectionString,
            bool useDiffEntity,
            int fileSize,
            string sizeLabel)
        {
            // ── setup ──────────────────────────────────────────────────────────────
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Allow files up to 20 MB so all test sizes succeed
            TestApplication.MaxAllowedFileSizeInKB = 20 * 1024;
            MockApplicationService(TestApplication);

            // ── generate random payload ────────────────────────────────────────────
            var data = new byte[fileSize];
            Random.Shared.NextBytes(data);

            // ── upload benchmark ───────────────────────────────────────────────────
            long uploadedFileId;

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new List<string> { nameof(FileItem.Size), nameof(FileItem.Name), nameof(FileItem.UID) }))
            {
                var uploadRequest = FilePostRequest.New();

                var sw = Stopwatch.StartNew();
                var uploadResult = await ApilaneService.PostFileAsync(uploadRequest, data);
                sw.Stop();

                _output.WriteLine($"Upload {sizeLabel}: {sw.ElapsedMilliseconds}ms");

                uploadedFileId = uploadResult.Match(
                    response =>
                    {
                        Assert.NotNull(response);
                        return response.Value;
                    },
                    error => throw new Exception($"Upload failed | {error.Code} | {error.Message} | {error.Property}"));

                Assert.True(uploadedFileId > 0, $"Upload of {sizeLabel} should succeed and return a valid file ID");
            }

            // ── download benchmark ─────────────────────────────────────────────────
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.ANONYMOUS))
            {
                var downloadRequest = FileGetByIdRequest.New(uploadedFileId);

                var sw = Stopwatch.StartNew();
                var downloadResult = await ApilaneService.GetFileByIdAsync<FileItem>(downloadRequest);
                sw.Stop();

                _output.WriteLine($"Download {sizeLabel}: {sw.ElapsedMilliseconds}ms");

                downloadResult.Match(
                    response =>
                    {
                        Assert.NotNull(response);
                        return response;
                    },
                    error => throw new Exception($"Download failed | {error.Code} | {error.Message} | {error.Property}"));
            }

            // ── cleanup ────────────────────────────────────────────────────────────
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "Files",
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.delete))
            {
                var deleteRequest = FileDeleteRequest.New(new List<long> { uploadedFileId });
                var deleteResult = await ApilaneService.DeleteFileAsync(deleteRequest);

                deleteResult.Match(
                    ids => ids,
                    error => throw new Exception($"Cleanup delete failed | {error.Code} | {error.Message} | {error.Property}"));
            }
        }
    }
}
