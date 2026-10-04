using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route("api/v1/instance")]
    public class InstanceController : PortalApiControllerBase
    {
        private readonly IInstanceSettingsService _instanceSettingsService;

        public InstanceController(IInstanceSettingsService instanceSettingsService)
        {
            _instanceSettingsService = instanceSettingsService;
        }

        /// <summary>
        /// Returns what the sign-in pages show before anyone is signed in: the instance title,
        /// whether registration is open and whether a password reset can be mailed. No session is needed.
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        [ProducesResponseType(typeof(InstanceResponse), StatusCodes.Status200OK)]
        public async Task<InstanceResponse> Get()
        {
            return await _instanceSettingsService.GetPublicAsync();
        }
    }
}
