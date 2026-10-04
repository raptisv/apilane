using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// What the sign-in pages need to know about this instance before anyone is signed in.
    /// </summary>
    public class InstanceResponse
    {
        /// <summary>
        /// The name of this Apilane instance, as set under Admin settings.
        /// </summary>
        [Required]
        public string InstanceTitle { get; set; } = string.Empty;

        /// <summary>
        /// Whether new users may register. When false, POST /api/v1/account answers 403.
        /// </summary>
        [Required]
        public bool AllowRegister { get; set; }

        /// <summary>
        /// Whether the instance can send mail. When false, a password reset cannot be requested.
        /// </summary>
        [Required]
        public bool IsMailSetup { get; set; }
    }
}
