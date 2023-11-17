using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum ForeignKeyLogic
    {
        [Display(Name = "On delete no action")]
        ON_DELETE_NO_ACTION = 0,

        [Display(Name = "On delete set null")]
        ON_DELETE_SET_NULL = 1,

        [Display(Name = "On delete cascade")]
        ON_DELETE_CASCADE = 2
    }
}
