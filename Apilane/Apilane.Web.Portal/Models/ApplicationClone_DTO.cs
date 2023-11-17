using Apilane.Common.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Web.Portal.Models
{
    public class ApplicationClone_DTO
    {
        [AttrRequired]
        public long ServerID { get; set; }

        [AttrRequired]
        [Display(Name = "Database type")]
        public int DatabaseType { get; set; }

        public string? ConnectionString{ get; set; }

        [AttrRequired]
        [Display(Name = "Clone data")]
        public bool CloneData { get; set; }
    }
}
