using Apilane.Common.Extensions;
using Apilane.Common.Security;
using Apilane.Common.Utilities;
using Apilane.Portal.Extensions;
using Apilane.Portal.Extentions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Settings.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Portal
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var environment = EnvVariables.GetEnvironment("ASPNETCORE_ENVIRONMENT");

            Console.WriteLine(environment.ToAsciiArt());

            builder.Host.UseSerilog();

            // appsettings.json is committed and holds defaults plus local-development sample values
            // (Url, ApiUrl, FilesPath and a placeholder InstallationKey). The environment-specific
            // file (git-ignored) and environment variables override them with real values and secrets;
            // the docker-compose setup uses environment variables.
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddEnvironmentVariables()
            .Build();

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration, new ConfigurationReaderOptions()
                {
                    SectionName = "Serilog",
                    FormatProvider = null
                })
                // Attach TraceId/SpanId (from the current Activity) to every log event.
                .Enrich.WithSpan()
                .CreateLogger();

            var appConfig = new PortalConfiguration(configuration);

            // The installation key authenticates the API to the portal. A missing, default or short
            // value leaves every application's configuration readable by anyone who can reach the
            // portal, so complain loudly at startup. (On an existing installation the value actually
            // used is the one stored in the database, checked further below.)
            var configuredKeyProblem = InstallationKeyPolicy.Validate(appConfig.InstallationKey);
            if (configuredKeyProblem is not null)
            {
                Log.Logger.Warning("SECURITY: {Problem}. Set a long random 'InstallationKey' (identical on the API) via appsettings.{Environment}.json or an environment variable.", configuredKeyProblem, environment);
            }

            builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite($"Data Source={Path.Combine(appConfig.FilesPath, "Apilane.db")}");
                options.EnableSensitiveDataLogging(true);
                //options.ConfigureWarnings(x => x.Ignore(RelationalEventId.AmbientTransactionWarning));
            });

            builder.Services.AddHttpContextAccessor();

            builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            builder.Services.Configure<IdentityOptions>(options =>
            {
                // Password settings
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
            });

            builder.Services
                .AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(appConfig.FilesPath));

            builder.Services.ConfigureApplicationCookie(op =>
            {
                op.Cookie.Name = "Apilane.Portal.Identity";
                op.Cookie.Domain = appConfig.AuthCookieDomain;
                // Only /swagger still answers with these redirects; the API answers 401 and 403 (UseApiStatusCodes).
                // The sign-in page of the UI reads 'returnUrl' in exactly this spelling.
                op.LoginPath = new PathString("/account/login");
                op.ReturnUrlParameter = "returnUrl";
                // Never the sign-in page: it sends a signed-in user straight back, in a loop.
                op.AccessDeniedPath = new PathString("/");
                op.UseApiStatusCodes();
            });

            builder.Services
                .AddServices(appConfig)
                .AddPortalApi()
                .AddOpenTelemetry(appConfig.OpenTelemetry);

            // No naming policy and no enum converter: the API servers read the answers of
            // /api/internal with property names as they are in the models and enums as numbers.
            builder.Services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });

            builder.Services.AddHealthChecks();

            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            });

            if (appConfig.MinThreads.HasValue)
            {
                ThreadPool.GetMinThreads(out int minWorker, out int minIOC);
                if (ThreadPool.SetMinThreads(appConfig.MinThreads.Value, minIOC))
                {
                    Log.Logger.Information($"Successfully set min worker threads to {appConfig.MinThreads.Value}");
                }
                else
                {
                    Log.Logger.Information($"Failed to set min worker threads to {appConfig.MinThreads.Value}");
                }
            }

            var app = builder.Build();

            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run((context) => ExceptionHandlerAsync(context, Log.Logger));
            });

            using (var serviceScope = app.Services.GetService<IServiceScopeFactory>()!.CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Create database and seed data for new installations
                context.Database.EnsureCreated();

                // Apply schema updates for existing databases (new tables, indexes)
                context.EnsureSchemaUpdated();

                // The key used at runtime is the one stored in the portal database (Instance > Settings);
                // configuration only seeds it on first start.
                var storedKeyProblem = InstallationKeyPolicy.Validate(context.GlobalSettings.SingleOrDefault()?.InstallationKey);
                if (storedKeyProblem is not null)
                {
                    Log.Logger.Warning("SECURITY: {Problem} (value stored in the portal database). Change it under Instance > Settings and set the same value on the API.", storedKeyProblem);
                }
            }

            if (environment == Common.Enums.HostingEnvironment.Development)
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.UseSerilogRequestLogging();

            // Defensive response headers on every portal response, static files included.
            app.Use(async (context, next) =>
            {
                var headers = context.Response.Headers;

                static void SetIfMissing(IHeaderDictionary headers, string name, string value)
                {
                    if (!headers.ContainsKey(name))
                    {
                        headers[name] = value;
                    }
                }

                SetIfMissing(headers, "X-Content-Type-Options", "nosniff");
                SetIfMissing(headers, "X-Frame-Options", "SAMEORIGIN");
                SetIfMissing(headers, "Referrer-Policy", "strict-origin-when-cross-origin");
                // Script and style sources are not restricted yet.
                SetIfMissing(headers, "Content-Security-Policy", "frame-ancestors 'self'; object-src 'none'; base-uri 'self'");

                await next();
            });

            // The Prometheus scrape endpoint (before auth so /metrics is not behind login).
            // Without metrics there is no meter provider, and /metrics answers 404.
            if (appConfig.OpenTelemetry.Metrics.Enabled)
            {
                app.UseOpenTelemetryPrometheusScrapingEndpoint(context => context.Request.Path == "/metrics");
            }

            app.UseForwardedHeaders();

            // The built UI (wwwroot/ui) is the only folder served as files, at the site root.
            var uiFiles = PortalApiDependencyInjection.CreateUiFileProvider(app.Environment);

            app.UseStaticFiles(PortalApiDependencyInjection.CreateStaticFileOptions(uiFiles));

            app.UseRouting();

            app.UsePortalApiDefaults();

            app.UseAuthentication();

            app.UsePortalAgentKeys();

            app.UseAuthorization();

            app.UsePortalApiDocs();

            // The management API (/api/v1) and the internal API of the API servers (/api/internal).
            app.MapControllers();

            app.MapHealthChecks("/health/liveness", AspNetCoreExtensions.SetupHealthCheck("live"));
            app.MapHealthChecks("/health/readiness", AspNetCoreExtensions.SetupHealthCheck("ready"));

            // Last: everything that is not answered above is a screen of the UI.
            app.MapPortalApiAndUi(uiFiles);

            app.Run(appConfig.Url);
        }

        private static Task ExceptionHandlerAsync(HttpContext context, ILogger logger)
        {
            var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
            if (exceptionHandlerPathFeature != null)
            {
                var exception = exceptionHandlerPathFeature.Error;
                logger.Error(exception, $"APPLICATION ERROR | Path {exceptionHandlerPathFeature.Path} | Exception {exception.Message}");
            }

            return Task.CompletedTask;
        }
    }
}
