using Apilane.Net.Models.Data;
using Apilane.Net.Request;
using Apilane.Net.Utilities;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilane.Net.Abstractions
{
    public interface IApilaneService
    {
        Task<Either<Models.Account.AccountLoginResponse<T>, ApilaneError>> AccountLoginAsync<T>(Models.Account.LoginItem loginItem, CancellationToken cancellationToken = default) where T : Models.Account.IApiUser;
        Task<Either<int, ApilaneError>> AccountLogoutAsync(AccountLogoutRequest request, CancellationToken cancellationToken = default);
        Task<Either<long, ApilaneError>> AccountRegisterAsync(Models.Account.IRegisterItem registerItem, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> AccountRenewAuthTokenAsync(AccountRenewAuthTokenRequest request, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> AccountUpdateAsync<T>(AccountUpdateRequest request, object updateItem, CancellationToken cancellationToken = default) where T : Models.Account.IApiUser;
        Task<Either<long[], ApilaneError>> DeleteDataAsync(Request.DataDeleteRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<long[], ApilaneError>> DeleteFileAsync(Request.FileDeleteRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<Models.Account.AccountUserDataResponse<T>, ApilaneError>> GetAccountUserDataAsync<T>(AccountUserDataRequest request, CancellationToken cancellationToken = default) where T : Models.Account.IApiUser;
        Task<Either<System.Collections.Generic.List<T>, ApilaneError>> GetAllDataAsync<T>(Request.DataGetAllRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<Models.ApplicationSchemaDto, ApilaneError>> GetApplicationSchemaAsync(string encryptionKey, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetCustomEndpointAsync(Request.CustomEndpointRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetCustomEndpointAsync<T>(Request.CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4, T5 Data5), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3, T4, T5>(Request.CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3, T4>(Request.CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3>(Request.CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2), ApilaneError>> GetCustomEndpointAsync<T1, T2>(Request.CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<DataResponse<T>, ApilaneError>> GetDataAsync<T>(Request.DataGetListRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetDataByIdAsync<T>(Request.DataGetByIdRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<DataTotalResponse<T>, ApilaneError>> GetDataTotalAsync<T>(Request.DataGetListRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetFileByIdAsync<T>(Request.FileGetByIdRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<DataResponse<T>, ApilaneError>> GetFilesAsync<T>(Request.FileGetListRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetStatsAggregateAsync(Request.StatsAggregateRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetStatsAggregateAsync<T>(Request.StatsAggregateRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetStatsDistinctAsync(Request.StatsDistinctRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetStatsDistinctAsync<T>(Request.StatsDistinctRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<long, ApilaneError>> HealthCheckAsync(CancellationToken cancellationToken = default);
        Task<Either<long[], ApilaneError>> PostDataAsync(Request.DataPostRequest apiRequest, object data, CancellationToken cancellationToken = default);
        Task<Either<long?, ApilaneError>> PostFileAsync(Request.FilePostRequest apiRequest, byte[] data, CancellationToken cancellationToken = default);
        Task<Either<int, ApilaneError>> PutDataAsync(Request.DataPutRequest apiRequest, object data, CancellationToken cancellationToken = default);
        Task<Either<OutTransactionData, ApilaneError>> TransactionDataAsync(Request.DataTransactionRequest apiRequest, InTransactionData data, CancellationToken cancellationToken = default);
        string UrlFor_Account_Manage_ForgotPassword();
        string UrlFor_Email_ForgotPassword(string email);
        string UrlFor_Email_RequestConfirmation(string email);
    }
}
