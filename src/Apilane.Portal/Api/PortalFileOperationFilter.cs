using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;

namespace Apilane.Portal.Api
{
    /// <summary>
    /// How a file download is described in the OpenAPI document. An action that returns a file
    /// declares its success response as [ProducesResponseType(typeof(Stream), 200)]; this filter
    /// then documents that response as binary 'application/octet-stream' instead of the
    /// 'application/json' every management API controller inherits. The error responses of the
    /// action stay JSON.
    /// </summary>
    public class PortalFileOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var fileStatusCodes = context.ApiDescription.SupportedResponseTypes
                .Where(x => x.Type == typeof(Stream))
                .Select(x => x.StatusCode.ToString());

            foreach (var statusCode in fileStatusCodes)
            {
                if (operation.Responses is null
                    || !operation.Responses.TryGetValue(statusCode, out var response)
                    || response is not OpenApiResponse fileResponse)
                {
                    continue;
                }

                // Keeps the schema Swashbuckle made for a Stream: a string in binary format.
                var schema = fileResponse.Content?.Values.FirstOrDefault()?.Schema;

                fileResponse.Content = new Dictionary<string, OpenApiMediaType>
                {
                    [MediaTypeNames.Application.Octet] = new OpenApiMediaType { Schema = schema }
                };
            }
        }
    }
}
