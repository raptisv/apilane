using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    public interface IPortalMcpOperationService
    {
        Task<JsonElement> DescribeAsync(string? operationId, CancellationToken cancellationToken);
        Task<(JsonElement Result, bool IsError)> CallAsync(ClaimsPrincipal principal,
            IDictionary<string, JsonElement> arguments, CancellationToken cancellationToken);
    }
}
