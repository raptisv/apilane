using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The portal users and their role. Callers are responsible for the Admin check.
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
        /// and a CONFLICT one when the user is the caller.
        /// </summary>
        Task<UserResponse> SetRoleAsync(string userId, UserRoleRequest request);
    }
}
