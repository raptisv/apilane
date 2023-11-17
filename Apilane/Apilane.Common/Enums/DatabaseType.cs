using System.ComponentModel.DataAnnotations;

namespace Apilane.Common.Enums
{
    public enum DatabaseType
    {
        [Display(Name = "SQL Lite")]
        SQLLite = 1,

        [Display(Name = "SQL Server")]
        SQLServer = 2,

        [Display(Name = "MySQL")]
        MySQL = 3
    }
}
