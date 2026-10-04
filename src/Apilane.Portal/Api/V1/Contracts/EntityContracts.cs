using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// An entity (table) of an application. In this API it is identified by its name, which is
    /// case-sensitive.
    /// </summary>
    public class EntityResponse
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// True for the entities every application has (Users, Files and the differentiation
        /// entity). They cannot be renamed or deleted.
        /// </summary>
        [Required]
        public bool IsSystem { get; set; }

        /// <summary>
        /// True when its records cannot be created, changed or deleted through the data API.
        /// </summary>
        [Required]
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// Whether every change of a record is kept as a history record.
        /// </summary>
        [Required]
        public bool RequireChangeTracking { get; set; }

        /// <summary>
        /// Whether its records belong to one record of the application's differentiation entity.
        /// </summary>
        [Required]
        public bool HasDifferentiationProperty { get; set; }

        /// <summary>
        /// Whether records can be created through the data API (not for Users: they register).
        /// </summary>
        [Required]
        public bool AllowPost { get; set; }

        /// <summary>
        /// Whether records can be changed. Change tracking can be turned on only when this is true.
        /// </summary>
        [Required]
        public bool AllowPut { get; set; }

        /// <summary>
        /// Whether records can be deleted.
        /// </summary>
        [Required]
        public bool AllowDelete { get; set; }

        /// <summary>
        /// Whether properties can be added to the entity.
        /// </summary>
        [Required]
        public bool AllowAddProperties { get; set; }

        /// <summary>
        /// The unique and foreign key constraints of the entity. A stored constraint that cannot
        /// be read is left out.
        /// </summary>
        [Required]
        public List<ConstraintResponse> Constraints { get; set; } = new List<ConstraintResponse>();

        /// <summary>
        /// The properties: the primary key first, then custom ones, then system ones, each group
        /// by name. Null in the entities list unless it is asked with IncludeProperties=true.
        /// </summary>
        public List<PropertyResponse>? Properties { get; set; }
    }

    /// <summary>
    /// A constraint of an entity. Type says which of the other values are set.
    /// </summary>
    public class ConstraintResponse
    {
        /// <summary>
        /// Unique or ForeignKey.
        /// </summary>
        [Required]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// True for a constraint the entity was created with. It cannot be changed or removed.
        /// </summary>
        [Required]
        public bool IsSystem { get; set; }

        /// <summary>
        /// Unique: the properties whose combined values must be unique. Empty for a foreign key.
        /// </summary>
        [Required]
        public List<string> Properties { get; set; } = new List<string>();

        /// <summary>
        /// ForeignKey: the property that holds the ID of a record of ForeignEntity.
        /// </summary>
        public string? Property { get; set; }

        /// <summary>
        /// ForeignKey: the entity the property points to.
        /// </summary>
        public string? ForeignEntity { get; set; }

        /// <summary>
        /// ForeignKey: what happens to a record when the record it points to is deleted:
        /// ON_DELETE_NO_ACTION, ON_DELETE_SET_NULL or ON_DELETE_CASCADE.
        /// </summary>
        public string? OnDelete { get; set; }
    }

    /// <summary>
    /// A property (column) of an entity. In this API it is identified by its name, which is
    /// case-sensitive.
    /// </summary>
    public class PropertyResponse
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// String, Number, Boolean or Date.
        /// </summary>
        [Required]
        public string Type { get; set; } = string.Empty;

        [Required]
        public bool IsPrimaryKey { get; set; }

        /// <summary>
        /// True for the properties the entity was created with. They cannot be changed, renamed or deleted.
        /// </summary>
        [Required]
        public bool IsSystem { get; set; }

        /// <summary>
        /// Position of the property in creation order (0 = first). The data browser shows its
        /// columns in this order.
        /// </summary>
        [Required]
        public int Position { get; set; }

        /// <summary>
        /// Whether a record must have a value for it.
        /// </summary>
        [Required]
        public bool Required { get; set; }

        /// <summary>
        /// String only: whether values are stored encrypted.
        /// </summary>
        [Required]
        public bool Encrypted { get; set; }

        /// <summary>
        /// String only: the regular expression values must match.
        /// </summary>
        public string? ValidationRegex { get; set; }

        /// <summary>
        /// Number only: 0 for an integer.
        /// </summary>
        public int? DecimalPlaces { get; set; }

        /// <summary>
        /// The smallest value of a Number, or the shortest length of a String.
        /// </summary>
        public long? Minimum { get; set; }

        /// <summary>
        /// The largest value of a Number, or the longest length of a String.
        /// </summary>
        public long? Maximum { get; set; }

        /// <summary>
        /// Whether the value of a record can be set through the data API. False for the primary
        /// key, Owner, Created, EmailConfirmed, LastLogin and the differentiation property.
        /// </summary>
        [Required]
        public bool AllowEdit { get; set; }

        /// <summary>
        /// True for the system dates Created and LastLogin, which are stored in UTC.
        /// </summary>
        [Required]
        public bool IsUtc { get; set; }

        /// <summary>
        /// Whether Minimum applies to this property (String and Number, not the primary key).
        /// </summary>
        [Required]
        public bool AllowMin { get; set; }

        /// <summary>
        /// Whether Maximum can be changed after the property was created (Number only: the
        /// maximum of a String is the size of its column).
        /// </summary>
        [Required]
        public bool AllowMaxEdit { get; set; }

        /// <summary>
        /// Whether ValidationRegex applies to this property (String only).
        /// </summary>
        [Required]
        public bool AllowValidationRegex { get; set; }
    }

    /// <summary>
    /// The values of a new entity.
    /// </summary>
    public class CreateEntityRequest
    {
        /// <summary>
        /// Letters and underscore only, 4 to 30 characters. It becomes the name of the table.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [RegularExpression(EntityNameRules.Pattern, ErrorMessage = EntityNameRules.PatternMessage)]
        [MinLength(EntityNameRules.MinLength, ErrorMessage = EntityNameRules.LengthMessage)]
        [MaxLength(EntityNameRules.MaxLength, ErrorMessage = EntityNameRules.LengthMessage)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// Whether every change of a record is kept as a history record. It costs performance:
        /// meant for entities that are not updated often.
        /// </summary>
        public bool RequireChangeTracking { get; set; }

        /// <summary>
        /// Whether its records belong to one record of the application's differentiation entity.
        /// Only for an application that has a differentiation entity. It cannot be changed later.
        /// </summary>
        public bool HasDifferentiationProperty { get; set; }
    }

    /// <summary>
    /// The values of an entity that can be changed after it was created.
    /// </summary>
    public class UpdateEntityRequest
    {
        /// <summary>
        /// Empty or left out removes the description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Whether every change of a record is kept as a history record. Can be true only for an
        /// entity with AllowPut.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public bool? RequireChangeTracking { get; set; }
    }

    /// <summary>
    /// The new name of an entity.
    /// </summary>
    public class RenameEntityRequest
    {
        /// <summary>
        /// Letters and underscore only, 4 to 30 characters.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [RegularExpression(EntityNameRules.Pattern, ErrorMessage = EntityNameRules.PatternMessage)]
        [MinLength(EntityNameRules.MinLength, ErrorMessage = EntityNameRules.LengthMessage)]
        [MaxLength(EntityNameRules.MaxLength, ErrorMessage = EntityNameRules.LengthMessage)]
        public string NewName { get; set; } = string.Empty;
    }

    /// <summary>
    /// The rules of an entity name, the same as on DBWS_Entity.Name.
    /// </summary>
    public static class EntityNameRules
    {
        public const string Pattern = "^[a-zA-Z_]+$";
        public const string PatternMessage = "Allowed characters are a-z, A-Z and _";
        public const int MinLength = 4;
        public const int MaxLength = 30;
        public const string LengthMessage = "Must be 4 to 30 characters";
    }
}
