using Apilane.Common.Extensions;
using System;
using System.Security.Cryptography;
using System.Text;

namespace Apilane.Common.Security
{
    /// <summary>
    /// One-way password hashing for application users: PBKDF2-HMAC-SHA512, a random per-password
    /// salt and constant-time verification.
    ///
    /// Stored format (all fields base64 except the iteration count):
    ///   $pbkdf2-sha512$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;
    ///
    /// The iteration count is embedded so it can be raised later without invalidating existing
    /// hashes. Values produced before this class existed are reversible DES ciphertext created by
    /// <see cref="ApplicationExtensions.ApplicationEncrypt"/>; <see cref="VerifyAny"/> still accepts
    /// them so existing users keep logging in, and reports that the stored value should be
    /// replaced with a hash.
    /// </summary>
    public static class PasswordHasher
    {
        public const string Prefix = "$pbkdf2-sha512$";

        public const int DefaultIterations = 100_000;

        private const int SaltSizeBytes = 16;

        private const int HashSizeBytes = 32;

        private const int MinimumIterations = 1_000;

        /// <summary>
        /// True when the value is in the hashed format produced by <see cref="Hash"/>.
        /// </summary>
        public static bool IsHash(string? value)
        {
            return !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Hashes a password with a fresh random salt. Two calls with the same password produce
        /// different values.
        /// </summary>
        public static string Hash(string password, int iterations = DefaultIterations)
        {
            ArgumentNullException.ThrowIfNull(password);

            if (iterations < MinimumIterations)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations), $"At least {MinimumIterations} iterations are required");
            }

            var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, HashSizeBytes);

            return $"{Prefix}{iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Verifies a password against a value produced by <see cref="Hash"/>. Returns false for
        /// null, malformed or non-hash input.
        /// </summary>
        public static bool Verify(string? password, string? storedHash)
        {
            if (password is null || !IsHash(storedHash))
            {
                return false;
            }

            var parts = storedHash!.Substring(Prefix.Length).Split('$');

            if (parts.Length != 3 ||
                !int.TryParse(parts[0], out var iterations) ||
                iterations < 1)
            {
                return false;
            }

            byte[] salt;
            byte[] expected;

            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expected = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            if (salt.Length == 0 || expected.Length == 0)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        /// <summary>
        /// Verifies a password against either the current hashed format or the legacy reversible
        /// format (DES ciphertext keyed by the application's encryption key).
        /// </summary>
        /// <param name="password">The password supplied by the user.</param>
        /// <param name="stored">The value stored in the Users.Password column.</param>
        /// <param name="wrappedAppEncryptionKey">
        /// The application's encryption key exactly as stored on <c>DBWS_Application.EncryptionKey</c>
        /// (i.e. wrapped with <see cref="Globals.EncryptionKey"/>). Only needed for legacy values.
        /// </param>
        /// <param name="needsRehash">
        /// True when the password matched a legacy value; the caller should replace the stored
        /// value with <see cref="Hash"/> so the reversible form disappears over time.
        /// </param>
        public static bool VerifyAny(string? password, string? stored, string? wrappedAppEncryptionKey, out bool needsRehash)
        {
            needsRehash = false;

            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
            {
                return false;
            }

            if (IsHash(stored))
            {
                return Verify(password, stored);
            }

            if (string.IsNullOrEmpty(wrappedAppEncryptionKey))
            {
                return false;
            }

            string legacy;

            try
            {
                legacy = wrappedAppEncryptionKey.ApplicationEncrypt(password);
            }
            catch (Exception)
            {
                // Unusable application key: treat as a failed verification rather than an error.
                return false;
            }

            var matches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(legacy),
                Encoding.UTF8.GetBytes(stored));

            needsRehash = matches;

            return matches;
        }

        /// <summary>
        /// True for the Password column of the system Users entity, which must never be decrypted,
        /// returned to application users, or stored as anything but a one-way hash.
        /// </summary>
        public static bool IsUsersPasswordProperty(string? entityName, string? propertyName)
        {
            return string.Equals(entityName, Globals.UsersEntityName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(propertyName, Globals.PasswordColumn, StringComparison.OrdinalIgnoreCase);
        }
    }
}
