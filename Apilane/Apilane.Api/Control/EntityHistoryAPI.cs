using Apilane.Api.Abstractions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api
{
    public class EntityHistoryAPI : IEntityHistoryAPI
    {
        private readonly IApplicationHelperService _applicationHelperService;

        public EntityHistoryAPI(
            IApplicationHelperService applicationHelperService)
        {
            _applicationHelperService = applicationHelperService;
        }

        public async Task<DataTotalResponse> GetPagedAsync(
            string appToken,
            long recordId,
            string entity,
            int? pageIndex,
            int? pageSize)
        {
            if (pageIndex is null || pageIndex < 1)
            {
                pageIndex = 1;
            }

            if (pageSize is null || pageSize > 1000)
            {
                pageSize = 1000;
            }

            var historyRecords = await _applicationHelperService.GetHistoryForRecordPagedAsync(appToken, entity, recordId, pageIndex.Value, pageSize.Value);

            return new DataTotalResponse()
            {
                Data = historyRecords.Data,
                Total = historyRecords.Total
            };
        }

        public Task DeleteAsync( string appToken, string entity, List<long>? recordIds) 
            => _applicationHelperService.DeleteHistoryAsync(appToken, entity, recordIds);
    }
}
