using Apilane.Common.Models;

namespace Apilane.Portal.Models
{
    /// <summary>
    /// The application permissions of one agent collaboration. A separate table lets existing
    /// Portal databases pick up the policy through the model-derived schema updater.
    /// </summary>
    public class PortalAgentPermission
    {
        public long CollaborationId { get; set; }

        public required DBWS_Collaborate Collaboration { get; set; }

        /// <summary>
        /// The complete saved policy. Resources omitted from it are denied.
        /// </summary>
        public string PermissionsJson { get; set; } = "[]";
    }
}
