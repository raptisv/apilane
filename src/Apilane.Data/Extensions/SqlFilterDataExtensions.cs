using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Data.Utilities;
using System;
using System.Linq;
using System.Text;
using static Apilane.Common.Models.FilterData;

namespace Apilane.Data.Extensions
{
    public static class SqlFilterDataExtensions
    {
        // Escape character for the LIKE patterns built from string filter values.
        // Not a backslash: MySQL string literals and the default MySQL/PostgreSQL LIKE escape both give '\' a meaning of its own.
        private const char LikeEscapeChar = '!';
        private static readonly string LikeEscapeClause = $" ESCAPE '{LikeEscapeChar}'";

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
                    // Do not write back to filterData.Value, the same filter can be converted more than once (e.g. data and total count)
                    var value = SqlUtilis.GetString(filterData.Value);

                    var propertySql = databaseType switch
                    {
                        DatabaseType.MySQL => $"`{entityName}`.`{filterData.Property}`",
                        DatabaseType.PostgreSQL => $"\"{entityName}\".\"{filterData.Property}\"",
                        _ => $"[{entityName}].[{filterData.Property}]"
                    };

                    filterData.CreateFilterSql(ref sqlfilter, propertySql, databaseType, filterData.Property, filterData.Type, value);
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
                        DatabaseType.PostgreSQL => filterData.Operator switch
                        {
                            FilterOperators.equal => $"\"{entityName}\".\"{filterData.Property}\" IS NULL ",
                            FilterOperators.notequal => $"\"{entityName}\".\"{filterData.Property}\" IS NOT NULL ",
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
            PropertyType propertyType,
            string value)
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
                            var decimalParts = value.Trim().Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim());
                            foreach (var decimalPart in decimalParts)
                            {
                                if (!decimal.TryParse(decimalPart, out decimal number))
                                {
                                    throw new FormatException($"'{value}' is not a valid number or group of numbers (e.g. 1,2,3,4)");
                                }
                            }

