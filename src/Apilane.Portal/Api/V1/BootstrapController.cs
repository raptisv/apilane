using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route("api/v1/bootstrap")]
    [AllowAnonymous]
    [NoAgent]
    public class BootstrapController : PortalApiControllerBase
    {
        private readonly IPortalBootstrapService _bootstrapService;

        public BootstrapController(IPortalBootstrapService bootstrapService)
        {
            _bootstrapService = bootstrapService;
        }

        /// <summary>
        /// Whether first-administrator setup is required. Never returns the administrator's credential.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(BootstrapResponse), StatusCodes.Status200OK)]
        public async Task<BootstrapResponse> Get()
        {
            return new BootstrapResponse { Required = await _bootstrapService.IsRequiredAsync() };
        }

        /// <summary>
        /// Completes first-administrator setup using the temporary password printed on the server,
        /// sets the chosen administrator email and password and signs in. Every other management operation is
        /// blocked until setup completes. A completed setup cannot be repeated.
        /// </summary>
        [HttpPost]
        [EnableRateLimiting(PortalRateLimitOptions.AccountPolicy)]
        [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
        public async Task<SessionResponse> Complete(BootstrapRequest request)
        {
            return await _bootstrapService.CompleteAsync(request);
        }
    }
}
