using Apilane.Portal.Api.V1.Contracts;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    /// <summary>
    /// The e-mail settings of an application the caller owns or collaborates on. The e-mail
    /// templates are not here: the browser reads and saves them on the API server.
    /// </summary>
    public interface IApplicationEmailSettingsService
    {
        Task<EmailSettingsResponse> GetAsync(string appToken);

        /// <summary>
        /// Saves the SMTP settings and the confirmation redirect URL together, then resets the API
        /// server's cache so it sends with the new settings.
        /// </summary>
        Task<EmailSettingsResponse> UpdateAsync(string appToken, EmailSettingsRequest request);
    }
}
