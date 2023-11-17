using Apilane.Api.Abstractions;
using Apilane.Api.Enums;
using Apilane.Api.Exceptions;
using Apilane.Api.Grains;
using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Web.Api.Filters;
using Apilane.Web.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Web.Api.Controllers
{
    [ApiController]
    [ServiceFilter(typeof(ApiExceptionFilter))]
    [Route("api/[controller]/[action]")]
    public class BaseApplicationApiController : Controller
    {
        protected readonly IClusterClient ClusterClient;

        public BaseApplicationApiController(IClusterClient clusterClient)
        {
            ClusterClient = clusterClient;
        }

        protected DBWS_Application Application = null!;
        protected bool UserHasFullAccess = false;
        protected Users? ApplicationUser = null;

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // On every call, validate user access to application

            // Validate token exists
            var queryService = context.HttpContext.RequestServices.GetService<IQueryDataService>() ?? throw new Exception($"Invalid service IQueryDataService");

            var applicationToken = queryService.AppToken;
            var authorizationToken = queryService.AuthToken;

            if (string.IsNullOrWhiteSpace(applicationToken))
            {
                throw new ApilaneException(AppErrors.ERROR, $"Query parameter '{Globals.ApplicationTokenQueryParam}' or Header '{Globals.ApplicationTokenHeaderName}' is required!");
            }

            // Load the application
            var applicationService = context.HttpContext.RequestServices.GetService<IApplicationService>() ?? throw new Exception($"Invalid service IApplicationService");
            Application = await applicationService.GetAsync(applicationToken);

            UserHasFullAccess = false;
            if (queryService.IsPortalRequest)
            {
                var portalInfoService = context.HttpContext.RequestServices.GetService<IPortalInfoService>() ?? throw new Exception($"Invalid service IPortalInfoService");
                UserHasFullAccess = await portalInfoService.UserOwnsApplicationAsync(authorizationToken, applicationToken);
            }

            // Validate
            if (!UserHasFullAccess)
            {
                if (!string.IsNullOrWhiteSpace(authorizationToken) &&
                    Guid.TryParse(authorizationToken, out var guidAuthToken))
                {
                    var grainRef = ClusterClient.GetGrain<IAuthTokenUserGrain>(guidAuthToken);
                    ApplicationUser = await grainRef.GetAsync(Application);
                }

                // Check limitations only for non-portal owners
                ValidateApplicationUserCanAccessTheApplication(
                    Application.Online,
                    (AppClientIPsLogics)Application.ClientIPsLogic,
                    Application.ClientIPsValue,
                    queryService.IPAddress);
            }

            await base.OnActionExecutionAsync(context, next);
        }

        protected DBWS_Entity GetEntity(string entityName)
        {
            return Application.Entities.SingleOrDefault(x => x.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ApilaneException(AppErrors.ERROR, $"Entity {entityName} does not exist");
        }

        private static void ValidateApplicationUserCanAccessTheApplication(
            bool isAppOnline,
            AppClientIPsLogics appClientIPsLogics,
            string? clientIPsValue,
            string ipAddress)
        {
            // Confirm application is online
            if (!isAppOnline)
            {
                throw new ApilaneException(AppErrors.SERVICE_UNAVAILABLE, "Application offline.");
            }

            // Confirm IP is allowed
            if (!IsClientIPAllowed(appClientIPsLogics, clientIPsValue, ipAddress))
            {
                throw new ApilaneException(AppErrors.SERVICE_UNAVAILABLE, $"Not allowed access from this IP '{ipAddress}'");
            }
        }

        private static bool IsClientIPAllowed(
            AppClientIPsLogics appClientIPsLogics,
            string? clientIPsValue,
            string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(clientIPsValue))
            {
                return true;
            }

            var ipList = clientIPsValue.Trim()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x));

            if (appClientIPsLogics == AppClientIPsLogics.Block && ipList.Any(x => x.Equals(ipAddress, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            else if (appClientIPsLogics == AppClientIPsLogics.Allow && !ipList.Any(x => x.Equals(ipAddress, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            return true;
        }
    }
}
