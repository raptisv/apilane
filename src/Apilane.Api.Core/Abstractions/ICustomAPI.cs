using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface ICustomAPI
    {
        Task<List<List<Dictionary<string, object?>>>> GetAsync(string appToken, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, DBWS_CustomEndpoint customEndpoint, Dictionary<string, string> uriParams);
        string GetQueryFixed(DBWS_CustomEndpoint item, Dictionary<string, string> uriParams);
        Task<List<List<Dictionary<string, object?>>>> TestQueryAsync(DBWS_CustomEndpoint item, Dictionary<string, string> uriParams);
    }
}
