using Apilane.Portal.Abstractions;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Apilane.Portal.Extensions
{
    public static class PortalMcpAuthorizationExtensions
    {
        private const string RateLimitPolicy = "McpOAuth";

        public static IServiceCollection AddPortalMcpAuthorization(this IServiceCollection services)
        {
            services.AddScoped<IMcpAuthorizationService, McpAuthorizationService>();
            services.AddOptions<McpOAuthRateLimitOptions>().BindConfiguration("McpOAuthRateLimit")
                .Validate(x => x.PermitLimit > 0 && x.WindowSeconds > 0, "McpOAuthRateLimit values must be greater than zero.")
                .ValidateOnStart();
            services.AddRateLimiter(options => options.AddPolicy(RateLimitPolicy, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<McpOAuthRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PermitLimit, Window = TimeSpan.FromSeconds(limits.WindowSeconds), QueueLimit = 0
                });
            }));
            return services;
        }

        public static IEndpointRouteBuilder MapPortalMcpAuthorization(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/api/mcp/.well-known/oauth-protected-resource", (HttpContext context, IMcpAuthorizationService service) => RunAsync(context, () =>
                Task.FromResult(Results.Json(new { resource = service.Resource, authorization_servers = new[] { service.Resource }, scopes_supported = new[] { "mcp" }, bearer_methods_supported = new[] { "header" } }))))
                .AllowAnonymous().ExcludeFromDescription();

            foreach (var path in new[]
            {
                "/api/mcp/.well-known/oauth-authorization-server", "/api/mcp/.well-known/openid-configuration",
                "/.well-known/oauth-authorization-server/api/mcp", "/.well-known/openid-configuration/api/mcp"
            })
            {
                endpoints.MapGet(path, (HttpContext context, IMcpAuthorizationService service) => RunAsync(context, () =>
                    Task.FromResult(Results.Json(new
                    {
                        issuer = service.Resource,
                        authorization_endpoint = service.Resource + "/oauth/authorize",
                        token_endpoint = service.Resource + "/oauth/token",
                        registration_endpoint = service.Resource + "/oauth/register",
                        response_types_supported = new[] { "code" },
                        grant_types_supported = new[] { "authorization_code", "refresh_token" },
                        token_endpoint_auth_methods_supported = new[] { "none" },
                        code_challenge_methods_supported = new[] { "S256" },
                        scopes_supported = new[] { "mcp" },
                        client_id_metadata_document_supported = false
                    }))))
                    .AllowAnonymous().ExcludeFromDescription();
            }

            endpoints.MapPost("/api/mcp/oauth/register", (HttpContext context, IMcpAuthorizationService service) => RunAsync(context, async () =>
            {
                if (!context.Request.HasJsonContentType())
                {
                    throw new McpOAuthException("invalid_request", "Use JSON client metadata.");
                }
                var request = await context.Request.ReadFromJsonAsync<McpClientRegistrationRequest>(context.RequestAborted)
                    ?? throw new McpOAuthException("invalid_client_metadata", "Client metadata is required.");
                return Results.Json(await service.RegisterAsync(request), statusCode: StatusCodes.Status201Created);
            })).AllowAnonymous().RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();

            endpoints.MapGet("/api/mcp/oauth/authorize", (HttpContext context, IMcpAuthorizationService service) => RunAsync(context, async () =>
            {
                var query = context.Request.Query;
                if (Read(query, "response_type") != "code" || Read(query, "code_challenge_method") != "S256"
                    || Read(query, "scope") is not "" and not "mcp")
                {
                    throw new McpOAuthException("invalid_request", "Only code flow with PKCE S256 and the mcp scope is supported.");
                }
                var redirect = await service.BeginAuthorizationAsync(Read(query, "client_id"), Read(query, "redirect_uri"),
                    Read(query, "code_challenge"), Read(query, "state"), Read(query, "resource"));
                return Results.Redirect(redirect);
            })).AllowAnonymous().RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();

            endpoints.MapPost("/api/mcp/oauth/token", (HttpContext context, IMcpAuthorizationService service) => RunAsync(context, async () =>
            {
                if (!string.Equals(context.Request.ContentType?.Split(';', 2)[0].Trim(),
                    "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
                    || context.Request.Headers.ContainsKey("Authorization"))
                {
                    throw new McpOAuthException("invalid_request", "Use a form-encoded public-client token request.");
                }
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                if (!string.IsNullOrEmpty(Read(form, "client_secret")))
                {
                    throw new McpOAuthException("invalid_client", "Client secrets are not supported.");
                }
                return Results.Json(await service.ExchangeAsync(new McpTokenRequest
                {
                    GrantType = Read(form, "grant_type"), ClientId = Read(form, "client_id"), Resource = Read(form, "resource"),
                    Code = Read(form, "code"), RedirectUri = Read(form, "redirect_uri"), CodeVerifier = Read(form, "code_verifier"),
                    RefreshToken = Read(form, "refresh_token")
                }));
            })).AllowAnonymous().RequireRateLimiting(RateLimitPolicy).ExcludeFromDescription();
            return endpoints;
        }

        private static async Task<IResult> RunAsync(HttpContext context, Func<Task<IResult>> action)
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            try
            {
                var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit is not null && !limit.IsReadOnly)
                {
                    limit.MaxRequestBodySize = 16384;
                }
                if (context.Request.ContentLength > 16384 || context.Request.QueryString.Value?.Length > 8192)
                {
                    throw new McpOAuthException("invalid_request", "The request is too large.");
                }
                return await action();
            }
            catch (McpOAuthException ex)
            {
                return Results.Json(new { error = ex.Error, error_description = ex.Message }, statusCode: ex.StatusCode);
            }
            catch (JsonException)
            {
                return Results.Json(new { error = "invalid_request", error_description = "Invalid JSON request." }, statusCode: 400);
            }
            catch (BadHttpRequestException)
            {
                return Results.Json(new { error = "invalid_request", error_description = "Invalid request body." }, statusCode: 400);
            }
            catch (InvalidDataException)
            {
                return Results.Json(new { error = "invalid_request", error_description = "Invalid request body." }, statusCode: 400);
            }
        }

        private static string Read(IEnumerable<KeyValuePair<string, StringValues>> values, string name)
        {
            foreach (var value in values)
            {
                if (value.Key == name)
                {
                    if (value.Value.Count != 1 || value.Value.ToString().Length > 2048)
                    {
                        throw new McpOAuthException("invalid_request", "Repeated or oversized OAuth parameters are not allowed.");
                    }
                    return value.Value.ToString();
                }
            }
            return string.Empty;
        }
    }

    public class McpOAuthRateLimitOptions
    {
        public int PermitLimit { get; set; } = 60;
        public int WindowSeconds { get; set; } = 60;
    }
}
