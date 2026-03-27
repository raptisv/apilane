using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Data.Extensions;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Data.Repository
{
    public class PostgreSQLDataStorageRepository : IDataStorageRepository
    {
        private string _connectionString;
        private NpgsqlConnection _databaseConnection = null!;

        public PostgreSQLDataStorageRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private void TryOpenConnection()
        {
            _databaseConnection ??= new NpgsqlConnection(_connectionString);

            if (_databaseConnection is not null &&
                _databaseConnection.State != ConnectionState.Open)
            {
                _databaseConnection.Open();
            }
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
                command.CommandTimeout = 0;
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
                command.CommandTimeout = 0;
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
                command.CommandTimeout = 0;
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
                command.CommandTimeout = 0;
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
                string.IsNullOrWhiteSpace(sort) ? $"ORDER BY \"{primaryKey}\"" : $"ORDER BY {sort}",
                (pageSize <= 0 || pageIndex <= 0) ? string.Empty : $"LIMIT {pageSize} OFFSET (({pageIndex} - 1) * {pageSize})");

            return await ExecTableAsync(cmdData);
        }

        public static void ConfirmDatabaseExists(string connString)
        {
            // Just test if database exists
            new PostgreSQLDataStorageRepository(connString).TryOpenConnection();
        }

        public Task CreateTableWithPrimaryKeyAsync(string tableName)
        {
            return ExecNQAsync($@"CREATE TABLE ""{tableName}""
                                (
                                    ""{Globals.PrimaryKeyColumn}"" BIGSERIAL NOT NULL PRIMARY KEY
                                );");
        }

        public Task RenameTableAsync(string oldTableName, string newTableName)
        {
            return ExecNQAsync($@"ALTER TABLE ""{oldTableName}"" RENAME TO ""{newTableName}"";");
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
                numDecimalPlaces,
                strMaxLength);

            return ExecNQAsync($@"ALTER TABLE ""{tableName}"" ADD {columnCmd}");
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

            foreach (var currentConstraint in currentForeignKeyConstraints)
            {
                if (!incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", currentConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // The constraint is deleted, remove it from database
                    var constrainName = $"FOREIGN_KEY_{entityName}_{string.Join("_", currentConstraint.GetForeignKeyPropertiesAsList())}";
                    constrainName = constrainName.Length > 63 ? constrainName.Substring(0, 63) : constrainName;

                    finalCmd += $@"ALTER TABLE ""{entityName}"" DROP CONSTRAINT IF EXISTS {constrainName};";
                }
            }

            foreach (var incomingConstraint in incomingForeignKeyConstraints)
            {
                if (!currentForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", incomingConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = $"FOREIGN_KEY_{entityName}_{string.Join("_", incomingConstraint.GetForeignKeyPropertiesAsList())}";
                    constrainName = constrainName.Length > 63 ? constrainName.Substring(0, 63) : constrainName;

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

            foreach (var currentConstraint in currentUniqueConstraints)
            {
                if (!incomingUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", currentConstraint.GetUniqueProperties()))))
                {
                    // The constraint is deleted, remove it from database
                    var constrainName = $"UNIQUE_{entityName}_{string.Join("_", currentConstraint.GetUniqueProperties())}";
                    constrainName = constrainName.Length > 63 ? constrainName.Substring(0, 63) : constrainName;

                    finalCmd += $@"ALTER TABLE ""{entityName}"" DROP CONSTRAINT IF EXISTS {constrainName};";
                }
            }

            foreach (var incomingConstraint in incomingUniqueConstraints)
            {
                if (!currentUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", incomingConstraint.GetUniqueProperties()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = $"UNIQUE_{entityName}_{string.Join("_", incomingConstraint.GetUniqueProperties())}";
                    constrainName = constrainName.Length > 63 ? constrainName.Substring(0, 63) : constrainName;

                    var properties = string.Join(",", incomingConstraint.GetUniqueProperties().Select(x => $"\"{x}\""));

                    finalCmd += $@"ALTER TABLE ""{entityName}"" ADD CONSTRAINT {constrainName} UNIQUE ({properties});";
                }
            }

            return finalCmd;
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

            return (await ExecTableAsync($@"SELECT DISTINCT ""{propertyName}""
                                         FROM ""{entityName}"" {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}")).ToDictionary();
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

            var groupByProps = (group?.Properties?.Any() ?? false) ? $", {string.Join(",", group.Properties.Select(x => GetProperty(x).Query + GetProperty(x).Alias))}" : string.Empty;
            var groupByGroup = (group?.Properties?.Any() ?? false) ? $"GROUP BY {string.Join(",", group.Properties.Select(x => GetProperty(x).Query))}" : string.Empty;

            var dtResult = await ExecTableAsync($@"SELECT {sqlAggProps}
                                        {groupByProps}
                                        FROM ""{entityName}""
                                        {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}
                                        {groupByGroup}
                                        ORDER BY {sqlAggPropsOrder}
                                        LIMIT {pageSize} OFFSET (({pageIndex} - 1) * {pageSize})");

            return dtResult.ToDictionary();
        }

        private static (string Query, string Alias) GetProperty(GroupData.GroupProperty property)
        {
            return property.Type switch
            {
                GroupData.GroupByType.Date_Year => ($" EXTRACT(YEAR FROM to_timestamp(\"{property.Name}\" / 1000.0)) ", $" AS \"{property.Alias}\""),
                GroupData.GroupByType.Date_Month => ($" EXTRACT(MONTH FROM to_timestamp(\"{property.Name}\" / 1000.0)) ", $" AS \"{property.Alias}\""),
                GroupData.GroupByType.Date_Day => ($" EXTRACT(DAY FROM to_timestamp(\"{property.Name}\" / 1000.0)) ", $" AS \"{property.Alias}\""),
                GroupData.GroupByType.Date_Hour => ($" EXTRACT(HOUR FROM to_timestamp(\"{property.Name}\" / 1000.0)) ", $" AS \"{property.Alias}\""),
                GroupData.GroupByType.Date_Minute => ($" EXTRACT(MINUTE FROM to_timestamp(\"{property.Name}\" / 1000.0)) ", $" AS \"{property.Alias}\""),
                GroupData.GroupByType.Date_Second => ($" EXTRACT(SECOND FROM to_timestamp(\"{property.Name}\" / 1000.0)) ", $" AS \"{property.Alias}\""),
                _ => ($"\"{property.Name}\"", $" AS \"{property.Alias}\""),
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
                return boolVal ? "TRUE" : "FALSE";
            }

            if (val is JsonElement jsonElement &&
                (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False))
            {
                return jsonElement.ValueKind == JsonValueKind.True ? "TRUE" : "FALSE";
            }

            if (val is decimal decimalVal)
            {
                return decimalVal.ToString().Replace(",", ".");
            }

            // All the rest
            return $"'{Utils.GetString(val)
                .Replace("'", "''")
                .Trim()}'";
        }
    }
}
