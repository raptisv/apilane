using System;

namespace Apilane.Portal.Models
{
    public class PortalAuditLog
    {
        public long ID { get; set; }
        public DateTime Timestamp { get; set; }
        public string UserId { get; set; } = null!;
        public string UserEmail { get; set; } = null!;

        /// <summary>
        /// The application ID this change relates to.
        /// Null for admin-level changes (servers, global settings, user roles).
        /// </summary>
        public long? AppID { get; set; }

        /// <summary>
        /// Human-readable entity type name (e.g. "Application", "Entity", "Property").
        /// </summary>
        public string EntityType { get; set; } = null!;

        /// <summary>
        /// A meaningful identifier for the changed entity (e.g. name, token, email).
        /// </summary>
        public string EntityIdentifier { get; set; } = null!;

        /// <summary>
        /// The action performed: "Created", "Modified", or "Deleted".
        /// </summary>
        public string Action { get; set; } = null!;

        /// <summary>
        /// JSON array of property-level changes.
        /// Each element has Property, OldValue, and NewValue fields.
        /// </summary>
        public string? Changes { get; set; }
    }
}
