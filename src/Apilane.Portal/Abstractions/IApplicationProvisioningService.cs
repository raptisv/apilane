using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Brings a new application into existence: on its API server first, then in the Portal.
    /// Any signed-in user may do it and becomes the owner.
    /// </summary>
    public interface IApplicationProvisioningService
    {
        /// <summary>
        /// Creates an empty application with the system entities and the default report.
        /// </summary>
        Task<ApplicationResponse> CreateAsync(CreateApplicationRequest request);

        /// <summary>
        /// Creates an application from an exported application.json, keeping its token and encryption key.
        /// </summary>
        Task<ApplicationResponse> ImportAsync(ImportApplicationRequest request);
    }
}
