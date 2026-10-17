using Apilane.Common.Helpers;
using Apilane.Common.Models;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Portal.Abstractions
{
    public interface IApiHttpService
    {
        /// <summary>
        /// One page of the records of an entity (pageIndex from 1), with the total number of records.
        /// </summary>
        Task<Either<DataTotalResponse, HttpStatusCode>> GetDataPageAsync(string serverUrl, string appToken, string entity, int pageIndex, int pageSize, string portalUserAuthToken);
        Task<Either<List<long>, HttpStatusCode>> ImportDataAsync(string serverUrl, string appToken, string entity, List<Dictionary<string, object?>> postData, string portalUserAuthToken);
        Task<Either<string, HttpStatusCode>> PostAsync(string url, string appToken, string portalUserAuthToken, object postData, IDictionary<string, string>? headers = null);
    }
}
