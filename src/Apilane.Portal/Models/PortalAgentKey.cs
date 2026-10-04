namespace Apilane.Portal.Models
{
    /// <summary>
    /// The key of an agent (see Api/PortalAgent.cs): one row per agent, removed with its user.
    /// The secret itself is never stored.
    /// </summary>
    public class PortalAgentKey
    {
        public string UserId { get; set; } = null!;

        /// <summary>
        /// The public part of the key: it finds this row.
        /// </summary>
        public string KeyId { get; set; } = null!;

        /// <summary>
        /// SHA-256 of the secret part of the key, as hex.
        /// </summary>
        public string SecretHash { get; set; } = null!;
    }
}
