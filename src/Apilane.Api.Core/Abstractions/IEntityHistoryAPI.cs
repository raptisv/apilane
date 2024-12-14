using Apilane.Common.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface IEntityHistoryAPI
    {
        Task DeleteAsync(string appToken, string entity, List<long>? recordIds);
        Task<DataTotalResponse> GetPagedAsync(string appToken, long recordID, string entity, int? pageIndex, int? pageSize);
    }
}
