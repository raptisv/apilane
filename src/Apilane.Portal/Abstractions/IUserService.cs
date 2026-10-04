using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The portal users, their role, and the agents among them. Callers are responsible for the Admin check.
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// Every portal user, the most recent sign-in first.
        /// </summary>
        Task<List<UserResponse>> GetAllAsync();

        /// <summary>
        /// Gives the user the Admin role or takes it away. Asking for the role the user already has
        /// changes nothing. Throws a NOT_FOUND <see cref="Api.PortalException"/> for an unknown id
        /// and a CONFLICT one when the user is the caller, or an agent that would become an administrator.
        /// </summary>
        Task<UserResponse> SetRoleAsync(string userId, UserRoleRequest request);

        /// <summary>
        /// Creates the agent {Name}@agent.local (see <see cref="Api.PortalAgent"/>) and its key. The
        /// key is in the answer and nowhere else. Throws a VALIDATION <see cref="Api.PortalException"/>
        /// on Name when an agent with that name exists.
        /// </summary>
        Task<AgentCreatedResponse> CreateAgentAsync(CreateAgentRequest request);

        /// <summary>
        /// Deletes an agent: its user, its key and its entries in the collaborator lists. Throws a
        /// NOT_FOUND <see cref="Api.PortalException"/> for an unknown id and for a user that is not an agent.
        /// </summary>
        Task DeleteAgentAsync(string userId);
    }
}
