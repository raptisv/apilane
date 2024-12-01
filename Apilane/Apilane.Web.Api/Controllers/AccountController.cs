using Apilane.Api.Abstractions;
using Apilane.Api.Configuration;
using Apilane.Api.Enums;
using Apilane.Api.Exceptions;
using Apilane.Api.Grains;
using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models.Dto;
using Apilane.Web.Api.Filters;
using Apilane.Web.Api.Models.ViewModels;
using Apilane.Web.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Orleans;
using System;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Apilane.Web.Api.Controllers
{
    [ServiceFilter(typeof(ApplicationLogActionFilter))]
    public class AccountController : BaseApplicationApiController
    {
        private readonly IAccountAPI _accountAPI;
        private readonly IQueryDataService _queryDataService;

        public AccountController(
            ApiConfiguration apiConfiguration,
            IAccountAPI accountAPI,
            IQueryDataService queryDataService,
            IClusterClient clusterClient) : base(apiConfiguration, clusterClient)
        {
            _accountAPI = accountAPI;
            _queryDataService = queryDataService;
        }

        public class AuthRequest
        {
            /// <summary>
            /// The user name
            /// </summary>
            public string? Username { get; set; }

            /// <summary>
            /// The user's email
            /// </summary>
            public string? Email { get; set; }

            /// <summary>
            /// The password
            /// </summary>
            public string Password { get; set; } = null!;
        }

        public class ChangePassRequest
        {
            /// <summary>
            /// The current password
            /// </summary>
            public string Password { get; set; } = null!;
            /// <summary>
            /// The new password
            /// </summary>
            public string NewPassword { get; set; } = null!;
        }

        /// <summary>
        /// Use this endpoint to perform user login. You can use email or username to login.
        /// </summary>
        /// <param name="credentials"></param>
        /// <returns>Returns the user properties and authorization token. Store the AuthToken string for subsequent api calls that require authorization</returns>
        [HttpPost]
        [Produces("application/json")]
        [ProducesResponseType(typeof(LoginResponseDto), (int)HttpStatusCode.OK)]
        public async Task<JsonResult> Login(
            [BindRequired] [FromBody] AuthRequest credentials)
        {
            if (credentials is null)
            {
                throw new ApilaneException(AppErrors.EMPTY_BODY);
            }

            var loginResult = await _accountAPI.LoginAsync(
                Application,
                GetEntity(nameof(Users)),
                credentials.Username,
                credentials.Email,
                credentials.Password);

            return Json(loginResult);
        }

        /// <summary>
        /// Use this endpoint get all data and security related to the user.
        /// </summary>
        /// <returns>Returns the authorized user dara</returns>
        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(UserDataDto), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<JsonResult> UserData()
        {
            var result = await _accountAPI.GetUserDataAsync(
                Application.Token,
                ApplicationUser ?? throw new ApilaneException(AppErrors.UNAUTHORIZED), 
                Application.Security_List);

            return Json(result);
        }

        /// <summary>
        /// Use this endpoint to perform user registration.
        /// </summary>
        /// <param name="user">The user to register</param>
        /// <returns>Returns the user ID created</returns>
        [HttpPost]
        [Produces("application/json")]
        [ProducesResponseType(typeof(long), (int)HttpStatusCode.OK)]
        public async Task<JsonResult> Register(
            [BindRequired] [FromBody] object user)
        {
            if (user == null)
            {
                throw new ApilaneException(AppErrors.EMPTY_BODY);
            }

            var userJObject = JsonObject.Parse(Utils.GetString(user))?.AsObject()
                ?? throw new ApilaneException( AppErrors.ERROR, "Could not parse body");

            var registerResult = await _accountAPI.RegisterAsync(
                Application.Token,
                GetEntity(nameof(Users)),
                (DatabaseType)Application.DatabaseType,
                Application.Server.ServerUrl,
                Application.GetEmailSettings(),
                Application.EncryptionKey,
                Application.DifferentiationEntity,
                userJObject,
                Application.AllowUserRegister);

            return Json(registerResult);
        }

        /// <summary>
        /// Use this endpoint to enable the user update his extra properties (e.g. Firstname, Lastname). System properties (e.g. Email, Username, Password, Roles) cannot be updated from this endpoint.
        /// </summary>
        /// <param name="user">The user data</param>
        /// <returns>The user along with his updated properties</returns>
        [HttpPut]
        [Produces("application/json")]
        [ProducesResponseType(typeof(object), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<JsonResult> Update(
            [BindRequired] [FromBody] object user)
        {
            var userJObject = JsonObject.Parse(Utils.GetString(user))?.AsObject()
                ?? throw new ApilaneException(AppErrors.ERROR, "Could not parse body");

            var result = await _accountAPI.UpdateAsync(
                Application.Token,
                GetEntity(nameof(Users)),
                ApplicationUser ?? throw new ApilaneException(AppErrors.UNAUTHORIZED),
                (DatabaseType)Application.DatabaseType,
                Application.EncryptionKey,
                Application.DifferentiationEntity,
                userJObject);

            // Reset grain cache
            if (Guid.TryParse(_queryDataService.AuthToken, out var guidAuthToken))
            {
                var grainRef = ClusterClient.GetGrain<IAuthTokenUserGrain>(guidAuthToken);
                await grainRef.ResetUserCacheAsync();
            }

            return Json(result);
        }

        /// <summary>
        /// Use this endpoint to enable the user to change his password.
        /// </summary>
        /// <param name="user"></param>
        /// <returns>Returns true if the action succeeded</returns>
        [HttpPut]
        [Produces("application/json")]
        [ProducesResponseType(typeof(bool), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<bool> ChangePassword(
            [BindRequired] [FromBody] ChangePassRequest user)
        {
            if (user == null)
            {
                throw new ApilaneException(AppErrors.EMPTY_BODY);
            }

            return await _accountAPI.ChangePasswordAsync(
                ApplicationUser ?? throw new ApilaneException(AppErrors.UNAUTHORIZED),
                Application.EncryptionKey,
                user.Password,
                user.NewPassword);
        }

        /// <summary>
        /// Use this endpoint to renew the user authentication token.
        /// </summary>
        /// <returns>Removes the given authentication token and returns a new one.Store the AuthToken string for subsequent api calls that require authorization.</returns>
        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<string> RenewAuthToken()
        {
            var newAuthToken = await _accountAPI.RenewAuthTokenAsync(
                ApplicationUser ?? throw new ApilaneException(AppErrors.UNAUTHORIZED));

            // Delete old auth token
            if (Guid.TryParse(_queryDataService.AuthToken, out var guidAuthToken))
            {
                var grainRef = ClusterClient.GetGrain<IAuthTokenUserGrain>(guidAuthToken);
                await grainRef.DeleteAsync(Application.ToDbInfo(ApiConfiguration.FilesPath));
            }

            return newAuthToken;
        }

        /// <summary>
        /// Use this endpoint to logout the user.
        /// </summary>
        /// <param name="everywhere"> Setting this to true, deletes all AuthTokens issued for the user. Use it to logout the user from every client he has logged in.</param>
        /// <returns>Returns the number of AuthTokens deleted</returns>
        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(int), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<long> Logout(bool everywhere = false)
        {
            if (everywhere)
            {
                var authTokens = await _accountAPI.GetAuthTokensAsync(
                    ApplicationUser?.ID ?? throw new ApilaneException(AppErrors.UNAUTHORIZED));

                foreach(var authToken in authTokens)
                {
                    // Delete auth token
                    if (Guid.TryParse(authToken, out var guidAuthToken))
                    {
                        var authTokenGrainRef = ClusterClient.GetGrain<IAuthTokenUserGrain>(guidAuthToken);
                        await authTokenGrainRef.DeleteAsync(Application.ToDbInfo(ApiConfiguration.FilesPath));
                    }
                }

                return authTokens.Count;
            }
            else
            {
                // Delete auth token
                if (Guid.TryParse(_queryDataService.AuthToken, out var guidAuthToken))
                {
                    var grainRef = ClusterClient.GetGrain<IAuthTokenUserGrain>(guidAuthToken);
                    await grainRef.DeleteAsync(Application.ToDbInfo(ApiConfiguration.FilesPath));
                }

                return 1;
            }
        }

        /// <summary>
        /// Hidden endpoint, used to confirm the user's email address.
        /// </summary>
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet]
        public async Task<IActionResult> Confirm(string token)
        {
            var redirectUrl = await _accountAPI.ConfirmAsync(Application.Token, token, Application.Name, Application.EmailConfirmationRedirectUrl);

            // Reset grain cache
            if (Guid.TryParse(_queryDataService.AuthToken, out var guidAuthToken))
            {
                var grainRef = ClusterClient.GetGrain<IAuthTokenUserGrain>(guidAuthToken);
                await grainRef.ResetUserCacheAsync();
            }

            if (redirectUrl is null)
            {
                redirectUrl = Request.Scheme + "://" + Request.Host + Request.PathBase + $"/App/{Application.Token}/Account/Manage/Error";
            }

            return Redirect(redirectUrl);
        }
    }
}
