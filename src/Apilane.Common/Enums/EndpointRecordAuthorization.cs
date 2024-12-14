using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum EndpointRecordAuthorization
    {
        [Display(Name = "All")]
        All = 0,

        [Display(Name = "Owned")]
        Owned = 1
    }
}
