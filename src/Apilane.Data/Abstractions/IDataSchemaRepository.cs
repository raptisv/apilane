using Apilane.Common.Enums;
using Apilane.Common.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Data.Abstractions
{
    public interface IDataSchemaRepository : IAsyncDisposable
    {
        Task CreateTableWithPrimaryKeyAsync(string tableName);
        Task RenameTableAsync(string oldTableName, string newTableName);
        Task DropTableAsync(string tableName);
        Task<bool> ExistsTableAsync(string tableName);
        Task<bool> ExistsColumnAsync(string tableName, string columnName);
        Task CreateColumnAsync(string tableName, string columnName, PropertyType type, bool notNull, int? numDecimalPlaces, long? strMaxLength);
        Task DropColumnAsync(string tableName, string columnName);
        Task RenameColumnAsync(string tableName, string oldColumnName, string newColumnName);
        Task SetConstraintsAsync(string tableName, List<(string Name, bool IsPrimaryKey, PropertyType Type, bool NotNull, int? NumDecimalPlaces)> tableColumns, List<EntityConstraint> incomingConstraints, List<EntityConstraint> currentConstraints);
    }
}
