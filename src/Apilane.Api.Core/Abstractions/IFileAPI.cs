using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Models.AppModules.Files;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Abstractions
{
    public interface IFileAPI
    {
        Task<List<long>> DeleteAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, string? differentiationEntity, string applicationEncryptionKey, string ids);
        Task<DataResponse> GetAsync(string appToken, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, string? differentiationEntity, string applicationEncryptionKey, int pageIndex, int pageSize, string? properties, string? filter, string? sort, bool getTotal);
        Task<object> GetByIdAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, string? differentiationEntity, string applicationEncryptionKey, long id, string? properties);
        FileInfo GetFileInfoAsync(string appToken, string fileUID);
        Task<Files?> GetFileItemAsync(long FileID);
        Task<Files?> GetFileItemAsync(string FileUID);
        Task<long> PostAsync(string appToken, DBWS_Entity entity, bool userHasFullAccess, Users? appUser, List<DBWS_Security> applicationSecurityList, DatabaseType databaseType, string? differentiationEntity, string applicationEncryptionKey, int maxAllowedFileSizeInKB, byte[] buffer, string FileName, string UID, bool Public);
    }
}
