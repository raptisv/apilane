using Apilane.Portal.Abstractions;
using Apilane.Portal.Models;
using Apilane.Portal.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Portal.Extensions
{
    public static class PortalMcpExtensions
    {
        public const string Path = "/api/mcp";

        public static IServiceCollection AddPortalMcp(this IServiceCollection services)
        {
            services.AddPortalMcpAuthorization();
            services.AddScoped<IPortalMcpOperationService, PortalMcpOperationService>();
            services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "Apilane Portal", Version = "1.0.0" };
                options.ServerInstructions = "Manage only the applications authorized for this connection. " +
                    "Call apilane_operations to discover operations, then request one operationId for its exact contract. " +
                    "Call PortalApplications_List through apilane_call to discover accessible applications and " +
                    "ApplicationPermissions_Get for current grants. All calls enforce live agent permissions. " +
                    "Renaming entities/properties, sharing, administrator operations and reading secrets are unavailable.";
            })
                // Per-request authentication and scope; no transport session can retain stale grants.
                .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
                .WithListToolsHandler(ListToolsAsync)
                .WithCallToolHandler(CallToolAsync);
            return services;
        }

        public static IApplicationBuilder UsePortalMcpAuthentication(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                if (!string.Equals(context.Request.Path.Value?.TrimEnd('/'), Path, StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                var service = context.RequestServices.GetRequiredService<IMcpAuthorizationService>();
                var host = context.RequestServices.GetRequiredService<IMcpPortalStore>();
                string resource;
                try
                {
                    resource = service.Resource;
                }
                catch (McpOAuthException ex)
                {
                    context.Response.StatusCode = ex.StatusCode;
                    await context.Response.WriteAsJsonAsync(new { error = ex.Error, error_description = ex.Message });
                    return;
                }
                context.Response.Headers.CacheControl = "no-store";
                // Cookies and raw agent keys are deliberately not MCP credentials.
                var authorization = context.Request.Headers.Authorization.ToString();
                var principal = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    && authorization.Length < 512
                    ? await service.AuthenticateAsync(authorization[7..]) : null;
                if (principal is null)
                {
                    context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{resource}/.well-known/oauth-protected-resource\", scope=\"mcp\"";
                    await host.WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                        "UNAUTHORIZED", "Authorize an MCP connection in the Portal.");
                    return;
                }

                if (context.Request.Headers.TryGetValue("Origin", out var origin)
                    && (origin.Count != 1 || !Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri)
                        || !string.Equals(originUri.GetLeftPart(UriPartial.Authority),
                            new Uri(resource).GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)))
                {
                    await host.WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                        "FORBIDDEN", "Origin is not permitted.");
                    return;
                }

                var bodyLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (bodyLimit is not null && !bodyLimit.IsReadOnly)
                {
                    bodyLimit.MaxRequestBodySize = 1024 * 1024;
                }
                if (context.Request.ContentLength > 1024 * 1024)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                context.User = principal;
                await next();
            });
        }

        public static IEndpointRouteBuilder MapPortalMcp(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapPortalMcpAuthorization();
            endpoints.MapMcp(Path).RequireAuthorization();
            return endpoints;
        }

        private static ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
        {
            RequireConnection(request);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new ListToolsResult
            {
                Tools = new List<Tool>
                {
                    new Tool
                    {
                        Name = "apilane_operations",
                        Description = "List available Portal management operations. Supply operationId to get its full API contract and referenced schemas before calling it. Runtime application and agent permissions still apply.",
                        InputSchema = JsonSerializer.SerializeToElement(new
                        {
                            type = "object", properties = new { operationId = new { type = "string" } }, additionalProperties = false
                        }),
                        Annotations = new ToolAnnotations { ReadOnlyHint = true, OpenWorldHint = false }
                    },
                    new Tool
                    {
                        Name = "apilane_call",
                        Description = "Execute an operationId discovered with apilane_operations. Put route parameters in path (strings), URL parameters in query, and the JSON request in body. Returns status, data and any warning. Writes and deletes can be destructive; inspect the contract and grants first.",
                        InputSchema = JsonSerializer.SerializeToElement(new
                        {
                            type = "object",
                            properties = new
                            {
                                operationId = new { type = "string" },
                                path = new { type = "object", additionalProperties = new { type = "string" } },
                                query = new { type = "object" },
                                body = new { }
                            },
                            required = new[] { "operationId" }, additionalProperties = false
                        }),
                        Annotations = new ToolAnnotations { ReadOnlyHint = false, DestructiveHint = true, OpenWorldHint = false }
                    }
                }
            });
        }

        private static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
        {
            RequireConnection(request);
            var services = request.Services ?? throw new InvalidOperationException("MCP services are unavailable.");
            var operations = services.GetRequiredService<IPortalMcpOperationService>();
            var arguments = request.Params.Arguments ?? new Dictionary<string, JsonElement>();
            try
            {
                if (request.Params.Name == "apilane_operations")
                {
                    if (arguments.Keys.Count > 1 || (arguments.Count == 1 && !arguments.ContainsKey("operationId")))
                    {
                        throw new ArgumentException("Supply only operationId, or no arguments to list operations.");
                    }
                    var id = arguments.TryGetValue("operationId", out var value) && value.ValueKind == JsonValueKind.String
                        ? value.GetString() : null;
                    if (arguments.Count != 0 && string.IsNullOrEmpty(id))
                    {
                        throw new ArgumentException("operationId must be a nonempty string.");
                    }
                    return Result(await operations.DescribeAsync(id, cancellationToken), false);
                }
                if (request.Params.Name == "apilane_call")
                {
                    var principal = request.User ?? throw new UnauthorizedAccessException("An MCP connection is required.");
                    var result = await operations.CallAsync(principal, arguments, cancellationToken);
                    return Result(result.Result, result.IsError);
                }
                throw new ArgumentException("Unknown tool.");
            }
            catch (ArgumentException ex)
            {
                return Result(JsonSerializer.SerializeToElement(new { error = ex.Message }), true);
            }
        }

        private static void RequireConnection<T>(RequestContext<T> request)
        {
            var services = request.Services ?? throw new InvalidOperationException("MCP services are unavailable.");
            var host = services.GetRequiredService<IMcpPortalStore>();
            if (!McpConnectionScope.IsConnection(request.User)
                || request.User?.Identity?.AuthenticationType != host.AgentAuthenticationType)
            {
                throw new UnauthorizedAccessException("An authorized MCP connection is required.");
            }
        }

        private static CallToolResult Result(JsonElement data, bool error)
        {
            return new CallToolResult
            {
                IsError = error, StructuredContent = data,
                Content = new List<ContentBlock> { new TextContentBlock { Text = data.GetRawText() } }
            };
        }
    }
}
