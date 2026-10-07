using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    public interface IPortalBootstrapService
    {
        Task<BootstrapCredential?> InitializeAsync();
        Task<bool> IsRequiredAsync();
        Task<SessionResponse> CompleteAsync(BootstrapRequest request);
    }

    /// <summary>
    /// Delivered to the server operator once per startup while setup is pending; never returned by an HTTP endpoint.
    /// </summary>
    public record BootstrapCredential(string TemporaryPassword);
}
