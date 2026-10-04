using Apilane.Portal.Services;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// Marks an action that ends with the API-server cache reset (IApiServerCacheReset): its
    /// success response may carry the 'Warning' header. The OpenAPI document describes the header
    /// for every action that has this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class ProducesCacheResetWarningAttribute : Attribute
    {
    }

    public class PortalWarningHeaderOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (!context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<ProducesCacheResetWarningAttribute>().Any()
                || operation.Responses is null)
            {
                return;
            }

            foreach (var response in operation.Responses.Where(x => x.Key.StartsWith('2')).Select(x => x.Value).OfType<OpenApiResponse>())
            {
                response.Headers ??= new Dictionary<string, IOpenApiHeader>();

                response.Headers[ApiServerCacheReset.WarningHeaderName] = new OpenApiHeader
                {
                    Description = "Present only when the change was saved and the API server could not be told to reload the application. " +
                        $"Its value is a text for the user: '{ApiServerCacheReset.WarningText}'. " +
                        "POST /api/v1/applications/{appToken}/cache-reset tries again.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                };
            }
        }
    }
}
