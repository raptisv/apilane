using System.Security.Cryptography;
using System.Text;

namespace Apilane.Common.Security
{
    public static class SecureCompare
    {
        /// <summary>
        /// Constant-time comparison of two secrets (installation keys, tokens), so an attacker
        /// cannot learn a secret byte by byte from response timing. Null or empty values never
        /// match anything.
        /// </summary>
        public static bool AreEqual(string? expected, string? provided)
        {
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(provided));
        }
    }
}
