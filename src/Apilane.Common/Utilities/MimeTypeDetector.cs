using System.Collections.Generic;
using System.IO;

namespace Apilane.Common.Utilities
{
    /// <summary>
    /// Detects MIME types from file names using file extensions.
    /// </summary>
    public static class MimeTypeDetector
    {
        private const string DefaultMimeType = "application/octet-stream";

        private static readonly IReadOnlyDictionary<string, string> MimeTypes = new Dictionary<string, string>
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".svg"] = "image/svg+xml",
            [".webp"] = "image/webp",
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv",
            [".json"] = "application/json",
            [".xml"] = "application/xml",
            [".html"] = "text/html",
            [".css"] = "text/css",
            [".js"] = "application/javascript",
            [".zip"] = "application/zip",
            [".rar"] = "application/vnd.rar",
            [".7z"] = "application/x-7z-compressed",
            [".tar"] = "application/x-tar",
            [".gz"] = "application/gzip",
            [".mp3"] = "audio/mpeg",
            [".wav"] = "audio/wav",
            [".mp4"] = "video/mp4",
            [".avi"] = "video/x-msvideo",
            [".mov"] = "video/quicktime",
            [".mkv"] = "video/x-matroska"
        };

        /// <summary>
        /// Gets the MIME type for the specified file name.
        /// </summary>
        /// <param name="fileName">The file name or path.</param>
        /// <returns>The detected MIME type, or <c>application/octet-stream</c> when unknown.</returns>
        public static string GetMimeType(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();

            if (string.IsNullOrEmpty(extension))
            {
                return DefaultMimeType;
            }

            return MimeTypes.TryGetValue(extension, out var mimeType)
                ? mimeType
                : DefaultMimeType;
        }
    }
}
