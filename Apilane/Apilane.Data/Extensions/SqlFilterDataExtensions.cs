using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Utilities;
using System;
using System.Linq;
using static Apilane.Common.Models.FilterData;

namespace Apilane.Data.Extensions
{
    public static class SqlFilterDataExtensions
    {
        public static string ToSqlExpression(
            this FilterData filterData,
            string entityName,
            DatabaseType databaseType)
        {
            string sqlfilter = string.Empty;

            // If we are on a parent filter item
            if (filterData.Filters != null && filterData.Filters.Any())
            {
                return "(" + string.Join(" " + filterData.Logic + " ", filterData.Filters.Select(filter => filter.ToSqlExpression(entityName, databaseType)).ToArray()) + ")";
            }

            // If we are on an actual filter, not a parent filter item
            if (filterData.Filters is null)
            {
                if (string.IsNullOrWhiteSpace(filterData.Property))
                {
                    throw new FormatException($"Filter property cannot be null or empty");
                }

                if (filterData.Value is not null)
                {
                    filterData.Value = SqlUtilis.GetString(filterData.Value);

                    var propertySql = databaseType switch
                    {
                        DatabaseType.MySQL => $"`{entityName}`.`{filterData.Property}`",
                        _ => $"[{entityName}].[{filterData.Property}]"
                    };

                    filterData.CreateFilterSql(ref sqlfilter, propertySql, databaseType, filterData.Property, filterData.Type);
                }
                else
                {
                    sqlfilter += databaseType switch
                    {
                        DatabaseType.MySQL => filterData.Operator switch
                        {
                            FilterOperators.equal => $"`{entityName}`.`{filterData.Property}` IS NULL ",
                            FilterOperators.notequal => $"`{entityName}`.`{filterData.Property}` IS NOT NULL ",
                            _ => throw new FormatException($"Null values accept only 'equals' and 'notequals' as an operator"),
                        },
                        _ => filterData.Operator switch
                        {
                            FilterOperators.equal => $"[{entityName}].[{filterData.Property}] IS NULL ",
                            FilterOperators.notequal => $"[{entityName}].[{filterData.Property}] IS NOT NULL ",
                            _ => throw new FormatException($"Null values accept only 'equals' and 'notequals' as an operator"),
                        }
                    };
                }
            }

            return sqlfilter;
        }

