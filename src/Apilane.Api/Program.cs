using Apilane.Api.Core.Configuration;
using Apilane.Common;
using Apilane.Common.Extensions;
using Apilane.Common.Utilities;
using Apilane.Api.Controllers;
using Apilane.Api.Extensions;
using Apilane.Api.Extentions;
using Apilane.Api.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Settings.Configuration;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                var builder = WebApplication.CreateBuilder(args);

                var environment = EnvVariables.GetEnvironment("ASPNETCORE_ENVIRONMENT");

                Console.WriteLine(environment.ToAsciiArt());

                builder.Host.UseSerilog();

                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile($"appsettings.{environment}.json", optional: false)
                    .AddEnvironmentVariables()
                    .Build();

                Log.Logger = new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration, new ConfigurationReaderOptions()
                    {
                        SectionName = "Serilog",
                        FormatProvider = null
                    }).CreateLogger();

                var appConfig = new ApiConfiguration(configuration);

                builder.AddOrleans(appConfig);

                builder.Services
                .AddMemoryCache()
                .AddMvc()
                .AddMvcOptions(options =>
                {
                    options.EnableEndpointRouting = false;
                });

                builder.Services
                .AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = null;
                    options.JsonSerializerOptions.Converters.Add(new CustomJsonConverterForType());
                    //options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                });

                builder.Services.AddSwaggerGen(c =>
                {
                    c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
                    c.IgnoreObsoleteActions();
                    c.IgnoreObsoleteProperties();
                    c.CustomSchemaIds(s => s.FullName!.Replace("+", "."));
                    c.SwaggerDoc("v1", new OpenApiInfo
                    {
                        Version = "v1",
                        Title = "Apilane API"
                    });
                    c.OperationFilter<DefaultHeaderFilter>();

					c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
					{
						Description = @"Authorization header using the Bearer scheme.
						Enter 'Bearer' [space] and then your token in the text input below.
						Example: 'Bearer 12345abcdef'",
						Name = "Authorization",
						In = ParameterLocation.Header,
						Type = SecuritySchemeType.ApiKey,
						Scheme = "Bearer"
					});

					c.AddSecurityRequirement(new OpenApiSecurityRequirement()
					{
						{
							new OpenApiSecurityScheme
							{
								Reference = new OpenApiReference
								{
									Type = ReferenceType.SecurityScheme,
									Id = "Bearer"
								},
								Scheme = "oauth2",
								Name = "Bearer",
								In = ParameterLocation.Header,
							},
							new List<string>()
						}
					});

					var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                    c.IncludeXmlComments(xmlPath);
                });

                builder.Services
                    .AddServices(appConfig);

                builder.Services.Configure<ApiBehaviorOptions>(options =>
                {
                    // Suppress model validations for objects sent from portal (e.g. entity creation)
                    options.SuppressModelStateInvalidFilter = true;
                });

                builder.Services.AddHealthChecks()
                    .AddCheck<PortalConnectivityHealthCheck>("Portal", tags: new string[] { "ready" });

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

                if (environment == Common.Enums.HostingEnvironment.Development)
                {
                    app.UseDeveloperExceptionPage();
                }

                app.UseSwagger();
                app.UseSwaggerUI((c) =>
                {
                    c.DefaultModelsExpandDepth(-1); // Disable swagger schemas at bottom

                    // Shows the application name (passed as ?appName= when opening Swagger from
                    // the portal) as a banner on the UI.
                    c.InjectJavascript("/swagger-custom.js");

                    // When Swagger is opened from a specific application (?appToken=...), apply that
                    // token to every request automatically — including the swagger.json fetch, so the
                    // appToken field is pre-filled (see DefaultHeaderFilter).
                    c.UseRequestInterceptor(
                        "(req) => { try { var t = new URLSearchParams(window.location.search).get('appToken'); " +
                        "if (t && !/[?&]appToken=[^&]/.test(req.url)) { req.url += (req.url.indexOf('?') < 0 ? '?' : '&') + 'appToken=' + encodeURIComponent(t); } } catch (e) {} return req; }");
                });

                app.UseSerilogRequestLogging();

                app.UseStaticFiles();

                app.UseRouting();

                app.UseCors(builder =>
                {
                    builder.AllowAnyOrigin();
                    builder.AllowAnyMethod();
                    builder.AllowAnyHeader();
                });

                app.Use(async (ctx, next) =>
                {
                    if (ctx.Request.Path.ToString().Contains("files/download", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Response.Headers["Cache-Control"] = "max-age=31536000";
                    }

                    if (ctx.Request.Headers.ContainsKey(Globals.AuthSignatureHeaderName))
                    {
                        // Signing requires hashing the request body. Multipart (file) uploads are not
                        // supported because verifying them would force buffering the whole upload, so
                        // reject them here — before the body is read — rather than buffering then failing.
                        var contentType = ctx.Request.ContentType;
                        if (!string.IsNullOrEmpty(contentType) &&
                            contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            ctx.Response.ContentType = "application/json";
                            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
                            {
                                Code = "UNAUTHORIZED", // matches AppErrors.UNAUTHORIZED.ToString() / ValidationError.UNAUTHORIZED
                                Message = "Request signing is not supported for file uploads.",
                                Property = (string?)null,
                                Entity = (string?)null
                            }));
                            return;
                        }

                        // Otherwise the raw body must be re-readable after model binding to verify the
                        // body hash. Enable buffering only for signed requests to avoid the cost elsewhere.
                        ctx.Request.EnableBuffering();
                    }

                    await next();
                });

                app.MapControllerRoute(
                    name: "areas",
                    pattern: "app/{apptoken}/{area:exists}/{controller}/{action=Index}/{id?}");

                app.MapHealthChecks("/health/liveness", AspNetCoreExtensions.SetupHealthCheck("live"));
                app.MapHealthChecks("/health/readiness", AspNetCoreExtensions.SetupHealthCheck("ready"));

                app.UseOpenTelemetryPrometheusScrapingEndpoint(context => context.Request.Path == "/metrics");

                app.Map("/", (c) =>
                {
                    return c.Response.WriteAsync(appConfig.Environment.ToAsciiArt("live"));
                });

                app.Map("/Version", (c) =>
                {
                    c.Response.ContentType = "application/json";
                    return c.Response.WriteAsync(JsonSerializer.Serialize(new { Version = Versioning.GetVersion(Assembly.GetExecutingAssembly()) }));
                });

                app.Run(appConfig.Url);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
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

        private class DefaultHeaderFilter : IOperationFilter
        {
            private readonly IHttpContextAccessor _httpContextAccessor;

            public DefaultHeaderFilter(IHttpContextAccessor httpContextAccessor)
            {
                _httpContextAccessor = httpContextAccessor;
            }

            public void Apply(OpenApiOperation operation, OperationFilterContext context)
            {
                if (context.ApiDescription.TryGetMethodInfo(out var method))
                {
                    // The request interceptor forwards ?appToken=... onto the swagger.json fetch, so
                    // pre-fill the field with that token when Swagger was opened for a specific app.
                    var appToken = _httpContextAccessor.HttpContext?.Request.Query[Globals.ApplicationTokenQueryParam].ToString();

                    operation.Parameters.Insert(0, new OpenApiParameter
                    {
                        Name = Globals.ApplicationTokenQueryParam,
                        In = ParameterLocation.Query,
                        Required = true,
                        AllowEmptyValue = false,
                        Description = "The application token (guid)",
                        Schema = new OpenApiSchema
                        {
                            Type = "string",
                            Default = string.IsNullOrEmpty(appToken)
                                ? null
                                : new Microsoft.OpenApi.Any.OpenApiString(appToken)
                        }
                    });
                }
            }
        }
    }
}
