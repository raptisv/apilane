using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Grains;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Filters;
using Apilane.Api.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Controllers
{
    [ApiController]
    [ServiceFilter(typeof(ApiExceptionFilter))]
    [Route("api/[controller]/[action]")]
    public class BaseApplicationApiController : Controller
    {
        private static readonly ConcurrentDictionary<string, bool> _systemTablesMigratedTokens = new();

        protected readonly ApiConfiguration ApiConfiguration;
        protected readonly IClusterClient ClusterClient;

        public BaseApplicationApiController(
            ApiConfiguration apiConfiguration,
            IClusterClient clusterClient)
        {
            ApiConfiguration = apiConfiguration;
            ClusterClient = clusterClient;
        }

        protected DBWS_Application Application = null!;
        protected bool UserHasFullAccess = false;
        protected Users? ApplicationUser = null;
        protected ApplicationRateLimiter ApplicationRateLimiter = null!;

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // On every call, validate user access to application

            // Validate token exists
            var queryService = context.HttpContext.RequestServices.GetRequiredService<IQueryDataService>();

            var applicationToken = queryService.AppToken;
            var authorizationToken = queryService.AuthToken;

            if (string.IsNullOrWhiteSpace(applicationToken))
            {
                throw new ApilaneException(AppErrors.ERROR, $"Query parameter '{Globals.ApplicationTokenQueryParam}' or Header '{Globals.ApplicationTokenHeaderName}' is required!");
            }

            // Load the application
            var applicationService = context.HttpContext.RequestServices.GetRequiredService<IApplicationService>();
            Application = await applicationService.GetAsync(applicationToken);
            ApplicationRateLimiter = ApplicationRateLimiter.GetOrCreate(applicationToken);

            // Ensure system tables exist in the main database (migration for existing apps)
            if (!_systemTablesMigratedTokens.ContainsKey(applicationToken))
            {
                var skipMigration = context.ActionDescriptor.EndpointMetadata.OfType<SkipSystemTablesMigrationAttribute>().Any();

                if (!skipMigration)
                {
                    var applicationBuilder = context.HttpContext.RequestServices.GetRequiredService<IApplicationBuilderService>();
                    await applicationBuilder.EnsureSystemTablesAsync();
                    _systemTablesMigratedTokens.TryAdd(applicationToken, true);
                }
            }

            UserHasFullAccess = false;
            if (queryService.IsPortalRequest)
            {
                var portalInfoService = context.HttpContext.RequestServices.GetRequiredService<IPortalInfoService>();
                UserHasFullAccess = await portalInfoService.UserOwnsApplicationAsync(authorizationToken, applicationToken);
            }

            // Validate
            if (!UserHasFullAccess)
            {
                // A signed request proves possession of the token without transmitting it: the
                // signature is verified and the user resolved inside AuthTokenByIdGrain. Otherwise
                // fall back to the bearer token.
                if (context.HttpContext.Request.Headers.ContainsKey(Globals.AuthSignatureHeaderName))
                {
                    ApplicationUser = await ResolveSignedRequestUserAsync(context.HttpContext.Request);
                }
                else if (!string.IsNullOrWhiteSpace(authorizationToken) &&
                    Guid.TryParse(authorizationToken, out var guidAuthToken))
                {
                    var grainRef = ClusterClient.GetGrain<IAuthTokenUserGrain>(guidAuthToken);
                    ApplicationUser = await grainRef.GetAsync(Application.ToDbInfo(ApiConfiguration.FilesPath), Application.AuthTokenExpireMinutes);
                }

                // Check limitations only for non-portal owners
                ValidateApplicationUserCanAccessTheApplication(
                    Application.Online,
                    (AppClientIPsLogics)Application.ClientIPsLogic,
                    Application.ClientIPsValue,
                    queryService.IPAddress);

                await EnforceRateLimitAsync(context, queryService);
            }

            await base.OnActionExecutionAsync(context, next);
        }

        private async Task EnforceRateLimitAsync(ActionExecutingContext context, IQueryDataService queryService)
        {
            var entityOrEndpoint = string.IsNullOrWhiteSpace(queryService.Entity)
                ? queryService.CustomEndpoint
                : queryService.Entity;

            if (string.IsNullOrWhiteSpace(entityOrEndpoint))
                return;

            if (!Enum.TryParse<SecurityActionType>(queryService.RouteAction, ignoreCase: true, out var actionType))
                return;

            var rateLimitItems = Application.Security_List
                .Where(s =>
                    s.Name.Equals(entityOrEndpoint, StringComparison.OrdinalIgnoreCase) &&
                    s.Action.Equals(actionType.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(s => s.RateLimit)
                .ToList();

            if (!rateLimitItems.IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
                return;

            var userIdentifier = ApplicationUser?.ID.ToString();

            var permitted = await ApplicationRateLimiter.TryAcquireAsync(
                maxRequests,
                timeWindow,
                userIdentifier,
                entityOrEndpoint,
                actionType.ToString(),
                context.HttpContext.RequestAborted);

            if (!permitted)
                throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED);
        }

        protected DBWS_Entity GetEntity(string entityName)
        {
            return Application.Entities.SingleOrDefault(x => x.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ApilaneException(AppErrors.ERROR, $"Entity {entityName} does not exist");
        }

        /// <summary>
        /// Builds the canonical string from the incoming signed request and resolves the user via
        /// the signed-auth grain. Throws <see cref="ApilaneException"/> with UNAUTHORIZED (carrying
        /// the failure reason) when the request is not authentic.
        /// </summary>
        private async Task<Users?> ResolveSignedRequestUserAsync(HttpRequest request)
        {
            var keyIdStr = request.Headers[Globals.AuthKeyIdHeaderName].ToString();
            var timestampStr = request.Headers[Globals.AuthTimestampHeaderName].ToString();
            var providedSignature = request.Headers[Globals.AuthSignatureHeaderName].ToString();

            if (string.IsNullOrWhiteSpace(keyIdStr) ||
                string.IsNullOrWhiteSpace(timestampStr) ||
                string.IsNullOrWhiteSpace(providedSignature))
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, "Incomplete signed-request headers");
            }

            if (!long.TryParse(keyIdStr, out var keyId) ||
                !long.TryParse(timestampStr, out var timestampMs))
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, "Invalid signed-request headers");
            }

            var body = await ReadBodyAsync(request);
            var canonical = RequestSignature.BuildCanonicalString(
                keyId.ToString(),
                request.Method,
                UriHelper.GetEncodedPathAndQuery(request),
                timestampMs.ToString(),
                body);

            return await ClusterClient
                .GetGrain<IAuthTokenByIdGrain>(keyId)
                .VerifyAndGetUserAsync(
                    Application.ToDbInfo(ApiConfiguration.FilesPath),
                    Application.AuthTokenExpireMinutes,
                    timestampMs,
                    canonical,
                    providedSignature);
        }

        private static async Task<byte[]> ReadBodyAsync(Microsoft.AspNetCore.Http.HttpRequest request)
        {
            // Buffering is enabled upstream for signed requests, so the body stream is seekable
            // and can be re-read here after model binding has already consumed it.
            if (request.Body is null || !request.Body.CanSeek)
            {
                return Array.Empty<byte>();
            }

            request.Body.Position = 0;
            using var ms = new System.IO.MemoryStream();
            await request.Body.CopyToAsync(ms);
            request.Body.Position = 0;
            return ms.ToArray();
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
