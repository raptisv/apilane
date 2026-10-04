using Apilane.Portal.Models;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Answers "who is calling and what may they do" for the management API. It is the only place
    /// in the API that reads the signed-in principal.
    /// </summary>
    public interface IPortalAccessService
    {
        /// <summary>
        /// The signed-in portal user, or null when there is no session or it has been revoked
        /// (signed out, or signed in again elsewhere). For a request with an agent key it is the agent.
        /// </summary>
        Task<ApplicationUser?> FindCurrentUserAsync();

        /// <summary>
        /// The signed-in portal user. Throws an UNAUTHORIZED <see cref="Api.PortalException"/> when there is none.
        /// </summary>
        Task<ApplicationUser> GetCurrentUserAsync();

        /// <summary>
        /// Whether the caller holds the portal Admin role.
        /// </summary>
        bool IsAdmin { get; }
    }
}
