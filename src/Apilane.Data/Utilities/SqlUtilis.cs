using Apilane.Common.Enums;
using System;

namespace Apilane.Data.Utilities
{
    public static class SqlUtilis
    {
        public static string GetString(object? val, string defaultValue = "")
        {
            return val is not null ?
                (val.ToString() ?? string.Empty).Replace("'", "''").Trim() :
                defaultValue;
        }

        /// <summary>
        /// Quotes an identifier (e.g. a column alias) for the given database, escaping the closing quote character.
        /// SQL Server: [..] with ']' doubled. MySQL: `..` with '`' doubled.
        /// PostgreSQL and SQLite: ".." with '"' doubled (SQLite's [..] form has no escape for ']').
        /// </summary>
        public static string QuoteIdentifier(string identifier, DatabaseType databaseType)
        {
            return databaseType switch
            {
                DatabaseType.SQLServer => $"[{identifier.Replace("]", "]]")}]",
                DatabaseType.MySQL => $"`{identifier.Replace("`", "``")}`",
                DatabaseType.PostgreSQL => $"\"{identifier.Replace("\"", "\"\"")}\"",
                DatabaseType.SQLLite => $"\"{identifier.Replace("\"", "\"\"")}\"",
                _ => throw new NotSupportedException($"Database type '{databaseType}' is not supported")
            };
        }

        /// <summary>
        /// The ORDER BY of a paged query: the requested sort, then the primary key when the sort does not
        /// have it. Rows with equal sort values come back in no defined order, which can differ between the
        /// queries of two pages: without the key, a record shows up on two pages and another on none.
        /// </summary>
        public static string GetPagingOrderBy(string? sort, string quotedPrimaryKey)
        {
            if (string.IsNullOrWhiteSpace(sort))
            {
                return $"ORDER BY {quotedPrimaryKey}";
            }

            return sort.Contains(quotedPrimaryKey, StringComparison.OrdinalIgnoreCase)
                ? $"ORDER BY {sort}"
                : $"ORDER BY {sort}, {quotedPrimaryKey}";
        }

        /// <summary>
        /// The number of rows the pages before <paramref name="pageIndex"/> hold, in 64 bits: in an int,
        /// (pageIndex - 1) * pageSize overflows from page 2,147,485 of 1000 rows on.
        /// </summary>
        public static long GetPagingOffset(int pageIndex, int pageSize)
        {
            return (long)(pageIndex - 1) * pageSize;
        }
    }
}
