using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface IStatsAPI
    {
        Task<List<Dictionary<string, object?>>> AggregateAsync(DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, string? differentiationEntity, string properties, int pageIndex, int pageSize, string? filter, string? groupBy, string orderDirection = "DESC");
        Task<CountDataHistoryDto> CountDataAndHistoryAsync(string appToken, DBWS_Entity entity);
        Task<List<Dictionary<string, object?>>> DistinctAsync(DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, string? differentiationEntity, string property, string? filter);
    }
}
