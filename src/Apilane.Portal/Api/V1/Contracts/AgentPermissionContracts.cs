using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// An operation required by an application endpoint. Serialized as Read, Write or Delete.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AgentPermissionAccess>))]
    public enum AgentPermissionAccess
    {
        Read,
        Write,
        Delete
    }

    /// <summary>
    /// Stable names for the parts of an application that an owner can grant to an agent.
    /// </summary>
    public static class AgentPermissionResources
    {
        public const string Application = "application";
        public const string Entities = "entities";
        public const string Security = "security";
        public const string CustomEndpoints = "custom-endpoints";
        public const string Reports = "reports";
        public const string EmailSettings = "email-settings";
        public const string AuditLog = "audit-log";
        public const string Schema = "schema";
        public const string Rebuild = "rebuild";
    }

    /// <summary>
    /// An agent's access to one application resource. Delete is an independent grant. Write and
    /// delete require read for resources that support reading. Omitted resources are denied.
    /// </summary>
    public class AgentPermissionGrant
    {
        [Required]
        public string Resource { get; set; } = string.Empty;

        [Required]
        public bool Read { get; set; }

        [Required]
        public bool Write { get; set; }

        [Required]
        public bool Delete { get; set; }
    }

    /// <summary>
    /// A resource and the operations that the Portal supports granting for it.
    /// </summary>
    public class AgentPermissionResource
    {
        [Required]
        public string Resource { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        [Required]
        public bool CanRead { get; set; }

        [Required]
        public bool CanWrite { get; set; }

        [Required]
        public bool CanDelete { get; set; }
    }

    /// <summary>
    /// The caller's current permissions and the resource catalogue. This discovery remains
    /// available to a collaborator even if every application permission has been removed.
    /// </summary>
    public class ApplicationPermissionsResponse
    {
        [Required]
        public bool IsAgent { get; set; }

        [Required]
        public List<AgentPermissionGrant> Permissions { get; set; } = new();

        [Required]
        public List<AgentPermissionResource> Resources { get; set; } = new();

        /// <summary>
        /// Application operations and whether the caller's current grants satisfy their fixed
        /// permission requirements. AdditionalRequirements describes checks dependent on another
        /// application, the request body or the selected entity.
        /// </summary>
        [Required]
        public List<AgentPermissionOperation> Operations { get; set; } = new();

        /// <summary>
        /// Restrictions that no application permission can override. Empty for a human caller.
        /// </summary>
        [Required]
        public List<string> Restrictions { get; set; } = new();
    }

    public class AgentPermissionOperation
    {
        [Required]
        public string Method { get; set; } = string.Empty;

        [Required]
        public string Path { get; set; } = string.Empty;

        [Required]
        public bool AllowedForThisApplication { get; set; }

        [Required]
        public List<AgentPermissionRequirement> Requirements { get; set; } = new();

        public string? AdditionalRequirements { get; set; }
    }

    public class AgentPermissionRequirement
    {
        [Required]
        public string Resource { get; set; } = string.Empty;

        /// <summary>
        /// The required operation: Read, Write or Delete.
        /// </summary>
        [Required]
        public AgentPermissionAccess Access { get; set; }
    }

    /// <summary>
    /// Replaces an agent collaborator's permissions. An empty list denies all application
    /// resources. Resources omitted from this complete policy are denied.
    /// </summary>
    public class UpdateAgentPermissionsRequest
    {
        [Required(ErrorMessage = "Required")]
        public required List<AgentPermissionGrant> Permissions { get; set; }
    }
}
