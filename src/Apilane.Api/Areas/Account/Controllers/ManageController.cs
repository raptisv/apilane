using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Api.Areas.Account.Models;
using Apilane.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Areas.Account.Controllers
{
    [Area("Account")]
    public class ManageController : Controller
    {
        private readonly ILogger<ManageController> _logger;
        private readonly IEmailAPI _emailAPI;
        private readonly IAccountAPI _accountAPI;
        private readonly IApplicationHelperService _applicationHelperService;
        private readonly IApplicationDataStoreFactory _applicationDataStoreFactory;
        protected DBWS_Application Application = null!;

        public ManageController(
            ILogger<ManageController> logger,
            IEmailAPI emailAPI,
            IAccountAPI accountAPI,
            IApplicationHelperService applicationHelperService,
            IApplicationDataStoreFactory aplicationDataStoreFactory)
        {
            _logger = logger;
            _emailAPI = emailAPI;
            _accountAPI = accountAPI;
            _applicationHelperService = applicationHelperService;
            _applicationDataStoreFactory = aplicationDataStoreFactory;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // On every call, validate user access to application

            var queryService = context.HttpContext.RequestServices.GetService<IQueryDataService>() ?? throw new Exception($"Invalid service IQueryDataService");
            var applicationService = context.HttpContext.RequestServices.GetService<IApplicationService>() ?? throw new Exception($"Invalid service IApplicationService");

            // Load the application

            Application = await applicationService.GetAsync(queryService.AppToken);

            await base.OnActionExecutionAsync(context, next);
        }

        [AllowAnonymous]
        public IActionResult ForgotPassword()
        {
            ViewBag.Application = Application;

            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            ViewBag.Application = Application;

            try
            {
                if (ModelState.IsValid)
                {
                    // The call of Email/ForgotPassword, so the page has its limit: one mail per address every
                    // 5 minutes, shared with the API and taken before the user is looked up. Without it the
                    // page could flood an address with mails.
                    await _emailAPI.ForgotPasswordAsync(Application, model.Email);

                    // Don't reveal that the user does not exist or is not confirmed

                    return RedirectToAction("ForgotPasswordConfirmation", "Manage");
                }
            }
            catch (ApilaneException ex) when (ex.Error == AppErrors.RATE_LIMIT_EXCEEDED)
            {
                // Said of any address, with or without an account, as the limit is taken before the lookup
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ModelState.AddModelError(string.Empty, "Too many requests. Please wait a few minutes before asking for another email.");
            }
            catch (ApilaneException ex) when (ex.Error == AppErrors.VALIDATION)
            {
                // What the checks of the view model let through and the API's own check refuses
                ModelState.AddModelError(nameof(model.Email), ex.CustomMessage ?? "Invalid Email");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error on 'Manage/ForgotPassword' for application '{Application.Name}': {ex.Message}");
                return RedirectToAction("Error", "Manage");
            }

            // If we got this far, something failed, redisplay form

            return View(model);
        }

        [AllowAnonymous]
        public IActionResult Error()
        {
            ViewBag.Application = Application;

            return View();
        }

        [AllowAnonymous]
        public IActionResult ForgotPasswordConfirmation()
        {
            ViewBag.Application = Application;

            return View();
        }

        [AllowAnonymous]
        public IActionResult ResetPasswordConfirmation()
        {
            ViewBag.Application = Application;

            return View();
        }

        [AllowAnonymous]
		public async Task<IActionResult> ResetPassword(string Token)
		{
			try
			{
				var userId = await _applicationHelperService.GetUserIdFromPasswordResetTokenAsync(Token);

				if (userId is null)
				{
					return RedirectToAction("Error", "Manage");
				}
			}
			catch (Exception ex)
			{
				Log.Logger.Error(ex, $"Error on 'Get Manage/ResetPassword' for application '{Application.Name}': {ex.Message}");
				return RedirectToAction("Error", "Manage");
			}

			ViewBag.Application = Application;
			ViewBag.Token = Token;

			return View();
		}

		[HttpPost]
		[AllowAnonymous]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model, [FromQuery] string Token)
		{
			ViewBag.Application = Application;
			// The view builds the form address from it: without it a form shown again after a validation error
			// would post without the token.
			ViewBag.Token = Token;

			if (!ModelState.IsValid)
			{
				return View(model);
			}

			try
			{
				var userIdFromToken = await _applicationHelperService.GetUserIdFromPasswordResetTokenAsync(Token);

				if (userIdFromToken is null)
				{
					return RedirectToAction("Error", "Manage");
				}

				var userId = await GetUserIdByEmailAsync(model.Email);

				if (userId is not null && userId.Value == userIdFromToken.Value)
				{
					await _accountAPI.ResetPasswordAsync(Application, userId.Value, model.Password);
				}

				return RedirectToAction("ResetPasswordConfirmation", "Manage");
			}
			catch (Exception ex)
			{
				Log.Logger.Error(ex, $"Error on 'Post Manage/ResetPassword' for application '{Application.Name}': {ex.Message}");
				return RedirectToAction("Error", "Manage");
			}
		}

		// Private: a public method of a controller is an anonymous action (/App/{token}/Account/Manage/GetUserIdByEmail),
		// and this one answers with the ID of the user of an address, and with nothing for an unknown one
		private async Task<long?> GetUserIdByEmailAsync(string userEmail)
        {
            var result = await _applicationDataStoreFactory.GetPagedDataAsync(
                nameof(Users),
                new List<string>() { nameof(Users.ID) },
                new FilterData(nameof(Users.Email), FilterData.FilterOperators.equal, userEmail, PropertyType.String),
                null, 1, 1);

            return result?.Count == 1 ? Utils.GetNullLong(result.Single()[nameof(Users.ID)]) : null;
        }
    }
}
