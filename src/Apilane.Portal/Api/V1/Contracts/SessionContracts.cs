using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// The signed-in user and the instance they are signed in to.
    /// </summary>
    public class SessionResponse
    {
        [Required]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Whether the user holds the portal Admin role (instance settings, servers, users).
        /// </summary>
        [Required]
        public bool IsAdmin { get; set; }

        /// <summary>
        /// The name of this Apilane instance, as set under Admin settings.
        /// </summary>
        [Required]
        public string InstanceTitle { get; set; } = string.Empty;

        /// <summary>
        /// The Portal version. A client that sees it change should reload.
        /// </summary>
        [Required]
        public string Version { get; set; } = string.Empty;
    }

    /// <summary>
    /// The token the signed-in user calls the API servers with (records, files, statistics).
    /// </summary>
    public class ApiTokenResponse
    {
        /// <summary>
        /// Sent to an API server as 'Authorization: Bearer', together with x-application-token.
        /// A secret that changes at every sign-in: keep it in memory only.
        /// </summary>
        [Required]
        public string Token { get; set; } = string.Empty;
    }
}
