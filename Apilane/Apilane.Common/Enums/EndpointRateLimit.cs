using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum EndpointRateLimit
    {
        [Display(Name = "None")]
        None = 0,

        [Display(Name = "Per second")]
        Per_Second = 1,

        [Display(Name = "Per minute")]
        Per_Minute = 2,

        [Display(Name = "Per hour")]
        Per_Hour = 3
    }
}
