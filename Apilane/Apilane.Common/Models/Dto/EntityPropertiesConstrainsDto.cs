using System.Collections.Generic;

namespace Apilane.Common.Models.Dto
{
    public class EntityPropertiesConstrainsDto
    {
        public List<DBWS_EntityProperty> Properties { get; set; } = null!;
        public List<EntityConstraint> Constraints { get; set; } = null!;
    }
}
