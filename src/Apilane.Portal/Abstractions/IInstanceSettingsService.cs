using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The instance settings as the management API shows and changes them: without secret values.
    /// Callers are responsible for the Admin check.
    /// </summary>
    public interface IInstanceSettingsService
    {
        Task<InstanceSettingsResponse> GetAsync();

        /// <summary>
        /// The few values anyone may read, signed in or not.
        /// </summary>
        Task<InstanceResponse> GetPublicAsync();

        Task<InstanceSettingsResponse> UpdateAsync(InstanceSettingsRequest request);
    }
}
