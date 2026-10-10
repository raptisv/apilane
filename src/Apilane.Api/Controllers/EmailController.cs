using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Common.Helpers;
using Apilane.Common.Models.Dto;
using Apilane.Api.Filters;
using Apilane.Api.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Orleans;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Api.Controllers
{
    public class EmailController : BaseApplicationApiController
    {
        private readonly IEmailAPI _emailAPI;
        private readonly IAccountAPI _accountAPI;

        public EmailController(
            ApiConfiguration apiConfiguration,
            IEmailAPI emailAPI,
            IAccountAPI accountAPI,
            IClusterClient clusterClient) : base(apiConfiguration, clusterClient)
        {
            _emailAPI = emailAPI;
            _accountAPI = accountAPI;
        }

        public class ConfirmEmailRequest
        {
            /// <summary>
            /// The confirmation token, as the template placeholder <c>{confirmation_token}</c> expanded it in the email.
            /// </summary>
            public string? Token { get; set; }
        }

        public class ResetPasswordRequest
        {
            /// <summary>
            /// The reset token, as the template placeholder <c>{reset_password_token}</c> expanded it in the email.
            /// </summary>
            public string? Token { get; set; }

            /// <summary>
            /// The new password: 8 to 100 characters.
            /// </summary>
            public string? Password { get; set; }
        }

        /// <summary>
        /// Use this endpoint to send an email to the user, to confirm his email.
        /// This endpoint is used when the user lost his initial confirmation email, and requests for a new one.
        /// </summary>
        /// <param name="email">The user email to confirm</param>
        /// <returns>Returns true even if the user email does not exist.</returns>
        [HttpGet]
        [ServiceFilter(typeof(ApplicationLogActionFilter))]
        [Produces("application/json")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        public async Task<JsonResult> RequestConfirmation([BindRequired] string email)
        {
            await _emailAPI.RequestConfirmationAsync(
                Application,
                email);

            return Json("OK");
        }

        /// <summary>
        /// Use this endpoint to send an email to the user, in case he forgot his password.
        /// </summary>
        /// <param name="email">Required. The user email</param>
        /// <returns>Returns true even if the user email does not exist.</returns>
        [HttpGet]
        [ServiceFilter(typeof(ApplicationLogActionFilter))]
        [Produces("application/json")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        public async Task<JsonResult> ForgotPassword(
            [BindRequired] string email)
		{
			await _emailAPI.ForgotPasswordAsync(
                Application,
                email);

            return Json("OK");
        }

        /// <summary>
        /// Use this endpoint to confirm the user's email with the token of the confirmation email, from your own
        /// server or page, instead of the link of the email. The token is the value of the template placeholder
        /// <c>{confirmation_token}</c>. It stays valid for one hour after the email was sent.
        /// </summary>
        /// <param name="request">The confirmation token</param>
        /// <returns>Returns "OK" when the email is confirmed (also when it already was). An unknown or expired token answers a VALIDATION error for the property Token.</returns>
        [HttpPost]
        [ServiceFilter(typeof(ApplicationLogActionFilter))]
        [Produces("application/json")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.BadRequest)]
        public async Task<JsonResult> ConfirmEmail([FromBody] ConfirmEmailRequest? request)
        {
            await _accountAPI.ConfirmEmailWithTokenAsync(Application, request?.Token);

            return Json("OK");
        }

        /// <summary>
        /// Use this endpoint to set a new password with the token of the reset email, from your own server or page,
        /// instead of the ready-made page. The token is the value of the template placeholder
        /// <c>{reset_password_token}</c>. It stays valid for 24 hours. A successful reset invalidates all the reset links
        /// and all the sessions of the user.
        /// </summary>
        /// <param name="request">The reset token and the new password</param>
        /// <returns>Returns "OK" when the password is changed. A password that is too short or too long, and an unknown or expired token, answer a VALIDATION error for the property Password or Token.</returns>
        [HttpPost]
        [ServiceFilter(typeof(ApplicationLogActionFilter))]
        [Produces("application/json")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.BadRequest)]
        public async Task<JsonResult> ResetPassword([FromBody] ResetPasswordRequest? request)
        {
            await _accountAPI.ResetPasswordWithTokenAsync(Application, request?.Token, request?.Password);

            return Json("OK");
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet]
        [ApplicationOwnerAuthorize]
        public async Task<JsonResult> GetEmails(long? ID = null)
        {
            var result = await _emailAPI.GetEmailsAsync(Application.Token, ID);

            var filtered = result.Where(x => EmailEvent.EmailEvents.Any(e => e.Code.ToString().Equals(x.EventCode, System.StringComparison.OrdinalIgnoreCase)));

            return Json(filtered.Select(x => new
            {
                x.ID,
                x.EventCode,
                x.Active,
                x.Subject,
                x.Content,
                Description = EmailEvent.EmailEvents.SingleOrDefault(e => e.Code.ToString().Equals(x.EventCode, System.StringComparison.OrdinalIgnoreCase))?.Description ?? x.EventCode
            }));
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPut]
        [ApplicationOwnerAuthorize]
        public async Task<JsonResult> Update([FromBody] EmailTemplateDto template)
        {
            await _emailAPI.UpdateAsync(Application.Token, template);

            return Json("OK");
        }
    }
}
