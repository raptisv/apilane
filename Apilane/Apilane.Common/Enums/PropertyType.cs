using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum PropertyType
    {
        [Display(Name = "String")]
        String = 1,

        [Display(Name = "Numeric")]
        Number = 2,

        [Display(Name = "Boolean")]
        Boolean = 3,

        [Display(Name = "Date")]
        Date = 4,
    }
}
