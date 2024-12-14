using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum ReportType
    {
        [Display(Name = "Grid")]
        Grid = 0,

        [Display(Name = "Pie chart")]
        Pie = 1,

        [Display(Name = "Line chart")]
        Line = 2
    }
}
