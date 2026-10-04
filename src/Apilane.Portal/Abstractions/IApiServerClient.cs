using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The calls the management API makes to an API server, as the signed-in user. Every failure is
    /// a <see cref="Api.PortalException"/>: VALIDATION (400) with the API server's own message and
    /// property when the API server answered 400, UPSTREAM_ERROR (502) for every other answer and
    /// when it cannot be reached. The clone routine (ICloneService) sends through IApiHttpService instead.
    /// </summary>
    public interface IApiServerClient
    {
        /// <summary>
        /// Makes the API server drop what it has cached about the application, so it reads the
        /// current definition from the Portal. The application must have its Server loaded.
        /// </summary>
        Task ClearCacheAsync(DBWS_Application application);

        /// <summary>
        /// The system entities a new application starts with, as the API server defines them.
        /// An empty <paramref name="differentiationEntity"/> means the application has none.
        /// </summary>
        Task<List<DBWS_Entity>> GetSystemEntitiesAsync(DBWS_Server server, string differentiationEntity);

        /// <summary>
        /// Creates the application on the API server: its database, its tables and its files folder.
        /// Sends the installation key of this instance.
        /// </summary>
        Task GenerateAsync(DBWS_Server server, DBWS_Application application);

        /// <summary>
        /// The properties and constraints a new entity of the application starts with, as the API
        /// server defines them. The application must have its Server loaded, as for every call below.
        /// </summary>
        Task<EntityPropertiesConstrainsDto> GetSystemPropertiesAndConstraintsAsync(DBWS_Application application, bool entityHasDifferentiationProperty);

        /// <summary>
        /// Creates the table of a new entity in the application's database.
        /// </summary>
        Task GenerateEntityAsync(DBWS_Application application, DBWS_Entity entity);

        /// <summary>
        /// Renames the table of an entity. <paramref name="entityId"/> is the entity's ID in the Portal.
        /// </summary>
        Task RenameEntityAsync(DBWS_Application application, long entityId, string newName);

        /// <summary>
        /// Drops the table of an entity, with all its records.
        /// </summary>
        Task DegenerateEntityAsync(DBWS_Application application, string entityName);

        /// <summary>
        /// Adds the column of a new property to the table of an entity.
        /// </summary>
        Task GeneratePropertyAsync(DBWS_Application application, string entityName, DBWS_EntityProperty property);

        /// <summary>
        /// Renames the column of a property. <paramref name="propertyId"/> is the property's ID in the Portal.
        /// </summary>
        Task RenameEntityPropertyAsync(DBWS_Application application, long propertyId, string newName);

        /// <summary>
        /// Drops the column of a property, with all its values. <paramref name="propertyId"/> is the property's ID in the Portal.
        /// </summary>
        Task DegeneratePropertyAsync(DBWS_Application application, long propertyId);

        /// <summary>
        /// Makes the table of an entity have exactly these constraints, system ones included:
        /// the API server adds the new ones and drops the ones that are no longer listed.
        /// </summary>
        Task GenerateConstraintsAsync(DBWS_Application application, string entityName, List<EntityConstraint> constraints);

        /// <summary>
        /// Empties the application: drops all its data and creates its tables again, empty, from
        /// the entities and properties the Portal has. The token, entities and properties stay.
        /// </summary>
        Task RebuildAsync(DBWS_Application application);

        /// <summary>
        /// Removes the application from the API server: its database and its files.
        /// </summary>
        Task DegenerateAsync(DBWS_Application application);

        /// <summary>
        /// The roles the application's users have (the distinct values of Users.Roles, a value
        /// holding several roles split on commas), blanks left out, each once. An answer that is
        /// not that list is an UPSTREAM_ERROR.
        /// </summary>
        Task<List<string>> GetUserRolesAsync(DBWS_Application application);
    }
}
