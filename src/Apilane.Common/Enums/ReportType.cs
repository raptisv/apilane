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
        Line = 2,

        [Display(Name = "Bar chart")]
        Bar = 3,

        [Display(Name = "Radar chart")]
        Radar = 4,

        [Display(Name = "Stacked bar chart")]
        StackedBar = 5
    }
}
