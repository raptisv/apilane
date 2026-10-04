using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// The constraints of an entity and what a new constraint can be built from.
    /// </summary>
    public class EntityConstraintsResponse
    {
        /// <summary>
        /// The unique and foreign key constraints of the entity, system ones included. A stored
        /// constraint that cannot be read is left out.
        /// </summary>
        [Required]
        public List<ConstraintResponse> Constraints { get; set; } = new List<ConstraintResponse>();

        [Required]
        public ConstraintCandidatesResponse Candidates { get; set; } = new ConstraintCandidatesResponse();
    }

    /// <summary>
    /// The names a constraint of the entity may use. Anything else is refused with 400.
    /// </summary>
    public class ConstraintCandidatesResponse
    {
        /// <summary>
        /// The properties a unique constraint can be made of: every property that is not encrypted.
        /// </summary>
        [Required]
        public List<string> UniqueProperties { get; set; } = new List<string>();

        /// <summary>
        /// The properties that can be a foreign key: custom Number properties with 0 decimal places.
        /// </summary>
        [Required]
        public List<string> ForeignKeyProperties { get; set; } = new List<string>();

        /// <summary>
        /// The entities a foreign key can point to: every entity of the application except Files,
        /// the entity itself included.
        /// </summary>
        [Required]
        public List<string> ForeignEntities { get; set; } = new List<string>();

        /// <summary>
        /// The values of OnDelete: ON_DELETE_NO_ACTION, ON_DELETE_SET_NULL and ON_DELETE_CASCADE.
        /// </summary>
        [Required]
        public List<string> OnDeleteActions { get; set; } = new List<string>();
    }

    /// <summary>
    /// The complete list of the custom constraints of an entity.
    /// </summary>
    public class ReplaceConstraintsRequest
    {
        /// <summary>
        /// Every custom constraint the entity should have after the call; an empty list removes
        /// them all. System constraints are kept by the server and must be left out.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public List<ConstraintRequest>? Constraints { get; set; }
    }

    /// <summary>
    /// One custom constraint. Type says which of the other values are used.
    /// </summary>
    public class ConstraintRequest
    {
        /// <summary>
        /// Unique or ForeignKey.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Must be false or left out: a system constraint cannot be sent.
        /// </summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// Unique: one or more of Candidates.UniqueProperties, each once. Their order is kept.
        /// </summary>
        public List<string>? Properties { get; set; }

        /// <summary>
        /// ForeignKey: one of Candidates.ForeignKeyProperties.
        /// </summary>
        public string? Property { get; set; }

        /// <summary>
        /// ForeignKey: one of Candidates.ForeignEntities.
        /// </summary>
        public string? ForeignEntity { get; set; }

        /// <summary>
        /// ForeignKey: one of Candidates.OnDeleteActions. A stored foreign key keeps its action: to
        /// change it, remove the foreign key in one call and add it again in the next.
        /// </summary>
        public string? OnDelete { get; set; }
    }

    /// <summary>
    /// The default sorting of an entity: the order its records are returned in when a request
    /// asks for none.
    /// </summary>
    public class DefaultOrderResponse
    {
        /// <summary>
        /// The properties to sort by, the first one first. Empty when the entity has no default sorting.
        /// </summary>
        [Required]
        public List<DefaultOrderItem> Items { get; set; } = new List<DefaultOrderItem>();

        /// <summary>
        /// The properties that can be sorted by: every property of the entity.
        /// </summary>
        [Required]
        public List<string> Candidates { get; set; } = new List<string>();
    }

    /// <summary>
    /// The complete default sorting of an entity.
    /// </summary>
    public class ReplaceDefaultOrderRequest
    {
        /// <summary>
        /// The properties to sort by, the first one first, each once. An empty list removes the
        /// default sorting.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public List<DefaultOrderItem>? Items { get; set; }
    }

    /// <summary>
    /// One property of a default sorting.
    /// </summary>
    public class DefaultOrderItem
    {
        /// <summary>
        /// The name of a property of the entity (case-sensitive).
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Property { get; set; } = string.Empty;

        /// <summary>
        /// asc or desc.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Direction { get; set; } = string.Empty;
    }
}
