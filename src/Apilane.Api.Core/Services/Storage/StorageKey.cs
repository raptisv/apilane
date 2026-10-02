using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using System;
using System.IO;

namespace Apilane.Api.Core.Services.Storage
{
    /// <summary>
    /// Storage locations are built as {application token}/files/{file id}. Both parts come from data an
    /// application owner can influence (the file id is read from the application's own Files table, which
    /// custom endpoint SQL can write), so each must be a plain name: no path separators, no "..", no drive
    /// or stream prefix. Anything else could address another application's files or any file on the host.
    /// </summary>
    public static class StorageKey
    {
        private static readonly char[] _forbiddenCharacters = { '/', '\\', ':', '\0' };

        /// <summary>
        /// Returns the segment unchanged when it is a plain name; throws NOT_FOUND otherwise.
        /// </summary>
        public static string EnsureSegment(string? segment)
        {
            if (string.IsNullOrWhiteSpace(segment) ||
                segment == "." ||
                segment == ".." ||
                segment.IndexOfAny(_forbiddenCharacters) >= 0 ||
                Path.IsPathRooted(segment))
            {
                throw new ApilaneException(AppErrors.NOT_FOUND, "File not found.");
            }

            return segment;
        }

        /// <summary>
        /// The object key used by the cloud providers.
        /// </summary>
        public static string Build(string applicationToken, string fileId)
        {
            return $"{EnsureSegment(applicationToken)}/files/{EnsureSegment(fileId)}";
        }

        /// <summary>
        /// The path used by the local file system provider. On top of the segment checks, the resolved
        /// path must stay inside the application's files folder.
        /// </summary>
        public static string BuildLocalPath(string rootPath, string applicationToken, string fileId)
        {
            var filesFolder = Path.GetFullPath(Path.Combine(rootPath, EnsureSegment(applicationToken), "files"));
            var path = Path.GetFullPath(Path.Combine(filesFolder, EnsureSegment(fileId)));

            // Exactly the folder plus the id: an id the file system would rewrite (trailing dots or
            // spaces on Windows resolve to another name, or to the folder itself) is not a plain name.
            if (!string.Equals(path, filesFolder + Path.DirectorySeparatorChar + fileId, StringComparison.Ordinal))
            {
                throw new ApilaneException(AppErrors.NOT_FOUND, "File not found.");
            }

            return path;
        }
    }
}
