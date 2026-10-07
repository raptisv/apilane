using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// Entities, properties, constraints, security rules and custom endpoints to add to an
    /// application. It is the payload format of the older Import schema page, so a payload written
    /// for that page works here: types are the numbers that are stored (TypeID), not their names.
    /// GET schema-import/diff answers with this same shape, ready to be posted back. Every list
    /// can be left out or null, which means none.
    /// </summary>
    public class SchemaImportRequest
    {
        /// <summary>
        /// Processed first. Entities without a foreign key go first, then the ones whose foreign
        /// keys lead to Users, the entity that is pointed to before the one that points to it;
        /// the others follow in the order they are listed here, so list an entity before the
        /// entities whose foreign keys point to it.
        /// </summary>
        public List<SchemaImportEntity>? Entities { get; set; }

        /// <summary>
        /// Processed after the entities.
        /// </summary>
        public List<SchemaImportSecurityRule>? Security { get; set; }

        /// <summary>
        /// Processed last.
        /// </summary>
        public List<SchemaImportCustomEndpoint>? CustomEndpoints { get; set; }
    }

    /// <summary>
    /// An entity to create, or an existing entity (same name, whatever its letter case) to add
    /// properties and constraints to.
    /// </summary>
    public class SchemaImportEntity
    {
        /// <summary>
        /// For a new entity: letters and underscore only, 4 to 30 characters.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Used for a new entity only; an existing entity keeps its description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Must equal the stored value when the entity exists.
        /// </summary>
        public bool RequireChangeTracking { get; set; }

        /// <summary>
        /// Must equal the stored value when the entity exists.
        /// </summary>
        public bool HasDifferentiationProperty { get; set; }

        /// <summary>
        /// Set by the diff: true when the application does not have the entity at all, false when
        /// only some of its properties or constraints are missing. The import ignores it.
        /// </summary>
        public bool IsNew { get; set; }

        /// <summary>
        /// The custom properties: system properties (ID, Owner, Created, ...) come with a new entity.
        /// </summary>
        public List<SchemaImportProperty>? Properties { get; set; }

        public List<SchemaImportConstraint>? Constraints { get; set; }
    }

    /// <summary>
    /// A property to create. When the entity has a property with this name (whatever its letter
    /// case), TypeID, Required, Encrypted, DecimalPlaces, Minimum, Maximum and ValidationRegex
    /// must equal the stored values; the description is not compared.
    /// </summary>
    public class SchemaImportProperty
    {
        /// <summary>
        /// For a new property: letters and underscore only, 4 to 120 characters, not ending in '_Data'.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 1 String, 2 Number, 3 Boolean, 4 Date.
        /// </summary>
        public int TypeID { get; set; }

        public bool Required { get; set; }

        /// <summary>
        /// The smallest value of a Number, or the shortest length of a String.
        /// </summary>
        public long? Minimum { get; set; }

        /// <summary>
        /// The largest value of a Number, or the longest length of a String.
        /// </summary>
        public long? Maximum { get; set; }

        /// <summary>
        /// Number only: 0 for an integer.
        /// </summary>
        public int? DecimalPlaces { get; set; }

        /// <summary>
        /// String only: whether values are stored encrypted.
        /// </summary>
        public bool Encrypted { get; set; }

        /// <summary>
        /// String only: the regular expression values must match.
        /// </summary>
        public string? ValidationRegex { get; set; }

        public string? Description { get; set; }
    }

    /// <summary>
    /// A constraint to add to the entity. References are validated against the complete imported
    /// schema and normalized to its names before any changes. An equivalent existing constraint
    /// is skipped with a warning, ignoring name casing, whitespace and unique-property order.
    /// </summary>
    public class SchemaImportConstraint
    {
        /// <summary>
        /// Must be false or left out: system constraints come with a new entity and cannot be
        /// imported. The diff always returns false.
        /// </summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// 1 Unique, 2 ForeignKey.
        /// </summary>
        [Range(1, 2, ErrorMessage = "Must be 1 (Unique) or 2 (ForeignKey)")]
        public int TypeID { get; set; }

        /// <summary>
        /// Unique: the property names, separated by commas ('Code' or 'Code,Owner'). ForeignKey:
        /// 'Property,Entity' or 'Property,Entity,ON_DELETE_NO_ACTION' (or ON_DELETE_SET_NULL,
        /// ON_DELETE_CASCADE, also numeric 0, 1, 2). Every name must be a valid identifier of an
        /// existing or imported item. Unique properties must be unencrypted and listed once.
        /// A foreign key needs a custom Number property with 0 decimal places and cannot point
        /// to Files. A unique constraint without properties is ignored.
        /// </summary>
        public string? Properties { get; set; }
    }

    /// <summary>
    /// A security rule to add, as it is stored. A rule is identified by TypeID, Name, RoleID and
    /// Action, whatever their letter case: when the application has it, the rest must be equal too.
    /// </summary>
    public class SchemaImportSecurityRule
    {
        /// <summary>
        /// The name of the entity or the custom endpoint, or 'Schema'.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 0 Entity, 1 CustomEndpoint, 2 Schema.
        /// </summary>
        public int TypeID { get; set; }

        /// <summary>
        /// ANONYMOUS, AUTHENTICATED or the name of a role.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string RoleID { get; set; } = string.Empty;

        /// <summary>
        /// get, post, put or delete.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// 0 All records, 1 Owned: only the records the user owns.
        /// </summary>
        public int Record { get; set; }

        /// <summary>
        /// The property names the role may read or write, separated by commas.
        /// </summary>
        public string? Properties { get; set; }

        /// <summary>
        /// Null or left out for no rate limit.
        /// </summary>
        public SchemaImportRateLimit? RateLimit { get; set; }
    }

    /// <summary>
    /// At most MaxRequests calls per time window.
    /// </summary>
    public class SchemaImportRateLimit
    {
        [Range(1, int.MaxValue, ErrorMessage = "Must be 1 or more")]
        public int MaxRequests { get; set; }

        /// <summary>
        /// 0 None (no limit), 1 Per_Second, 2 Per_Minute, 3 Per_Hour.
        /// </summary>
        [Range(0, 3, ErrorMessage = "Must be 0 (None), 1 (Per_Second), 2 (Per_Minute) or 3 (Per_Hour)")]
        public int TimeWindowType { get; set; }
    }

    /// <summary>
    /// A custom endpoint to create. One whose name the application already has (whatever its
    /// letter case) is skipped with a warning; its query and description are not compared.
    /// </summary>
    public class SchemaImportCustomEndpoint
    {
        /// <summary>
        /// Letters only (a-z, A-Z), at most 80. Spaces before and after are removed.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// The SQL the API server runs, stored as sent.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Query { get; set; } = string.Empty;
    }

    /// <summary>
    /// The outcome of an import that went through.
    /// </summary>
    public class SchemaImportResponse
    {
        /// <summary>
        /// One text per item of the payload that the application already had and that was
        /// therefore skipped, in the order they were processed. Empty when everything was new.
        /// </summary>
        [Required]
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
