using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum ConstraintType
    {
        [Display(Name = "Unique")]
        Unique = 1,

        [Display(Name = "Foreign key")]
        ForeignKey = 2
    }
}
