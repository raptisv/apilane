using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route("api/v1/session")]
    public class SessionController : PortalApiControllerBase
    {
        private readonly IPortalAccountService _portalAccountService;

        public SessionController(IPortalAccountService portalAccountService)
        {
            _portalAccountService = portalAccountService;
        }

        /// <summary>
        /// Returns the signed-in user. A 401 means there is no session: sign in with POST /api/v1/session.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
        public async Task<SessionResponse> Get()
        {
            return await _portalAccountService.GetSessionAsync();
        }

        /// <summary>
        /// Returns the token the signed-in user calls the API servers with: records, files, statistics
        /// and the other data calls are made straight to the API server, with this token as
        /// 'Authorization: Bearer' and the application token as 'x-application-token'. The token
        /// changes at every sign-in, so fetch it again after an API server answers 401.
        /// Never cached (Cache-Control: no-store); keep it in memory only.
        /// </summary>
        [HttpGet("api-token")]
        [NoAgent]
        [ProducesResponseType(typeof(ApiTokenResponse), StatusCodes.Status200OK)]
        public async Task<ApiTokenResponse> GetApiToken()
        {
            return await _portalAccountService.GetApiTokenAsync();
        }

        /// <summary>
        /// Signs in with e-mail and password and sets the session cookie. A user has one session at
        /// a time: signing in ends the session the user had before. Any failure is 401 with the
        /// message 'Invalid login attempt.'. No session is needed. At most a few calls per minute
        /// from one IP address, then 429.
        /// </summary>
        [HttpPost]
        [NoAgent]
        [AllowAnonymous]
        [EnableRateLimiting(PortalRateLimitOptions.AccountPolicy)]
        [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
        public async Task<SessionResponse> Create(SignInRequest request)
        {
            return await _portalAccountService.SignInAsync(request);
        }

        /// <summary>
        /// Signs out: ends the session and removes the cookie. Also answers 204 when there is no
        /// session, or when the cookie belongs to a session that has already ended.
        /// </summary>
        // Anonymous, so that a browser tab holding a cookie that is no longer valid can still drop it.
        [HttpDelete]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Delete()
        {
            await _portalAccountService.SignOutAsync();

            return NoContent();
        }
    }
}
