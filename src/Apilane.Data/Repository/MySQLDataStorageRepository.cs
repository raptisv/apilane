using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Data.Extensions;
using Apilane.Data.Utilities;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Data.Repository
{
    public class MySQLDataStorageRepository : IDataStorageRepository
    {
        private string _connectionString;
        private MySqlConnection _databaseConnection = null!;

        public MySQLDataStorageRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private void OpenConnection()
        {
            _databaseConnection ??= new MySqlConnection(_connectionString);

            if (_databaseConnection is not null &&
                _databaseConnection.State != ConnectionState.Open)
            {
                _databaseConnection.Open();
            }
        }

        private void TryOpenConnection()
        {
            OpenConnection();

            // Every command goes through here, so every command runs in the current transaction scope, also
            // when the connection was opened before the scope began.
            TransactionEnlistment.JoinAmbientTransaction(_databaseConnection);
        }

        public ValueTask DisposeAsync()
        {
            if (_databaseConnection is not null &&
                _databaseConnection.State != ConnectionState.Closed)
            {
                _databaseConnection.Close();
            }

            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_databaseConnection is not null &&
                _databaseConnection.State != ConnectionState.Closed)
            {
                _databaseConnection.Close();
            }
        }

        private static DbDataAdapter GetAdapter()
        {
            var dbProviderFactory = DatabaseType.MySQL.GetDbProviderFactory();
            return dbProviderFactory.CreateDataAdapter() ?? throw new Exception("Could not create data adapter");
        }

        public Task<int> ExecCountAsync(string tables, string primaryKey, string? filter = null, string? group = null)
        {
            TryOpenConnection();

            // Total
            var cmdTotal = string.Format("SELECT COUNT({0}) FROM {1} {2} {3}",
                primaryKey,
                tables,
                string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {Utils.GetString(filter)}",
                string.IsNullOrWhiteSpace(group) ? string.Empty : "GROUP BY " + Utils.GetString(group));

            using (var command = _databaseConnection.CreateCommand())
            {
                // Total
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(0);
                command.CommandType = CommandType.Text;
                command.CommandText = cmdTotal;
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return Task.FromResult(Utils.GetInt(reader[0]));
                    }
                }
            }

            return Task.FromResult(0);
        }

        public async Task<object?> ExecScalarAsync(string cmd)
        {
            TryOpenConnection();

            using (DbCommand command = _databaseConnection.CreateCommand())
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(0);
                command.CommandType = CommandType.Text;
                command.CommandText = cmd;
                return await command.ExecuteScalarAsync();
            }
        }

        public async Task<int> ExecNQAsync(string cmd)
        {
            TryOpenConnection();

            using (DbCommand command = _databaseConnection.CreateCommand())
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(0);
                command.CommandType = CommandType.Text;
                command.CommandText = cmd;
                return await command.ExecuteNonQueryAsync();
            }
        }

        public Task<DataTable> ExecTableAsync(string cmd)
        {
            TryOpenConnection();

            DbDataAdapter adapter = GetAdapter();
            DataTable datatable = new DataTable();
            using (DbCommand command = _databaseConnection.CreateCommand())
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(0);
                command.CommandType = CommandType.Text;
                command.CommandText = cmd;
                adapter.SelectCommand = command;
                adapter.Fill(datatable);
            }

            return Task.FromResult(datatable);
        }

        public Task<DataTableCollection> ExecTablesAsync(string cmd)
        {
            TryOpenConnection();

            DbDataAdapter adapter = GetAdapter();
            DataSet dataSet = new DataSet();
            using (DbCommand command = _databaseConnection.CreateCommand())
            {
                command.CommandTimeout = TransactionEnlistment.GetCommandTimeout(command.CommandTimeout);
                command.CommandText = cmd;
                adapter.SelectCommand = command;
                adapter.Fill(dataSet);
            }

            return Task.FromResult(dataSet.Tables);
        }

        public async Task<DataTable> ExecPagingAsync(
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
                (string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {filter}"),
                string.IsNullOrWhiteSpace(group) ? string.Empty : $"GROUP BY {group}",
                SqlUtilis.GetPagingOrderBy(sort, $"`{primaryKey}`"),
                (pageSize <= 0 || pageIndex <= 0) ? string.Empty : $"LIMIT {SqlUtilis.GetPagingOffset(pageIndex, pageSize)},{pageSize}");

            return await ExecTableAsync(cmdData);
        }

        public static void ConfirmDatabaseExists(string connString)
        {
            // Just test if database exists. Dispose, otherwise the connection never returns to the pool.
            // Call it outside a TransactionScope: a connection opened inside one joins it, and a second
            // connection in the same transaction is not supported.
            using (var repository = new MySQLDataStorageRepository(connString))
            {
                repository.OpenConnection();
            }
        }

        public Task CreateTableWithPrimaryKeyAsync(string tableName)
        {
            return ExecNQAsync($@"CREATE TABLE `{tableName}`
                                ( 
                                    `{Globals.PrimaryKeyColumn}` BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY
                                );");
        }

        public async Task RenameTableAsync(string oldTableName, string newTableName, List<EntityConstraint> constraints)
        {
            // The constraint names carry the entity name, and a later save of the constraints drops and
            // creates them by that name: rename them with the table. A constraint is renamed only when it
            // exists and its new name is free: unique keys are named per table, foreign keys per database.
            var existing = (await ExecTableAsync($@"SELECT TABLE_NAME AS table_name, CONSTRAINT_NAME AS constraint_name, CONSTRAINT_TYPE AS constraint_type
                                                    FROM information_schema.TABLE_CONSTRAINTS
                                                    WHERE CONSTRAINT_SCHEMA = DATABASE()
                                                    AND CONSTRAINT_TYPE IN ('UNIQUE', 'FOREIGN KEY');")).Rows.Cast<DataRow>()
                .Select(row => (
                    Table: Convert.ToString(row["table_name"]) ?? string.Empty,
                    Name: Convert.ToString(row["constraint_name"]) ?? string.Empty,
                    Type: Convert.ToString(row["constraint_type"]) ?? string.Empty))
                .ToList();

            bool Exists(string name, string type, string? table)
            {
                return existing.Any(x =>
                    x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                    x.Type.Equals(type, StringComparison.OrdinalIgnoreCase) &&
                    (table is null || x.Table.Equals(table, StringComparison.OrdinalIgnoreCase)));
            }

            var cmd = $@"RENAME TABLE `{oldTableName}` TO `{newTableName}`;";
            var foreignKeysCmd = string.Empty;

            foreach (var constraint in constraints)
            {
                if (constraint.TypeID == (int)ConstraintType.Unique && constraint.GetUniqueProperties().Count > 0)
                {
                    var oldName = GetUniqueConstraintName(oldTableName, constraint);
                    var newName = GetUniqueConstraintName(newTableName, constraint);

                    if (Exists(oldName, "UNIQUE", oldTableName) && !Exists(newName, "UNIQUE", oldTableName))
                    {
                        cmd += $"ALTER TABLE `{newTableName}` RENAME INDEX {oldName} TO {newName};";
                        existing.Add((oldTableName, newName, "UNIQUE"));
                    }
                }
                else if (constraint.TypeID == (int)ConstraintType.ForeignKey)
                {
                    var oldName = GetForeignKeyConstraintName(oldTableName, constraint);
                    var newName = GetForeignKeyConstraintName(newTableName, constraint);

                    if (Exists(oldName, "FOREIGN KEY", oldTableName) && !Exists(newName, "FOREIGN KEY", null))
                    {
                        // A foreign key cannot be renamed: it is dropped and added under the new name, in one
                        // statement. Its rows were checked when it was added, so they are not checked again.
                        var properties = constraint.GetForeignKeyProperties();
                        foreignKeysCmd += $@"ALTER TABLE `{newTableName}` DROP FOREIGN KEY {oldName},
                                             ADD CONSTRAINT {newName} FOREIGN KEY (`{properties.Property}`) REFERENCES `{properties.FKEntity}`(`ID`) {GetFKLogic(properties.FKLogic)};";
                        existing.Add((oldTableName, newName, "FOREIGN KEY"));
                    }
                }
            }

            await ExecNQAsync(cmd);

            if (!string.IsNullOrWhiteSpace(foreignKeysCmd))
            {
                try
                {
                    await ExecNQAsync($"SET FOREIGN_KEY_CHECKS = 0;{foreignKeysCmd}");
                }
                finally
                {
                    // The setting belongs to the connection, which the repository keeps for the request
                    await ExecNQAsync("SET FOREIGN_KEY_CHECKS = 1;");
                }
            }
        }

        public Task DropTableAsync(string tableName)
        {
            return ExecNQAsync($"DROP TABLE `{tableName}`;");
        }

        public async Task<bool> ExistsTableAsync(string tableName)
        {
            var result = await ExecScalarAsync(
                $@"SELECT COUNT(*) FROM information_schema.tables
                   WHERE table_schema = DATABASE()
                   AND table_name = '{EscapeLiteral(tableName)}';");

            return Utils.GetLong(result, 0) > 0;
        }

        public async Task<bool> ExistsColumnAsync(string tableName, string columnName)
        {
            var result = await ExecScalarAsync(
                $@"SELECT COUNT(*) FROM information_schema.columns
                   WHERE table_schema = DATABASE()
                   AND table_name = '{EscapeLiteral(tableName)}'
                   AND column_name = '{EscapeLiteral(columnName)}';");

            return Utils.GetLong(result, 0) > 0;
        }

        /// <summary>
        /// A name as the content of a MySQL string literal: the backslash escapes there too.
        /// </summary>
        private static string EscapeLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("'", "''");
        }

        public Task CreateColumnsAsync(
            string tableName,
            List<(string Name, PropertyType Type, bool NotNull, int? NumDecimalPlaces, long? StrMaxLength)> columns)
        {
            if (columns.Count == 0)
            {
                return Task.CompletedTask;
            }

            // One ALTER for all the columns: every ALTER TABLE is a slow, committed operation of its own.
            return ExecNQAsync($"ALTER TABLE `{tableName}` {string.Join(", ", columns.Select(x =>
                $"ADD {GetCreate(x.Name, x.Type, x.NotNull, x.NumDecimalPlaces, x.StrMaxLength)}"))};");
        }

        private static string GetCreate(
            string columnName,
            PropertyType type,
            bool notNull,
            int? numDecimalPlaces,
            long? strMaxLength)
        {
            switch (type)
            {
                case PropertyType.Number:
                    if (!numDecimalPlaces.HasValue || numDecimalPlaces.Value == 0)
                    {
                        return $"`{columnName}` BIGINT {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                    }
                    else
                    {
                        return $"`{columnName}` DECIMAL(18,{numDecimalPlaces.Value}) {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                    }
                case PropertyType.Boolean:
                    return $"`{columnName}` BOOL {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                case PropertyType.Date:
                    return $"`{columnName}` BIGINT {(notNull ? "NOT NULL" : "NULL")}";
                case PropertyType.String:
                    {
                        if (strMaxLength is null)
                        {
                            return $"`{columnName}` TEXT {(notNull ? "NOT NULL" : "NULL")}";
                        }

                        if (strMaxLength > 16_383)
                        {
                            throw new Exception("Max length can be up to 16.383 characters");
                        }

                        return $"`{columnName}` VARCHAR({strMaxLength.Value}) {(notNull ? $"NOT NULL DEFAULT ''" : "NULL")}";
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public Task DropColumnAsync(string tableName, string columnName)
        { 
            return ExecNQAsync($@"ALTER TABLE `{tableName}` DROP COLUMN `{columnName}`;");
        }

        public Task RenameColumnAsync(string tableName, string oldColumnName, string newColumnName)
        {
            return ExecNQAsync($@"ALTER TABLE `{tableName}` RENAME COLUMN `{oldColumnName}` TO `{newColumnName}`;");
        }

        public Task SetConstraintsAsync(
            string tableName,
            List<(string Name, bool IsPrimaryKey, PropertyType Type, bool NotNull, int? NumDecimalPlaces)> tableColumns,
            List<EntityConstraint> incomingConstraints,
            List<EntityConstraint> currentConstraints)
        {
            var finalCmd = GetForeignKeyConstraintsCommand(
                tableName,
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
            string entityName,
            IEnumerable<EntityConstraint> incomingForeignKeyConstraints,
            IEnumerable<EntityConstraint> currentForeignKeyConstraints)
        {
            // IMPORTANT! For MySQL constraint names are trimed due to 64 chars limitation for identifiers.

            var finalCmd = string.Empty;

            incomingForeignKeyConstraints = incomingForeignKeyConstraints.DistinctBy(x => string.Join("_", x.GetForeignKeyPropertiesAsList())).ToList();

            foreach (var currentConstraint in currentForeignKeyConstraints)
            {
                if (!incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", currentConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // The constraint is deleted, remove it from database
                    var constrainName = GetForeignKeyConstraintName(entityName, currentConstraint);

                    finalCmd += $@"ALTER TABLE `{entityName}` DROP FOREIGN KEY {constrainName};";
                }
            }

            foreach (var incomingConstraint in incomingForeignKeyConstraints)
            {
                if (!currentForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", incomingConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = GetForeignKeyConstraintName(entityName, incomingConstraint);

                    var properties = incomingConstraint.GetForeignKeyProperties();

                    finalCmd += $@"ALTER TABLE `{entityName}` ADD CONSTRAINT {constrainName} FOREIGN KEY (`{properties.Property}`) REFERENCES `{properties.FKEntity}`(`ID`) {GetFKLogic(properties.FKLogic)};";
                }
            }

            return finalCmd;
        }

        private static string GetUniqueConstraintsCommand(
           string entityName,
           IEnumerable<EntityConstraint> incomingUniqueConstraints,
           IEnumerable<EntityConstraint> currentUniqueConstraints)
        {
            // IMPORTANT! For MySQL constraint names are trimed due to 64 chars limitation for identifiers.

            var finalCmd = string.Empty;

            incomingUniqueConstraints = incomingUniqueConstraints.DistinctBy(x => string.Join("_", x.GetUniqueProperties())).ToList();

            foreach (var currentConstraint in currentUniqueConstraints)
            {
                if (!incomingUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", currentConstraint.GetUniqueProperties()))))
                {
                    // The constraint is deleted, remove it from database
                    var constrainName = GetUniqueConstraintName(entityName, currentConstraint);

                    finalCmd += $@"ALTER TABLE `{entityName}` DROP INDEX {constrainName};";
                }
            }

            foreach (var incomingConstraint in incomingUniqueConstraints)
            {
                if (!currentUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", incomingConstraint.GetUniqueProperties()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = GetUniqueConstraintName(entityName, incomingConstraint);

                    var properties = string.Join(",", incomingConstraint.GetUniqueProperties().Select(x => $"`{x}`"));

                    finalCmd += $@"ALTER TABLE `{entityName}` ADD CONSTRAINT {constrainName} UNIQUE ({properties});";
                }
            }

            return finalCmd;
        }

        // IMPORTANT! For MySQL constraint names are trimed due to 64 chars limitation for identifiers.

        private static string GetForeignKeyConstraintName(string entityName, EntityConstraint constraint)
        {
            var constrainName = $"FOREIGN_KEY_{entityName}_{string.Join("_", constraint.GetForeignKeyPropertiesAsList())}";
            return constrainName.Length > 64 ? constrainName.Substring(0, 64) : constrainName;
        }

        private static string GetUniqueConstraintName(string entityName, EntityConstraint constraint)
        {
            var constrainName = $"UNIQUE_{entityName}_{string.Join("_", constraint.GetUniqueProperties())}";
            return constrainName.Length > 64 ? constrainName.Substring(0, 64) : constrainName;
        }

        private static string GetFKLogic(ForeignKeyLogic logic) => logic switch
        {
            ForeignKeyLogic.ON_DELETE_NO_ACTION => "ON DELETE NO ACTION ON UPDATE NO ACTION",
            ForeignKeyLogic.ON_DELETE_SET_NULL => "ON DELETE SET NULL ON UPDATE SET NULL",
            _ => "ON DELETE CASCADE ON UPDATE CASCADE"
        };

        public async Task<Dictionary<string, object?>?> GetDataByIdAsync(
            string entityName,
            long id,
            List<string>? entityProperties)
        {
            var result = await ExecPagingAsync($"`{entityName}`", Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"`{x}`")),
                filter: $" `{entityName}`.`{Globals.PrimaryKeyColumn}` = {id}",
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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.MySQL);
            string? strSort = sort?.ToSqlExpression(DatabaseType.MySQL);

            var resultData = await ExecPagingAsync($"`{entityName}`", Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"`{x}`")),
                pageIndex: pageIndex, pageSize: pageSize, filter: strFilter, sort: strSort);

            return resultData.ToDictionary();
        }

        public async Task<long> GetDataCountAsync(string entityName, FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.MySQL);

            return await ExecCountAsync(
                $"`{entityName}`",
                Globals.PrimaryKeyColumn,
                filter: strFilter);
        }

        public async Task<long?> CreateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            bool allowInsertIdentity)
        {
            var insertCmd = $@" INSERT INTO `{entityName}` 
                                ({string.Join(",", propertiesValues.Select(x => $"`{x.Key}`"))}) 
                                VALUES 
                                ({string.Join(",", propertiesValues.Select(x => $"{GetSqlValue(x.Value)}"))});

                                SELECT LAST_INSERT_ID();";

            var result = await ExecScalarAsync(insertCmd);

            return Utils.GetNullLong(result);
        }

        public Task SyncIdSequenceAsync(string entityName)
        {
            // Nothing to do: AUTO_INCREMENT moves past an ID that was inserted with its value.
            return Task.CompletedTask;
        }

        public async Task<long> DeleteDataAsync(string entityName, FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.MySQL);

            var result = await ExecNQAsync($"DELETE FROM `{entityName}` " +
                                               $"{(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")};");

            return Utils.GetLong(result, 0);
        }

        public async Task<long> UpdateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.MySQL);

            var insertCmd = $@" UPDATE `{entityName}`
                                SET {string.Join(",", propertiesValues.Select(x => $"`{x.Key}` = {GetSqlValue(x.Value)}"))}
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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.MySQL);

            // At most one page of values, like every other read, in a fixed order
            return (await ExecTableAsync($@"SELECT DISTINCT `{propertyName}`
                                     FROM `{entityName}` {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}
                                     ORDER BY `{propertyName}`
                                     LIMIT {Globals.MaxPageSize}")).ToDictionary();
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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.MySQL);
            string sqlAggProps = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}(`{x.Name}`) as '{x.Alias}' "));
            string sqlAggPropsOrder = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}(`{x.Name}`) {(sortAsc ? "ASC" : "DESC")} "));

            var groupByProps = (group?.Properties?.Any() ?? false) ? $", + {string.Join(",", group.Properties.Select(x => $"{GetGroupExpression(x)} AS {SqlUtilis.QuoteIdentifier(x.Alias, DatabaseType.MySQL)}"))}" : string.Empty;
            var groupByGroup = (group?.Properties?.Any() ?? false) ? $"GROUP BY {string.Join(",", group.Properties.Select(x => GetGroupExpression(x)))}" : string.Empty;

            // Groups with equal aggregates are ordered by the group values, so that the pages do not overlap
            var groupByOrder = (group?.Properties?.Any() ?? false) ? $", {string.Join(",", group.Properties.Select(x => GetGroupExpression(x)))}" : string.Empty;

            var dtResult = await ExecTableAsync($@"SELECT {sqlAggProps}
                                        {groupByProps}
                                        FROM `{entityName}`
                                        {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}
                                        {groupByGroup}
                                        ORDER BY {sqlAggPropsOrder}{groupByOrder}
                                        LIMIT {SqlUtilis.GetPagingOffset(pageIndex, pageSize)},{pageSize} ");

            return dtResult.ToDictionary();
        }

        private static string GetGroupExpression(GroupData.GroupProperty property)
        {
            // In UTC and as integers, like the other databases: FROM_UNIXTIME answers in the session time zone
            // (the server's), so the date is counted from the epoch instead. A NULL date stays in the NULL group.
            var utcDate = $"DATE_ADD(CAST('1970-01-01' AS DATETIME), INTERVAL FLOOR(`{property.Name}` / 1000) SECOND)";

            return property.Type switch
            {
                GroupData.GroupByType.Date_Year => $" EXTRACT(YEAR FROM {utcDate}) ",
                GroupData.GroupByType.Date_Month => $" EXTRACT(MONTH FROM {utcDate}) ",
                GroupData.GroupByType.Date_Day => $" EXTRACT(DAY FROM {utcDate}) ",
                GroupData.GroupByType.Date_Hour => $" EXTRACT(HOUR FROM {utcDate}) ",
                GroupData.GroupByType.Date_Minute => $" EXTRACT(MINUTE FROM {utcDate}) ",
                GroupData.GroupByType.Date_Second => $" EXTRACT(SECOND FROM {utcDate}) ",
                _ => $"`{property.Name}`",
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
                return longVal.ToString(CultureInfo.InvariantCulture);
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
                return decimalVal.ToString(CultureInfo.InvariantCulture);
            }

            // All the rest
            return $"'{Utils.GetString(val)
                .Replace("'", "''")
                .Replace("\\", "\\\\")
                .Trim()}'";
        }
    }
}
