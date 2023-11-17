using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Apilane.Api.Abstractions
{
    public interface IApplicationDataService
    {
        List<FilterData> GetSystemFilters(bool userHasFullAccess, string? differentiationEntity, DBWS_Entity entity, (Users? User, List<DBWS_Security> Security) User_Security);
        Task<List<long>> DeleteAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, string? differentiationEntity, string applicationEncryptionKey, string Ids, (Users? User, List<DBWS_Security> Security) userSecurity);
        List<DBWS_EntityProperty> GetEntityNotAllowedProperties(DBWS_Entity entity, List<DBWS_Security> data_UserSecurity);
        List<DBWS_EntityProperty> GetAllowedProperties(List<DBWS_EntityProperty> currentProperties, DBWS_Entity entity, List<DBWS_Security> data_UserSecurity);
        Task<DataResponse> GetAsync(string appToken, string? differentiationEntity, string applicationEncryptionKey, DBWS_Entity entity, int pageIndex, int pageSize, FilterData? filter, List<SortData>? sort, string? properties, (Users? User, List<DBWS_Security> Security) Data_UserSecurity, bool getTotal);
        Task<Dictionary<string, object?>> GetByIDAsync(string appToken, bool userHasFullAccess, DBWS_Entity entity, string? differentiationEntity, string applicationEncryptionKey, long id, string? properties, (Users? User, List<DBWS_Security> Security) Data_UserSecurity);
        FilterData? GetFilterData(DBWS_Entity entity, string? filter, List<DBWS_Security> security);
        Task<DataTotalResponse> GetHistoryByIdAsync(string appToken, bool userHasFullAccess, string? differentiationEntity, string applicationEncryptionKey, DBWS_Entity entity, long id, int? pageIndex, int? pageSize, (Users? User, List<DBWS_Security> Security) Data_UserSecurity);
        object? GetPropertyValue(string? differentiationEntity, string applicationEncryptionKey, DBWS_Entity entity, DBWS_EntityProperty property, JsonObject Object, Users? user);
        List<SortData>? GetSortData(DBWS_Entity entity, string? sortString, List<DBWS_Security> security);
        Task<Dictionary<string, object?>?> GetUserByIdAsync(string? appToken, long userID);
        Task<List<long>> PostAsync(string appToken, DBWS_Entity entity, DatabaseType databaseType, string? differentiationEntity, string applicationEncryptionKey, object item, (Users? User, List<DBWS_Security> Security) Data_UserSecurity);
        Task<long> PutAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, DatabaseType databaseType, string? differentiationEntity, string applicationEncryptionKey, object item, (Users? User, List<DBWS_Security> Security) Data_UserSecurity);
    }
}
