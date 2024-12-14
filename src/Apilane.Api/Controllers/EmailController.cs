using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Common.Helpers;
using Apilane.Common.Models.Dto;
using Apilane.Api.Filters;
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

        public EmailController(
            ApiConfiguration apiConfiguration, 
            IEmailAPI emailAPI,
            IClusterClient clusterClient) : base(apiConfiguration, clusterClient)
        {
            _emailAPI = emailAPI;
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
            var emailSettings = Application.GetEmailSettings()
                ?? throw new ApilaneException(AppErrors.ERROR, $"Missing application email settings. Please navigate to the portal to the application's Email section.");

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
			var emailSettings = Application.GetEmailSettings()
				?? throw new ApilaneException(AppErrors.ERROR, $"Missing application email settings. Please navigate to the portal to the application's Email section.");

			await _emailAPI.ForgotPasswordAsync(
                Application,
                email);

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
