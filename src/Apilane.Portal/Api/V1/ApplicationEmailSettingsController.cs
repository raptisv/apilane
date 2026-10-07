using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    /// <summary>
    /// The e-mail settings of an application. The e-mail templates are read and saved on the API
    /// server ({ServerUrl}/api/Email/GetEmails and /api/Email/Update), not here.
    /// </summary>
    [Route(RoutePrefix + "/email-settings")]
    [AgentPermission("email-settings")]
    public class ApplicationEmailSettingsController : PortalApplicationApiControllerBase
    {
        private readonly IApplicationEmailSettingsService _emailSettingsService;

        public ApplicationEmailSettingsController(IApplicationEmailSettingsService emailSettingsService)
        {
            _emailSettingsService = emailSettingsService;
        }

        /// <summary>
        /// Returns the SMTP settings of the application and its e-mail confirmation redirect URL.
        /// The mail password is never returned. For the owner and collaborators.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(EmailSettingsResponse), StatusCodes.Status200OK)]
        public async Task<EmailSettingsResponse> Get(string appToken)
        {
            return await _emailSettingsService.GetAsync(appToken);
        }

        /// <summary>
        /// Saves the SMTP settings and the e-mail confirmation redirect URL, all together. Every
        /// value is written (null or empty clears it) except MailPassword, which null keeps and an
        /// empty string clears. For human owners and collaborators only: agents cannot change
        /// mail transport or confirmation redirects, regardless of their application grants.
        /// A 'Warning' response header means the settings were saved and the API server could not
        /// be refreshed: it keeps sending with the old settings until its cache expires.
        /// </summary>
        [HttpPut]
        [NoAgent]
        [ProducesCacheResetWarning]
        [ProducesResponseType(typeof(EmailSettingsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<EmailSettingsResponse> Update(string appToken, EmailSettingsRequest request)
        {
            return await _emailSettingsService.UpdateAsync(appToken, request);
        }
    }
}
