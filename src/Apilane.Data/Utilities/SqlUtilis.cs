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
    }
}
