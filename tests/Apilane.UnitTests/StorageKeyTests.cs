using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Services.Storage;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace Apilane.UnitTests
{
    /// <summary>
    /// File ids are read from the application's Files table, which its owner can write with custom SQL.
    /// A storage location must never leave the application's own files folder, whatever the id says.
    /// </summary>
    [TestClass]
    public class StorageKeyTests
    {
        private const string AppToken = "11111111-1111-1111-1111-111111111111";
        private const string OtherAppToken = "22222222-2222-2222-2222-222222222222";

        private string _root = null!;
        private LocalFileSystemProvider _provider = null!;
        private string _outsideFile = null!;

        [TestInitialize]
        public void Initialize()
        {
            _root = Path.Combine(Path.GetTempPath(), "apilane-storage-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>()
                {
                    ["Url"] = "http://localhost:5001",
                    ["PortalUrl"] = "http://localhost:5000",
                    ["FilesPath"] = _root,
                    ["InstallationKey"] = "unit-tests-installation-key-not-a-secret"
                })
                .Build();

            _provider = new LocalFileSystemProvider(new ApiConfiguration(configuration));

            // Another application's data, sitting next to ours as it does on the API server
            _outsideFile = Path.Combine(_root, OtherAppToken, $"{OtherAppToken}.db");
            Directory.CreateDirectory(Path.GetDirectoryName(_outsideFile) ?? _root);
            File.WriteAllText(_outsideFile, "other tenant data");
        }

        [TestCleanup]
        public void Cleanup()
        {
            Directory.Delete(_root, recursive: true);
        }

        private static async Task AssertNotFoundAsync(Func<Task> action)
        {
            var ex = await Assert.ThrowsExactlyAsync<ApilaneException>(action);

            Assert.AreEqual(AppErrors.NOT_FOUND, ex.Error);
        }

        [TestMethod]
        public async Task LocalProvider_PlainFileId_RoundTrips()
        {
            var fileId = Guid.NewGuid().ToString();
            var content = Encoding.UTF8.GetBytes("hello");

            using (var upload = new MemoryStream(content))
            {
                await _provider.PutAsync(AppToken, fileId, upload, content.Length);
            }

            Assert.IsTrue(await _provider.ExistsAsync(AppToken, fileId));
            Assert.AreEqual(content.Length, await _provider.GetSizeAsync(AppToken, fileId));

            using (var download = await _provider.GetAsync(AppToken, fileId))
            using (var reader = new StreamReader(download))
            {
                Assert.AreEqual("hello", await reader.ReadToEndAsync());
            }

            await _provider.DeleteAsync(AppToken, fileId);

            Assert.IsFalse(await _provider.ExistsAsync(AppToken, fileId));
        }

        public static IEnumerable<object[]> EscapingFileIds()
        {
            yield return new object[] { $"../../{OtherAppToken}/{OtherAppToken}.db" };
            yield return new object[] { $"..\\..\\{OtherAppToken}\\{OtherAppToken}.db" };
            yield return new object[] { $"../{OtherAppToken}.db" };
            yield return new object[] { "sub/file" };
            yield return new object[] { ".." };
            yield return new object[] { "." };
            yield return new object[] { "" };
            yield return new object[] { "C:\\Windows\\win.ini" };
            yield return new object[] { "/etc/passwd" };
            yield return new object[] { "file.txt:stream" };

            // Windows drops trailing dots and spaces, so these would resolve to the files folder itself
            if (OperatingSystem.IsWindows())
            {
                yield return new object[] { "..." };
                yield return new object[] { ".. " };
            }
        }

        [TestMethod]
        [DynamicData(nameof(EscapingFileIds))]
        public async Task LocalProvider_FileIdThatIsNotAPlainName_IsRejectedEverywhere(string fileId)
        {
            await AssertNotFoundAsync(() => _provider.GetAsync(AppToken, fileId));
            await AssertNotFoundAsync(() => _provider.ExistsAsync(AppToken, fileId));
            await AssertNotFoundAsync(() => _provider.GetSizeAsync(AppToken, fileId));
            await AssertNotFoundAsync(() => _provider.DeleteAsync(AppToken, fileId));

            using (var upload = new MemoryStream(new byte[] { 1 }))
            {
                await AssertNotFoundAsync(() => _provider.PutAsync(AppToken, fileId, upload, 1));
            }

            // Nothing outside the application's folder was read, replaced or removed
            Assert.AreEqual("other tenant data", File.ReadAllText(_outsideFile));
        }

        [TestMethod]
        public async Task LocalProvider_AbsolutePathToAnExistingFile_IsRejected()
        {
            await AssertNotFoundAsync(() => _provider.GetAsync(AppToken, _outsideFile));
            await AssertNotFoundAsync(() => _provider.DeleteAsync(AppToken, _outsideFile));

            Assert.IsTrue(File.Exists(_outsideFile));
        }

        [TestMethod]
        [DataRow("../other")]
        [DataRow("a/b")]
        [DataRow("..")]
        [DataRow("")]
        public async Task LocalProvider_ApplicationTokenThatIsNotAPlainName_IsRejected(string appToken)
        {
            // ExistsAsync, not GetAsync: a missing folder is NOT_FOUND there anyway, which would hide a
            // token that was accepted and merely pointed nowhere
            await AssertNotFoundAsync(() => _provider.ExistsAsync(appToken, Guid.NewGuid().ToString()));
        }

        [TestMethod]
        public void Build_PlainSegments_ReturnsTheObjectKey()
        {
            Assert.AreEqual($"{AppToken}/files/abc", StorageKey.Build(AppToken, "abc"));
        }

        [TestMethod]
        [DataRow("../x")]
        [DataRow("a/b")]
        [DataRow("a\\b")]
        [DataRow("..")]
        public void Build_FileIdWithPathSyntax_Throws(string fileId)
        {
            var ex = Assert.ThrowsExactly<ApilaneException>(() => StorageKey.Build(AppToken, fileId));

            Assert.AreEqual(AppErrors.NOT_FOUND, ex.Error);
        }
    }
}
