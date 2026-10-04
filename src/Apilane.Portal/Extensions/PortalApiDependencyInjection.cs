using Apilane.Common.Security;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.Internal;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Apilane.Portal.Extensions
{
    /// <summary>
    /// Everything the management API (/api/v1), the internal API of the API servers
    /// (/api/internal) and the Portal UI add to the host.
    /// </summary>
    public static class PortalApiDependencyInjection
    {
        // The folder under the web root that holds the built UI. It is a folder, not an address:
        // its content is served at the site root.
        private const string UiFolder = "ui";
        private const string UiIndexFile = "index.html";
        private const string UiAssetsPath = "/assets";
        private const string SwaggerPath = "/swagger";

        // Where an agent key counts. Not /api/internal: there a Bearer value is a user's API-server token.
        private const string AgentApiPath = PortalApiErrors.PathPrefix + "/v1";
        private const string BearerScheme = "Bearer";

        // The only addresses the Portal answers itself; every other address belongs to the UI.
        // Under these an unknown address is 404, never the UI's index page: an API server or a
        // monitor must not get a page with status 200.
        // The dev server of the UI forwards the same addresses to the Portal (vite.config.ts).
        private static readonly PathString[] _serverPaths =
        {
            PortalApiErrors.PathPrefix,
            SwaggerPath,
            "/health",
            "/metrics"
        };

        public static IServiceCollection AddPortalApi(this IServiceCollection services)
        {
            services
                .AddScoped<IPortalAccessService, PortalAccessService>()
                .AddScoped<IServerService, ServerService>()
                .AddScoped<IAdminApplicationService, AdminApplicationService>()
                .AddScoped<IUserService, UserService>()
                .AddScoped<IInstanceSettingsService, InstanceSettingsService>()
                .AddScoped<IBackupService, BackupService>()
                .AddScoped<IAuditLogService, AuditLogService>()
                .AddScoped<IPortalAccountService, PortalAccountService>()
                .AddScoped<IPortalMailService, PortalMailService>()
                .AddScoped<IPortalLinkBuilder, PortalLinkBuilder>()
                .AddScoped<IApplicationAccessService, ApplicationAccessService>()
                .AddScoped<IApplicationService, ApplicationService>()
                .AddScoped<IApplicationProvisioningService, ApplicationProvisioningService>()
                .AddScoped<IApiServerClient, ApiServerClient>()
                .AddScoped<IApiServerCacheReset, ApiServerCacheReset>()
                .AddScoped<IApplicationWriteScope, ApplicationWriteScope>()
                .AddScoped<IEntityManagementService, EntityManagementService>()
                .AddScoped<IPropertyManagementService, PropertyManagementService>()
                .AddScoped<IEntityConstraintService, EntityConstraintService>()
                .AddScoped<IEntityDefaultOrderService, EntityDefaultOrderService>()
                .AddScoped<ICollaboratorService, CollaboratorService>()
                .AddScoped<IApplicationEmailSettingsService, ApplicationEmailSettingsService>()
                .AddScoped<ISecurityRulesService, SecurityRulesService>()
                .AddScoped<ICustomEndpointService, CustomEndpointService>()
                .AddScoped<IReportService, ReportService>()
                .AddScoped<ISchemaImportService, SchemaImportService>()
                .AddScoped<IApplicationComparisonService, ApplicationComparisonService>()
                .AddScoped<IApplicationCloneService, ApplicationCloneService>()
                .AddScoped<PortalSessionFilter>()
                .AddScoped<PortalApiExceptionFilter>()
                .AddScoped<InstallationKeyFilter>()
                .AddSingleton<PortalCsrfFilter>()
                // Replaces the validator AddIdentity registered, so a cookie refresh keeps its session token.
                .AddScoped<ISecurityStampValidator, PortalSecurityStampValidator>()
                // Registered before AddControllers, which only adds its ProblemDetails factory when none exists.
                .AddSingleton<IClientErrorFactory, PortalClientErrorFactory>();

            // An invalid request body gets the same error body as every other failure.
            // PostConfigure, because AddControllers sets its own factory after this method has run.
            services.PostConfigure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                    PortalApiErrors.ValidationResult(context.HttpContext, context.ModelState, context.ActionDescriptor);
            });

            // Checked at startup: the limiter throws on such values, which would turn every sign-in into a 500.
            services.AddOptions<PortalRateLimitOptions>()
                .BindConfiguration(PortalRateLimitOptions.ConfigurationSection)
                .Validate(x => x.PermitLimit > 0 && x.WindowSeconds > 0, "AccountRateLimit: PermitLimit and WindowSeconds must be greater than 0.")
                .ValidateOnStart();

            // Sign in, register and the password-reset request can be called by anyone, so each IP
            // address gets a small budget for the three together. Only actions that carry the
            // policy are limited; the middleware runs for /api requests only (UsePortalApiDefaults).
            // The address is the connection's remote address: behind a proxy that is not on loopback
            // all visitors share the proxy's budget (see PortalRateLimitOptions for the setting to use).
            services.AddRateLimiter(options =>
            {
                options.AddPolicy(PortalRateLimitOptions.AccountPolicy, context =>
                {
                    var limits = context.RequestServices.GetRequiredService<IOptions<PortalRateLimitOptions>>().Value;

                    return RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limits.PermitLimit,
                            Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                            QueueLimit = 0
                        });
                });

                options.OnRejected = async (context, _) =>
                {
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers[HeaderNames.RetryAfter] = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                    }

                    await PortalApiErrors.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status429TooManyRequests,
                        PortalErrorCode.TooManyRequests,
                        "Too many attempts. Wait a moment and try again.");
                };
            });

            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Version = "v1",
                    Title = "Apilane Portal API",
                    Description = "Management API of an Apilane instance: servers, applications, entities and their settings. " +
                        "Records, files and custom endpoints of an application are served by the Apilane API server. " +
                        $"Every POST, PUT, PATCH and DELETE request must send the header '{PortalCsrfFilter.HeaderName}: {PortalCsrfFilter.HeaderValue}'; " +
                        "without it the answer is 403 FORBIDDEN. " +
                        "A script or an AI agent sends an agent key instead of the session cookie, as 'Authorization: Bearer {key}', and needs no such header " +
                        "(an administrator creates agents with POST /api/v1/admin/agents). An agent key is refused with 403 FORBIDDEN on every DELETE, " +
                        "on everything under /api/v1/admin, and on the calls that create, import, clone or rebuild an application, read its encryption key, " +
                        "return the API-server token, sign in, register or set a password."
                });

                // Only /api/v1 is the contract: /api/internal is for the API servers.
                options.DocInclusionPredicate((_, api) =>
                    (api.RelativePath ?? string.Empty).StartsWith("api/v1", StringComparison.OrdinalIgnoreCase));

                options.CustomOperationIds(api =>
                    $"{api.ActionDescriptor.RouteValues["controller"]}_{api.ActionDescriptor.RouteValues["action"]}");

                options.SupportNonNullableReferenceTypes();

                options.OperationFilter<PortalFileOperationFilter>();
                options.OperationFilter<PortalWarningHeaderOperationFilter>();

                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml"));

                // The same document on Windows and Linux, whatever line ending the comments were written with.
                options.SwaggerGeneratorOptions.XmlCommentEndOfLine = "\n";
            });

            return services;
        }

        /// <summary>
        /// API calls must never be answered with a redirect to the login page: they get 401/403 and
        /// the standard error body. Everything else keeps the default cookie behaviour.
        /// </summary>
        public static void UseApiStatusCodes(this CookieAuthenticationOptions options)
        {
            var redirectToLogin = options.Events.OnRedirectToLogin;
            var redirectToAccessDenied = options.Events.OnRedirectToAccessDenied;

            options.Events.OnRedirectToLogin = context =>
            {
                return PortalApiErrors.IsApiRequest(context.HttpContext)
                    ? PortalApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, PortalErrorCode.Unauthorized, PortalApiErrors.UnauthorizedMessage)
                    : redirectToLogin(context);
            };

            options.Events.OnRedirectToAccessDenied = async context =>
            {
                if (!PortalApiErrors.IsApiRequest(context.HttpContext))
                {
                    await redirectToAccessDenied(context);
                    return;
                }

                // Role checks run in the authorization middleware, before PortalSessionFilter.
                // A revoked session is 401 whatever its roles.
                var access = context.HttpContext.RequestServices.GetRequiredService<IPortalAccessService>();

                if (await access.FindCurrentUserAsync() is null)
                {
                    await PortalApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized, PortalErrorCode.Unauthorized, PortalApiErrors.UnauthorizedMessage);
                    return;
                }

                await PortalApiErrors.WriteAsync(context.HttpContext, StatusCodes.Status403Forbidden, PortalErrorCode.Forbidden, PortalApiErrors.ForbiddenMessage);
            };
        }

        /// <summary>
        /// What every API request gets whatever happens to it: answers are never stored by browsers
        /// or proxies, and an exception thrown outside an action (cookie validation, the session
        /// filter) still produces the standard error body. Also where the rate limit of the
        /// anonymous account endpoints is applied. Call after UseRouting and before UseAuthentication.
        /// </summary>
        public static IApplicationBuilder UsePortalApiDefaults(this IApplicationBuilder app)
        {
            var isDevelopment = app.ApplicationServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

            app.UseWhen(PortalApiErrors.IsApiRequest, branch =>
            {
                branch.UseExceptionHandler(error => error.Run(context =>
                {
                    // Same rule as PortalApiExceptionFilter: the detail is for developers only.
                    var message = isDevelopment
                        ? context.Features.Get<IExceptionHandlerFeature>()?.Error.Message ?? PortalApiErrors.ErrorMessage
                        : PortalApiErrors.ErrorMessage;

                    context.Response.Headers[HeaderNames.CacheControl] = "no-store";

                    return PortalApiErrors.WriteAsync(context, StatusCodes.Status500InternalServerError, PortalErrorCode.Error, message);
                }));

                branch.Use(async (context, next) =>
                {
                    context.Response.Headers[HeaderNames.CacheControl] = "no-store";
                    await next();
                });

                // Inside the /api branch, so nothing outside the API can ever be limited.
                branch.UseRateLimiter();
            });

            return app;
        }

        /// <summary>
        /// Agent keys (see <see cref="PortalAgent"/>). A request under /api/v1 that sends
        /// 'Authorization: Bearer ...' is authenticated by that value alone: a valid key makes it the
        /// agent's request, whatever cookie came with it, and anything else is 401 with one body.
        /// Right after that, the one check of what an agent may not call: any DELETE, anything under
        /// /api/v1/admin, and the actions marked <see cref="NoAgentAttribute"/>.
        /// Call after UseAuthentication and before UseAuthorization.
        /// </summary>
        public static IApplicationBuilder UsePortalAgentKeys(this IApplicationBuilder app)
        {
            app.UseWhen(
                context => context.Request.Path.StartsWithSegments(AgentApiPath)
                    && context.Request.Headers.Authorization.ToString().StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase),
                branch => branch.Use(async (context, next) =>
                {
                    var agent = await FindAgentByKeyAsync(context);

                    if (agent is null)
                    {
                        await PortalApiErrors.WriteAsync(context, StatusCodes.Status401Unauthorized, PortalErrorCode.Unauthorized, PortalAgent.InvalidKeyMessage);
                        return;
                    }

                    // The claims of the login cookie (AppClaimsPrincipalFactory), so everything after
                    // this sees a normal user. No role claim: an agent is never an administrator.
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[]
                        {
                            new Claim("Id", agent.Id),
                            new Claim("PortalUserAuthToken", agent.AdminAuthToken ?? string.Empty),
                            new Claim("UserEmail", agent.Email ?? string.Empty)
                        },
                        PortalAgent.AuthenticationType));

                    if (HttpMethods.IsDelete(context.Request.Method)
                        || context.Request.Path.StartsWithSegments("/" + PortalAdminApiControllerBase.RoutePrefix)
                        || context.GetEndpoint()?.Metadata.GetMetadata<NoAgentAttribute>() is not null)
                    {
                        await PortalApiErrors.WriteAsync(context, StatusCodes.Status403Forbidden, PortalErrorCode.Forbidden, PortalAgent.RefusedMessage);
                        return;
                    }

                    await next();
                }));

            return app;
        }

        // The agent a Bearer value belongs to, or null: no key, a malformed one, an unknown KeyId
        // and a wrong secret all look the same to the caller.
        private static async Task<ApplicationUser?> FindAgentByKeyAsync(HttpContext context)
        {
            var key = context.Request.Headers.Authorization.ToString().Substring(BearerScheme.Length).Trim();

            if (!PortalAgent.TryReadKey(key, out var keyId, out var secretHash))
            {
                return null;
            }

            var dbContext = context.RequestServices.GetRequiredService<ApplicationDbContext>();

            var stored = await dbContext.AgentKeys.AsNoTracking().FirstOrDefaultAsync(x => x.KeyId == keyId);

            // Compared in constant time.
            if (stored is null || !SecureCompare.AreEqual(stored.SecretHash, secretHash))
            {
                return null;
            }

            var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == stored.UserId);

            return user is not null && PortalAgent.IsAgent(user.Email) ? user : null;
        }

        /// <summary>
        /// The files of the built UI: the folder 'ui' under the web root. The folder is created when
        /// it is missing (the UI has not been built yet), so a build made while the Portal runs is
        /// served without a restart. Without a web root (the Portal was started from a folder that
        /// has no 'wwwroot') nothing is served, and the API still starts.
        /// </summary>
        public static IFileProvider CreateUiFileProvider(IWebHostEnvironment environment)
        {
            if (string.IsNullOrEmpty(environment.WebRootPath))
            {
                return new NullFileProvider();
            }

            var folder = Path.Combine(environment.WebRootPath, UiFolder);

            Directory.CreateDirectory(folder);

            return new PhysicalFileProvider(folder);
        }

        /// <summary>
        /// Serves the built UI at the site root. Vite puts a content hash in every file name under
        /// /assets, so those never change; everything else is revalidated.
        /// </summary>
        public static StaticFileOptions CreateStaticFileOptions(IFileProvider uiFiles)
        {
            return new StaticFileOptions
            {
                FileProvider = uiFiles,
                OnPrepareResponse = context =>
                {
                    context.Context.Response.Headers[HeaderNames.CacheControl] =
                        context.Context.Request.Path.StartsWithSegments(UiAssetsPath)
                            ? "public, max-age=31536000, immutable"
                            : "no-cache";
                }
            };
        }

        /// <summary>
        /// Serves the API contract and its browser at /swagger to signed-in users only.
        /// Call after UseAuthentication.
        /// </summary>
        public static IApplicationBuilder UsePortalApiDocs(this IApplicationBuilder app)
        {
            app.UseWhen(
                context => context.Request.Path.StartsWithSegments(SwaggerPath),
                branch => branch.Use(async (context, next) =>
                {
                    // The same rule as the API: a cookie whose session was revoked is not signed in.
                    var access = context.RequestServices.GetRequiredService<IPortalAccessService>();

                    if (await access.FindCurrentUserAsync() is null)
                    {
                        await context.ChallengeAsync();
                        return;
                    }

                    await next();
                }));

            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint($"{SwaggerPath}/v1/swagger.json", "Apilane Portal API v1");
                options.DocumentTitle = "Apilane Portal API";
            });

            return app;
        }

        /// <summary>
        /// Maps the Portal UI and the API's "no such route" answer. Call after every other route.
        /// </summary>
        public static void MapPortalApiAndUi(this WebApplication app, IFileProvider uiFiles)
        {
            // Any GET that is neither a file nor one of the Portal's own addresses is a screen of the
            // UI: its index page handles it. "Not a file" means the last segment has no dot, so
            // client routes must not end in one.
            app.MapFallback("{*path:nonfile}", async context =>
            {
                if (_serverPaths.Any(x => context.Request.Path.StartsWithSegments(x)))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var index = uiFiles.GetFileInfo(UiIndexFile);

                if (!index.Exists)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync(
                        "The Portal UI has not been built. In src/Apilane.Portal.Ui run 'npm ci' once, then 'npm run build' " +
                        "(or 'npm run dev' and browse http://localhost:5173/).");
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                await context.Response.SendFileAsync(index);
            })
            .WithMetadata(new HttpMethodMetadata(new[] { HttpMethods.Get, HttpMethods.Head }));

            // An unknown API route is a JSON 404, never the UI's index page.
            // A known route called with the wrong method ends here too.
            app.MapFallback($"{PortalApiErrors.PathPrefix}/{{**path}}", context =>
                PortalApiErrors.WriteAsync(context, StatusCodes.Status404NotFound, PortalErrorCode.NotFound, "Unknown API route or method."));
        }
    }
}
