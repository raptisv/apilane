using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Data.Abstractions;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Data.Repository.Factory
{
    public class ApplicationDataStoreFactory : IApplicationDataStoreFactory
    {
        private IDataStorageRepository? _dataStore = null;
        private readonly ApplicationDbInfoDto? _applicationDbInfo;
        private readonly Lazy<ValueTask<ApplicationDbInfoDto>>? _loadApplicationTask;
        private readonly Tracer? _tracer;

        public ApplicationDataStoreFactory(
            Lazy<ValueTask<ApplicationDbInfoDto>> loadApplicationTask,
            Tracer? tracer = null)
        {
            _loadApplicationTask = loadApplicationTask;
            _tracer = tracer;
        }

        public ApplicationDataStoreFactory(
            ApplicationDbInfoDto applicationDbInfo,
            Tracer? tracer = null)
        {
            _applicationDbInfo = applicationDbInfo;
            _tracer = tracer;
        }

        public async Task<IDataStorageRepository> CurrentDataStoreAsync()
        {
            if (_dataStore is null)
            {
                // Look for direct assignment
                var applicationDbInfo = _applicationDbInfo;

                // If not direct value provided, fetch value from lazy task
                if (applicationDbInfo is null && _loadApplicationTask is not null)
                {
                    applicationDbInfo = await _loadApplicationTask.Value;
                }

                // One of the two should have been provided, else throw
                if (applicationDbInfo is null)
                {
                    throw new Exception("No application DB info provided");
                }

                // Initialize the datastore
                _dataStore = applicationDbInfo.DatabaseType switch
                {
                    DatabaseType.SQLServer => new SQLServerDataStorageRepository(applicationDbInfo.ConnectionString),
                    DatabaseType.MySQL => new MySQLDataStorageRepository(applicationDbInfo.ConnectionString),
                    DatabaseType.SQLLite => new SQLiteDataStorageRepository(applicationDbInfo.ConnectionString),
                    _ => throw new NotImplementedException(),
                };
            }

            return _dataStore;
        }

        public async ValueTask DisposeAsync()
            => await (await CurrentDataStoreAsync()).DisposeAsync();

        public void Dispose()
            => CurrentDataStoreAsync().GetAwaiter().GetResult().Dispose();

        #region Schema

        public async Task CreateTableWithPrimaryKeyAsync(string tableName)
        {
            using var trace = _tracer?.StartActiveSpan("CreateTableWithPrimaryKeyAsync");
            await (await CurrentDataStoreAsync()).CreateTableWithPrimaryKeyAsync(tableName);
        }
        public async Task<bool> ExistsTableAsync(string tableName)
        {
            using var trace = _tracer?.StartActiveSpan("ExistsTableAsync");
            return await (await CurrentDataStoreAsync()).ExistsTableAsync(tableName);
        }

        public async Task DropTableAsync(string tableName)
        {
            using var trace = _tracer?.StartActiveSpan("DropTableAsync");
            await (await CurrentDataStoreAsync()).DropTableAsync(tableName);
        }

        public async Task<bool> ExistsColumnAsync(string tableName, string columnName)
        {
            using var trace = _tracer?.StartActiveSpan("ExistsColumnAsync");
            return await (await CurrentDataStoreAsync()).ExistsColumnAsync(tableName, columnName);
        }

        public async Task RenameTableAsync(string oldTableName, string newTableName)
        {
            using var trace = _tracer?.StartActiveSpan("RenameTableAsync");
            await (await CurrentDataStoreAsync()).RenameTableAsync(oldTableName, newTableName);
        }

        public async Task CreateColumnAsync(string tableName, string columnName, PropertyType type, bool notNull, int? numDecimalPlaces, long? strMaxLength)
        {
            using var trace = _tracer?.StartActiveSpan("CreateColumnAsync");
            await (await CurrentDataStoreAsync()).CreateColumnAsync(tableName, columnName, type, notNull, numDecimalPlaces, strMaxLength);
        }

        public async Task DropColumnAsync(string tableName, string columnName)
        {
            using var trace = _tracer?.StartActiveSpan("DropColumnAsync");
            await (await CurrentDataStoreAsync()).DropColumnAsync(tableName, columnName);
        }

        public async Task RenameColumnAsync(string tableName, string oldColumnName, string newColumnName)
        {
            using var trace = _tracer?.StartActiveSpan("RenameColumnAsync");
            await (await CurrentDataStoreAsync()).RenameColumnAsync(tableName, oldColumnName, newColumnName);
        }

        public async Task SetConstraintsAsync(string tableName, List<(string Name, bool IsPrimaryKey, PropertyType Type, bool NotNull, int? NumDecimalPlaces)> tableColumns, List<EntityConstraint> incomingConstraints, List<EntityConstraint> currentConstraints)
        {
            using var trace = _tracer?.StartActiveSpan("SetConstraintsAsync");
            await (await CurrentDataStoreAsync()).SetConstraintsAsync(tableName, tableColumns, incomingConstraints, currentConstraints);
        }

        #endregion

        #region Data

        public async Task<Dictionary<string, object?>?> GetDataByIdAsync(string entityName, long id, List<string>? entityProperties)
        {
            using var trace = _tracer?.StartActiveSpan("GetDataByIdAsync");
            trace?.SetAttribute("entity", entityName);

            return await (await CurrentDataStoreAsync()).GetDataByIdAsync(entityName, id, entityProperties);
        }

        public async Task<long> GetDataCountAsync(string entityName, FilterData? filter)
        {
            using var trace = _tracer?.StartActiveSpan("GetDataCountAsync");
            trace?.SetAttribute("entity", entityName);

            return await (await CurrentDataStoreAsync()).GetDataCountAsync(entityName, filter);
        }

        public async Task<long?> CreateDataAsync(string entityName, Dictionary<string, object?> propertyValues, bool allowInsertIdentity)
        {
            using var trace = _tracer?.StartActiveSpan("CreateDataAsync");
            trace?.SetAttribute("entity", entityName);

            return await(await CurrentDataStoreAsync()).CreateDataAsync(entityName, propertyValues, allowInsertIdentity);
        }

        public async Task<List<Dictionary<string, object?>>> GetPagedDataAsync(string entityName, List<string>? entityProperties, FilterData? filter, List<SortData>? sort, int pageIndex, int pageSize)
        {
            using var trace = _tracer?.StartActiveSpan("GetPagedDataAsync");
            trace?.SetAttribute("entity", entityName);

            return await (await CurrentDataStoreAsync()).GetPagedDataAsync(entityName, entityProperties, filter, sort, pageIndex, pageSize);
        }

        public async Task<long> DeleteDataAsync(string entityName, FilterData? filter)
        {
            using var trace = _tracer?.StartActiveSpan("DeleteDataAsync");
            trace?.SetAttribute("entity", entityName);

            return await (await CurrentDataStoreAsync()).DeleteDataAsync(entityName, filter);
        }

        public async Task<long> UpdateDataAsync(string entityName, Dictionary<string, object?> propertyValues, FilterData? filter)
        {
            using var trace = _tracer?.StartActiveSpan("UpdateDataAsync");
            trace?.SetAttribute("entity", entityName);

            return await (await CurrentDataStoreAsync()).UpdateDataAsync(entityName, propertyValues, filter);
        }

        public async Task<List<List<Dictionary<string, object?>>>> ExecuteCustomAsync(string command)
        {
            using var trace = _tracer?.StartActiveSpan("ExecuteCustomAsync");
            trace?.SetAttribute("command", command);

            return await (await CurrentDataStoreAsync()).ExecuteCustomAsync(command);
        }

        public async Task<List<Dictionary<string, object?>>> DistinctDataAsync(string entityName, string propertyName, FilterData? filter)
        {
            using var trace = _tracer?.StartActiveSpan("DistinctDataAsync");
            trace?.SetAttribute("entity", entityName);

            return await (await CurrentDataStoreAsync()).DistinctDataAsync(entityName, propertyName, filter);
        }

        public async Task<List<Dictionary<string, object?>>> AggregateDataAsync(string entityName, AggregateData aggregates, FilterData? filter, GroupData? group, bool sortAsc, int pageIndex, int pageSize)
        {
            using var trace = _tracer?.StartActiveSpan("AggregateDataAsync");
            trace?.SetAttribute("entity", entityName);

            return await (await CurrentDataStoreAsync()).AggregateDataAsync(entityName, aggregates, filter, group, sortAsc, pageIndex, pageSize);
        }

        #endregion
    }
}
