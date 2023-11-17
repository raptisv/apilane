using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum AppClientIPsLogics
    {
        [Display(Name = "Block only the following IP addresses")]
        Block = 0,

        [Display(Name = "Allow only the following IP addresses")]
        Allow = 1
    }
}
