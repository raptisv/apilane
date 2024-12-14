using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum HostingEnvironment
    {
        [Display(Name = "Development")]
        Development = 0,

        [Display(Name = "Production")]
        Production = 1
    }
}
