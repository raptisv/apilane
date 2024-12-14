using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Data.Extensions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Data.Repository
{
    public class SQLiteDataStorageRepository : IDataStorageRepository
    {
        public string _connectionString;
        private SQLiteConnection _databaseConnection = null!;

        public SQLiteDataStorageRepository(
            string connectionString)
        {
            _connectionString = connectionString;
        }

        private void TryOpenConnection()
        {
            _databaseConnection ??= new SQLiteConnection(_connectionString);

            if (_databaseConnection != null && _databaseConnection.State != ConnectionState.Open)
            {
                _databaseConnection.Open();

                // Enable extensions
                _databaseConnection.EnableExtensions(true);

                //Enable fts5
                _databaseConnection.LoadExtension(GetPathToSqliteInterop(), "sqlite3_fts5_init");
            }
        }

        private static string GetPathToSqliteInterop()
        {
            var pathToSqliteInterop = string.Empty;

            // On linux the path to SQLite.Interop.dll is defferent.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // This is the default path on linux
                pathToSqliteInterop = "/app/runtimes/linux-x64/native";
            }

            // Can be overriden by this environment variable
            var customPathToSqliteInterop = Environment.GetEnvironmentVariable("CUSTOM_PATH_TO_SQLITE_INTEROP");
            if (!string.IsNullOrWhiteSpace(customPathToSqliteInterop))
            {
                pathToSqliteInterop = customPathToSqliteInterop;
            }

            return Path.Combine(pathToSqliteInterop, "SQLite.Interop.dll");
        }

        public ValueTask DisposeAsync()
        {
            if (_databaseConnection is not null &&
                _databaseConnection.State != ConnectionState.Closed)
            {
                _databaseConnection.Close();
            }

            GC.SuppressFinalize(this);

            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_databaseConnection is not null &&
                _databaseConnection.State != ConnectionState.Closed)
            {
                _databaseConnection.Close();
            }

            GC.SuppressFinalize(this);
        }

        private async Task<object?> ExecScalarAsync(string cmd)
        {
            TryOpenConnection();

            using (SQLiteCommand command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandType = CommandType.Text;
                command.CommandText = $"PRAGMA foreign_keys=on;{cmd}";
                return await command.ExecuteScalarAsync();
            }
        }

        public async Task<int> ExecNQAsync(string cmd)
        {
            TryOpenConnection();

            if (!string.IsNullOrWhiteSpace(cmd))
            {
                using (SQLiteCommand command = new SQLiteCommand(_databaseConnection))
                {
                    command.CommandType = CommandType.Text;
                    command.CommandText = $"PRAGMA foreign_keys=on;{cmd}";
                    return await command.ExecuteNonQueryAsync();
                }
            }

            return 0;
        }

        public SQLiteDataAdapter GetLiteAdapter()
        {
            return new SQLiteDataAdapter(string.Empty, _databaseConnection);
        }

        public Task<DataTable> ExecTableAsync(string cmd)
        {
            TryOpenConnection();

            var adapter = GetLiteAdapter();
            var datatable = new DataTable();
            using (SQLiteCommand command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandType = CommandType.Text;
                command.CommandText = $"PRAGMA foreign_keys=on;{cmd}";
                adapter.SelectCommand = command;
                adapter.Fill(datatable);
            }

            return Task.FromResult(datatable);
        }

        public Task<DataTableCollection> ExecTablesAsync(string cmd)
        {
            TryOpenConnection();

            SQLiteDataAdapter adapter = GetLiteAdapter();
            DataSet dataSet = new DataSet();
            DataTable result = new DataTable();
            using (SQLiteCommand command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandText = $"PRAGMA foreign_keys=on;{cmd}";
                adapter.SelectCommand = command;
                adapter.Fill(dataSet);
            }

            return Task.FromResult(dataSet.Tables);
        }

        private Task<DataTable> ExecPagingAsync(
            string tables,
            string primaryKey,
            string fields = "*",
            int pageSize = -1,
            int pageIndex = -1,
            string? filter = null,
            string? sort = null,
            string? group = null)
        {
            TryOpenConnection();

            var cmdData = string.Format("SELECT {0} FROM {1} {2} {3} {4} {5}",
                fields,
                tables,
                (string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {filter} COLLATE NOCASE"),
                string.IsNullOrWhiteSpace(group) ? string.Empty : $"GROUP BY {group}",
                string.IsNullOrWhiteSpace(sort) ? $"ORDER BY [{primaryKey}]" : $"ORDER BY {sort}",
                (pageSize <= 0 || pageIndex <= 0) ? string.Empty : string.Format("LIMIT {1} OFFSET (({0} - 1) * {1}) ", pageIndex, pageSize));

            using (var command = new SQLiteCommand(_databaseConnection))
            {
                var adapter = GetLiteAdapter();
                command.CommandText = cmdData;
                adapter.SelectCommand = command;
                var dataSet = new DataSet();
                adapter.Fill(dataSet);

                return Task.FromResult(dataSet.Tables[0]);
            }
        }

        private Task<long> ExecCountAsync(string table, string primaryKey, string? filter = null, string? group = null)
        {
            TryOpenConnection();

            var cmdTotal = string.Format("SELECT COUNT({0}) FROM {1} {2} {3}",
                primaryKey,
                table,
                string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {filter} COLLATE NOCASE",
                string.IsNullOrWhiteSpace(group) ? string.Empty : $"GROUP BY {group}");

            using (var command = new SQLiteCommand(_databaseConnection))
            {
                command.CommandType = CommandType.Text;
                command.CommandText = cmdTotal;
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return Task.FromResult(Utils.GetLong(reader[0]));
                    }
                }
            }

            return Task.FromResult((long)0);
        }

        public static void GenerateDatabase(string path)
        {
            SQLiteConnection.CreateFile(path);

            /* Consider using this right after creating the database
                PRAGMA main.page_size = 4096;
                PRAGMA main.cache_size=10000;
                PRAGMA main.locking_mode=EXCLUSIVE;
                PRAGMA main.synchronous=NORMAL;
                PRAGMA main.journal_mode=WAL;
                PRAGMA main.cache_size=5000;
            */
        }

        public Task CreateTableWithPrimaryKeyAsync(string tableName)
        {
            return ExecNQAsync($@"CREATE TABLE [{tableName}] 
                                ( 
                                    [{Globals.PrimaryKeyColumn}] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT
                                );");
        }

        public Task RenameTableAsync(string oldTableName, string newTableName)
        {
            return ExecNQAsync($@"ALTER TABLE [{oldTableName}] RENAME TO [{newTableName}];");
        }

        public Task DropTableAsync(string tableName)
        {
            return ExecNQAsync($"DROP TABLE [{tableName}];");
        }

        public async Task<bool> ExistsTableAsync(string tableName)
        {
            try
            {
                await ExecNQAsync($"SELECT * FROM {tableName} LIMIT 1;");
            }
            catch
            {
                return false;
            }

            return true;
        }

        public async Task<bool> ExistsColumnAsync(string tableName, string columnName)
        {
            try
            {
                await ExecNQAsync($"SELECT [{columnName}] FROM [{tableName}] LIMIT 1;");
            }
            catch
            {
                return false;
            }

            return true;
        }

        public Task CreateColumnAsync(
            string tableName,
            string columnName,
            PropertyType type,
            bool notNull,
            int? numDecimalPlaces,
            long? strMaxLength)
        {
            var columnCmd = GetCreate(
                columnName,
                type,
                notNull,
                numDecimalPlaces);

            return ExecNQAsync($@"ALTER TABLE [{tableName}] ADD {columnCmd}");
        }

        private static string GetCreate(
            string columnName,
            PropertyType type,
            bool notNull,
            int? numDecimalPlaces)
        {
            switch (type)
            {
                case PropertyType.Number:
                    if (!numDecimalPlaces.HasValue || numDecimalPlaces.Value == 0)
                    {
                        return $"[{columnName}] INTEGER {(notNull ? "DEFAULT 0 NOT NULL" : "NULL")}";
                    }
                    else
                    {
                        return $"[{columnName}] NUMERIC(18,{numDecimalPlaces.Value}) {(notNull ? "DEFAULT 0 NOT NULL" : "NULL")}";
                    }
                case PropertyType.Boolean:
                    return $"[{columnName}] BOOLEAN {(notNull ? "DEFAULT 0 NOT NULL" : "NULL")}";
                case PropertyType.Date:
                    return $"[{columnName}] BIGINT {(notNull ? "DEFAULT 0 NOT NULL" : "NULL")}";
                case PropertyType.String:
                    return $"[{columnName}] TEXT {(notNull ? "DEFAULT '' NOT NULL" : "NULL")}";
                default:
                    throw new NotImplementedException();
            }
        }

        public Task DropColumnAsync(string tableName, string columnName)
        {
            return ExecNQAsync($@"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}];");
        }

        public Task RenameColumnAsync(string tableName, string oldColumnName, string newColumnName)
        {
            return ExecNQAsync($"ALTER TABLE [{tableName}] RENAME COLUMN [{oldColumnName}] TO [{newColumnName}];");
        }

        public Task SetConstraintsAsync(
            string tableName,
            List<(string Name, bool IsPrimaryKey, PropertyType Type, bool NotNull, int? NumDecimalPlaces)> tableColumns,
            List<EntityConstraint> incomingConstraints,
            List<EntityConstraint> currentConstraints)
        {
            // IMPORTANT! Get FK first, then Unique, because SQLite might require drop/recreate the table so any constraints before would be dropped.

            var finalCmd = GetForeignKeyConstraintsCommand(
                tableName,
                tableColumns,
                incomingConstraints.Where(x => x.TypeID == (int)ConstraintType.ForeignKey),
                currentConstraints.Where(x => x.TypeID == (int)ConstraintType.ForeignKey));

            finalCmd += GetUniqueConstraintsCommand(
                tableName,
                incomingConstraints.Where(x => x.TypeID == (int)ConstraintType.Unique),
                currentConstraints.Where(x => x.TypeID == (int)ConstraintType.Unique));

            if (!string.IsNullOrWhiteSpace(finalCmd))
            {
                return ExecNQAsync(finalCmd);
            }

            return Task.CompletedTask;
        }

        private static string GetForeignKeyConstraintsCommand(
            string tableName,
            List<(string Name, bool IsPrimaryKey, PropertyType Type, bool NotNull, int? NumDecimalPlaces)> tableColumns,
            IEnumerable<EntityConstraint> incomingForeignKeyConstraints,
            IEnumerable<EntityConstraint> currentForeignKeyConstraints)
        {
            var finalCmd = string.Empty;
            var foreignKeysChanged = false;

            incomingForeignKeyConstraints = incomingForeignKeyConstraints.DistinctBy(x => string.Join("_", x.GetForeignKeyPropertiesAsList())).ToList();

            foreach (var currentConstraint in currentForeignKeyConstraints)
            {
                if (!incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", currentConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // Nothing to do here, just notify that we need to recreate the table.
                    foreignKeysChanged = true;
                    break;
                }
            }

            foreach (var incomingConstraint in incomingForeignKeyConstraints)
            {
                if (!currentForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", incomingConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // Nothing to do here, just notify that we need to recreate the table.          
                    foreignKeysChanged = true;
                    break;
                }
            }

            if (foreignKeysChanged)
            {
                var sqliteForeighKeysStringCreate = new List<string>();

                foreach (var currentConstraint in currentForeignKeyConstraints)
                {
                    if (incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", currentConstraint.GetForeignKeyPropertiesAsList()))))
                    {
                        // The constraint still exists, keep it
                        var constrainName = $"FOREIGN_KEY_{tableName}_{string.Join("_", currentConstraint.GetForeignKeyPropertiesAsList())}";
                        var properties = currentConstraint.GetForeignKeyProperties();
                        var constraintText = $"CONSTRAINT {constrainName} FOREIGN KEY ([{properties.Property}]) REFERENCES [{properties.FKEntity}]([ID]) {GetFKLogic(properties.FKLogic)}";
                        sqliteForeighKeysStringCreate.Add(constraintText);
                    }
                }

                foreach (var incomingConstraint in incomingForeignKeyConstraints)
                {
                    if (!currentForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", incomingConstraint.GetForeignKeyPropertiesAsList()))))
                    {
                        // The constraint does not exist, create it
                        var constrainName = $"FOREIGN_KEY_{tableName}_{string.Join("_", incomingConstraint.GetForeignKeyPropertiesAsList())}";
                        var properties = incomingConstraint.GetForeignKeyProperties();
                        var constraintText = $"CONSTRAINT {constrainName} FOREIGN KEY ([{properties.Property}]) REFERENCES [{properties.FKEntity}]([ID]) {GetFKLogic(properties.FKLogic)}";
                        sqliteForeighKeysStringCreate.Add(constraintText);
                    }
                }

                var propertiesString = tableColumns.Where(x => !x.IsPrimaryKey).Select(x => x.Name).ToList();
                var propertiesStringCreate = tableColumns.Where(x => !x.IsPrimaryKey).Select(x => GetCreate(x.Name, x.Type, x.NotNull, x.NumDecimalPlaces)).ToList();
                var fksCmd = sqliteForeighKeysStringCreate.Any() ? $",{string.Join(",", sqliteForeighKeysStringCreate)}" : string.Empty;
                finalCmd += $@"DROP TABLE IF EXISTS [{tableName}__backup__];
                                CREATE TABLE [{tableName}__backup__]([ID] INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,{string.Join(",", propertiesStringCreate)}{fksCmd});
                                INSERT INTO [{tableName}__backup__] SELECT [ID],{string.Join(",", propertiesString.Select(x => $"[{x}]"))} FROM [{tableName}];
                                DROP TABLE [{tableName}];
                                ALTER TABLE [{tableName}__backup__] RENAME TO [{tableName}];";
            }

            return finalCmd;
        }

        private static string GetUniqueConstraintsCommand(
           string entityName,
           IEnumerable<EntityConstraint> incomingUniqueConstraints,
           IEnumerable<EntityConstraint> currentUniqueConstraints)
        {
            var finalCmd = string.Empty;

            incomingUniqueConstraints = incomingUniqueConstraints.DistinctBy(x => string.Join("_", x.GetUniqueProperties())).ToList();

            foreach (var currentConstraint in currentUniqueConstraints)
            {
                if (!incomingUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", currentConstraint.GetUniqueProperties()))))
                {
                    // The constraint is deleted, remove it from database
                    var constrainName = $"UNIQUE_{entityName}_{string.Join("_", currentConstraint.GetUniqueProperties())}";

                    finalCmd += $"DROP INDEX IF EXISTS {constrainName};";
                }
            }

            foreach (var incomingConstraint in incomingUniqueConstraints)
            {
                if (!currentUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", incomingConstraint.GetUniqueProperties()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = $"UNIQUE_{entityName}_{string.Join("_", incomingConstraint.GetUniqueProperties())}";
                    var properties = string.Join(",", incomingConstraint.GetUniqueProperties().Select(x => $"[{x}]"));

                    finalCmd += $"CREATE UNIQUE INDEX IF NOT EXISTS {constrainName} ON [{entityName}]({properties});";
                }
            }

            return finalCmd;
        }

        private static string GetFKLogic(ForeignKeyLogic logic) => logic switch
        {
            ForeignKeyLogic.ON_DELETE_CASCADE => "ON DELETE CASCADE ON UPDATE CASCADE",
            ForeignKeyLogic.ON_DELETE_SET_NULL => "ON DELETE SET NULL ON UPDATE SET NULL",
            _ => "ON DELETE NO ACTION ON UPDATE NO ACTION"
        };

        public async Task<Dictionary<string, object?>?> GetDataByIdAsync(
            string entityName,
            long id,
            List<string>? entityProperties)
        {
            var result = await ExecPagingAsync(entityName, Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"[{x}]")),
                filter: $" [{entityName}].[{Globals.PrimaryKeyColumn}] = {id}",
                sort: null,
                pageIndex: 1,
                pageSize: 1);

            if (result.Rows.Count == 1)
            {
                return result.Rows[0].ToDictionary();
            }

            return null;
        }

        public async Task<List<Dictionary<string, object?>>> GetPagedDataAsync(
            string entityName,
            List<string>? entityProperties,
            FilterData? filter,
            List<SortData>? sort,
            int pageIndex,
            int pageSize)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);
            string? strSort = sort?.ToSqlExpression(DatabaseType.SQLLite);

            var resultData = await ExecPagingAsync($"[{entityName}]", Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"[{x}]")),
                pageIndex: pageIndex, pageSize: pageSize, filter: strFilter, sort: strSort);

            return resultData.ToDictionary();
        }

        public async Task<long> GetDataCountAsync(string entityName, FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);

            return await ExecCountAsync(
                $"[{entityName}]",
                Globals.PrimaryKeyColumn,
                filter: strFilter);
        }

        public async Task<long?> CreateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            bool allowInsertIdentity)
        {
            var insertCmd = $@" INSERT INTO [{entityName}] 
                                ({string.Join(",", propertiesValues.Select(x => $"[{x.Key}]"))}) 
                                VALUES 
                                ({string.Join(",", propertiesValues.Select(x => $"{GetSqlValue(x.Value)}"))});

                                SELECT last_insert_rowid();";

            var result = await ExecScalarAsync(insertCmd);

            return Utils.GetNullLong(result);
        }

        public async Task<long> DeleteDataAsync(string entityName, FilterData? filter)
        {
            var strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);

            var cmd = $@"DELETE FROM [{entityName}]
                        {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")};";

            var result = await ExecNQAsync(cmd);

            return Utils.GetLong(result, 0);
        }

        public async Task<long> UpdateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);

            var insertCmd = $@" UPDATE [{entityName}] 
                                SET {string.Join(",", propertiesValues.Select(x => $"[{x.Key}] = {GetSqlValue(x.Value)}"))}
                                {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")};";

            var result = await ExecNQAsync(insertCmd);

            return Utils.GetLong(result, 0);
        }

        public async Task<List<List<Dictionary<string, object?>>>> ExecuteCustomAsync(string command)
        {
            var result = await ExecTablesAsync(command);
            return result.ToDictionary();
        }

        public async Task<List<Dictionary<string, object?>>> DistinctDataAsync(
            string entityName,
            string propertyName,
            FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);

            var result = await ExecTableAsync($@"SELECT DISTINCT [{propertyName}] 
                                                 FROM [{entityName}] {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}");

            return result.ToDictionary();
        }

        public async Task<List<Dictionary<string, object?>>> AggregateDataAsync(
            string entityName,
            AggregateData aggregates,
            FilterData? filter,
            GroupData? group,
            bool sortAsc,
            int pageIndex,
            int pageSize)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLLite);
            string sqlAggProps = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}([{x.Name}]) as '{x.Alias}' "));
            string sqlAggPropsOrder = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}([{x.Name}]) {(sortAsc ? "ASC" : "DESC")} "));

            var groupByProps = (group?.Properties?.Any() ?? false) ? $", + {string.Join(",", group.Properties.Select(x => GetProperty(x).Query + x.Alias))}" : string.Empty;
            var groupByGroup = (group?.Properties?.Any() ?? false) ? $"GROUP BY {string.Join(",", group.Properties.Select(x => GetProperty(x).Query))}" : string.Empty;

            var dtResult = await ExecTableAsync($@"SELECT {sqlAggProps}
                                        {groupByProps} 
                                        FROM [{entityName}] 
                                        {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter} COLLATE NOCASE")} 
                                        {groupByGroup} 
                                        ORDER BY {sqlAggPropsOrder}
                                        LIMIT {pageSize} OFFSET (({pageIndex} - 1) * {pageSize})");

            return dtResult.ToDictionary();
        }

        private static (string Query, string Alias) GetProperty(GroupData.GroupProperty property)
        {
            return property.Type switch
            {
                GroupData.GroupByType.Date_Year => ($"strftime('%Y', datetime([{property.Name}]/1000, 'unixepoch'))", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Month => ($"strftime('%m', datetime([{property.Name}]/1000, 'unixepoch'))", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Day => ($"strftime('%d', datetime([{property.Name}]/1000, 'unixepoch'))", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Hour => ($"strftime('%H', datetime([{property.Name}]/1000, 'unixepoch'))", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Minute => ($"strftime('%M', datetime([{property.Name}]/1000, 'unixepoch'))", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Second => ($"strftime('%S', datetime([{property.Name}]/1000, 'unixepoch'))", $" AS {property.Alias}"),
                _ => ($"[{property.Name}]", $" AS {property.Alias}"),
            };
        }

        private static string GetSqlValue(object? val)
        {
            if (val is null)
            {
                return "NULL";
            }

            if (val is long longVal)
            {
                return longVal.ToString();
            }

            if (val is bool boolVal)
            {
                return boolVal ? "1" : "0";
            }

            if (val is JsonElement jsonElement &&
                (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False))
            {
                return jsonElement.ValueKind == JsonValueKind.True ? "1" : "0";
            }

            if (val is decimal decimalVal)
            {
                return decimalVal.ToString().Replace(",", ".");
            }

            // All the rest
            return $"'{Utils.GetString(val).Replace("'", "''").Trim()}'";
        }
    }
}
