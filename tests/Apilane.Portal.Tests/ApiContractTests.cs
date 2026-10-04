using Apilane.Common;
using Apilane.Portal.Api;
using Apilane.Portal.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Apilane.Portal.Tests
{
    /// <summary>
    /// Rules every management API endpoint has to keep, checked for all of them at once so a new
    /// endpoint cannot quietly break them. The endpoints are read from ApiExplorer, the source the
    /// OpenAPI document is built from.
    /// </summary>
    [Collection(PortalCollection.Name)]
    public class ApiContractTests
    {
        private const string ApiPrefix = "api/v1";
        private const string ContractsNamespace = "Apilane.Portal.Api.V1.Contracts";

        // Values that must never leave the Portal through the API. The same names the audit log masks
        // (ApplicationDbContext.SensitiveProperties). A request may carry them; a response may not.
        private static readonly string[] _secretPropertyNames =
        {
            "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "AdminAuthToken",
            "EncryptionKey", "ConnectionString", "MailPassword", "InstallationKey"
        };

        // The only response properties that carry a secret, each served by one endpoint made for it:
        // GET session/api-token and GET applications/{appToken}/connection-info.
        private static readonly string[] _allowedSecretProperties =
        {
            "ApiTokenResponse.Token",
            "ConnectionInfoResponse.EncryptionKey"
        };

        // The only endpoints under api/v1 that may be called without a session.
        private static readonly string[] _anonymousActions =
        {
            "Instance.Get",
            "Session.Create",
            "Session.Delete",
            "PortalAccount.Register",
            "PortalAccount.RequestPasswordReset",
            "PortalAccount.ResetPassword"
        };

        // The only endpoints that carry the sign-in rate limit.
        private static readonly string[] _rateLimitedActions =
        {
            "Session.Create",
            "PortalAccount.Register",
            "PortalAccount.RequestPasswordReset"
        };

        private readonly PortalFactory _portal;

        public ApiContractTests(PortalFactory portal)
        {
            _portal = portal;
            _portal.ResetApiServer();
        }

        [Fact]
        public void Actions_Should_Exist()
        {
            // Guards the tests below against passing because nothing was found.
            Assert.NotEmpty(GetApiDescriptions());
        }

        [Fact]
        public void Actions_Should_Only_Expose_Contract_Types()
        {
            var offenders = new List<string>();

            foreach (var api in GetApiDescriptions())
            {
                var types = new HashSet<Type>();

                foreach (var type in GetResponseTypes(api))
                {
                    Collect(type, types);
                }

                foreach (var parameter in api.ParameterDescriptions)
                {
                    Collect(parameter.Type, types);
                }

                offenders.AddRange(types
                    .Where(x => x.Namespace != ContractsNamespace)
                    .Select(x => $"{Describe(api)} exposes {x.FullName}"));
            }

            // Database models and view models stay behind the API; the contract classes are the only shapes clients see.
            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void Actions_Should_Declare_Their_Success_Response()
        {
            // An action that returns IActionResult without [ProducesResponseType] hides its body
            // from the contract and from the test above.
            var offenders = GetApiDescriptions()
                .Where(api => !api.SupportedResponseTypes.Any(x =>
                    x.StatusCode == StatusCodes.Status204NoContent
                    // 202: accepted, and by design nothing to say about the outcome.
                    || (x.StatusCode == StatusCodes.Status202Accepted && Describe(api) == "POST api/v1/account/password-reset-requests")
                    || (x.StatusCode >= 200 && x.StatusCode < 300 && x.Type is not null && x.Type != typeof(void))))
                .Select(Describe)
                .ToList();

            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void Writes_Should_Declare_The_403_Of_The_Csrf_Header()
        {
            // The same methods PortalCsrfFilter lets through without the header.
            var safeMethods = new[] { "GET", "HEAD", "OPTIONS", "TRACE" };

            var offenders = GetApiDescriptions()
                .Where(api => !safeMethods.Contains(api.HttpMethod, StringComparer.OrdinalIgnoreCase))
                .Where(api => !api.SupportedResponseTypes.Any(x => x.StatusCode == StatusCodes.Status403Forbidden))
                .Select(Describe)
                .ToList();

            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void Responses_Should_Not_Have_Secret_Properties()
        {
            var types = new HashSet<Type>();

            foreach (var type in GetApiDescriptions().SelectMany(GetResponseTypes))
            {
                Collect(type, types);
            }

            var properties = types
                .SelectMany(x => x.GetProperties().Select(p => (Type: x, Property: p)))
                .ToList();

            var offenders = properties
                // A bool flag such as HasMailPassword says a secret is set without carrying it.
                .Where(x => x.Property.PropertyType != typeof(bool)
                    && (_secretPropertyNames.Contains(x.Property.Name, StringComparer.OrdinalIgnoreCase)
                        || x.Property.Name.EndsWith("Password", StringComparison.OrdinalIgnoreCase)))
                .Select(x => $"{x.Type.Name}.{x.Property.Name}")
                .Except(_allowedSecretProperties)
                .ToList();

            Assert.NotEmpty(types);
            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));

            // An entry left behind after a rename would hide the next mistake.
            var existing = properties.Select(x => $"{x.Type.Name}.{x.Property.Name}").ToList();
            Assert.All(_allowedSecretProperties, x => Assert.Contains(x, existing));
        }

        [Fact]
        public void Secret_Responses_Should_Be_Returned_By_One_Endpoint_Each()
        {
            var secretTypes = _allowedSecretProperties.Select(x => x.Split('.')[0]).ToList();

            var endpoints = GetApiDescriptions()
                .Where(api => GetResponseTypes(api).Any(type =>
                {
                    var found = new HashSet<Type>();
                    Collect(type, found);
                    return found.Any(x => secretTypes.Contains(x.Name));
                }))
                .Select(Describe)
                .OrderBy(x => x)
                .ToList();

            Assert.Equal(
                new[] { "GET api/v1/applications/{appToken}/connection-info", "GET api/v1/session/api-token" },
                endpoints);
        }

        [Fact]
        public void Endpoints_Should_Use_The_Api_Base_Class_And_Require_A_Session()
        {
            var endpoints = _portal.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(x => (x.RoutePattern.RawText ?? string.Empty).TrimStart('/').StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.NotEmpty(endpoints);

            var offenders = new List<string>();

            foreach (var endpoint in endpoints)
            {
                var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();

                if (action is null || !typeof(PortalApiControllerBase).IsAssignableFrom(action.ControllerTypeInfo))
                {
                    offenders.Add($"{endpoint.DisplayName} is not an action of a PortalApiControllerBase controller.");
                    continue;
                }

                var name = $"{action.ControllerName}.{action.ActionName}";

                if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null && !_anonymousActions.Contains(name))
                {
                    offenders.Add($"{name} allows anonymous calls and is not on the allow-list.");
                }

                var route = (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
                var isAdminRoute = route.Equals(PortalAdminApiControllerBase.RoutePrefix, StringComparison.OrdinalIgnoreCase)
                    || route.StartsWith(PortalAdminApiControllerBase.RoutePrefix + "/", StringComparison.OrdinalIgnoreCase);

                // The same metadata the authorization middleware reads.
                var requiresAdmin = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(x => x.Roles == Globals.AdminRoleName);

                if (isAdminRoute && !requiresAdmin)
                {
                    offenders.Add($"{name} is under {PortalAdminApiControllerBase.RoutePrefix} and does not require the Admin role.");
                }

                if (typeof(PortalAdminApiControllerBase).IsAssignableFrom(action.ControllerTypeInfo) && !isAdminRoute)
                {
                    offenders.Add($"{name} is an admin controller and its route does not start with {PortalAdminApiControllerBase.RoutePrefix}.");
                }

                var isApplicationRoute = route.Equals(PortalApplicationApiControllerBase.RoutePrefix, StringComparison.OrdinalIgnoreCase)
                    || route.StartsWith(PortalApplicationApiControllerBase.RoutePrefix + "/", StringComparison.OrdinalIgnoreCase);
                var isApplicationController = typeof(PortalApplicationApiControllerBase).IsAssignableFrom(action.ControllerTypeInfo);

                // The base class declares the 404 of an unknown or inaccessible application.
                if (isApplicationRoute != isApplicationController)
                {
                    offenders.Add($"{name}: routes under {PortalApplicationApiControllerBase.RoutePrefix} and PortalApplicationApiControllerBase controllers must go together.");
                }
            }

            Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
        }

        [Fact]
        public void Anonymous_Allow_List_Should_Name_Existing_Anonymous_Actions()
        {
            // An entry left behind after a rename, or after [AllowAnonymous] was removed, would hide the next mistake.
            var anonymous = GetActionNames(x => x.Metadata.GetMetadata<IAllowAnonymous>() is not null);

            Assert.Equal(_anonymousActions.OrderBy(x => x), anonymous.OrderBy(x => x));
        }

        [Fact]
        public void Rate_Limit_Should_Be_On_The_Three_Anonymous_Account_Actions_Only()
        {
            var limited = _portal.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .Where(x => x.Metadata.GetMetadata<EnableRateLimitingAttribute>() is not null)
                .ToList();

            // Every limited endpoint is an API action: no MVC page carries the policy.
            Assert.All(limited, x => Assert.StartsWith(
                ApiPrefix,
                ((x as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty).TrimStart('/'),
                StringComparison.OrdinalIgnoreCase));

            var names = GetActionNames(x => x.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == PortalRateLimitOptions.AccountPolicy);

            Assert.Equal(_rateLimitedActions.OrderBy(x => x), names.OrderBy(x => x));
        }

        private List<string> GetActionNames(Func<RouteEndpoint, bool> filter)
        {
            return _portal.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .OfType<RouteEndpoint>()
                .Where(x => (x.RoutePattern.RawText ?? string.Empty).TrimStart('/').StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase))
                .Where(filter)
                .Select(x => x.Metadata.GetMetadata<ControllerActionDescriptor>())
                .OfType<ControllerActionDescriptor>()
                .Select(x => $"{x.ControllerName}.{x.ActionName}")
                .Distinct()
                .ToList();
        }

        [Fact]
        public async Task OpenApi_Document_Should_Match_The_Committed_Contract()
        {
            var contractPath = Path.Combine(FindRepositoryRoot(), "openapi", "portal-v1.json");

            var client = await _portal.CreateAdminClientAsync();
            var response = await client.GetAsync("/swagger/v1/swagger.json");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var current = Normalize(await response.Content.ReadAsStringAsync());

            if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(contractPath) ?? throw new InvalidOperationException("No contract folder."));
                await File.WriteAllTextAsync(contractPath, current);
                return;
            }

            Assert.True(File.Exists(contractPath), $"{contractPath} is missing. {UpdateHint}");

            var committed = Normalize(await File.ReadAllTextAsync(contractPath));

            try
            {
                Assert.Equal(committed, current);
            }
            catch (EqualException exception)
            {
                throw new XunitException($"The API no longer matches openapi/portal-v1.json. {UpdateHint}{Environment.NewLine}{exception.Message}");
            }
        }

        [Fact]
        public async Task OpenApi_Document_Should_Describe_The_Csrf_Header()
        {
            var client = await _portal.CreateAdminClientAsync();

            var document = await client.GetStringAsync("/swagger/v1/swagger.json");

            Assert.Contains("X-Apilane-Portal: 1", document);
        }

        [Fact]
        public async Task OpenApi_Document_With_A_Revoked_Session_Should_Redirect_To_Login()
        {
            var (email, password) = await _portal.CreateUserAsync();

            var first = await _portal.CreateSignedInClientAsync(email, password);
            Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/swagger/v1/swagger.json")).StatusCode);

            // Signing in again revokes the first session; its cookie is still valid as a cookie.
            await _portal.CreateSignedInClientAsync(email, password);

            var response = await first.GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
        }

        [Fact]
        public async Task OpenApi_Document_Anonymous_Should_Redirect_To_Login()
        {
            var client = _portal.CreateAnonymousClient();

            var response = await client.GetAsync("/swagger/v1/swagger.json");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location?.ToString());
        }

        private const string UpdateHint =
            "If the change is intended, run the tests once with the environment variable UPDATE_OPENAPI=1, " +
            "then 'npm run api:types' in src/Apilane.Portal.Ui, and commit both files.";

        private static string Normalize(string json)
        {
            return json.Replace("\r\n", "\n").TrimEnd() + "\n";
        }

        private List<ApiDescription> GetApiDescriptions()
        {
            return _portal.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
                .ApiDescriptionGroups.Items
                .SelectMany(x => x.Items)
                .Where(x => (x.RelativePath ?? string.Empty).StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static IEnumerable<Type> GetResponseTypes(ApiDescription api)
        {
            return api.SupportedResponseTypes
                .Select(x => x.Type)
                .OfType<Type>()
                .Where(x => x != typeof(void));
        }

        private static string Describe(ApiDescription api)
        {
            return $"{api.HttpMethod} {api.RelativePath}";
        }

        /// <summary>
        /// Gathers the application-defined types reachable from <paramref name="type"/>: through
        /// collections, generic arguments and public properties.
        /// </summary>
        private static void Collect(Type type, HashSet<Type> found)
        {
            if (type == typeof(void) || type.IsGenericParameter)
            {
                return;
            }

            var elementType = type.GetElementType();

            if (type.IsArray && elementType is not null)
            {
                Collect(elementType, found);
                return;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    Collect(argument, found);
                }
            }

            var isFramework = type.Namespace is null
                || type.Namespace.StartsWith("System", StringComparison.Ordinal)
                || type == typeof(IFormFile);

            if (isFramework)
            {
                return;
            }

            // A constructed generic contract (ListResponse<ServerResponse>) is checked by its definition.
            var key = type.IsGenericType ? type.GetGenericTypeDefinition() : type;

            if (!found.Add(key))
            {
                return;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Collect(property.PropertyType, found);
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Apilane.sln")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not find Apilane.sln above the test output folder.");
        }
    }
}