        private static void CreateFilterSql(
            this FilterData filterData,
            ref string sqlfilter,
            string propertySql,
            DatabaseType databaseType,
            string propertyName,
            PropertyType propertyType)
        {
            switch (propertyType)
            {
                case PropertyType.Number:
                    {
                        //validate numeric values
                        if (filterData.Operator == FilterOperators.contains ||
                            filterData.Operator == FilterOperators.notcontains)
                        {
                            // Numeric contains && notcontains can be comma separated decimals (e.g. 1,2,3,4) so validate this way
                            var decimalParts = (filterData.Value?.ToString() ?? string.Empty).Trim().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
                            foreach (var decimalPart in decimalParts)
                            {
                                if (!decimal.TryParse(decimalPart, out decimal number))
                                {
                                    throw new FormatException($"'{filterData.Value}' is not a valid number or group of numbers (e.g. 1,2,3,4)");
                                }
                            }

                            // Reconstruct to the clean value just to be sure
                            filterData.Value = string.Join(",", decimalParts);
                        }
                        else
                        {
                            // All other filter operators accept only decimals so validate this way
                            if (!decimal.TryParse((filterData.Value ?? string.Empty).ToString(), out decimal number))
                            {
                                throw new FormatException($"'{filterData.Value}' is not a valid number");
                            }
                        }

                        sqlfilter += filterData.Operator switch
                        {
                            FilterOperators.equal => propertySql + "=" + filterData.Value,
                            FilterOperators.notequal => propertySql + "<>" + filterData.Value,
                            FilterOperators.greater => propertySql + ">" + filterData.Value,
                            FilterOperators.greaterorequal => propertySql + ">=" + filterData.Value,
                            FilterOperators.less => propertySql + "<" + filterData.Value,
                            FilterOperators.lessorequal => propertySql + "<=" + filterData.Value,
                            FilterOperators.contains => propertySql + " IN (" + filterData.Value + ")",
                            FilterOperators.notcontains => propertySql + " NOT IN (" + filterData.Value + ")",
                            _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                        };
                    }
                    break;
                case PropertyType.Date:
                    {
                        DateTime? result = Utils.ParseDate(filterData.Value?.ToString() ?? string.Empty);

                        if (!result.HasValue)
                        {
                            throw new FormatException($"Unsupported date format for property '{propertyName}'");
                        }

                        string dateTimeValue = Utils.GetUnixTimestampMilliseconds(result.Value).ToString();

                        sqlfilter += filterData.Operator switch
                        {
                            FilterOperators.equal => propertySql + "=" + $"{dateTimeValue}",
                            FilterOperators.notequal => propertySql + "<>" + $"{dateTimeValue}",
                            FilterOperators.greater => propertySql + ">" + $"{dateTimeValue}",
                            FilterOperators.greaterorequal => propertySql + ">=" + $"{dateTimeValue}",
                            FilterOperators.less => propertySql + "<" + $"{dateTimeValue}",
                            FilterOperators.lessorequal => propertySql + "<=" + $"{dateTimeValue}",
                            _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                        };
                    }
                    break;
                case PropertyType.Boolean:
                    {
                        string ISNULL = databaseType switch
                        {
                            DatabaseType.SQLServer => "ISNULL",
                            DatabaseType.MySQL or DatabaseType.SQLLite => "IFNULL",
                            _ => throw new NotImplementedException(),
                        };

                        sqlfilter += filterData.Operator switch
                        {
                            FilterOperators.equal => $"{ISNULL}({propertySql}, 0) = {(Utils.GetBool(filterData.Value) ? "1" : "0")}",
                            FilterOperators.notequal => $"{ISNULL}({propertySql}, 0) <> {(Utils.GetBool(filterData.Value) ? "1" : "0")}",
                            _ => throw new FormatException($"Error operator for property '{propertyName}' - Boolean properties allow only 'equal' and 'notequal' as filter operator"),
                        };
                    }
                    break;
                case PropertyType.String:
                    {
                        if (databaseType == DatabaseType.SQLLite)
                        {
                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{propertySql} like '{filterData.Value}'",
                                FilterOperators.notequal => $"{propertySql} not like '{filterData.Value}'",
                                FilterOperators.startswith => $"{propertySql} like '{filterData.Value}%'",
                                FilterOperators.endswith => $"{propertySql} like '%{filterData.Value}'",
                                FilterOperators.contains => $"{propertySql} like '%{filterData.Value}%'",
                                FilterOperators.notcontains => $"{propertySql} not like '%{filterData.Value}%'",
                                FilterOperators.less => $"{propertySql} < '{filterData.Value}'",
                                FilterOperators.lessorequal => $"{propertySql} <= '{filterData.Value}'",
                                FilterOperators.greater => $"{propertySql} > '{filterData.Value}'",
                                FilterOperators.greaterorequal => $"{propertySql} >= '{filterData.Value}'",
                                _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                            };
                        }
                        else if (databaseType == DatabaseType.MySQL)
                        {
                            var strValue = filterData.Value?.ToString()?.Replace("\\", "\\\\") ?? string.Empty;

                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{propertySql} like N'{strValue}'",
                                FilterOperators.notequal => $"{propertySql} not like N'{strValue}'",
                                FilterOperators.startswith => $"{propertySql} like N'{strValue}%'",
                                FilterOperators.endswith => $"{propertySql} like N'%{strValue}'",
                                FilterOperators.contains => $"{propertySql} like N'%{strValue}%'",
                                FilterOperators.notcontains => $"{propertySql} not like N'%{strValue}%'",
                                FilterOperators.less => $"{propertySql} < N'{strValue}'",
                                FilterOperators.lessorequal => $"{propertySql} <= N'{strValue}'",
                                FilterOperators.greater => $"{propertySql} > N'{strValue}'",
                                FilterOperators.greaterorequal => $"{propertySql} >= N'{strValue}'",
                                _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                            };
                        }
                        else
                        {
                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{propertySql} like N'{filterData.Value}'",
                                FilterOperators.notequal => $"{propertySql} not like N'{filterData.Value}'",
                                FilterOperators.startswith => $"{propertySql} like N'{filterData.Value}%'",
                                FilterOperators.endswith => $"{propertySql} like N'%{filterData.Value}'",
                                FilterOperators.contains => $"{propertySql} like N'%{filterData.Value}%'",
                                FilterOperators.notcontains => $"{propertySql} not like N'%{filterData.Value}%'",
                                FilterOperators.less => $"{propertySql} < N'{filterData.Value}'",
                                FilterOperators.lessorequal => $"{propertySql} <= N'{filterData.Value}'",
                                FilterOperators.greater => $"{propertySql} > N'{filterData.Value}'",
                                FilterOperators.greaterorequal => $"{propertySql} >= N'{filterData.Value}'",
                                _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                            };
                        }
                    }
                    break;
                default:
                    throw new FormatException($"Not implemented");
            }
        }
    }
}
