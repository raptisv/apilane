using Apilane.Common.Attributes;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Portal.Models
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

        [Display(Name = "Entities to clone")]
        public List<string>? EntitiesToClone { get; set; }
    }
}
