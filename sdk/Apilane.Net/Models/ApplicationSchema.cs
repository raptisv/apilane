using System.Collections.Generic;

namespace Apilane.Net.Models
{
    public class ApplicationSchemaDto
    {
        public int AuthTokenExpireMinutes { get; set; }
        public bool AllowLoginUnconfirmedEmail { get; set; }
        public bool ForceSingleLogin { get; set; }
        public bool Online { get; set; }
        public bool AllowUserRegister { get; set; }
        public int MaxAllowedFileSizeInKB { get; set; }
        public List<EntitySchemaDto> Entities { get; set; } = null!;

        public class EntitySchemaDto
        {
            public bool ChangeTracking { get; set; }
            public string Name { get; set; } = null!;
            public string Description { get; set; } = null!;
            public bool IsSystem { get; set; }
            public bool IsReadOnly { get; set; }
            public List<PropertySchemaDto> Properties { get; set; } = null!;

            public class PropertySchemaDto
            {
                public bool IsPrimaryKey { get; set; }
                public bool IsSystem { get; set; }
                public string Name { get; set; } = null!;
                public string Description { get; set; } = null!;
                public string FK_Entity { get; set; } = null!;
                public string FK_Entity_FirstStringProperty { get; set; } = null!;
                public bool Encrypted { get; set; }
                public bool IsUnique { get; set; }
                public long? Minimum { get; set; }
                public long? Maximum { get; set; }
                public int? DecimalPlaces { get; set; }
                public bool Required { get; set; }
                public string Type { get; set; } = null!;
                public string ValidationRegex { get; set; } = null!;
            }
        }
    }
}
