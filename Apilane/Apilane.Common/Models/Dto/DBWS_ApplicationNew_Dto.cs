using Apilane.Common.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Models.Dto
{
    public class DBWS_ApplicationNew_Dto
    {
        [AttrRequired]
        [Display(Name = "Name")]
        [MinLength(4), MaxLength(100)]
        public string Name { get; set; } = null!;

        [AttrRequired]
        [Display(Name = "Server")]
        public long ServerID { get; set; }

        [Display(Name = "Differentiation entity")]
        [MaxLength(40)]
        public string? DifferentiationEntity { get; set; }

        public string Token { get; set; } = null!;

        public string EncryptionKey { get; set; } = null!;

        public string? ConnectionString { get; set; }

        [AttrRequired]
        [Display(Name = "Database type")]
        public int DatabaseType { get; set; }
    }
}
