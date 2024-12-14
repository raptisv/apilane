using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Apilane.Common.Extensions
{
    public static class DatatableExtensions
    {
        public static Dictionary<string, object?> ToDictionary(this DataRow dr)
        {
            return dr.Table.Columns.Cast<DataColumn>().ToDictionary(
                column => column.ColumnName,
                column => (dr[column] == DBNull.Value ? null : dr[column])
            );
        }

        public static List<Dictionary<string, object?>> ToDictionary(this DataTable dt)
        {
            return dt.AsEnumerable().Select(
                row =>
                {
                    return dt.Columns.Cast<DataColumn>().ToDictionary(
                        column =>
                        {
                            return column.ColumnName;
                        },
                        column =>
                        {
                            return (row[column] == DBNull.Value ? null : row[column]);
                        });
                }).ToList();
        }

        public static List<List<Dictionary<string, object?>>> ToDictionary(this DataTableCollection dtc)
        {
            var result = new List<List<Dictionary<string, object?>>>();

            foreach (DataTable dt in dtc)
            {
                result.Add(dt.ToDictionary());
            }

            return result;
        }
    }
}
