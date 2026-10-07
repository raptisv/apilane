using Apilane.Portal.Api.V1.Contracts;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    public interface IMcpAuthorizationService
    {
        string Resource { get; }
        Task<McpClientRegistrationResponse> RegisterAsync(McpClientRegistrationRequest request);
        Task<string> BeginAuthorizationAsync(string clientId, string redirectUri, string challenge, string state, string resource);
        Task<McpAuthorizationResponse> GetAuthorizationAsync(string requestId);
        Task<McpRedirectResponse> ApproveAsync(McpApproveRequest request);
        Task<McpRedirectResponse> DenyAsync(string requestId);
        Task<List<McpConnectionResponse>> GetConnectionsAsync();
        Task RevokeAsync(string id);
        Task<McpTokenResponse> ExchangeAsync(McpTokenRequest request);
        Task<ClaimsPrincipal?> AuthenticateAsync(string accessToken);
    }
}
