using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Data.Extensions;
using Apilane.Data.Utilities;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Data.Repository
{
    public class PostgreSQLDataStorageRepository : IDataStorageRepository
    {
        // The longest identifier PostgreSQL keeps (NAMEDATALEN - 1): it cuts longer ones
        private const int MaxIdentifierLength = 63;

        private string _connectionString;
        private NpgsqlConnection _databaseConnection = null!;

        public PostgreSQLDataStorageRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private void OpenConnection()
        {
            _databaseConnection ??= new NpgsqlConnection(_connectionString);

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
            var dbProviderFactory = DatabaseType.PostgreSQL.GetDbProviderFactory();
            return dbProviderFactory.CreateDataAdapter() ?? throw new Exception("Could not create data adapter");
        }

        public Task<int> ExecCountAsync(string tables, string primaryKey, string? filter = null, string? group = null)
        {
            TryOpenConnection();

            var cmdTotal = string.Format("SELECT COUNT(\"{0}\") FROM {1} {2} {3}",
                primaryKey,
                tables,
                string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {Utils.GetString(filter)}",
                string.IsNullOrWhiteSpace(group) ? string.Empty : "GROUP BY " + Utils.GetString(group));

            using (var command = _databaseConnection.CreateCommand())
            {
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
                string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {filter}",
                string.IsNullOrWhiteSpace(group) ? string.Empty : $"GROUP BY {group}",
                SqlUtilis.GetPagingOrderBy(sort, $"\"{primaryKey}\""),
                (pageSize <= 0 || pageIndex <= 0) ? string.Empty : $"LIMIT {pageSize} OFFSET {SqlUtilis.GetPagingOffset(pageIndex, pageSize)}");

            return await ExecTableAsync(cmdData);
        }

        public static void ConfirmDatabaseExists(string connString)
        {
            // Just test if database exists. Dispose, otherwise the connection never returns to the pool.
            // Call it outside a TransactionScope: a connection opened inside one joins it, and a second
            // connection in the same transaction is not supported.
            using (var repository = new PostgreSQLDataStorageRepository(connString))
            {
                repository.OpenConnection();
            }
        }

        public Task CreateTableWithPrimaryKeyAsync(string tableName)
        {
            return ExecNQAsync($@"CREATE TABLE ""{tableName}""
                                (
                                    ""{Globals.PrimaryKeyColumn}"" BIGSERIAL NOT NULL PRIMARY KEY
                                );");
        }

        public Task RenameTableAsync(string oldTableName, string newTableName, List<EntityConstraint> constraints)
        {
            var cmd = $@"ALTER TABLE ""{oldTableName}"" RENAME TO ""{newTableName}"";";

            // The constraint names carry the entity name, and a later save of the constraints drops and
            // creates them by that name: rename them with the table. A constraint is renamed only when it
            // exists and its new name is free (the index of a unique constraint is named per schema), so
            // the errors for those two cases are ignored. A long name can be in the database as it was cut
            // before names got a hash: the hashed name is tried first, then the cut one.
            foreach (var constraint in constraints)
            {
                string oldFullName;
                string newFullName;

                if (constraint.TypeID == (int)ConstraintType.ForeignKey)
                {
                    oldFullName = GetForeignKeyConstraintFullName(oldTableName, constraint);
                    newFullName = GetForeignKeyConstraintFullName(newTableName, constraint);
                }
                else if (constraint.TypeID == (int)ConstraintType.Unique && constraint.GetUniqueProperties().Count > 0)
                {
                    oldFullName = GetUniqueConstraintFullName(oldTableName, constraint);
                    newFullName = GetUniqueConstraintFullName(newTableName, constraint);
                }
                else
                {
                    continue;
                }

                var newName = GetConstraintName(newFullName);

                foreach (var oldName in new[] { GetConstraintName(oldFullName), GetLegacyConstraintName(oldFullName) }.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    cmd += $@"DO $$
                              BEGIN
                                  ALTER TABLE ""{newTableName}"" RENAME CONSTRAINT {oldName} TO {newName};
                              EXCEPTION WHEN undefined_object OR duplicate_object OR duplicate_table THEN
                                  NULL;
                              END $$;";
                }
            }

            return ExecNQAsync(cmd);
        }

        public Task DropTableAsync(string tableName)
        {
            return ExecNQAsync($@"DROP TABLE ""{tableName}"";");
        }

        public async Task<bool> ExistsTableAsync(string tableName)
        {
            var result = await ExecScalarAsync(
                $@"SELECT COUNT(*) FROM information_schema.tables
                   WHERE table_schema = current_schema()
                   AND table_name = '{tableName.Replace("'", "''")}';");

            return Utils.GetLong(result, 0) > 0;
        }

        public async Task<bool> ExistsColumnAsync(string tableName, string columnName)
        {
            var result = await ExecScalarAsync(
                $@"SELECT COUNT(*) FROM information_schema.columns
                   WHERE table_schema = current_schema()
                   AND table_name = '{tableName.Replace("'", "''")}' 
                   AND column_name = '{columnName.Replace("'", "''")}';");

            return Utils.GetLong(result, 0) > 0;
        }

        public Task CreateColumnsAsync(
            string tableName,
            List<(string Name, PropertyType Type, bool NotNull, int? NumDecimalPlaces, long? StrMaxLength)> columns)
        {
            if (columns.Count == 0)
            {
                return Task.CompletedTask;
            }

            return ExecNQAsync($@"ALTER TABLE ""{tableName}"" {string.Join(", ", columns.Select(x =>
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
                        return $"\"{columnName}\" BIGINT {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                    }
                    else
                    {
                        return $"\"{columnName}\" NUMERIC(18,{numDecimalPlaces.Value}) {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                    }
                case PropertyType.Boolean:
                    return $"\"{columnName}\" BOOLEAN {(notNull ? "NOT NULL DEFAULT FALSE" : "NULL")}";
                case PropertyType.Date:
                    return $"\"{columnName}\" BIGINT {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                case PropertyType.String:
                    {
                        if (strMaxLength is null)
                        {
                            return $"\"{columnName}\" TEXT {(notNull ? "NOT NULL DEFAULT ''" : "NULL")}";
                        }

                        if (strMaxLength.Value > 10_485_760)
                        {
                            throw new Exception("Max length can be up to 10.485.760 characters");
                        }

                        return $"\"{columnName}\" VARCHAR({strMaxLength.Value}) {(notNull ? $"NOT NULL DEFAULT ''" : "NULL")}";
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public Task DropColumnAsync(string tableName, string columnName)
        {
            return ExecNQAsync($@"ALTER TABLE ""{tableName}"" DROP COLUMN ""{columnName}"";");
        }

        public Task RenameColumnAsync(string tableName, string oldColumnName, string newColumnName)
        {
            return ExecNQAsync($@"ALTER TABLE ""{tableName}"" RENAME COLUMN ""{oldColumnName}"" TO ""{newColumnName}"";");
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
            // IMPORTANT! For PostgreSQL constraint names are limited to 63 bytes.

            var finalCmd = string.Empty;

            incomingForeignKeyConstraints = incomingForeignKeyConstraints.DistinctBy(x => string.Join("_", x.GetForeignKeyPropertiesAsList())).ToList();

            var keptFullNames = currentForeignKeyConstraints
                .Where(c => incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", c.GetForeignKeyPropertiesAsList()))))
                .Select(c => GetForeignKeyConstraintFullName(entityName, c))
                .ToList();

            foreach (var currentConstraint in currentForeignKeyConstraints)
            {
                if (!incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", currentConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // The constraint is deleted, remove it from database
                    foreach (var constrainName in GetConstraintNamesToDrop(GetForeignKeyConstraintFullName(entityName, currentConstraint), keptFullNames))
                    {
                        finalCmd += $@"ALTER TABLE ""{entityName}"" DROP CONSTRAINT IF EXISTS {constrainName};";
                    }
                }
            }

            foreach (var incomingConstraint in incomingForeignKeyConstraints)
            {
                if (!currentForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", incomingConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = GetConstraintName(GetForeignKeyConstraintFullName(entityName, incomingConstraint));

                    var properties = incomingConstraint.GetForeignKeyProperties();

                    finalCmd += $@"ALTER TABLE ""{entityName}"" ADD CONSTRAINT {constrainName} FOREIGN KEY (""{properties.Property}"") REFERENCES ""{properties.FKEntity}""(""ID"") {GetFKLogic(properties.FKLogic)};";
                }
            }

            return finalCmd;
        }

        private static string GetUniqueConstraintsCommand(
           string entityName,
           IEnumerable<EntityConstraint> incomingUniqueConstraints,
           IEnumerable<EntityConstraint> currentUniqueConstraints)
        {
            // IMPORTANT! For PostgreSQL constraint names are limited to 63 bytes.

            var finalCmd = string.Empty;

            incomingUniqueConstraints = incomingUniqueConstraints.DistinctBy(x => string.Join("_", x.GetUniqueProperties())).ToList();

            var keptFullNames = currentUniqueConstraints
                .Where(c => incomingUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", c.GetUniqueProperties()))))
                .Select(c => GetUniqueConstraintFullName(entityName, c))
                .ToList();

            foreach (var currentConstraint in currentUniqueConstraints)
            {
                if (!incomingUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", currentConstraint.GetUniqueProperties()))))
                {
                    // The constraint is deleted, remove it from database
                    foreach (var constrainName in GetConstraintNamesToDrop(GetUniqueConstraintFullName(entityName, currentConstraint), keptFullNames))
                    {
                        finalCmd += $@"ALTER TABLE ""{entityName}"" DROP CONSTRAINT IF EXISTS {constrainName};";
                    }
                }
            }

            foreach (var incomingConstraint in incomingUniqueConstraints)
            {
                if (!currentUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", incomingConstraint.GetUniqueProperties()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = GetConstraintName(GetUniqueConstraintFullName(entityName, incomingConstraint));

                    var properties = string.Join(",", incomingConstraint.GetUniqueProperties().Select(x => $"\"{x}\""));

                    finalCmd += $@"ALTER TABLE ""{entityName}"" ADD CONSTRAINT {constrainName} UNIQUE ({properties});";
                }
            }

            return finalCmd;
        }

        private static string GetForeignKeyConstraintFullName(string entityName, EntityConstraint constraint)
        {
            return $"FOREIGN_KEY_{entityName}_{string.Join("_", constraint.GetForeignKeyPropertiesAsList())}";
        }

        private static string GetUniqueConstraintFullName(string entityName, EntityConstraint constraint)
        {
            return $"UNIQUE_{entityName}_{string.Join("_", constraint.GetUniqueProperties())}";
        }

        /// <summary>
        /// The name of a constraint in the database: names are limited to 63 bytes, so a longer one keeps its
        /// start and ends with a hash of the full name. Only cut, two long names that start alike would be one
        /// name: the second constraint could not be added, and removing either would drop the other.
        /// </summary>
        private static string GetConstraintName(string fullName)
        {
            if (fullName.Length <= MaxIdentifierLength)
            {
                return fullName;
            }

            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullName))).Substring(0, 8).ToLowerInvariant();

            return $"{fullName.Substring(0, MaxIdentifierLength - hash.Length - 1)}_{hash}";
        }

        /// <summary>
        /// A long name as it was cut before names got a hash: existing databases still have such constraints.
        /// </summary>
        private static string GetLegacyConstraintName(string fullName)
        {
            return fullName.Length > MaxIdentifierLength ? fullName.Substring(0, MaxIdentifierLength) : fullName;
        }

        /// <summary>
        /// The names to drop a constraint by: its name and, for a long name, the cut name it had before names
        /// got a hash. The cut name is left alone when a constraint that stays has the same one: the
        /// constraint in the database under it may be that one.
        /// </summary>
        private static IEnumerable<string> GetConstraintNamesToDrop(string fullName, List<string> keptFullNames)
        {
            var name = GetConstraintName(fullName);
            var legacyName = GetLegacyConstraintName(fullName);

            yield return name;

            if (!legacyName.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                !keptFullNames.Any(x => GetLegacyConstraintName(x).Equals(legacyName, StringComparison.OrdinalIgnoreCase)))
            {
                yield return legacyName;
            }
        }

        private static string GetFKLogic(ForeignKeyLogic logic) => logic switch
        {
            ForeignKeyLogic.ON_DELETE_NO_ACTION => "ON DELETE NO ACTION ON UPDATE NO ACTION",
            ForeignKeyLogic.ON_DELETE_SET_NULL => "ON DELETE SET NULL",
            _ => "ON DELETE CASCADE"
        };

        public async Task<Dictionary<string, object?>?> GetDataByIdAsync(
            string entityName,
            long id,
            List<string>? entityProperties)
        {
            var result = await ExecPagingAsync($"\"{entityName}\"", Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"\"{x}\"")),
                filter: $" \"{entityName}\".\"{Globals.PrimaryKeyColumn}\" = {id}",
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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.PostgreSQL);
            string? strSort = sort?.ToSqlExpression(DatabaseType.PostgreSQL);

            var resultData = await ExecPagingAsync($"\"{entityName}\"", Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"\"{x}\"")),
                pageIndex: pageIndex, pageSize: pageSize, filter: strFilter, sort: strSort);

            return resultData.ToDictionary();
        }

        public async Task<long> GetDataCountAsync(string entityName, FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.PostgreSQL);

            return await ExecCountAsync(
                $"\"{entityName}\"",
                Globals.PrimaryKeyColumn,
                filter: strFilter);
        }

        public async Task<long?> CreateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            bool allowInsertIdentity)
        {
            var insertCmd = $@" INSERT INTO ""{entityName}""
                                ({string.Join(",", propertiesValues.Select(x => $"\"{x.Key}\""))})
                                VALUES
                                ({string.Join(",", propertiesValues.Select(x => $"{GetSqlValue(x.Value)}"))})
                                RETURNING ""{Globals.PrimaryKeyColumn}"";";

            var result = await ExecScalarAsync(insertCmd);

            return Utils.GetNullLong(result);
        }

        public async Task SyncIdSequenceAsync(string entityName)
        {
            // A BIGSERIAL does not move when a record is inserted with its ID, so the next record created
            // without one (from the sequence) would collide with an imported ID. Set the sequence to the
            // largest ID, but only when that is past it: never backwards (that would hand out the IDs of
            // deleted records again), and not on an empty table (MAX is NULL, the comparison is false).
            // pg_sequence_last_value is NULL while the sequence was never used (PostgreSQL 10 and later).
            // setval is not transactional: after a rolled back import the sequence only keeps a gap.
            var table = SqlUtilis.QuoteIdentifier(entityName, DatabaseType.PostgreSQL);
            var tableLiteral = table.Replace("'", "''");

            await ExecScalarAsync($@" SELECT setval(seq, max_id)
                                      FROM (SELECT pg_get_serial_sequence('{tableLiteral}', '{Globals.PrimaryKeyColumn}')::regclass AS seq,
                                                   (SELECT MAX(""{Globals.PrimaryKeyColumn}"") FROM {table}) AS max_id) s
                                      WHERE max_id > COALESCE(pg_sequence_last_value(seq), 0);");
        }

        public async Task<long> DeleteDataAsync(string entityName, FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.PostgreSQL);

            var result = await ExecNQAsync($@"DELETE FROM ""{entityName}""" +
                $" {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")};");

            return Utils.GetLong(result, 0);
        }

        public async Task<long> UpdateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.PostgreSQL);

            var insertCmd = $@" UPDATE ""{entityName}""
                                SET {string.Join(",", propertiesValues.Select(x => $"\"{x.Key}\" = {GetSqlValue(x.Value)}"))}
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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.PostgreSQL);

            // At most one page of values, like every other read, in a fixed order
            return (await ExecTableAsync($@"SELECT DISTINCT ""{propertyName}""
                                         FROM ""{entityName}"" {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}
                                         ORDER BY ""{propertyName}""
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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.PostgreSQL);
            string sqlAggProps = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}(\"{x.Name}\") AS \"{x.Alias}\" "));
            string sqlAggPropsOrder = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}(\"{x.Name}\") {(sortAsc ? "ASC" : "DESC")} "));

            var groupByProps = (group?.Properties?.Any() ?? false) ? $", {string.Join(",", group.Properties.Select(x => $"{GetGroupExpression(x)} AS {SqlUtilis.QuoteIdentifier(x.Alias, DatabaseType.PostgreSQL)}"))}" : string.Empty;
            var groupByGroup = (group?.Properties?.Any() ?? false) ? $"GROUP BY {string.Join(",", group.Properties.Select(x => GetGroupExpression(x)))}" : string.Empty;

            // Groups with equal aggregates are ordered by the group values, so that the pages do not overlap
            var groupByOrder = (group?.Properties?.Any() ?? false) ? $", {string.Join(",", group.Properties.Select(x => GetGroupExpression(x)))}" : string.Empty;

            var dtResult = await ExecTableAsync($@"SELECT {sqlAggProps}
                                        {groupByProps}
                                        FROM ""{entityName}""
                                        {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}
                                        {groupByGroup}
                                        ORDER BY {sqlAggPropsOrder}{groupByOrder}
                                        LIMIT {pageSize} OFFSET {SqlUtilis.GetPagingOffset(pageIndex, pageSize)}");

            return dtResult.ToDictionary();
        }

        private static string GetGroupExpression(GroupData.GroupProperty property)
        {
            // In UTC and as integers, like the other databases: to_timestamp answers a timestamp with time zone,
            // whose parts are read in the session time zone (the server's), and the seconds have a fraction.
            var utcDate = $"(to_timestamp(\"{property.Name}\" / 1000.0) AT TIME ZONE 'UTC')";

            return property.Type switch
            {
                GroupData.GroupByType.Date_Year => $" CAST(EXTRACT(YEAR FROM {utcDate}) AS INTEGER) ",
                GroupData.GroupByType.Date_Month => $" CAST(EXTRACT(MONTH FROM {utcDate}) AS INTEGER) ",
                GroupData.GroupByType.Date_Day => $" CAST(EXTRACT(DAY FROM {utcDate}) AS INTEGER) ",
                GroupData.GroupByType.Date_Hour => $" CAST(EXTRACT(HOUR FROM {utcDate}) AS INTEGER) ",
                GroupData.GroupByType.Date_Minute => $" CAST(EXTRACT(MINUTE FROM {utcDate}) AS INTEGER) ",
                GroupData.GroupByType.Date_Second => $" CAST(FLOOR(EXTRACT(SECOND FROM {utcDate})) AS INTEGER) ",
                _ => $"\"{property.Name}\"",
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
                return boolVal ? "TRUE" : "FALSE";
            }

            if (val is JsonElement jsonElement &&
                (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False))
            {
                return jsonElement.ValueKind == JsonValueKind.True ? "TRUE" : "FALSE";
            }

            if (val is decimal decimalVal)
            {
                return decimalVal.ToString(CultureInfo.InvariantCulture);
            }

            // All the rest
            return $"'{Utils.GetString(val)
                .Replace("'", "''")
                .Trim()}'";
        }
    }
}
