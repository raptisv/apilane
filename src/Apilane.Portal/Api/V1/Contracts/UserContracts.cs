using System;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// A portal user: someone who can sign in to this Apilane instance.
    /// </summary>
    public class UserResponse
    {
        [Required]
        public string ID { get; set; } = string.Empty;

        [Required]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Whether the user holds the portal Admin role.
        /// </summary>
        [Required]
        public bool IsAdmin { get; set; }

        /// <summary>
        /// Whether this is the user making the call. Their own role cannot be changed.
        /// </summary>
        [Required]
        public bool IsCurrentUser { get; set; }

        /// <summary>
        /// When the user last signed in, in UTC.
        /// </summary>
        [Required]
        public DateTime LastLogin { get; set; }
    }

    /// <summary>
    /// The role a portal user should have.
    /// </summary>
    public class UserRoleRequest
    {
        /// <summary>
        /// True makes the user an administrator, false an ordinary user.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public bool? IsAdmin { get; set; }
    }
}
