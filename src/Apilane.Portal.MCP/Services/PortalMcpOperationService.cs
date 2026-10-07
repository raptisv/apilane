using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    /// <summary>
    /// Calls only classified management operations through MVC itself, retaining its validation,
    /// session, agent-permission and exception filters. Never accepts a URL or caller identity.
    /// </summary>
    public class PortalMcpOperationService : IPortalMcpOperationService
    {
        private readonly EndpointDataSource _endpoints;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpContextAccessor _accessor;
        private readonly ISwaggerProvider _swagger;
        private readonly IMcpPortalStore _host;
        private readonly IMcpOperationPolicy _operationPolicy;

        public PortalMcpOperationService(EndpointDataSource endpoints, IServiceScopeFactory scopeFactory,
            IHttpContextAccessor accessor, ISwaggerProvider swagger, IMcpPortalStore host,
            IMcpOperationPolicy operationPolicy)
        {
            _endpoints = endpoints;
            _scopeFactory = scopeFactory;
            _accessor = accessor;
            _swagger = swagger;
            _host = host;
            _operationPolicy = operationPolicy;
        }

        public Task<JsonElement> DescribeAsync(string? operationId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operations = GetOperations();
            using var writer = new StringWriter();
            _swagger.GetSwagger("v1").SerializeAsV3(new OpenApiJsonWriter(writer));
            using var document = JsonDocument.Parse(writer.ToString());
            var paths = document.RootElement.GetProperty("paths");
            var summaries = new List<object>();
            foreach (var operation in operations.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (operationId is not null && !string.Equals(operation.Id, operationId, StringComparison.Ordinal))
                {
                    continue;
                }

                var contract = paths.GetProperty(operation.Path).GetProperty(operation.Method.ToLowerInvariant());
                summaries.Add(new
                {
                    operationId = operation.Id,
                    method = operation.Method,
                    path = operation.Path,
                    summary = contract.TryGetProperty("summary", out var summary) ? summary.GetString() : null,
                    permissions = _operationPolicy.GetPermissions(operation.Endpoint, operation.Method)
                        .Select(x => new { resource = x.Resource, access = x.Access }),
                    contract = operationId is null ? (JsonElement?)null : contract.Clone()
                });
            }

            if (operationId is not null && summaries.Count == 0)
            {
                throw new ArgumentException("Unknown or unavailable operationId.");
            }

            var schemas = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (operationId is not null)
            {
                var operation = operations[operationId];
                var contract = paths.GetProperty(operation.Path).GetProperty(operation.Method.ToLowerInvariant());
                CollectSchemas(contract, document.RootElement.GetProperty("components").GetProperty("schemas"), schemas);
            }

            return Task.FromResult(JsonSerializer.SerializeToElement(new { operations = summaries, schemas }));
        }

        public async Task<(JsonElement Result, bool IsError)> CallAsync(ClaimsPrincipal principal,
            IDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
        {
            if (principal.Identity?.AuthenticationType != _host.AgentAuthenticationType
                || !McpConnectionScope.IsConnection(principal))
            {
                throw new UnauthorizedAccessException("An authorized MCP connection is required.");
            }

            if (arguments.Keys.Any(x => x is not "operationId" and not "path" and not "query" and not "body"))
            {
                throw new ArgumentException("Unexpected tool argument.");
            }

            var operationId = arguments.TryGetValue("operationId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() : null;
            if (operationId is null || !GetOperations().TryGetValue(operationId, out var operation))
            {
                throw new ArgumentException("Unknown or unavailable operationId.");
            }

            var routeValues = new RouteValueDictionary(operation.Action.RouteValues);
            var routeArguments = ReadObject(arguments, "path");
            var routeNames = operation.Endpoint.RoutePattern.Parameters.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
            if (routeArguments.Keys.Any(x => !routeNames.Contains(x)))
            {
                throw new ArgumentException("Unexpected path parameter.");
            }

            foreach (var parameter in routeNames)
            {
                if (!routeArguments.TryGetValue(parameter, out var value) || value.ValueKind != JsonValueKind.String
                    || string.IsNullOrEmpty(value.GetString()) || value.GetString()?.Length > 256)
                {
                    throw new ArgumentException($"Supply path.{parameter} as a nonempty string.");
                }
                routeValues[parameter] = value.GetString();
            }

            var path = "/" + string.Join("/", operation.Endpoint.RoutePattern.PathSegments.Select(segment =>
                string.Concat(segment.Parts.Select(part => part switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternParameterPart parameter => Uri.EscapeDataString(routeValues[parameter.Name]?.ToString() ?? string.Empty),
                    RoutePatternSeparatorPart separator => separator.Content,
                    _ => throw new InvalidOperationException("Unsupported route part.")
                }))));
            var query = ReadObject(arguments, "query");
            var queryValues = query.SelectMany(x => QueryValues(x.Key, x.Value)).ToArray();
            var queryString = QueryString.Create(queryValues);
            if (queryString.Value?.Length > 16384)
            {
                throw new ArgumentException("Query is too large.");
            }

            var body = arguments.TryGetValue("body", out var bodyValue) ? bodyValue.GetRawText() : string.Empty;
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            if (bodyBytes.Length > 1024 * 1024 || (HttpMethods.IsGet(operation.Method) && bodyBytes.Length > 0))
            {
                throw new ArgumentException("Body is too large or is not permitted for this operation.");
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = principal,
                RequestAborted = cancellationToken
            };
            context.SetEndpoint(operation.Endpoint);
            context.Request.Method = operation.Method;
            context.Request.Path = path;
            context.Request.RouteValues = routeValues;
            context.Request.QueryString = queryString;
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("portal-mcp.internal");
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bodyBytes.Length;
            using var requestBody = new MemoryStream(bodyBytes);
            context.Request.Body = requestBody;
            using var responseBody = new LimitedResponseStream();
            context.Response.Body = responseBody;

            var activity = Activity.Current;
            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            Task dispatch;
            // HttpContextAccessor clears its shared holder when assigned. Do not inherit the
            // outer request's execution context, or assigning the child would clear its caller.
            // Suppression must end synchronously, before awaiting the isolated invocation.
            using (ExecutionContext.SuppressFlow())
            {
                dispatch = Task.Run(async () =>
                {
                    Activity.Current = activity;
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = uiCulture;
                    _accessor.HttpContext = context;
                    try
                    {
                        // Endpoint delegates do not run middleware. Enforce authorization explicitly,
                        // then let MVC run the same filters as a direct management API request.
                        var policy = await AuthorizationPolicy.CombineAsync(
                            scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>(),
                            operation.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
                        if (policy is not null && !(await scope.ServiceProvider.GetRequiredService<IAuthorizationService>()
                            .AuthorizeAsync(principal, context, policy)).Succeeded)
                        {
                            throw new UnauthorizedAccessException("Operation is not authorized.");
                        }

                        var requestDelegate = operation.Endpoint.RequestDelegate
                            ?? throw new InvalidOperationException("Management endpoint has no handler.");
                        await requestDelegate(context);
                    }
                    finally
                    {
                        _accessor.HttpContext = null;
                        Activity.Current = null;
                    }
                }, cancellationToken);
            }
            await dispatch;

            JsonElement? data = null;
            if (responseBody.Length > 0)
            {
                responseBody.Position = 0;
                using var response = await JsonDocument.ParseAsync(responseBody, cancellationToken: cancellationToken);
                data = response.RootElement.Clone();
            }

            var result = JsonSerializer.SerializeToElement(new
            {
                status = context.Response.StatusCode,
                data,
                warning = context.Response.Headers.TryGetValue("Warning", out var warning) ? warning.ToString() : null
            });
            return (result, context.Response.StatusCode >= 400);
        }

        private Dictionary<string, Operation> GetOperations()
        {
            var result = new Dictionary<string, Operation>(StringComparer.Ordinal);
            foreach (var endpoint in _endpoints.Endpoints.OfType<RouteEndpoint>())
            {
                var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
                var method = methods?.Count == 1 ? methods[0] : null;
                var route = endpoint.RoutePattern.RawText;
                if (action is null || method is null || route is null || !_operationPolicy.IsAvailable(action, endpoint, method))
                {
                    continue;
                }

                var id = $"{action.ControllerName}_{action.ActionName}";
                // OpenAPI removes routing constraints such as ':long' from parameter names.
                var contractPath = "/" + string.Join("/", endpoint.RoutePattern.PathSegments.Select(segment =>
                    string.Concat(segment.Parts.Select(part => part switch
                    {
                        RoutePatternLiteralPart literal => literal.Content,
                        RoutePatternParameterPart parameter => "{" + parameter.Name + "}",
                        RoutePatternSeparatorPart separator => separator.Content,
                        _ => throw new InvalidOperationException("Unsupported route part.")
                    }))));
                result.Add(id, new Operation(id, method, contractPath, endpoint, action));
            }
            return result;
        }

        private static Dictionary<string, JsonElement> ReadObject(IDictionary<string, JsonElement> arguments, string name)
        {
            if (!arguments.TryGetValue(name, out var value))
            {
                return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            }
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException($"{name} must be an object.");
            }
            return value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        }

        private static IEnumerable<KeyValuePair<string, string?>> QueryValues(string key, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray().SelectMany(x => QueryValues(key, x));
            }
            if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined)
            {
                throw new ArgumentException("Query parameters must be strings, numbers, booleans or arrays of them.");
            }
            return new[] { new KeyValuePair<string, string?>(key, value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()) };
        }

        private static void CollectSchemas(JsonElement element, JsonElement definitions, Dictionary<string, JsonElement> schemas)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "$ref" && property.Value.GetString() is string reference
                        && reference.StartsWith("#/components/schemas/", StringComparison.Ordinal))
                    {
                        var name = reference["#/components/schemas/".Length..];
                        if (!schemas.ContainsKey(name) && definitions.TryGetProperty(name, out var definition))
                        {
                            schemas.Add(name, definition.Clone());
                            CollectSchemas(definition, definitions, schemas);
                        }
                    }
                    else
                    {
                        CollectSchemas(property.Value, definitions, schemas);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    CollectSchemas(item, definitions, schemas);
                }
            }
        }

        private sealed record Operation(string Id, string Method, string Path, RouteEndpoint Endpoint, ControllerActionDescriptor Action);

        private sealed class LimitedResponseStream : MemoryStream
        {
            private const int MaximumLength = 8 * 1024 * 1024;

            public override void Write(byte[] buffer, int offset, int count)
            {
                CheckLength(count);
                base.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                CheckLength(buffer.Length);
                base.Write(buffer);
            }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                CheckLength(buffer.Length);
                return base.WriteAsync(buffer, cancellationToken);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                CheckLength(count);
                return base.WriteAsync(buffer, offset, count, cancellationToken);
            }

            private void CheckLength(int count)
            {
                if (Position + count > MaximumLength)
                {
                    throw new InvalidOperationException("The operation response exceeds the MCP response limit. Use pagination.");
                }
            }
        }
    }
}
