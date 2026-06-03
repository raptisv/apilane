using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface ICloudStorageProvider
    {
        /// <summary>
        /// Retrieves the stored content for the specified file.
        /// </summary>
        /// <param name="applicationToken">The application token.</param>
        /// <param name="fileId">The file identifier.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A stream containing the file content.</returns>
        Task<Stream> GetAsync(string applicationToken, string fileId, CancellationToken ct = default);

        /// <summary>
        /// Stores the specified content and returns the created file identifier.
        /// </summary>
        /// <param name="applicationToken">The application token.</param>
        /// <param name="fileId">The file identifier.</param>
        /// <param name="content">The content to store.</param>
        /// <param name="contentLength">The length of the content.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The stored file identifier.</returns>
        Task<string> PutAsync(string applicationToken, string fileId, Stream content, long contentLength, CancellationToken ct = default);

        /// <summary>
        /// Deletes the specified file.
        /// </summary>
        /// <param name="applicationToken">The application token.</param>
        /// <param name="fileId">The file identifier.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task DeleteAsync(string applicationToken, string fileId, CancellationToken ct = default);

        /// <summary>
        /// Determines whether the specified file exists.
        /// </summary>
        /// <param name="applicationToken">The application token.</param>
        /// <param name="fileId">The file identifier.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns><c>true</c> if the file exists; otherwise, <c>false</c>.</returns>
        Task<bool> ExistsAsync(string applicationToken, string fileId, CancellationToken ct = default);

        /// <summary>
        /// Gets the size of the specified file.
        /// </summary>
        /// <param name="applicationToken">The application token.</param>
        /// <param name="fileId">The file identifier.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The file size in bytes.</returns>
        Task<long> GetSizeAsync(string applicationToken, string fileId, CancellationToken ct = default);
    }
}
