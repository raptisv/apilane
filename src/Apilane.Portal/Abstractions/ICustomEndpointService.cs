using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The custom endpoints of an application the caller owns or collaborates on. An endpoint is
    /// found by its ID inside that application only: an ID of another application is NOT_FOUND.
    /// Every write follows <see cref="IApplicationWriteScope"/>.
    /// </summary>
    public interface ICustomEndpointService
    {
        /// <summary>
        /// The custom endpoints of the application, in the order they were created.
        /// </summary>
        Task<List<CustomEndpointResponse>> GetAllAsync(string appToken);

        /// <summary>
        /// One custom endpoint of the application.
        /// </summary>
        Task<CustomEndpointResponse> GetAsync(string appToken, long id);

        /// <summary>
        /// Creates a custom endpoint with a name no other endpoint of the application has.
        /// </summary>
        Task<CustomEndpointResponse> CreateAsync(string appToken, CustomEndpointRequest request);

        /// <summary>
        /// Changes the name, description and query of a custom endpoint.
        /// </summary>
        Task<CustomEndpointResponse> UpdateAsync(string appToken, long id, CustomEndpointRequest request);

        /// <summary>
        /// Deletes a custom endpoint.
        /// </summary>
        Task DeleteAsync(string appToken, long id);

        /// <summary>
        /// The parameters and addresses a custom endpoint with this name and query would have. Saves nothing.
        /// </summary>
        Task<CustomEndpointPreviewResponse> PreviewAsync(string appToken, CustomEndpointPreviewRequest request);
    }
}
