using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// One entry of the audit log: who changed what, and when.
    /// </summary>
    public class AuditLogEntryResponse
    {
        [Required]
        public long ID { get; set; }

        /// <summary>
        /// When the change was made, in UTC.
        /// </summary>
        [Required]
        public DateTime Timestamp { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string UserEmail { get; set; } = string.Empty;

        /// <summary>
        /// The kind of thing that changed, e.g. Server, Global Settings, User Role, Application, Entity.
        /// </summary>
        [Required]
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// The name of the thing that changed, e.g. a server name or a user's e-mail.
        /// </summary>
        [Required]
        public string EntityIdentifier { get; set; } = string.Empty;

        /// <summary>
        /// Created, Modified or Deleted for a change; Downloaded for a database backup download.
        /// </summary>
        [Required]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// The changed properties. Empty when the entry has no detail.
        /// </summary>
        [Required]
        public List<AuditChangeResponse> Changes { get; set; } = new List<AuditChangeResponse>();
    }

    /// <summary>
    /// The old and the new value of one property, exactly as stored: plain text, or JSON text for
    /// Security, EntConstraints and EntDefaultOrder. Secret values are stored as "***".
    /// </summary>
    public class AuditChangeResponse
    {
        [Required]
        public string Property { get; set; } = string.Empty;

        /// <summary>
        /// Null when the item was created, or when the value was null.
        /// </summary>
        public string? OldValue { get; set; }

        /// <summary>
        /// Null when the item was deleted, or when the value is null.
        /// </summary>
        public string? NewValue { get; set; }
    }
}
