using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services.Storage
{
    public class LocalFileSystemProvider : ICloudStorageProvider
    {
        private readonly string _rootPath;

        public LocalFileSystemProvider(ApiConfiguration apiConfiguration)
        {
            _rootPath = string.IsNullOrWhiteSpace(apiConfiguration.FilesPath)
                ? Path.GetTempPath()
                : apiConfiguration.FilesPath;
        }

        private string BuildPath(string appToken, string fileId)
            => Path.Combine(_rootPath, appToken, "files", fileId);

        public Task<Stream> GetAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var path = BuildPath(applicationToken, fileId);
            try
            {
                Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult(stream);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                throw new ApilaneException(AppErrors.NOT_FOUND, $"File '{fileId}' not found.");
            }
        }

        public async Task<string> PutAsync(string applicationToken, string fileId, Stream content, long contentLength, CancellationToken ct = default)
        {
            var path = BuildPath(applicationToken, fileId);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                await content.CopyToAsync(fileStream, ct);
                return path;
            }
            catch (IOException ex)
            {
                throw new ApilaneException(AppErrors.ERROR, $"Failed to write file '{fileId}': {ex.Message}");
            }
        }

        public Task DeleteAsync(string applicationToken, string fileId, CancellationToken ct = default)
        {
            var path = BuildPath(applicationToken, fileId);
            var info = new FileInfo(path);
            if (info.Exists)
            {
                info.Delete();
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string applicationToken, string fileId, CancellationToken ct = default)
            => Task.FromResult(File.Exists(BuildPath(applicationToken, fileId)));

        public Task<long> GetSizeAsync(string applicationToken, string fileId, CancellationToken ct = default)
            => Task.FromResult(new FileInfo(BuildPath(applicationToken, fileId)).Length);
    }
}
