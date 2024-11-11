using Apilane.Common.Extensions;
using Apilane.Common.Utilities;
using Apilane.Web.Portal.Extensions;
using Apilane.Web.Portal.Extentions;
using Apilane.Web.Portal.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Settings.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Web.Portal
{
    public class Program
    {
        public static void Main(string[] args)
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

            var appConfig = new PortalConfiguration(configuration);

            builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite($"Data Source={Path.Combine(appConfig.FilesPath, "Apilane.db")}");
                options.EnableSensitiveDataLogging(true);
                //options.ConfigureWarnings(x => x.Ignore(RelationalEventId.AmbientTransactionWarning));
            });

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
                op.AccessDeniedPath = new PathString("/Account/Login");
            });

            builder.Services
                .AddServices(appConfig)
                .AddAssets();

            builder.Services.AddMvc();

            builder.Services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
                //options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
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

            builder.Services.Configure<FormOptions>(x =>
            {
                x.ValueCountLimit = int.MaxValue;
            });

            var app = builder.Build();

            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run((context) => ExceptionHandlerAsync(context, Log.Logger));
            });

            using (var serviceScope = app.Services.GetService<IServiceScopeFactory>()!.CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                
                // Ensure database creation
                context.Database.EnsureCreated();

                // Execute migration queries
                var listOfQueries = new List<string>()
                {
                    // Add any migration queries here
                };

                foreach(var item in listOfQueries)
                {
                    try
                    {
                        context.Database.ExecuteSqlRaw(item);
                        context.SaveChanges();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error executing query '{item}' | {ex.Message}");
                    }
                }
            }

            if (environment == Common.Enums.HostingEnvironment.Development)
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseSerilogRequestLogging();

            app.UseWebOptimizer();

            app.UseForwardedHeaders();

            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthentication();

            app.UseAuthorization();

            app.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Applications}/{action=Index}/{id?}");

            app.MapControllerRoute(
                name: "AppRoute",
                pattern: "App/{appid}/{controller}/{action}");


            app.MapControllerRoute(
                name: "EntRoute",
                pattern: "App/{appid}/Ent/{entid}/{controller}/{action}");

            app.MapControllerRoute(
                name: "PropRoute",
                pattern: "App/{appid}/Ent/{entid}/Prop/{propid}/{controller}/{action}");

            app.MapHealthChecks("/health/liveness", AspNetCoreExtensions.SetupHealthCheck("live"));
            app.MapHealthChecks("/health/readiness", AspNetCoreExtensions.SetupHealthCheck("ready"));

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
