using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The entities of an application the caller owns or collaborates on. An entity is found by
    /// its exact, case-sensitive name. Every write follows <see cref="IApplicationWriteScope"/>.
    /// </summary>
    public interface IEntityManagementService
    {
        /// <summary>
        /// The custom and system entities of the application by name, with their properties only
        /// when includeProperties is true.
        /// </summary>
        Task<List<EntityResponse>> GetAllAsync(string appToken, bool includeProperties);

        /// <summary>
        /// One entity with its properties.
        /// </summary>
        Task<EntityResponse> GetAsync(string appToken, string entityName);

        /// <summary>
        /// Creates a custom entity with the properties and constraints the API server gives a new one.
        /// </summary>
        Task<EntityResponse> CreateAsync(string appToken, CreateEntityRequest request);

        /// <summary>
        /// Changes the description and the change tracking of an entity, system entities included.
        /// </summary>
        Task<EntityResponse> UpdateAsync(string appToken, string entityName, UpdateEntityRequest request);

        /// <summary>
        /// Renames a custom entity that no foreign key points to.
        /// </summary>
        Task<EntityResponse> RenameAsync(string appToken, string entityName, RenameEntityRequest request);

        /// <summary>
        /// Deletes a custom entity that no foreign key points to, with all its records.
        /// </summary>
        Task DeleteAsync(string appToken, string entityName);
    }
}
