using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Clones an application for the management API. The copying itself is done by
    /// <see cref="ICloneService"/>, the same routine the Razor page starts. The owner and
    /// collaborators may clone; the clone belongs to whoever starts it.
    /// </summary>
    public interface IApplicationCloneService
    {
        /// <summary>
        /// Saves the new application in the Portal and starts the clone in the background.
        /// </summary>
        Task<CloneStartedResponse> StartAsync(string appToken, CloneApplicationRequest request);

        /// <summary>
        /// The progress of a clone. Throws a NOT_FOUND <see cref="Api.PortalException"/> unless the
        /// operation exists, clones this application and was started by the caller.
        /// </summary>
        Task<CloneOperationResponse> GetAsync(string appToken, string operationId);
    }
}
