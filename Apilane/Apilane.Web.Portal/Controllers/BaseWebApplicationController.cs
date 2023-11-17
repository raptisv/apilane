using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Web.Portal.Abstractions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Web.Portal.Controllers
{
    public class BaseWebApplicationController : BaseWebController
    {
        protected DBWS_Application Application = null!;

        public BaseWebApplicationController(
            ApplicationDbContext dbContext,
            IApiHttpService apiHttpService)
            : base(dbContext, apiHttpService)
        {
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            var AppToken = Utils.GetString(RouteData.Values["appid"]);

            Application = Applications.SingleOrDefault(x => x.Token.Equals(AppToken))
                ?? throw new Exception($"Application with token '{AppToken}' not found");

            ViewBag.Application = Application;
        }

        public override void OnActionExecuted(ActionExecutedContext context)
        {
            base.OnActionExecuted(context);

            var controller = Utils.GetString(context.RouteData.Values["controller"]).ToLower();
            var action = Utils.GetString(context.RouteData.Values["action"]).ToLower();

            // After action, reset the application that has just been edited, excluding the following actions
            var resetApp = !(
                (controller.Equals("application") && action.Equals("delete")) ||
                (controller.Equals("customendpoints") && action.Equals("geturl")) ||
                controller.Equals("collaborate") ||
                controller.Equals("reports") ||
                action.Equals("index") ||
                Request.Method.ToLower().Equals("get"));

            if (Application is not null && resetApp)
            {
                Task.Run(async () => await ResetAppAsync(Application.Server.ServerUrl, Application.Token, PortalUserAuthToken));
            }
        }
    }
}
