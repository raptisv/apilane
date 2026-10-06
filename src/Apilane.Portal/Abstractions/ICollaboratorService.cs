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
        /// The agents (see <see cref="Api.PortalAgent"/>) the application is not shared with yet, by name.
        /// </summary>
        Task<List<AvailableAgentResponse>> GetAvailableAgentsAsync(string appToken);

        /// <summary>
        /// Shares the application with an e-mail address and, when the instance mail is configured,
        /// tells that address by mail. An agent's address is never mailed.
        /// </summary>
        Task<CollaboratorAddedResponse> AddAsync(string appToken, AddCollaboratorRequest request);

        /// <summary>
        /// Replaces the permissions of an agent collaborator of this application. Owner only.
        /// </summary>
        Task<CollaboratorResponse> UpdatePermissionsAsync(string appToken, long id, UpdateAgentPermissionsRequest request);

        /// <summary>
        /// Stops sharing the application with one collaborator of it.
        /// </summary>
        Task DeleteAsync(string appToken, long id);
    }
}
