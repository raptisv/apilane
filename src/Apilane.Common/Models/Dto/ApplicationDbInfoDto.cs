using Apilane.Common.Enums;

namespace Apilane.Common.Models.Dto
{
    public class ApplicationDbInfoDto
    {
        public required DatabaseType DatabaseType { get; set; }
        public required string ConnectionString { get; set; }
        public required string? DifferentiationEntity { get; set; }
    }
}