                            // Reconstruct to the clean value just to be sure
                            value = string.Join(",", decimalParts);
                        }
                        else
                        {
                            // All other filter operators accept only decimals so validate this way
                            if (!decimal.TryParse(value, out decimal number))
                            {
                                throw new FormatException($"'{value}' is not a valid number");
                            }
                        }

                        sqlfilter += filterData.Operator switch
                        {
                            FilterOperators.equal => propertySql + "=" + value,
                            FilterOperators.notequal => propertySql + "<>" + value,
                            FilterOperators.greater => propertySql + ">" + value,
                            FilterOperators.greaterorequal => propertySql + ">=" + value,
                            FilterOperators.less => propertySql + "<" + value,
                            FilterOperators.lessorequal => propertySql + "<=" + value,
                            FilterOperators.contains => propertySql + " IN (" + value + ")",
                            FilterOperators.notcontains => propertySql + " NOT IN (" + value + ")",
                            _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                        };
                    }
                    break;
                case PropertyType.Date:
                    {
                        DateTime? result = Utils.ParseDate(value);

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
                            DatabaseType.PostgreSQL => "COALESCE",
                            _ => throw new NotImplementedException(),
                        };

                        if (databaseType == DatabaseType.PostgreSQL)
                        {
                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{ISNULL}({propertySql}, FALSE) = {(Utils.GetBool(value) ? "TRUE" : "FALSE")}",
                                FilterOperators.notequal => $"{ISNULL}({propertySql}, FALSE) <> {(Utils.GetBool(value) ? "TRUE" : "FALSE")}",
                                _ => throw new FormatException($"Error operator for property '{propertyName}' - Boolean properties allow only 'equal' and 'notequal' as filter operator"),
                            };
                        }
                        else
                        {
                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{ISNULL}({propertySql}, 0) = {(Utils.GetBool(value) ? "1" : "0")}",
                                FilterOperators.notequal => $"{ISNULL}({propertySql}, 0) <> {(Utils.GetBool(value) ? "1" : "0")}",
                                _ => throw new FormatException($"Error operator for property '{propertyName}' - Boolean properties allow only 'equal' and 'notequal' as filter operator"),
                            };
                        }
                    }
                    break;
                case PropertyType.String:
                    {
                        // The LIKE operators get the value as a pattern with its wildcards escaped, so that e.g. equal 'cust_1'
                        // does not also match 'custA1'. The comparison operators (<, <=, >, >=) use the plain value.
                        // equal/notequal must stay case-insensitive: SQL Server and MySQL use =/<> (their collations are
                        // case-insensitive, and LIKE there compares per character and differs from = on trailing blanks,
                        // expansions like ß/ss and ignorable characters); SQLite and PostgreSQL need (I)LIKE for that.
                        var pattern = EscapeLikePattern(value, databaseType);

                        if (databaseType == DatabaseType.SQLLite)
                        {
                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{propertySql} like '{pattern}'{LikeEscapeClause}",
                                FilterOperators.notequal => $"{propertySql} not like '{pattern}'{LikeEscapeClause}",
                                FilterOperators.startswith => $"{propertySql} like '{pattern}%'{LikeEscapeClause}",
                                FilterOperators.endswith => $"{propertySql} like '%{pattern}'{LikeEscapeClause}",
                                FilterOperators.contains => $"{propertySql} like '%{pattern}%'{LikeEscapeClause}",
                                FilterOperators.notcontains => $"{propertySql} not like '%{pattern}%'{LikeEscapeClause}",
                                FilterOperators.less => $"{propertySql} < '{value}'",
                                FilterOperators.lessorequal => $"{propertySql} <= '{value}'",
                                FilterOperators.greater => $"{propertySql} > '{value}'",
                                FilterOperators.greaterorequal => $"{propertySql} >= '{value}'",
                                _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                            };
                        }
                        else if (databaseType == DatabaseType.MySQL)
                        {
                            var strValue = value.Replace("\\", "\\\\");
                            var strPattern = pattern.Replace("\\", "\\\\");

                            // No N'' prefix: in MySQL that is utf8mb3, which cannot hold supplementary characters (e.g. emoji)
                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{propertySql} = '{strValue}'",
                                FilterOperators.notequal => $"{propertySql} <> '{strValue}'",
                                FilterOperators.startswith => $"{propertySql} like '{strPattern}%'{LikeEscapeClause}",
                                FilterOperators.endswith => $"{propertySql} like '%{strPattern}'{LikeEscapeClause}",
                                FilterOperators.contains => $"{propertySql} like '%{strPattern}%'{LikeEscapeClause}",
                                FilterOperators.notcontains => $"{propertySql} not like '%{strPattern}%'{LikeEscapeClause}",
                                FilterOperators.less => $"{propertySql} < '{strValue}'",
                                FilterOperators.lessorequal => $"{propertySql} <= '{strValue}'",
                                FilterOperators.greater => $"{propertySql} > '{strValue}'",
                                FilterOperators.greaterorequal => $"{propertySql} >= '{strValue}'",
                                _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                            };
                        }
                        else if (databaseType == DatabaseType.PostgreSQL)
                        {
                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{propertySql} ILIKE '{pattern}'{LikeEscapeClause}",
                                FilterOperators.notequal => $"{propertySql} NOT ILIKE '{pattern}'{LikeEscapeClause}",
                                FilterOperators.startswith => $"{propertySql} ILIKE '{pattern}%'{LikeEscapeClause}",
                                FilterOperators.endswith => $"{propertySql} ILIKE '%{pattern}'{LikeEscapeClause}",
                                FilterOperators.contains => $"{propertySql} ILIKE '%{pattern}%'{LikeEscapeClause}",
                                FilterOperators.notcontains => $"{propertySql} NOT ILIKE '%{pattern}%'{LikeEscapeClause}",
                                FilterOperators.less => $"{propertySql} < '{value}'",
                                FilterOperators.lessorequal => $"{propertySql} <= '{value}'",
                                FilterOperators.greater => $"{propertySql} > '{value}'",
                                FilterOperators.greaterorequal => $"{propertySql} >= '{value}'",
                                _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                            };
                        }
                        else
                        {
                            sqlfilter += filterData.Operator switch
                            {
                                FilterOperators.equal => $"{propertySql} = N'{value}'",
                                FilterOperators.notequal => $"{propertySql} <> N'{value}'",
                                FilterOperators.startswith => $"{propertySql} like N'{pattern}%'{LikeEscapeClause}",
                                FilterOperators.endswith => $"{propertySql} like N'%{pattern}'{LikeEscapeClause}",
                                FilterOperators.contains => $"{propertySql} like N'%{pattern}%'{LikeEscapeClause}",
                                FilterOperators.notcontains => $"{propertySql} not like N'%{pattern}%'{LikeEscapeClause}",
                                FilterOperators.less => $"{propertySql} < N'{value}'",
                                FilterOperators.lessorequal => $"{propertySql} <= N'{value}'",
                                FilterOperators.greater => $"{propertySql} > N'{value}'",
                                FilterOperators.greaterorequal => $"{propertySql} >= N'{value}'",
                                _ => throw new FormatException($"Error operator for property '{propertyName}'"),
                            };
                        }
                    }
                    break;
                default:
                    throw new FormatException($"Not implemented");
            }
        }

        /// <summary>
        /// Escapes the LIKE pattern characters of a filter value so that it matches literally.
        /// Use the result together with <see cref="LikeEscapeClause"/>.
        /// </summary>
        private static string EscapeLikePattern(string value, DatabaseType databaseType)
        {
            var pattern = new StringBuilder(value.Length);

            foreach (var c in value)
            {
                // '[' opens a character class on SQL Server only
                if (c == LikeEscapeChar || c == '%' || c == '_' ||
                    (c == '[' && databaseType == DatabaseType.SQLServer))
                {
                    pattern.Append(LikeEscapeChar);
                }

                pattern.Append(c);
            }

            return pattern.ToString();
        }
    }
}
