using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// The values of a new property. Values that do not apply to the type are ignored, not refused.
    /// </summary>
    public class CreatePropertyRequest
    {
        /// <summary>
        /// Letters and underscore only, 4 to 120 characters, not ending in '_Data'. It becomes the
        /// name of the column.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [RegularExpression(PropertyRules.Pattern, ErrorMessage = PropertyRules.PatternMessage)]
        [MinLength(PropertyRules.MinLength, ErrorMessage = PropertyRules.LengthMessage)]
        [MaxLength(PropertyRules.MaxLength, ErrorMessage = PropertyRules.LengthMessage)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// String, Number, Boolean or Date. It cannot be changed later.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Whether a record must have a value for it. It cannot be changed later.
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// String only: whether values are stored encrypted. It cannot be changed later.
        /// </summary>
        public bool Encrypted { get; set; }

        /// <summary>
        /// String only: the regular expression values must match.
        /// </summary>
        public string? ValidationRegex { get; set; }

        /// <summary>
        /// Number only, and required for it: 0 (an integer) to 8. It cannot be changed later.
        /// </summary>
        public int? DecimalPlaces { get; set; }

        /// <summary>
        /// The smallest value of a Number, or the shortest length of a String (not negative).
        /// Between -9007199254740991 and 9007199254740991.
        /// </summary>
        public long? Minimum { get; set; }

        /// <summary>
        /// The largest value of a Number, or the longest length of a String (at least 1). For a
        /// String it is the size of the column and cannot be changed later; without it the column
        /// has no limit. Between -9007199254740991 and 9007199254740991.
        /// </summary>
        public long? Maximum { get; set; }
    }

    /// <summary>
    /// The values of a property that can be changed after it was created. Every value replaces the
    /// stored one: left out or null removes it.
    /// </summary>
    public class UpdatePropertyRequest
    {
        public string? Description { get; set; }

        /// <summary>
        /// String only (AllowValidationRegex); ignored for the other types.
        /// </summary>
        public string? ValidationRegex { get; set; }

        /// <summary>
        /// String and Number only (AllowMin); ignored for the other types. Not negative for a String.
        /// Between -9007199254740991 and 9007199254740991.
        /// </summary>
        public long? Minimum { get; set; }

        /// <summary>
        /// Number only (AllowMaxEdit); ignored for the other types: the maximum of a String is
        /// the size of its column and stays as it is. Between -9007199254740991 and 9007199254740991.
        /// </summary>
        public long? Maximum { get; set; }
    }

    /// <summary>
    /// The new name of a property.
    /// </summary>
    public class RenamePropertyRequest
    {
        /// <summary>
        /// Letters and underscore only, 4 to 120 characters, not ending in '_Data'.
        /// </summary>
        [Required(ErrorMessage = "Required")]
        [RegularExpression(PropertyRules.Pattern, ErrorMessage = PropertyRules.PatternMessage)]
        [MinLength(PropertyRules.MinLength, ErrorMessage = PropertyRules.LengthMessage)]
        [MaxLength(PropertyRules.MaxLength, ErrorMessage = PropertyRules.LengthMessage)]
        public string NewName { get; set; } = string.Empty;
    }

    /// <summary>
    /// The rules of a property. The name rules are those of DBWS_EntityProperty.Name; its pattern is
    /// checked in two steps here, so a name that ends in '_Data' gets a message that says so.
    /// </summary>
    public static class PropertyRules
    {
        public const string Pattern = "^[a-zA-Z_]+$";
        public const string PatternMessage = "Allowed characters are a-z, A-Z and _";
        public const string ForbiddenSuffix = "_Data";
        public const string ForbiddenSuffixMessage = "The name cannot end with '_Data'";
        public const int MinLength = 4;
        public const int MaxLength = 120;
        public const string LengthMessage = "Must be 4 to 120 characters";
        public const int MaxDecimalPlaces = 8;

        /// <summary>
        /// The largest whole number a JSON client in a browser reads and writes exactly (2^53 - 1).
        /// A Minimum or Maximum in a request stays within it, on both sides of zero.
        /// </summary>
        public const long LimitRange = 9007199254740991;
        public const string LimitRangeMessage = "Must be between -9007199254740991 and 9007199254740991";

        public static bool IsOutsideLimitRange(long? value)
        {
            return value.HasValue && (value.Value < -LimitRange || value.Value > LimitRange);
        }
    }
}
