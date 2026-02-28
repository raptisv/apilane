using Apilane.Common.Models;
using System.Collections.Generic;

namespace Apilane.Portal.Models
{
    public class ImportSchemaRequest
    {
        public List<ImportEntityItem> Entities { get; set; } = new();
        public List<DBWS_Security> Security { get; set; } = new();
        public List<ImportCustomEndpointItem> CustomEndpoints { get; set; } = new();
    }

    public class ImportEntityItem
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public bool RequireChangeTracking { get; set; }
        public bool HasDifferentiationProperty { get; set; }
        /// <summary>True when the entity does not exist in the target application at all.</summary>
        public bool IsNew { get; set; }
        public List<ImportPropertyItem> Properties { get; set; } = new();
        public List<EntityConstraint> Constraints { get; set; } = new();
    }

    public class ImportPropertyItem
    {
        public string Name { get; set; } = null!;
        public int TypeID { get; set; }
        public bool Required { get; set; }
        public long? Minimum { get; set; }
        public long? Maximum { get; set; }
        public int? DecimalPlaces { get; set; }
        public bool Encrypted { get; set; }
        public string? ValidationRegex { get; set; }
        public string? Description { get; set; }
    }

    public class ImportCustomEndpointItem
    {
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string Query { get; set; } = null!;
    }
}
