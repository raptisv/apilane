using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Agents: accounts for scripts and AI agents that call the management API with a key.
    /// An agent is a normal portal user whose address ends with <see cref="EmailSuffix"/>; that
    /// alone marks it. It has no password, so it cannot sign in. Its key is
    /// 'apl_{KeyId}_{Secret}', sent as 'Authorization: Bearer {key}' and checked in
    /// UsePortalAgentKeys (PortalApiDependencyInjection), which is also the one place that says
    /// what an agent may not call.
    /// </summary>
    public static class PortalAgent
    {
        public const string EmailSuffix = "@agent.local";

        /// <summary>
        /// The authentication type of a request that was let in by an agent key.
        /// </summary>
        public const string AuthenticationType = "AgentKey";

        public const string InvalidKeyMessage = "The agent key is not valid.";
        public const string RefusedMessage = "An agent cannot do this. A person has to do it in the Portal.";

        private const string KeyPrefix = "apl";
        private const int KeyIdLength = 12;

        public static bool IsAgent(string? email)
        {
            return email is not null && email.EndsWith(EmailSuffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A new key, with the two values that are stored for it: the KeyId and the hash of the secret.
        /// </summary>
        public static string NewKey(out string keyId, out string secretHash)
        {
            keyId = RandomNumberGenerator.GetHexString(KeyIdLength, lowercase: true);

            var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

            secretHash = Hash(secret);

            return $"{KeyPrefix}_{keyId}_{secret}";
        }

        /// <summary>
        /// Takes a key apart. False when the value does not have the shape of a key.
        /// </summary>
        public static bool TryReadKey(string key, out string keyId, out string secretHash)
        {
            // The secret is base64url, so it can hold '_' itself: only the first two split.
            var parts = key.Split('_', 3);

            if (parts.Length != 3 || parts[0] != KeyPrefix || parts[1].Length != KeyIdLength || parts[2].Length == 0)
            {
                keyId = string.Empty;
                secretHash = string.Empty;
                return false;
            }

            keyId = parts[1];
            secretHash = Hash(parts[2]);
            return true;
        }

        private static string Hash(string secret)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        }
    }

    /// <summary>
    /// Marks an action or controller an agent may not call. Everything under /api/v1/admin and
    /// DELETE actions without a granular permission classification are also refused.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class NoAgentAttribute : Attribute
    {
    }
}
