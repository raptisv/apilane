using Apilane.Net.Models.Account;
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
        Task<Either<AccountLoginResponse<T>, ApilaneError>> AccountLoginAsync<T>(AccountLoginRequest request, CancellationToken cancellationToken = default) where T : IApiUser;
        Task<Either<int, ApilaneError>> AccountLogoutAsync(AccountLogoutRequest request, CancellationToken cancellationToken = default);
        Task<Either<long, ApilaneError>> AccountRegisterAsync(AccountRegisterRequest request, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> AccountRenewAuthTokenAsync(AccountRenewAuthTokenRequest request, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> AccountUpdateAsync<T>(AccountUpdateRequest request, object updateItem, CancellationToken cancellationToken = default) where T : IApiUser;
        Task<Either<long[], ApilaneError>> DeleteDataAsync(DataDeleteRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<long[], ApilaneError>> DeleteFileAsync(FileDeleteRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<AccountUserDataResponse<T>, ApilaneError>> GetAccountUserDataAsync<T>(AccountUserDataRequest request, CancellationToken cancellationToken = default) where T : IApiUser;
        Task<Either<System.Collections.Generic.List<T>, ApilaneError>> GetAllDataAsync<T>(DataGetAllRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<Models.ApplicationSchemaDto, ApilaneError>> GetApplicationSchemaAsync(string encryptionKey, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetCustomEndpointAsync(CustomEndpointRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetCustomEndpointAsync<T>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4, T5 Data5), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3, T4, T5>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3, T4>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2), ApilaneError>> GetCustomEndpointAsync<T1, T2>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<DataResponse<T>, ApilaneError>> GetDataAsync<T>(DataGetListRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetDataByIdAsync<T>(DataGetByIdRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<DataTotalResponse<T>, ApilaneError>> GetDataTotalAsync<T>(DataGetListRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetFileByIdAsync<T>(FileGetByIdRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<DataResponse<T>, ApilaneError>> GetFilesAsync<T>(FileGetListRequest apiRequest, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetStatsAggregateAsync(StatsAggregateRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetStatsAggregateAsync<T>(StatsAggregateRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetStatsDistinctAsync(StatsDistinctRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetStatsDistinctAsync<T>(StatsDistinctRequest apiRequest, CancellationToken cancellationToken = default);
        Task<Either<long, ApilaneError>> HealthCheckAsync(CancellationToken cancellationToken = default);
        Task<Either<long[], ApilaneError>> PostDataAsync(DataPostRequest apiRequest, object data, CancellationToken cancellationToken = default);
        Task<Either<long?, ApilaneError>> PostFileAsync(FilePostRequest apiRequest, byte[] data, CancellationToken cancellationToken = default);
        Task<Either<int, ApilaneError>> PutDataAsync(DataPutRequest apiRequest, object data, CancellationToken cancellationToken = default);
        Task<Either<OutTransactionData, ApilaneError>> TransactionDataAsync(DataTransactionRequest apiRequest, InTransactionData data, CancellationToken cancellationToken = default);
        string UrlFor_Account_Manage_ForgotPassword();
        string UrlFor_Email_ForgotPassword(string email);
        string UrlFor_Email_RequestConfirmation(string email);
    }
}
