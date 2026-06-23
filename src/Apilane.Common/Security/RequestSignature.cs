using System;
using System.Security.Cryptography;
using System.Text;

namespace Apilane.Common.Security
{
    /// <summary>
    /// Builds and verifies HMAC signatures for the signed-request authentication scheme.
    ///
    /// The client proves possession of its secret (the <c>AuthTokens.Token</c> GUID) without ever
    /// transmitting it: it signs a canonical representation of the request and sends only the
    /// resulting signature plus non-secret metadata (key id, timestamp).
    ///
    /// The canonical string is a newline-joined list of, in order:
    ///   keyId, HTTP method (upper-case), path+query (as sent on the wire), unix-ms timestamp,
    ///   base64(SHA-256(body bytes)).
    ///
    /// IMPORTANT: This exact construction must be mirrored byte-for-byte by every client SDK
    /// (.NET and JavaScript). Changing it is a breaking protocol change.
    /// </summary>
    public static class RequestSignature
    {
        public static string BuildCanonicalString(
            string keyId,
            string method,
            string pathAndQuery,
            string timestamp,
            byte[]? body)
        {
            return string.Join("\n", new[]
            {
                keyId,
                (method ?? string.Empty).ToUpperInvariant(),
                pathAndQuery ?? string.Empty,
                timestamp ?? string.Empty,
                ComputeBodyHash(body)
            });
        }

        public static string ComputeBodyHash(byte[]? body)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(body ?? Array.Empty<byte>()));
        }

        public static string ComputeSignature(string secret, string canonicalString)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalString)));
        }

        /// <summary>
        /// Constant-time comparison of two signatures to avoid timing side-channels.
        /// </summary>
        public static bool SignaturesMatch(string expected, string provided)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected ?? string.Empty),
                Encoding.UTF8.GetBytes(provided ?? string.Empty));
        }
    }
}
