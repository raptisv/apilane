using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Apilane.Portal.Api;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Portal.Tests.Infrastructure
{
    /// <summary>
    /// A signed-in client and the cookie header it holds, for tests that replay the cookie.
    /// </summary>
    public record PortalSession(HttpClient Client, string Cookie);

    /// <summary>
    /// An application row a test added, and its encryption key as it was before it was stored encrypted.
    /// </summary>
    public record SeededApplication(DBWS_Application Application, string EncryptionKey);

    /// <summary>
    /// Hosts the real Portal in memory on a throw-away SQLite database. Program.Main reads its
    /// settings from environment variables (after appsettings.json), so that is how the test
    /// values get in, without any change to how the Portal starts.
    /// </summary>
    public class PortalFactory : WebApplicationFactory<Program>
    {
        public const string AdminEmail = "admin@portal.test";

        // The password the Portal gives the administrator it seeds on first start.
        public const string AdminPassword = "admin";

        // Never resolves, so a test can not reach an API server running on the developer's machine.
        public const string ApiUrl = "http://apilane-api.invalid";

        private static readonly Regex _antiforgeryField = new Regex(
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.Compiled);

        // Environment variables belong to the whole process: only one host may start at a time.
        private static readonly object _environmentLock = new object();

        private readonly string _filesPath;
        private readonly Dictionary<string, string> _environment;

        /// <summary>
        /// Stands in for the API servers the Portal calls. Strict: a call a test did not script
        /// fails instead of going out on the network.
        /// </summary>
        public FakeApiServer ApiServer { get; } = new FakeApiServer();

        /// <summary>
        /// Puts the fake API server back to how it was created: nothing scripted, nothing recorded.
        /// </summary>
        public void ResetApiServer()
        {
            ApiServer.Reset();
        }

        /// <summary>
        /// Stands in for the SMTP sender on every host of this factory.
        /// </summary>
        public RecordingEmailService Mail { get; } = new RecordingEmailService();

        public PortalFactory()
        {
            _filesPath = Path.Combine(Path.GetTempPath(), "apilane-portal-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_filesPath);

            _environment = new Dictionary<string, string>
            {
                ["FilesPath"] = _filesPath,
                ["Url"] = "http://localhost",
                ["ApiUrl"] = ApiUrl,
                ["InstanceTitle"] = "Apilane tests",
                ["InstallationKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                ["AdminEmail"] = AdminEmail,
                // Program.cs maps the /metrics endpoint unconditionally, which needs the meter provider.
                ["OpenTelemetry__Metrics__Enabled"] = "true"
            };

            Start(this);
        }

        /// <summary>
        /// A second Portal host on the same database with some services replaced or reconfigured.
        /// It is disposed together with this factory.
        /// </summary>
        public WebApplicationFactory<Program> CreateHost(Action<IServiceCollection> configureServices)
        {
            var host = WithWebHostBuilder(builder => builder.ConfigureTestServices(configureServices));

            Start(host);

            return host;
        }

        /// <summary>
        /// A second Portal host that runs as Production, with some services replaced. Only
        /// IHostEnvironment changes: Program.cs reads ASPNETCORE_ENVIRONMENT itself.
        /// </summary>
        public WebApplicationFactory<Program> CreateProductionHost(Action<IServiceCollection> configureServices)
        {
            var host = WithWebHostBuilder(builder => builder
                .UseEnvironment("Production")
                .ConfigureTestServices(configureServices));

            Start(host);

            return host;
        }

        /// <summary>
        /// A second Portal host whose web root is a new empty folder, or one holding ui/index.html
        /// with the given content. It is disposed together with this factory.
        /// </summary>
        public WebApplicationFactory<Program> CreateHostWithUi(string? indexHtml)
        {
            var webRoot = Path.Combine(_filesPath, "webroot-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(webRoot, "ui"));

            if (indexHtml is not null)
            {
                File.WriteAllText(Path.Combine(webRoot, "ui", "index.html"), indexHtml);
            }

            var host = WithWebHostBuilder(builder => builder
                .UseWebRoot(webRoot)
                // In Development the static web assets manifest still serves the real wwwroot (a built UI included); point it at a file that does not exist.
                .UseSetting(WebHostDefaults.StaticWebAssetsKey, Path.Combine(webRoot, "no-manifest.json")));

            Start(host);

            return host;
        }

        /// <summary>
        /// A client with its own cookie jar that does not follow redirects, so a test can tell
        /// "401" from "302 to the login page". It sends the CSRF header, like the Portal UI does.
        /// </summary>
        public HttpClient CreateAnonymousClient(WebApplicationFactory<Program>? host = null)
        {
            return CreateClient(host, handleCookies: true);
        }

        /// <summary>
        /// A client that keeps no cookies: the test sends the Cookie header itself.
        /// </summary>
        public HttpClient CreateCookielessClient(WebApplicationFactory<Program>? host = null)
        {
            return CreateClient(host, handleCookies: false);
        }

        /// <summary>
        /// Signs in through the real login form and returns the client holding the session cookie.
        /// </summary>
        public async Task<PortalSession> SignInAsync(string email, string password, WebApplicationFactory<Program>? host = null)
        {
            var client = CreateAnonymousClient(host);

            var loginPage = await client.GetStringAsync("/Account/Login");
            var match = _antiforgeryField.Match(loginPage);
            Assert.True(match.Success, "The login page has no antiforgery field.");

            var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Password"] = password,
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
            }));

            Assert.True(
                response.StatusCode == HttpStatusCode.Redirect,
                $"Login as {email} did not succeed (status {(int)response.StatusCode}).");

            return new PortalSession(client, response.GetSetCookieHeader());
        }

        /// <summary>
        /// The antiforgery token of a Razor form, for a test that posts the form like a browser does.
        /// </summary>
        public async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string formUrl)
        {
            var match = _antiforgeryField.Match(await client.GetStringAsync(formUrl));
            Assert.True(match.Success, $"{formUrl} has no antiforgery field.");

            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        /// <summary>
        /// Signs in through POST /api/v1/session and returns the client holding the session cookie.
        /// </summary>
        public async Task<PortalSession> SignInWithApiAsync(string email, string password, WebApplicationFactory<Program>? host = null)
        {
            var client = CreateAnonymousClient(host);

            var response = await client.PostAsync("/api/v1/session", new { Email = email, Password = password }.ToJsonContent());

            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"API sign-in as {email} did not succeed (status {(int)response.StatusCode}).");

            return new PortalSession(client, response.GetSetCookieHeader());
        }

        /// <summary>
        /// Sets the two instance settings the account endpoints depend on. The seeded state is
        /// registration allowed and no mail settings; a test class that changes it puts it back.
        /// </summary>
        public Task SetAccountSettingsAsync(bool allowRegister, bool mailConfigured)
        {
            return WithDbContextAsync(async dbContext =>
            {
                var settings = await dbContext.GlobalSettings.SingleAsync();

                settings.AllowRegisterToPortal = allowRegister;
                settings.MailServer = mailConfigured ? "smtp.portal.test" : null;
                settings.MailServerPort = mailConfigured ? 587 : null;
                settings.MailUserName = mailConfigured ? "mailer" : null;
                settings.MailPassword = mailConfigured ? "mail-password-for-tests" : null;
                settings.MailFromAddress = mailConfigured ? "portal@portal.test" : null;
                settings.MailFromDisplayName = mailConfigured ? "Portal tests" : null;

                return await dbContext.SaveChangesAsync();
            });
        }

        public async Task<HttpClient> CreateSignedInClientAsync(string email, string password)
        {
            return (await SignInAsync(email, password)).Client;
        }

        /// <summary>
        /// Signs in as a new administrator. The seeded administrator is left alone, because every
        /// sign-in revokes the previous session of the same user.
        /// </summary>
        public async Task<HttpClient> CreateAdminClientAsync()
        {
            var (email, password) = await CreateAdminUserAsync();

            return await CreateSignedInClientAsync(email, password);
        }

        /// <summary>
        /// Signs in as a new user without the Admin role.
        /// </summary>
        public async Task<HttpClient> CreateUserClientAsync()
        {
            var (email, password) = await CreateUserAsync();

            return await CreateSignedInClientAsync(email, password);
        }

        /// <summary>
        /// Creates a portal user without the Admin role and returns its e-mail and password.
        /// </summary>
        public async Task<(string Email, string Password)> CreateUserAsync()
        {
            var email = $"user-{Guid.NewGuid():N}@portal.test";
            var password = $"pw-{Guid.NewGuid():N}";

            using var scope = Services.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var result = await userManager.CreateAsync(
                new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    DateRegistered = DateTime.UtcNow,
                    LastLogin = DateTime.UtcNow
                },
                password);

            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(x => x.Description)));

            return (email, password);
        }

        /// <summary>
        /// Creates a portal user with the Admin role and returns its e-mail and password.
        /// </summary>
        public async Task<(string Email, string Password)> CreateAdminUserAsync()
        {
            var (email, password) = await CreateUserAsync();

            // Written straight to the table, like AdminController.SetUserRole: the seeded role's
            // normalized name is not what UserManager.AddToRoleAsync looks for.
            await WithDbContextAsync(async dbContext =>
            {
                var user = await dbContext.Users.SingleAsync(x => x.Email == email);
                var role = await dbContext.Roles.SingleAsync(x => x.Name == Globals.AdminRoleName);

                dbContext.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });

                return await dbContext.SaveChangesAsync();
            });

            return (email, password);
        }

        /// <summary>
        /// Reads or seeds the Portal database directly. Rows saved here produce no audit entries,
        /// because there is no signed-in request.
        /// </summary>
        public async Task<T> WithDbContextAsync<T>(Func<ApplicationDbContext, Task<T>> action)
        {
            using var scope = Services.CreateScope();

            return await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        /// <summary>
        /// Adds a server with a unique name and a unique URL and returns it.
        /// </summary>
        public async Task<DBWS_Server> CreateServerAsync(string namePrefix = "server")
        {
            var server = new DBWS_Server
            {
                Name = $"{namePrefix}-{Guid.NewGuid():N}",
                ServerUrl = $"https://api-{Guid.NewGuid():N}.example.test",
                DateModified = DateTime.UtcNow
            };

            await WithDbContextAsync(dbContext =>
            {
                dbContext.Servers.Add(server);
                return dbContext.SaveChangesAsync();
            });

            return server;
        }

        /// <summary>
        /// Adds a bare application row on a server. It exists in the Portal database only.
        /// </summary>
        public async Task<DBWS_Application> CreateApplicationAsync(long serverId)
        {
            var token = Guid.NewGuid().ToString();

            var application = new DBWS_Application
            {
                Token = token,
                UserID = "nobody",
                Name = $"app-{token}",
                ServerID = serverId,
                EncryptionKey = Guid.NewGuid().ToString("N"),
                AuthTokenExpireMinutes = 60,
                MaxAllowedFileSizeInKB = 1024,
                DateModified = DateTime.UtcNow
            };

            await WithDbContextAsync(dbContext =>
            {
                dbContext.Applications.Add(application);
                return dbContext.SaveChangesAsync();
            });

            return application;
        }

        /// <summary>
        /// Adds an application owned by a portal user and shared with the given e-mail addresses.
        /// The encryption key is stored the way the Portal stores it; the connection string is a
        /// unique value a test can look for in a response. It exists in the Portal database only.
        /// </summary>
        public async Task<SeededApplication> CreateApplicationAsync(long serverId, string ownerEmail, string name, params string[] collaboratorEmails)
        {
            var encryptionKey = Guid.NewGuid().ToString("N").Substring(0, 8);

            return await WithDbContextAsync(async dbContext =>
            {
                var owner = await dbContext.Users.SingleAsync(x => x.Email == ownerEmail);

                var application = new DBWS_Application
                {
                    Token = Guid.NewGuid().ToString(),
                    UserID = owner.Id,
                    AdminEmail = ownerEmail,
                    Name = name,
                    ServerID = serverId,
                    Online = true,
                    DatabaseType = (int)DatabaseType.SQLServer,
                    EncryptionKey = encryptionKey.Encrypt(Globals.EncryptionKey),
                    ConnectionString = $"Server=db-{Guid.NewGuid():N};Database=app",
                    AuthTokenExpireMinutes = 60,
                    MaxAllowedFileSizeInKB = 1024,
                    DateModified = DateTime.UtcNow,
                    Collaborates = collaboratorEmails
                        .Select(x => new DBWS_Collaborate { UserEmail = x, DateModified = DateTime.UtcNow })
                        .ToList()
                };

                dbContext.Applications.Add(application);
                await dbContext.SaveChangesAsync();

                return new SeededApplication(application, encryptionKey);
            });
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                // Both IApiHttpService (Razor pages) and IApiServerClient (the API) send through this client.
                services.AddHttpClient(ApiServerClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => ApiServer);

                services.RemoveAll<IEmailService>();
                services.AddSingleton<IEmailService>(Mail);

                // Every test calls from the same address: the sign-in rate limit must not get in the way.
                services.Configure<PortalRateLimitOptions>(options => options.PermitLimit = 1_000_000);

                // Controllers that exist only for tests (ErrorTestController).
                services.AddControllers().AddApplicationPart(typeof(PortalFactory).Assembly);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            // Release the database file before removing the folder.
            SqliteConnection.ClearAllPools();

            try
            {
                Directory.Delete(_filesPath, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp folder is harmless; the next run uses a new one.
            }
        }

        /// <summary>
        /// Starts a host while the test settings are in the environment, then puts the previous
        /// values back. Program.Main has read every setting by the time the host is up, so a
        /// second factory can never pick up, or overwrite, the settings of this one.
        /// </summary>
        private void Start(WebApplicationFactory<Program> host)
        {
            lock (_environmentLock)
            {
                var previous = _environment.Keys.ToDictionary(x => x, Environment.GetEnvironmentVariable);

                try
                {
                    foreach (var item in _environment)
                    {
                        Environment.SetEnvironmentVariable(item.Key, item.Value);
                    }

                    // Touching Services runs Program.Main and returns once the host has started.
                    Assert.NotNull(host.Services);
                }
                finally
                {
                    foreach (var item in previous)
                    {
                        Environment.SetEnvironmentVariable(item.Key, item.Value);
                    }
                }
            }
        }

        private HttpClient CreateClient(WebApplicationFactory<Program>? host, bool handleCookies)
        {
            var client = (host ?? this).CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = handleCookies
            });

            client.DefaultRequestHeaders.Add(PortalCsrfFilter.HeaderName, PortalCsrfFilter.HeaderValue);

            return client;
        }
    }

    /// <summary>
    /// One Portal host for all test classes. They share its database, so a test asserts on rows it
    /// created itself (unique names) or on a seeded row by its ID, never on how many rows exist,
    /// and it never changes the seeded administrator or the seeded server. They also share the
    /// fake API server, so every test class resets it in its constructor and a test scripts the
    /// answers it needs itself.
    /// </summary>
    [CollectionDefinition(Name)]
    public class PortalCollection : ICollectionFixture<PortalFactory>
    {
        public const string Name = "Portal";
    }
}
