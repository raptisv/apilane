using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// Compares two applications the caller owns or collaborates on: their entities, custom
    /// endpoints and security rules. Reads only.
    /// </summary>
    public interface IApplicationComparisonService
    {
        /// <summary>
        /// What the target application has more (Added), less (Removed) or different (Changed)
        /// than the application of <paramref name="appToken"/>. Names are matched exactly. Throws
        /// a VALIDATION <see cref="Api.PortalException"/> when both tokens are the same and a
        /// NOT_FOUND one when the caller cannot see either application.
        /// </summary>
        Task<ApplicationComparisonResponse> CompareAsync(string appToken, string targetAppToken);
    }
}
