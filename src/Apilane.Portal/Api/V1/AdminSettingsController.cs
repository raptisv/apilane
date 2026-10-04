using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/settings")]
    public class AdminSettingsController : PortalAdminApiControllerBase
    {
        private readonly IInstanceSettingsService _instanceSettingsService;

        public AdminSettingsController(IInstanceSettingsService instanceSettingsService)
        {
            _instanceSettingsService = instanceSettingsService;
        }

        /// <summary>
        /// Returns the instance settings. The mail password and the installation key are not
        /// returned, only whether they are set. Admin only.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(InstanceSettingsResponse), StatusCodes.Status200OK)]
        public async Task<InstanceSettingsResponse> Get()
        {
            return await _instanceSettingsService.GetAsync();
        }

        /// <summary>
        /// Changes the instance settings. Every value is written; an empty mail setting is cleared.
        /// A null InstallationKey or MailPassword keeps the stored value, and an empty MailPassword
        /// clears it. After a new InstallationKey the API servers must be restarted with it. Admin only.
        /// </summary>
        [HttpPut]
        [ProducesResponseType(typeof(InstanceSettingsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<InstanceSettingsResponse> Update(InstanceSettingsRequest request)
        {
            return await _instanceSettingsService.UpdateAsync(request);
        }
    }
}
