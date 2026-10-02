using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Extensions;
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

        // The authentication token this request presented: the bearer token itself, or the key id of a
        // signed request whose signature was verified (a signed request does not carry the token).
        private Guid? _bearerAuthToken;
        private long? _signedAuthTokenId;

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
                    var grainRef = ClusterClient.GetAuthTokenUserGrain(Application.Token, guidAuthToken);
                    ApplicationUser = await grainRef.GetAsync(Application.ToDbInfo(ApiConfiguration.FilesPath), Application.AuthTokenExpireMinutes);
                    _bearerAuthToken = guidAuthToken;
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

            var user = await ClusterClient
                .GetAuthTokenByIdGrain(Application.Token, keyId)
                .VerifyAndGetUserAsync(
                    Application.ToDbInfo(ApiConfiguration.FilesPath),
                    Application.AuthTokenExpireMinutes,
                    timestampMs,
                    canonical,
                    providedSignature);

            if (user is not null)
            {
                // Only a verified signature may act on the token behind the key id (logout, renew)
                _signedAuthTokenId = keyId;
            }

            return user;
        }

        /// <summary>
        /// Deletes the authentication token this request was made with, whether it was sent as a bearer
        /// token or used to sign the request.
        /// </summary>
        protected async Task DeleteCurrentAuthTokenAsync()
        {
            var applicationDbInfo = Application.ToDbInfo(ApiConfiguration.FilesPath);

            if (_signedAuthTokenId.HasValue)
            {
                await ClusterClient.GetAuthTokenByIdGrain(Application.Token, _signedAuthTokenId.Value).DeleteAsync(applicationDbInfo);
            }
            else if (_bearerAuthToken.HasValue)
            {
                await ClusterClient.GetAuthTokenUserGrain(Application.Token, _bearerAuthToken.Value).DeleteAsync(applicationDbInfo);
            }
        }

        /// <summary>
        /// Drops the user cached for the authentication token this request was made with, so that changes
        /// to the user are visible on the next request.
        /// </summary>
        protected async Task ResetCurrentUserCacheAsync()
        {
            if (_signedAuthTokenId.HasValue)
            {
                await ClusterClient.GetAuthTokenByIdGrain(Application.Token, _signedAuthTokenId.Value).ResetUserCacheAsync(Application.ToDbInfo(ApiConfiguration.FilesPath));
            }
            else if (_bearerAuthToken.HasValue)
            {
                await ClusterClient.GetAuthTokenUserGrain(Application.Token, _bearerAuthToken.Value).ResetUserCacheAsync();
            }
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
