using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Apilane.Api.Abstractions
{
    public interface IDataAPI
    {
        Task<List<long>> DeleteAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, string? differentiationEntity, string applicationEncryptionKey, string ids);
        Task<DataResponse> GetAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, DatabaseType databaseType, string? differentiationEntity, string applicationEncryptionKey, int pageIndex, int pageSize, string? filter, string? sort, string? properties, bool getTotal);
        Task<Dictionary<string, object?>> GetByIDAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, string? differentiationEntity, string applicationEncryptionKey, long id, string? properties);
        Task<DataTotalResponse> GetHistoryByIDAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, string? differentiationEntity, string applicationEncryptionKey, long id, int? pageIndex, int? pageSize);
        Task<List<long>> PostAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, DatabaseType databaseType, string? differentiationEntity, string applicationEncryptionKey, object item);
        Task<long> PutAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, DatabaseType databaseType, string? differentiationEntity, string applicationEncryptionKey, object item);
        Task<OutTransactionData> TransactionAsync(DBWS_Application application, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, DatabaseType databaseType, string? differentiationEntity, string applicationEncryptionKey, InTransactionData data);
        Task<bool> AllowGetSchemaAsync(string appToken, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList);
    }
}
