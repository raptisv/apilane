using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Data.Extensions
{
    public static class SqlSortDataExtensions
    {
        public static string ToSqlExpression(this List<SortData> sortData, DatabaseType databaseType)
        {
            return string.Join(",", sortData.Select(x => x.GetSortString(databaseType)));
        }

        private static string GetSortString(this SortData sortData, DatabaseType databaseType)
        {
            if (string.IsNullOrWhiteSpace(sortData.Property))
            {
                throw new Exception("Property cannot be empty");
            }

            return databaseType == DatabaseType.MySQL
                ? $"`{Utils.GetString(sortData.Property)}` {sortData.GetDirection()}"
                : $"[{Utils.GetString(sortData.Property)}] {sortData.GetDirection()}";
        }

        private static string GetDirection(this SortData sortData)
        {
            return sortData.Direction.ToLower().Equals("desc") ? " desc" : " asc";
        }
    }
}
