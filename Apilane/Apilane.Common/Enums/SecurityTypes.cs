using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum SecurityTypes
    {
        [Display(Name = "Entity")]
        Entity = 0,

        [Display(Name = "Custom endpoint")]
        CustomEndpoint = 1
    }
}
