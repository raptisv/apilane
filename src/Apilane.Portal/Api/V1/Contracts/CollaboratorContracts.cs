using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// A user an application is shared with. Human collaborators have full application access,
    /// except sharing; agents have the permissions granted by the owner.
    /// </summary>
    public class CollaboratorResponse
    {
        [Required]
        public long ID { get; set; }

        /// <summary>
        /// The e-mail address the application is shared with, as it was entered.
        /// </summary>
        [Required]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The resolved access for an agent, including denied resources; null for a person.
        /// </summary>
        public List<AgentPermissionGrant>? Permissions { get; set; }
    }

    /// <summary>
    /// A new collaborator, and whether the notification mail was handed over for sending.
    /// </summary>
    public class CollaboratorAddedResponse
    {
        [Required]
        public long ID { get; set; }

        /// <summary>
        /// The e-mail address the application is shared with, trimmed.
        /// </summary>
        [Required]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The resolved access for an agent, including denied resources; null for a person.
        /// </summary>
        public List<AgentPermissionGrant>? Permissions { get; set; }

        /// <summary>
        /// False when the instance has no mail settings, the mail could not be handed over for
        /// sending, or the address is an agent's. The application is shared either way.
        /// </summary>
        [Required]
        public bool NotificationSent { get; set; }
    }

    /// <summary>
    /// An agent an application can be shared with.
    /// </summary>
    public class AvailableAgentResponse
    {
        /// <summary>
        /// The agent's name: its address without @agent.local.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The agent's address: the value to share the application with.
        /// </summary>
        [Required]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Other applications the agent can access that are also in the caller's normal
        /// Applications list. Excludes the current application and applications hidden from the caller.
        /// </summary>
        [Required]
        public List<AgentApplicationSummaryResponse> Applications { get; set; } = new List<AgentApplicationSummaryResponse>();
    }

    /// <summary>
    /// An application visible to both the caller and an available agent. No secrets or permissions.
    /// </summary>
    public class AgentApplicationSummaryResponse
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Shares an application with another user.
    /// </summary>
    public class AddCollaboratorRequest
    {
        /// <summary>
        /// The e-mail address of a user of this instance. Spaces around it are removed. The share
        /// takes effect only for an account with exactly this address.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The complete access policy for an agent. Omit it to grant read-only access; an empty
        /// list denies all resources. Only agents accept this field. The owner can edit it later.
        /// </summary>
        public List<AgentPermissionGrant>? Permissions { get; set; }
    }
}
