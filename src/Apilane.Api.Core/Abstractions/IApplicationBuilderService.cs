using Apilane.Common.Enums;
using Apilane.Common.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface IApplicationBuilderService
    {
        Task BuildApplicationAsync(DBWS_Application application);
        Task DegenerateEntityAsync(DBWS_Application application, string entityName);
        Task DegeneratePropertyAsync(DBWS_Application application, long propertyId);
        ValueTask DisposeAsync();
        Task DropApplicationDataAsync(DBWS_Application application, DatabaseType databaseType, string connectionString);
        Task GenerateConstraintsAsync(DBWS_Entity entity, List<EntityConstraint> incomingConstraints, List<EntityConstraint> currentConstraints);
        Task<List<long>> ImportDataAsync(DBWS_Application application, DBWS_Entity entity, List<Dictionary<string, object?>> data);
        Task GenerateEntityAsync(DBWS_Application application, DBWS_Entity entity);
        Task GeneratePropertyAsync(DatabaseType databaseType, string entityName, DBWS_EntityProperty property);
        Task RenameEntityAsync(DBWS_Application application, long entityID, string newName);
        Task RenameEntityPropertyAsync(DBWS_Application application, long propertyID, string newName);
    }
}
