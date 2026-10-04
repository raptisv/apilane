using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The properties of an entity of an application the caller owns or collaborates on. Entity
    /// and property are found by their exact, case-sensitive names. Every write follows
    /// <see cref="IApplicationWriteScope"/>.
    /// </summary>
    public interface IPropertyManagementService
    {
        /// <summary>
        /// The properties of the entity: the primary key first, then custom ones, then system
        /// ones, each group by name.
        /// </summary>
        Task<List<PropertyResponse>> GetAllAsync(string appToken, string entityName);

        /// <summary>
        /// One property.
        /// </summary>
        Task<PropertyResponse> GetAsync(string appToken, string entityName, string propertyName);

        /// <summary>
        /// Adds a custom property to the entity and its column on the API server.
        /// </summary>
        Task<PropertyResponse> CreateAsync(string appToken, string entityName, CreatePropertyRequest request);

        /// <summary>
        /// Changes the description and the rules of a custom property that its type allows to change.
        /// </summary>
        Task<PropertyResponse> UpdateAsync(string appToken, string entityName, string propertyName, UpdatePropertyRequest request);

        /// <summary>
        /// Renames a custom property that no constraint of the entity names, and its column.
        /// </summary>
        Task<PropertyResponse> RenameAsync(string appToken, string entityName, string propertyName, RenamePropertyRequest request);

        /// <summary>
        /// Deletes a custom property that no constraint of the entity names, with its column and values.
        /// </summary>
        Task DeleteAsync(string appToken, string entityName, string propertyName);
    }
}
