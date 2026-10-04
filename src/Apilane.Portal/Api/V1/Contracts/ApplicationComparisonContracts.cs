using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Api.V1.Contracts
{
    /// <summary>
    /// What differs between two applications. The application of the route is the source, the
    /// other one the target: Added is what only the target has, Removed what only the source
    /// has, Changed what both have with different values (Before is the source, After the
    /// target). Names are matched exactly, so 'Orders' and 'orders' are two different things.
    /// Every list is empty when the applications are identical.
    /// </summary>
    public class ApplicationComparisonResponse
    {
        /// <summary>
        /// The name of the application of the route.
        /// </summary>
        [Required]
        public string ApplicationSource { get; set; } = string.Empty;

        /// <summary>
        /// The name of the application it is compared to.
        /// </summary>
        [Required]
        public string ApplicationTarget { get; set; } = string.Empty;

        [Required]
        public ComparisonEntities Entities { get; set; } = new ComparisonEntities();

        [Required]
        public ComparisonCustomEndpoints CustomEndpoints { get; set; } = new ComparisonCustomEndpoints();

        [Required]
        public ComparisonSecurity Security { get; set; } = new ComparisonSecurity();
    }

    /// <summary>
    /// The entities that differ, system entities included.
    /// </summary>
    public class ComparisonEntities
    {
        [Required]
        public List<ComparisonEntity> Added { get; set; } = new List<ComparisonEntity>();

        [Required]
        public List<ComparisonEntity> Removed { get; set; } = new List<ComparisonEntity>();

        /// <summary>
        /// The entities both applications have, when their description, flags, custom properties
        /// or custom constraints differ.
        /// </summary>
        [Required]
        public List<ComparisonChangedEntity> Changed { get; set; } = new List<ComparisonChangedEntity>();
    }

    /// <summary>
    /// An entity only one of the two applications has, with its custom properties and constraints.
    /// </summary>
    public class ComparisonEntity
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public bool RequireChangeTracking { get; set; }

        [Required]
        public bool HasDifferentiationProperty { get; set; }

        /// <summary>
        /// Without the primary key and the system properties.
        /// </summary>
        [Required]
        public List<ComparisonProperty> Properties { get; set; } = new List<ComparisonProperty>();

        /// <summary>
        /// Without the system constraints.
        /// </summary>
        [Required]
        public List<ComparisonConstraint> Constraints { get; set; } = new List<ComparisonConstraint>();
    }

    /// <summary>
    /// A custom property with the values that are compared.
    /// </summary>
    public class ComparisonProperty
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// String, Number, Boolean or Date.
        /// </summary>
        [Required]
        public string TypeLabel { get; set; } = string.Empty;

        /// <summary>
        /// The type as it is stored: 1 String, 2 Number, 3 Boolean, 4 Date.
        /// </summary>
        [Required]
        public int TypeID { get; set; }

        [Required]
        public bool Required { get; set; }

        public long? Minimum { get; set; }

        public long? Maximum { get; set; }

        public int? DecimalPlaces { get; set; }

        [Required]
        public bool Encrypted { get; set; }

        public string? ValidationRegex { get; set; }

        public string? Description { get; set; }
    }

    /// <summary>
    /// A custom constraint, as it is stored.
    /// </summary>
    public class ComparisonConstraint
    {
        /// <summary>
        /// 1 Unique, 2 ForeignKey.
        /// </summary>
        [Required]
        public int TypeID { get; set; }

        /// <summary>
        /// Unique: the property names, separated by commas. ForeignKey: 'Property,Entity' or
        /// 'Property,Entity,ON_DELETE_x'.
        /// </summary>
        public string? Properties { get; set; }
    }

    /// <summary>
    /// An entity both applications have, and what differs in it.
    /// </summary>
    public class ComparisonChangedEntity
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Of Description, RequireChangeTracking and HasDifferentiationProperty, the ones that differ.
        /// </summary>
        [Required]
        public List<ComparisonFieldChange> MetadataChanges { get; set; } = new List<ComparisonFieldChange>();

        [Required]
        public List<ComparisonProperty> PropertiesAdded { get; set; } = new List<ComparisonProperty>();

        /// <summary>
        /// The custom properties both entities have (matched whatever their letter case) with
        /// different values.
        /// </summary>
        [Required]
        public List<ComparisonChangedProperty> PropertiesChanged { get; set; } = new List<ComparisonChangedProperty>();

        [Required]
        public List<ComparisonRemovedProperty> PropertiesRemoved { get; set; } = new List<ComparisonRemovedProperty>();

        /// <summary>
        /// Constraints are matched by TypeID and Properties, whatever the letter case.
        /// </summary>
        [Required]
        public List<ComparisonConstraint> ConstraintsAdded { get; set; } = new List<ComparisonConstraint>();

        [Required]
        public List<ComparisonConstraint> ConstraintsRemoved { get; set; } = new List<ComparisonConstraint>();
    }

    /// <summary>
    /// A property both entities have, and the values that differ.
    /// </summary>
    public class ComparisonChangedProperty
    {
        /// <summary>
        /// The name in the source application.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Of Type, Required, Minimum, Maximum, DecimalPlaces, Encrypted, ValidationRegex and
        /// Description, the ones that differ.
        /// </summary>
        [Required]
        public List<ComparisonFieldChange> Changes { get; set; } = new List<ComparisonFieldChange>();
    }

    /// <summary>
    /// A property only the source entity has.
    /// </summary>
    public class ComparisonRemovedProperty
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// String, Number, Boolean or Date.
        /// </summary>
        [Required]
        public string TypeLabel { get; set; } = string.Empty;
    }

    /// <summary>
    /// One value that differs, as text. Null means the value is not set; booleans read 'True' and 'False'.
    /// </summary>
    public class ComparisonFieldChange
    {
        [Required]
        public string Field { get; set; } = string.Empty;

        /// <summary>
        /// The value in the source application.
        /// </summary>
        public string? Before { get; set; }

        /// <summary>
        /// The value in the target application.
        /// </summary>
        public string? After { get; set; }
    }

    /// <summary>
    /// The custom endpoints that differ.
    /// </summary>
    public class ComparisonCustomEndpoints
    {
        [Required]
        public List<ComparisonCustomEndpoint> Added { get; set; } = new List<ComparisonCustomEndpoint>();

        [Required]
        public List<ComparisonCustomEndpoint> Removed { get; set; } = new List<ComparisonCustomEndpoint>();

        /// <summary>
        /// The custom endpoints both applications have with a different query or description.
        /// </summary>
        [Required]
        public List<ComparisonChangedCustomEndpoint> Changed { get; set; } = new List<ComparisonChangedCustomEndpoint>();
    }

    /// <summary>
    /// A custom endpoint only one of the two applications has.
    /// </summary>
    public class ComparisonCustomEndpoint
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        public string Query { get; set; } = string.Empty;
    }

    /// <summary>
    /// A custom endpoint both applications have. Both pairs are always filled: compare them to
    /// see which one differs.
    /// </summary>
    public class ComparisonChangedCustomEndpoint
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public string? DescriptionBefore { get; set; }

        public string? DescriptionAfter { get; set; }

        [Required]
        public string QueryBefore { get; set; } = string.Empty;

        [Required]
        public string QueryAfter { get; set; } = string.Empty;
    }

    /// <summary>
    /// The security rules that differ. Only rules of an entity or a custom endpoint the
    /// application still has are compared; Schema rules are not compared at all.
    /// </summary>
    public class ComparisonSecurity
    {
        [Required]
        public List<ComparisonSecurityRule> Added { get; set; } = new List<ComparisonSecurityRule>();

        [Required]
        public List<ComparisonSecurityRule> Removed { get; set; } = new List<ComparisonSecurityRule>();

        /// <summary>
        /// The rules both applications have (same type, name, role and action) with a different
        /// record scope, property list or rate limit.
        /// </summary>
        [Required]
        public List<ComparisonChangedSecurityRule> Changed { get; set; } = new List<ComparisonChangedSecurityRule>();
    }

    /// <summary>
    /// A security rule, as text to show.
    /// </summary>
    public class ComparisonSecurityRule
    {
        /// <summary>
        /// Type, name, role and action in one text: 'Entity Orders - ANONYMOUS get'.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// ANONYMOUS, AUTHENTICATED or the name of a role.
        /// </summary>
        [Required]
        public string Role { get; set; } = string.Empty;

        /// <summary>
        /// Entity or CustomEndpoint.
        /// </summary>
        [Required]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// A text such as '10 request per minute'; null when the rule has no rate limit.
        /// </summary>
        public string? RateLimit { get; set; }

        [Required]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// All or Owned.
        /// </summary>
        [Required]
        public string Record { get; set; } = string.Empty;

        /// <summary>
        /// The property names of the rule, sorted and separated by commas. Empty when it has none.
        /// </summary>
        [Required]
        public string Properties { get; set; } = string.Empty;
    }

    /// <summary>
    /// A security rule both applications have, in the source and in the target.
    /// </summary>
    public class ComparisonChangedSecurityRule
    {
        /// <summary>
        /// Type, name, role and action in one text.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public ComparisonSecurityRule SecurityBefore { get; set; } = new ComparisonSecurityRule();

        [Required]
        public ComparisonSecurityRule SecurityAfter { get; set; } = new ComparisonSecurityRule();
    }
}
