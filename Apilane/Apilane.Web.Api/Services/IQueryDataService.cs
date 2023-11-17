using System.Collections.Generic;

namespace Apilane.Web.Api.Services
{
    public interface IQueryDataService
    {
        string AppToken { get; }
        string AuthToken { get; }
        string RouteAction { get; }
        string RouteController { get; }
        string IPAddress { get; }
        string Entity { get; }
        string CustomEndpoint { get; }
        bool IsPortalRequest { get; }
        Dictionary<string, string> UriParams { get; }
    }
}
