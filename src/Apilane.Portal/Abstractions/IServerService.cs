using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The API servers registered on this instance. Callers are responsible for the Admin check.
    /// </summary>
    public interface IServerService
    {
        Task<List<ServerResponse>> GetAllAsync();

        /// <summary>
        /// Name and address of every server, by name. For any signed-in user.
        /// </summary>
        Task<List<ServerSummaryResponse>> GetSummariesAsync();

        /// <summary>
        /// Throws a NOT_FOUND <see cref="Api.PortalException"/> for an unknown id.
        /// </summary>
        Task<ServerResponse> GetAsync(long id);

        /// <summary>
        /// Throws a VALIDATION <see cref="Api.PortalException"/> when the url is not absolute.
        /// </summary>
        Task<ServerResponse> CreateAsync(ServerRequest request);

        Task<ServerResponse> UpdateAsync(long id, ServerRequest request);

        /// <summary>
        /// Throws a CONFLICT <see cref="Api.PortalException"/> while applications are hosted on the server.
        /// </summary>
        Task DeleteAsync(long id);
    }
}
