using Apilane.Common.Enums;
using Apilane.Common.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface IApplicationAPI
    {
        Task DegenerateAsync(DBWS_Application application, DatabaseType databaseType, string connectionString);
        Task DegenerateEntityAsync(DBWS_Application application, string entityName);
        Task DegeneratePropertyAsync(DBWS_Application application, long propertyID);
        Task GenerateConstraintsAsync(DBWS_Application application, List<EntityConstraint> incomingConstraints, string entityName);
        Task<List<long>> ImportDataAsync(DBWS_Application application, List<Dictionary<string, object?>> data, string entityName);
        Task GenerateEntityAsync(DBWS_Application application, DBWS_Entity entity);
        Task GeneratePropertyAsync(string appToken, DatabaseType databaseType, DBWS_EntityProperty property, string entityName);
        double GetStorageUsedInMB(string appToken, DatabaseType databaseType);
        Task RebuildAsync(DBWS_Application application);
        Task RenameEntityAsync(DBWS_Application application, long entityID, string newName);
        Task RenameEntityPropertyAsync(DBWS_Application application, long propertyID, string newName);
        Task ResetAppAsync(string appToken);
    }
}
