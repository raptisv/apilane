using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The applications of the signed-in user. Access rules are those of <see cref="IApplicationAccessService"/>.
    /// </summary>
    public interface IApplicationService
    {
        /// <summary>
        /// The applications the caller owns or collaborates on, owned first, then by name.
        /// </summary>
        Task<List<ApplicationResponse>> GetAllAsync();

        /// <summary>
        /// One application the caller owns or collaborates on.
        /// </summary>
        Task<ApplicationResponse> GetAsync(string appToken);

        /// <summary>
        /// What a client needs to call the application on its API server, the encryption key included.
        /// </summary>
        Task<ConnectionInfoResponse> GetConnectionInfoAsync(string appToken);

        /// <summary>
        /// One page of the application's audit log, newest first.
        /// </summary>
        Task<ListResponse<AuditLogEntryResponse>> GetAuditLogAsync(string appToken, PageQuery page);

        /// <summary>
        /// Makes the API server reload the application. Allowed for the owner, a collaborator and any administrator.
        /// </summary>
        Task ResetCacheAsync(string appToken);

        /// <summary>
        /// Renames the application and, unless it is on SQLite, sets its connection string. The
        /// stored database type decides; a null connection string keeps the stored one.
        /// </summary>
        Task<ApplicationResponse> UpdateAsync(string appToken, UpdateApplicationRequest request);

        /// <summary>
        /// Takes the application online or offline, then resets the API server's cache.
        /// </summary>
        Task<ApplicationResponse> SetStatusAsync(string appToken, SetApplicationStatusRequest request);

        /// <summary>
        /// Drops all data of the application on the API server and creates its tables again, empty.
        /// </summary>
        Task RebuildAsync(string appToken);

        /// <summary>
        /// Removes the application from the API server, then from the Portal with everything it owns.
        /// </summary>
        Task DeleteAsync(string appToken);
    }
}
