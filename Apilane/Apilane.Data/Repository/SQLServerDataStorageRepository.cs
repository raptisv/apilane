﻿using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Data.Extensions;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Data.Repository
{
    public class SQLServerDataStorageRepository : IDataStorageRepository
    {
        private string _connectionString;

        private SqlConnection _databaseConnection = null!;

        public SQLServerDataStorageRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private void TryOpenConnection()
        {
            _databaseConnection ??= new SqlConnection(_connectionString);

            if (_databaseConnection is not null && _databaseConnection.State != ConnectionState.Open)
            {
                _databaseConnection.Open();
            }
        }

        public SqlConnection GetConnection()
        {
            return _databaseConnection;
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
            var dbProviderFactory = DatabaseType.SQLServer.GetDbProviderFactory();
            return dbProviderFactory.CreateDataAdapter() ?? throw new Exception("Could not create data adapter");
        }

        public Task<int> ExecCountAsync(string tables, string primaryKey, string? filter = null, string? group = null)
        {
            TryOpenConnection();

            // Total
            string cmdTotal = string.Format("SELECT COUNT({0}) FROM {1} {2} {3}",
                    Utils.GetString(primaryKey),
                    Utils.GetString(tables),
                    string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {Utils.GetString(filter)}",
                    string.IsNullOrWhiteSpace(Utils.GetString(group)) ? string.Empty : "GROUP BY " + Utils.GetString(group));

            using (DbCommand command = _databaseConnection.CreateCommand())
            {
                // Total
                command.CommandTimeout = 0;
                command.CommandType = CommandType.Text;
                command.CommandText = cmdTotal;
                using (DbDataReader reader = command.ExecuteReader())
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
                (string.IsNullOrWhiteSpace(filter) ? string.Empty : $"WHERE {filter}"),
                string.IsNullOrWhiteSpace(group) ? string.Empty : $"GROUP BY {group}",
                string.IsNullOrWhiteSpace(sort) ? $"ORDER BY [{primaryKey}]" : $"ORDER BY {sort}",
                (pageSize <= 0 || pageIndex <= 0) ? string.Empty : string.Format("OFFSET (({0} - 1) * {1}) ROWS FETCH NEXT {1} ROWS ONLY", pageIndex, pageSize));

            return await ExecTableAsync(cmdData);
        }

        public static void ConfirmDatabaseExists(string connString)
        {
            // Just test if database exists
            new SQLServerDataStorageRepository(connString).TryOpenConnection();
        }

        public Task CreateTableWithPrimaryKeyAsync(string tableName)
        {
            return ExecNQAsync($@"CREATE TABLE [{tableName}] 
                                ( 
                                    [{Globals.PrimaryKeyColumn}] BIGINT NOT NULL PRIMARY KEY IDENTITY(1,1)
                                );");
        }

        public Task RenameTableAsync(string oldTableName, string newTableName)
        {
            return ExecNQAsync($@"exec sp_rename '{oldTableName}', '{newTableName}';");
        }

        public Task DropTableAsync(string tableName)
        {
            return ExecNQAsync($"DROP TABLE [{tableName}];");
        }

        public async Task<bool> ExistsTableAsync(string tableName)
        {
            try
            {
                await ExecNQAsync($"SELECT TOP 1 * FROM {tableName};");
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
                await ExecNQAsync($"SELECT TOP 1 [{columnName}] FROM [{tableName}];");
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
                numDecimalPlaces,
                strMaxLength);

            return ExecNQAsync($@"ALTER TABLE [{tableName}] ADD {columnCmd}");
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
                        return $"[{columnName}] BIGINT {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                    }
                    else
                    {
                        return $"[{columnName}] DECIMAL(18,{numDecimalPlaces.Value}) {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                    }
                case PropertyType.Boolean:
                    return $"[{columnName}] BIT {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                case PropertyType.Date:
                    return $"[{columnName}] BIGINT {(notNull ? "NOT NULL DEFAULT 0" : "NULL")}";
                case PropertyType.String:
                    {
                        if (strMaxLength is null)
                        {
                            return $"[{columnName}] NVARCHAR(MAX) {(notNull ? "NOT NULL DEFAULT ''" : "NULL")}";
                        }

                        if (strMaxLength.Value > 4_000)
                        {
                            throw new Exception("Max length can be up to 4.000 characters");
                        }

                        return $"[{columnName}] NVARCHAR({strMaxLength.Value}) {(notNull ? "NOT NULL DEFAULT ''" : "NULL")}";
                    }
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
            return ExecNQAsync($@"EXEC sp_rename '{tableName + "." + oldColumnName}', '{newColumnName}', 'COLUMN';");
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
            // IMPORTANT! For SqlServer constraint names are trimed due to 128 chars limitation for identifiers.

            var finalCmd = string.Empty;

            incomingForeignKeyConstraints = incomingForeignKeyConstraints.DistinctBy(x => string.Join("_", x.GetForeignKeyPropertiesAsList())).ToList();

            foreach (var currentConstraint in currentForeignKeyConstraints)
            {
                if (!incomingForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", currentConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // The constraint is deleted, remove it from database
                    var constrainName = $"FOREIGN_KEY_{entityName}_{string.Join("_", currentConstraint.GetForeignKeyPropertiesAsList())}";
                    constrainName = constrainName.Length > 128 ? constrainName.Substring(0, 128) : constrainName;

                    finalCmd += $@"IF EXISTS(SELECT * FROM sys.objects Where Name = '{constrainName}')
                                   ALTER TABLE [{entityName}] DROP CONSTRAINT {constrainName};";
                }
            }

            foreach (var incomingConstraint in incomingForeignKeyConstraints)
            {
                if (!currentForeignKeyConstraints.Any(x => string.Join(",", x.GetForeignKeyPropertiesAsList()).Equals(string.Join(",", incomingConstraint.GetForeignKeyPropertiesAsList()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = $"FOREIGN_KEY_{entityName}_{string.Join("_", incomingConstraint.GetForeignKeyPropertiesAsList())}";
                    constrainName = constrainName.Length > 128 ? constrainName.Substring(0, 128) : constrainName;

                    var properties = incomingConstraint.GetForeignKeyProperties();

                    finalCmd += $@"IF NOT EXISTS(SELECT * FROM sys.objects Where Name = '{constrainName}')
                                   ALTER TABLE [{entityName}] ADD CONSTRAINT {constrainName} FOREIGN KEY ([{properties.Property}]) REFERENCES [{properties.FKEntity}]([ID]) {GetFKLogic(properties.FKLogic)};";
                }
            }

            return finalCmd;
        }

        private static string GetUniqueConstraintsCommand(
           string entityName,
           IEnumerable<EntityConstraint> incomingUniqueConstraints,
           IEnumerable<EntityConstraint> currentUniqueConstraints)
        {
            // IMPORTANT! For SqlServer constraint names are trimed due to 128 chars limitation for identifiers.

            var finalCmd = string.Empty;

            incomingUniqueConstraints = incomingUniqueConstraints.DistinctBy(x => string.Join("_", x.GetUniqueProperties())).ToList();

            foreach (var currentConstraint in currentUniqueConstraints)
            {
                if (!incomingUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", currentConstraint.GetUniqueProperties()))))
                {
                    // The constraint is deleted, remove it from database
                    var constrainName = $"UNIQUE_{entityName}_{string.Join("_", currentConstraint.GetUniqueProperties())}";
                    constrainName = constrainName.Length > 128 ? constrainName.Substring(0, 128) : constrainName;

                    finalCmd += $@"IF EXISTS(SELECT * FROM sys.objects Where Name = '{constrainName}')
                                   ALTER TABLE [{entityName}] DROP CONSTRAINT {constrainName};";
                }
            }

            foreach (var incomingConstraint in incomingUniqueConstraints)
            {
                if (!currentUniqueConstraints.Any(x => string.Join(",", x.GetUniqueProperties()).Equals(string.Join(",", incomingConstraint.GetUniqueProperties()))))
                {
                    // The constraint does not exist, create it
                    var constrainName = $"UNIQUE_{entityName}_{string.Join("_", incomingConstraint.GetUniqueProperties())}";
                    constrainName = constrainName.Length > 128 ? constrainName.Substring(0, 128) : constrainName;

                    var properties = string.Join(",", incomingConstraint.GetUniqueProperties().Select(x => $"[{x}]"));

                    finalCmd += $@"IF NOT EXISTS(SELECT * FROM sys.objects Where Name = '{constrainName}')
                                   ALTER TABLE [{entityName}] ADD CONSTRAINT {constrainName} UNIQUE ({properties});";
                }
            }

            return finalCmd;
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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLServer);
            string? strSort = sort?.ToSqlExpression(DatabaseType.SQLServer);

            var resultData = await ExecPagingAsync($"[{entityName}]", Globals.PrimaryKeyColumn,
                fields: entityProperties is null ? "*" : string.Join(",", entityProperties.Select(x => $"[{x}]")),
                pageIndex: pageIndex, pageSize: pageSize, filter: strFilter, sort: strSort);

            return resultData.ToDictionary();
        }

        public async Task<long> GetDataCountAsync(string entityName, FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLServer);

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

                                SELECT @@IDENTITY;";

            if (allowInsertIdentity)
            {
                await ExecNQAsync($"SET IDENTITY_INSERT [{entityName}] ON;");
            }

            var result = await ExecScalarAsync(insertCmd);

            if (allowInsertIdentity)
            {
                await ExecNQAsync($"SET IDENTITY_INSERT [{entityName}] OFF;");
            }

            return Utils.GetNullLong(result);
        }

        public async Task<long> DeleteDataAsync(string entityName, FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLServer);

            var result = await ExecNQAsync($"DELETE FROM [{entityName}] " +
                                               $"{(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")};");

            return Utils.GetLong(result, 0);
        }

        public async Task<long> UpdateDataAsync(
            string entityName,
            Dictionary<string, object?> propertiesValues,
            FilterData? filter)
        {
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLServer);

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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLServer);

            return (await ExecTableAsync($@"SELECT DISTINCT [{propertyName}] 
                                     FROM [{entityName}] {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")}")).ToDictionary();
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
            string? strFilter = filter?.ToSqlExpression(entityName, DatabaseType.SQLServer);
            string sqlAggProps = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}([{x.Name}]) as '{x.Alias}' "));
            string sqlAggPropsOrder = string.Join(",", aggregates.Properties.Select(x => $" {x.Aggregate}([{x.Name}]) {(sortAsc ? "ASC" : "DESC")} "));

            var groupByProps = (group?.Properties?.Any() ?? false) ? $", + {string.Join(",", group.Properties.Select(x => GetProperty(x).Query + x.Alias))}" : string.Empty;
            var groupByGroup = (group?.Properties?.Any() ?? false) ? $"GROUP BY {string.Join(",", group.Properties.Select(x => GetProperty(x).Query))}" : string.Empty;

            var dtResult = await ExecTableAsync($@"SELECT {sqlAggProps}
                                        {groupByProps} 
                                        FROM [{entityName}] 
                                        {(string.IsNullOrWhiteSpace(strFilter) ? string.Empty : $"WHERE {strFilter}")} 
                                        {groupByGroup} 
                                        ORDER BY {sqlAggPropsOrder}
                                        OFFSET (({pageIndex} - 1) * {pageSize}) ROWS FETCH NEXT {pageSize} ROWS ONLY");

            return dtResult.ToDictionary();
        }

        private static (string Query, string Alias) GetProperty(GroupData.GroupProperty property)
        {
            return property.Type switch
            {
                GroupData.GroupByType.Date_Year => ($" DATEPART(YEAR, dateadd(s, [{property.Name}]/1000, '1970-01-01')) ", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Month => ($" DATEPART(MONTH, dateadd(s, [{property.Name}]/1000, '1970-01-01')) ", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Day => ($" DATEPART(DAY, dateadd(s, [{property.Name}]/1000, '1970-01-01')) ", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Hour => ($" DATEPART(HOUR, dateadd(s, [{property.Name}]/1000, '1970-01-01')) ", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Minute => ($" DATEPART(MINUTE, dateadd(s, [{property.Name}]/1000, '1970-01-01')) ", $" AS {property.Alias}"),
                GroupData.GroupByType.Date_Second => ($" DATEPART(SECOND, dateadd(s, [{property.Name}]/1000, '1970-01-01')) ", $" AS {property.Alias}"),
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
