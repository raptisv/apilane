using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Portal.Controllers
{
    [Authorize]
    public class BaseWebController : Controller
    {
        protected readonly ApplicationDbContext DBContext;
        protected readonly IApiHttpService ApiHttpService;

        protected List<DBWS_Application> Applications = null!;
        protected string PortalUserAuthToken = null!;

        public BaseWebController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
        { 
            DBContext = dbContext;
            ApiHttpService = apiHttpService;
        }

        protected ApplicationUser GetUser(string userId)
        {
            var currentUser = DBContext.Users.FirstOrDefault(x => x.Id == userId)
                ?? throw new Exception("User not found");

            return currentUser;
        }

        protected async Task ResetAppAsync(string serverUrl, string appToken, string portalUserAuthToken)
        {
            var apiResponse = await ApiHttpService.GetAsync($"{serverUrl.Trim('/')}/api/Application/ClearCache", appToken, portalUserAuthToken);

            apiResponse.Match(
                jsonString => "OK",
                errorHttpStatusCode =>
                {
                    throw new Exception($"Could not get a response from Api server | Error code {errorHttpStatusCode}");
                }
            );
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            var AppToken = Utils.GetString(RouteData.Values["appid"]);
            var EntityName = Utils.GetString(RouteData.Values["entid"]);
            var PropertyName = Utils.GetString(RouteData.Values["propid"]);

            var controller = Utils.GetString(context.RouteData.Values["controller"]).ToLower();
            var action = Utils.GetString(context.RouteData.Values["action"]).ToLower();

            // This is the stored AdminAuthToken
            PortalUserAuthToken = User.Identity?.GetPortalUserAuthToken()
                ?? throw new Exception("User not logged in");

            // Get the latest user data
            var currentUser = GetUser(User.Identity.GetUserId());

            // Check if the user logged in
            if (currentUser == null || !(currentUser.AdminAuthToken ?? string.Empty).Equals(PortalUserAuthToken))
            {
                context.Result = new RedirectResult("~/Account/Login");
                return;
            }

            if (controller.Equals("account"))
            {
                if (action.Equals("login") ||
                    action.Equals("register") ||
                    action.Equals("logoff") ||
                    action.Equals("info"))
                {
                    return;
                }
            }

            Applications = GetUserApplications(currentUser)
                .OrderByDescending(x => x.UserID.Equals(User.Identity.GetUserId()))
                .ThenBy(x => x.Name).ToList();

            ViewBag.Applications = Applications;
        }

        private List<DBWS_Application> GetUserApplications(ApplicationUser user)
        {
            // Get shared app Ids
            var userCollaborateAppIds = DBContext.Collaborations
                .Where(x => x.UserEmail == user.Email)
                .Select(c => c.AppID)
                .ToList();

            // Get user and shared apps
            var userApplications = DBContext.Applications
                .Include(a => a.Collaborates)
                .Include(a => a.CustomEndpoints)
                .Include(a => a.Server)
                .Include(a => a.Entities)
                .ThenInclude(e => e.Properties)
                .AsSplitQuery()
                .ToList()
                .Where(x => x.UserID == user.Id || userCollaborateAppIds.Contains(x.ID))
                .ToList();

            return userApplications;
        }
    }
}