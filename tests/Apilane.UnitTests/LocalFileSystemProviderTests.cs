using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Services.Storage;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Apilane.UnitTests
{
    [TestClass]
    public class LocalFileSystemProviderTests
    {
        private string _tempDir = string.Empty;
        private LocalFileSystemProvider _provider = null!;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LocalFileSystemProviderTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new System.Collections.Generic.Dictionary<string, string?>
                {
                    ["Url"] = "http://localhost",
                    ["FilesPath"] = _tempDir,
                    ["PortalUrl"] = "http://localhost:5000",
                    ["InstallationKey"] = "test-key",
                    ["MinThreads"] = "4",
                    ["FileStorage:Provider"] = "LocalFileSystem",
                    ["InvalidFilesExtentions:0"] = ".exe",
                    ["OpenTelemetry:Metrics:Enabled"] = "false",
                    ["OpenTelemetry:Tracing:Enabled"] = "false",
                    ["OpenTelemetry:Tracing:Url"] = "http://localhost",
                    ["OpenTelemetry:Tracing:SampleRatio"] = "0.1",
                })
                .Build();

            var apiConfig = new ApiConfiguration(config);
            _provider = new LocalFileSystemProvider(apiConfig);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }

        [TestMethod]
        public async Task PutAsync_Then_ExistsAsync_And_GetSizeAsync_Should_Work()
        {
            // Arrange
            var appToken = "app-token-1";
            var fileId = "file-001.txt";
            var content = "Hello, Apilane!";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            // Act
            await _provider.PutAsync(appToken, fileId, stream, stream.Length);
            var exists = await _provider.ExistsAsync(appToken, fileId);
            var size = await _provider.GetSizeAsync(appToken, fileId);

            // Assert
            Assert.IsTrue(exists);
            Assert.AreEqual(Encoding.UTF8.GetByteCount(content), (int)size);
        }

        [TestMethod]
        public async Task GetAsync_After_PutAsync_Should_Return_Correct_Content()
        {
            // Arrange
            var appToken = "app-token-2";
            var fileId = "file-002.txt";
            var content = "Stream content check";
            using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            await _provider.PutAsync(appToken, fileId, uploadStream, uploadStream.Length);

            // Act
            using var downloadStream = await _provider.GetAsync(appToken, fileId);
            using var reader = new StreamReader(downloadStream);
            var result = await reader.ReadToEndAsync();

            // Assert
            Assert.AreEqual(content, result);
        }

        [TestMethod]
        public async Task DeleteAsync_Should_Remove_File()
        {
            // Arrange
            var appToken = "app-token-3";
            var fileId = "file-003.txt";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("delete me"));

            await _provider.PutAsync(appToken, fileId, stream, stream.Length);
            Assert.IsTrue(await _provider.ExistsAsync(appToken, fileId));

            // Act
            await _provider.DeleteAsync(appToken, fileId);

            // Assert
            Assert.IsFalse(await _provider.ExistsAsync(appToken, fileId));
        }

        [TestMethod]
        public async Task DeleteAsync_NonExistent_File_Should_Not_Throw()
        {
            // Should be a no-op
            await _provider.DeleteAsync("no-app", "no-file.txt");
        }

        [TestMethod]
        public async Task GetAsync_NonExistent_File_Should_Throw_ApilaneException_NotFound()
        {
            // Act & Assert
            var ex = await Assert.ThrowsExceptionAsync<ApilaneException>(
                () => _provider.GetAsync("ghost-app", "ghost-file.txt"));

            Assert.AreEqual(AppErrors.NOT_FOUND, ex.Error);
        }
    }
}
