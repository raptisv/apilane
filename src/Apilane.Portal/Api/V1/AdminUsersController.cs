using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/users")]
    public class AdminUsersController : PortalAdminApiControllerBase
    {
        private readonly IUserService _userService;

        public AdminUsersController(IUserService userService)
        {
            _userService = userService;
        }

        /// <summary>
        /// Lists every portal user, the most recent sign-in first. Admin only.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(ListResponse<UserResponse>), StatusCodes.Status200OK)]
        public async Task<ListResponse<UserResponse>> List()
        {
            return new ListResponse<UserResponse>(await _userService.GetAllAsync());
        }

        /// <summary>
        /// Makes a user an administrator or an ordinary user. Asking for the role the user already
        /// has changes nothing and succeeds. Answers 409 CONFLICT for your own user. The user gets
        /// the new role within 30 minutes, or at once when they sign in again. Admin only.
        /// </summary>
        [HttpPut("{userId}/role")]
        [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
        public async Task<UserResponse> SetRole(string userId, UserRoleRequest request)
        {
            return await _userService.SetRoleAsync(userId, request);
        }
    }
}
