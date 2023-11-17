using Apilane.Common.Helpers;
using Apilane.Common.Models;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Web.Portal.Abstractions
{
    public interface IApiHttpService
    {
        Task<Either<DataResponse, HttpStatusCode>> GetAllDataAsync(string serverUrl, string appToken, string entity, string portalUserAuthToken);
        Task<Either<List<long>, HttpStatusCode>> ImportDataAsync(string serverUrl, string appToken, string entity, List<Dictionary<string, object?>> postData, string portalUserAuthToken);
        Task<Either<string, HttpStatusCode>> GetAsync(string url, string appToken, string portalUserAuthToken);
        Task<Either<string, HttpStatusCode>> PostAsync(string url, string appToken, string portalUserAuthToken, object postData);
    }
}
