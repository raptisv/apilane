using Apilane.Api.Abstractions;
using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Web.Api.Areas.Account.Models;
using Apilane.Web.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Web.Api.Areas.Account.Controllers
{
    [Area("Account")]
    public class ManageController : Controller
    {
        private readonly ILogger<ManageController> _logger;
        private readonly IApplicationEmailService _appEmailService;
        private readonly IApplicationHelperService _applicationHelperService;
        private readonly IApplicationDataStoreFactory _applicationDataStoreFactory;
        protected DBWS_Application Application = null!;

        public ManageController(
            ILogger<ManageController> logger,
            IApplicationEmailService appEmailService,
            IApplicationHelperService applicationHelperService,
            IApplicationDataStoreFactory aplicationDataStoreFactory)
        {
            _logger = logger;
            _appEmailService = appEmailService;
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
                    var userId = await GetUserIdByEmailAsync(model.Email);

                    if (userId is not null)
                    {
                        var drUserThatAcceptsTheEmail = await GetUserByIdAsync(userId.Value);

                        if (drUserThatAcceptsTheEmail is not null)
                        {
                            await _appEmailService.SendEmailFromApplicationAsync(
                                    Application.Token,
                                    Application.Server.ServerUrl,
                                    Application.GetEmailSettings(),
                                    EmailEventsCodes.UserForgotPassword,
                                    drUserThatAcceptsTheEmail,
                                    drUserThatAcceptsTheEmail);
                        }
                    }

                    // Don't reveal that the user does not exist or is not confirmed

                    return RedirectToAction("ForgotPasswordConfirmation", "Manage");
                }
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
				var userId = await _applicationHelperService.GetUserIdFromPasswordResetTokenAsync(Application.Token, Token);

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

			if (!ModelState.IsValid)
			{
				return View(model);
			}

			try
			{
				var userIdFromToken = await _applicationHelperService.GetUserIdFromPasswordResetTokenAsync(Application.Token, Token);

				if (userIdFromToken is null)
				{
					return RedirectToAction("Error", "Manage");
				}

				var userId = await GetUserIdByEmailAsync(model.Email);

				if (userId is not null && userId.Value == userIdFromToken.Value)
				{
					await _applicationDataStoreFactory.UpdateDataAsync(
						nameof(Users),
						new Dictionary<string, object?>()
						{
							{ nameof(Users.Password), Application.EncryptionKey.ApplicationEncrypt(model.Password) }
						},
						new FilterData(nameof(Users.ID), FilterData.FilterOperators.equal, userId.Value, PropertyType.Number));

					await _applicationHelperService.DeletePasswordResetTokenAsync(Application.Token, Token);
				}

				return RedirectToAction("ResetPasswordConfirmation", "Manage");
			}
			catch (Exception ex)
			{
				Log.Logger.Error(ex, $"Error on 'Post Manage/ResetPassword' for application '{Application.Name}': {ex.Message}");
				return RedirectToAction("Error", "Manage");
			}
		}

		public async Task<long?> GetUserIdByEmailAsync(string userEmail)
        {
            var result = await _applicationDataStoreFactory.GetPagedDataAsync(
                nameof(Users),
                new List<string>() { nameof(Users.ID) },
                new FilterData(nameof(Users.Email), FilterData.FilterOperators.equal, userEmail, PropertyType.String),
                null, 1, 1);

            return result?.Count == 1 ? Utils.GetNullLong(result.Single()[nameof(Users.ID)]) : null;
        }

        private async Task<Dictionary<string, object?>?> GetUserByIdAsync(long userID)
        {
            var result = await _applicationDataStoreFactory.GetPagedDataAsync(
                nameof(Users),
                new List<string>() { nameof(Users.ID), nameof(Users.Username), nameof(Users.Email), nameof(Users.EmailConfirmed), nameof(Users.Roles), nameof(Users.Created), nameof(Users.LastLogin), nameof(Users.Password) },
                new FilterData(nameof(Users.ID), FilterData.FilterOperators.equal, userID, PropertyType.Number),
                null, 1, 1);

            return result?.Count == 1 ? result.Single() : null;
        }
    }
}