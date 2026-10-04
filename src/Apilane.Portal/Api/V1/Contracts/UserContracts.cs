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

    /// <summary>
    /// A new agent: a portal user for a script or an AI agent, which calls this API with a key.
    /// </summary>
    public class CreateAgentRequest
    {
        /// <summary>
        /// Lower-case letters, digits and dashes, 3 to 40 characters. The agent's address is {Name}@agent.local.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [RegularExpression("^[a-z0-9-]{3,40}$", ErrorMessage = "Must be 3 to 40 characters: lower-case letters, digits and dashes")]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// The agent that was created, with its key.
    /// </summary>
    public class AgentCreatedResponse
    {
        /// <summary>
        /// The ID of the agent's user, as in the list of users.
        /// </summary>
        [Required]
        public string ID { get; set; } = string.Empty;

        /// <summary>
        /// The agent's address: share an application with it to give the agent access.
        /// </summary>
        [Required]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The key the agent sends as 'Authorization: Bearer {Key}'. This answer is the only time it
        /// is given: the Portal keeps a hash of it and cannot show it again.
        /// </summary>
        [Required]
        public string Key { get; set; } = string.Empty;
    }
}
