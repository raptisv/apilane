using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApiServerClient : IApiServerClient
    {
        /// <summary>
        /// The named HttpClient ApiHttpService uses too (ServiceDependencyInjection).
        /// </summary>
        public const string HttpClientName = "Api";

        public const string UnusableAnswerMessage = "The API server could not be reached or answered with an error.";

        // A plain-text body longer than this is not a message. The API server's own Message can be
        // longer (a database driver's connection error is), and its end is the part that says what is wrong.
        private const int MaxMessageLength = 300;
        private const int MaxApiMessageLength = 1000;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IPortalAccessService _portalAccessService;
        private readonly PortalConfiguration _portalConfiguration;
        private readonly ILogger<ApiServerClient> _logger;

        public ApiServerClient(
            IHttpClientFactory httpClientFactory,
            IPortalAccessService portalAccessService,
            PortalConfiguration portalConfiguration,
            ILogger<ApiServerClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _portalAccessService = portalAccessService;
            _portalConfiguration = portalConfiguration;
            _logger = logger;
        }

        public async Task ClearCacheAsync(DBWS_Application application)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(application.Server)}/api/Application/ClearCache");

            await SendAsync(request, application.Token);
        }

        public async Task<List<DBWS_Entity>> GetSystemEntitiesAsync(DBWS_Server server, string differentiationEntity)
        {
            var url = $"{BaseUrl(server)}/api/ApplicationNew/GetSystemEntities?differentiationEntity={Uri.EscapeDataString(differentiationEntity)}";

            // The application does not exist yet, so the token header is empty.
            var json = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), appToken: string.Empty);

            List<DBWS_Entity>? entities = null;

            try
            {
                entities = JsonSerializer.Deserialize<List<DBWS_Entity>>(json);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "API server answer could not be read | {Url}", url);
            }

            if (entities is null || entities.Any(x => x is null || x.Properties is null))
            {
                throw PortalException.Upstream("Invalid response from Api server");
            }

            return entities;
        }

        public async Task GenerateAsync(DBWS_Server server, DBWS_Application application)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(server)}/api/ApplicationNew/Generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(application), Encoding.UTF8, "application/json")
            };

            request.Headers.Add(Globals.InstallationKeyHeaderName, _portalConfiguration.InstallationKey);

            await SendAsync(request, application.Token);
        }

        public async Task<EntityPropertiesConstrainsDto> GetSystemPropertiesAndConstraintsAsync(DBWS_Application application, bool entityHasDifferentiationProperty)
        {
            var url = $"{BaseUrl(application.Server)}/api/Application/GetSystemPropertiesAndConstraints?entityHasDifferentiationProperty={entityHasDifferentiationProperty}";

            var json = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), application.Token);

            EntityPropertiesConstrainsDto? result = null;

            try
            {
                result = JsonSerializer.Deserialize<EntityPropertiesConstrainsDto>(json);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "API server answer could not be read | {Url}", url);
            }

            if (result is null
                || result.Properties is null
                || result.Constraints is null
                || result.Properties.Any(x => x is null)
                || result.Constraints.Any(x => x is null))
            {
                throw PortalException.Upstream("Invalid response from Api server");
            }

            return result;
        }

        public async Task GenerateEntityAsync(DBWS_Application application, DBWS_Entity entity)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl(application.Server)}/api/Application/GenerateEntity")
            {
                Content = new StringContent(JsonSerializer.Serialize(entity), Encoding.UTF8, "application/json")
            };

            await SendAsync(request, application.Token);
        }

        public async Task RenameEntityAsync(DBWS_Application application, long entityId, string newName)
        {
            var url = $"{BaseUrl(application.Server)}/api/Application/RenameEntity?ID={entityId}&NewName={Uri.EscapeDataString(newName)}";

            await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), application.Token);
        }

        public async Task DegenerateEntityAsync(DBWS_Application application, string entityName)
        {
            var url = $"{BaseUrl(application.Server)}/api/Application/DegenerateEntity?Entity={Uri.EscapeDataString(entityName)}";

            await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), application.Token);
        }

        public async Task GeneratePropertyAsync(DBWS_Application application, string entityName, DBWS_EntityProperty property)
        {
            var url = $"{BaseUrl(application.Server)}/api/Application/GenerateProperty?Entity={Uri.EscapeDataString(entityName)}";

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(property), Encoding.UTF8, "application/json")
            };

            await SendAsync(request, application.Token);
        }

        public async Task RenameEntityPropertyAsync(DBWS_Application application, long propertyId, string newName)
        {
            var url = $"{BaseUrl(application.Server)}/api/Application/RenameEntityProperty?ID={propertyId}&NewName={Uri.EscapeDataString(newName)}";

            await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), application.Token);
        }

        public async Task DegeneratePropertyAsync(DBWS_Application application, long propertyId)
        {
            var url = $"{BaseUrl(application.Server)}/api/Application/DegenerateProperty?ID={propertyId}";

            await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), application.Token);
        }

        public async Task GenerateConstraintsAsync(DBWS_Application application, string entityName, List<EntityConstraint> constraints)
        {
            var url = $"{BaseUrl(application.Server)}/api/Application/GenerateConstraints?Entity={Uri.EscapeDataString(entityName)}";

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(constraints), Encoding.UTF8, "application/json")
            };

            await SendAsync(request, application.Token);
        }

        public async Task RebuildAsync(DBWS_Application application)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(application.Server)}/api/Application/Rebuild");

            await SendAsync(request, application.Token);
        }

        public async Task DegenerateAsync(DBWS_Application application)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl(application.Server)}/api/Application/Degenerate");

            await SendAsync(request, application.Token);
        }

        public async Task<List<string>> GetUserRolesAsync(DBWS_Application application)
        {
            var url = $"{BaseUrl(application.Server)}/api/Stats/Distinct?Entity=Users&Property=Roles";

            var json = await SendAsync(new HttpRequestMessage(HttpMethod.Get, url), application.Token);

            // The answer is [{ "Roles": "a" }, { "Roles": "b,c" }, ...].
            List<Dictionary<string, JsonElement>>? rows = null;

            try
            {
                rows = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            }
            catch (JsonException exception)
            {
                _logger.LogWarning(exception, "API server answer could not be read | {Url}", url);
            }

            if (rows is null || rows.Any(x => x is null))
            {
                throw PortalException.Upstream("Invalid response from Api server");
            }

            var values = rows
                .Select(x => x.TryGetValue("Roles", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                .OfType<string>()
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            // Single values first, then the split ones, no trimming.
            return values
                .Where(x => !x.Contains(','))
                .Concat(values.Where(x => x.Contains(',')).SelectMany(x => x.Split(',')).Where(x => !string.IsNullOrWhiteSpace(x)))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string BaseUrl(DBWS_Server server)
        {
            return server.ServerUrl.Trim('/');
        }

        /// <summary>
        /// Sends the request with the headers every Portal call carries and returns the body of a
        /// successful answer. Anything else becomes a <see cref="PortalException"/>.
        /// </summary>
        private async Task<string> SendAsync(HttpRequestMessage request, string appToken)
        {
            var user = await _portalAccessService.GetCurrentUserAsync();

            request.Headers.Add(Globals.ClientIdHeaderName, Globals.ClientIdHeaderValuePortal);
            request.Headers.Add(Globals.ApplicationTokenHeaderName, appToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AdminAuthToken ?? string.Empty);

            HttpStatusCode status;
            string body;

            try
            {
                using var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(request);

                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return body;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException)
            {
                _logger.LogWarning(exception, "API server could not be reached | {Url}", request.RequestUri);

                throw PortalException.Upstream(UnusableAnswerMessage);
            }

            _logger.LogWarning("API server answered {StatusCode} | {Url}", (int)status, request.RequestUri);

            var error = ReadError(body);

            // A 400 is the API server refusing what was asked: its text tells the user what to change.
            if (status == HttpStatusCode.BadRequest)
            {
                throw PortalException.UpstreamValidation(error.Message, error.Property);
            }

            throw PortalException.Upstream(error.Message);
        }

        /// <summary>
        /// The API server's error body is { Code, Message, Property, Entity }. A proxy in front of it
        /// answers with an HTML page or nothing at all: that is not a message for the caller.
        /// </summary>
        private static (string Message, string? Property) ReadError(string body)
        {
            var error = ParseError(body);

            // Without a message of its own, the body is the message (plain text answers).
            var apiMessage = string.IsNullOrWhiteSpace(error?.Message) ? null : error.Message;
            var message = apiMessage ?? body;

            if (string.IsNullOrWhiteSpace(message)
                || message.TrimStart().StartsWith('<')
                || (apiMessage is null && message.Length > MaxMessageLength))
            {
                message = UnusableAnswerMessage;
            }
            else if (message.Length > MaxApiMessageLength)
            {
                message = message[..MaxApiMessageLength];
            }

            return (message, error?.Property);
        }

        private static ApiServerError? ParseError(string body)
        {
            try
            {
                return JsonSerializer.Deserialize<ApiServerError>(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private class ApiServerError
        {
            public string? Message { get; set; }
            public string? Property { get; set; }
        }
    }
}
