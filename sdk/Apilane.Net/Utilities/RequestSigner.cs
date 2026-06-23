using System;
using System.Security.Cryptography;
using System.Text;

namespace Apilane.Net.Utilities
{
    /// <summary>
    /// Client-side counterpart of the server's signed-request scheme. Produces an HMAC-SHA256
    /// signature that proves possession of the secret (the AuthToken) WITHOUT transmitting it.
    ///
    /// The canonical string MUST match the server byte-for-byte:
    ///   keyId \n METHOD(upper) \n path+query \n timestamp(unix ms) \n base64(sha256(body))
    /// </summary>
    public static class RequestSigner
    {
        public const string KeyIdHeader = "x-auth-keyid";
        public const string TimestampHeader = "x-auth-timestamp";
        public const string SignatureHeader = "x-auth-signature";

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
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(body ?? Array.Empty<byte>()));
            }
        }

        public static string ComputeSignature(string secret, string canonicalString)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalString)));
            }
        }
    }
}
