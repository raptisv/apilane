using Apilane.Common.Attributes;
using System;
using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Models
{
    public class EntityConstraint
    {
        [AttrRequired]
        [Display(Name = "Is system")]
        public bool IsSystem { get; set; }

        /// <summary>
        /// <see cref="Enums.ConstraintType"/>
        /// </summary>
        [AttrRequired]
        [Display(Name = "Type")]
        [Range(1, int.MaxValue)]
        public int TypeID { get; set; }

        [Display(Name = "Properties")]
        public string? Properties { get; set; }
    }
}
