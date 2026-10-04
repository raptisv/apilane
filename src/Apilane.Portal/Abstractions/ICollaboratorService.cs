using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The users an application is shared with. Every method is for the owner only: a collaborator
    /// gets a FORBIDDEN <see cref="Api.PortalException"/>, anyone else NOT_FOUND.
    /// </summary>
    public interface ICollaboratorService
    {
        /// <summary>
        /// The collaborators of the application, in the order they were added.
        /// </summary>
        Task<List<CollaboratorResponse>> GetAllAsync(string appToken);

        /// <summary>
        /// Shares the application with an e-mail address and, when the instance mail is configured,
        /// tells that address by mail.
        /// </summary>
        Task<CollaboratorAddedResponse> AddAsync(string appToken, AddCollaboratorRequest request);

        /// <summary>
        /// Stops sharing the application with one collaborator of it.
        /// </summary>
        Task DeleteAsync(string appToken, long id);
    }
}
