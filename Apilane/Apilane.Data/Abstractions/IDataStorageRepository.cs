using Apilane.Common.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Data.Abstractions
{
    public interface IDataStorageRepository : IDataSchemaRepository, IAsyncDisposable, IDisposable
    {
        Task<List<Dictionary<string, object?>>> AggregateDataAsync(string entityName, AggregateData aggregates, FilterData? filter, GroupData? group, bool sortAsc, int pageIndex, int pageSize);
        Task<List<Dictionary<string, object?>>> DistinctDataAsync(string entityName, string propertyName, FilterData? filter);
        Task<List<List<Dictionary<string, object?>>>> ExecuteCustomAsync(string command);
        Task<Dictionary<string, object?>?> GetDataByIdAsync(string entityName, long id, List<string>? entityProperties);
        Task<long> GetDataCountAsync(string entityName, FilterData? filter);
        Task<long> DeleteDataAsync(string entityName, FilterData? filter);
        Task<long?> CreateDataAsync(string entityName, Dictionary<string, object?> propertyValues, bool allowInsertIdentity);
        Task<long> UpdateDataAsync(string entityName, Dictionary<string, object?> propertyValues, FilterData? filter);
        Task<List<Dictionary<string, object?>>> GetPagedDataAsync(string entityName, List<string>? entityProperties, FilterData? filter, List<SortData>? sort, int pageIndex, int pageSize);
    }
}
