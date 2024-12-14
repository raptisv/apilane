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
        Task<Either<long[], ApilaneError>> DeleteDataAsync(DataDeleteRequest request, CancellationToken cancellationToken = default);
        Task<Either<long[], ApilaneError>> DeleteFileAsync(FileDeleteRequest request, CancellationToken cancellationToken = default);
        Task<Either<AccountUserDataResponse<T>, ApilaneError>> GetAccountUserDataAsync<T>(AccountUserDataRequest request, CancellationToken cancellationToken = default) where T : IApiUser;
        Task<Either<System.Collections.Generic.List<T>, ApilaneError>> GetAllDataAsync<T>(DataGetAllRequest request, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<Models.ApplicationSchemaDto, ApilaneError>> GetApplicationSchemaAsync(DataGetSchemaRequest request, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetCustomEndpointAsync(CustomEndpointRequest request, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetCustomEndpointAsync<T>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4, T5 Data5), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3, T4, T5>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3, T4 Data4), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3, T4>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2, T3 Data3), ApilaneError>> GetCustomEndpointAsync<T1, T2, T3>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<(T1 Data1, T2 Data2), ApilaneError>> GetCustomEndpointAsync<T1, T2>(CustomEndpointRequest customendpoint, CancellationToken cancellationToken = default);
        Task<Either<DataResponse<T>, ApilaneError>> GetDataAsync<T>(DataGetListRequest request, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetDataByIdAsync<T>(DataGetByIdRequest request, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<DataTotalResponse<T>, ApilaneError>> GetDataTotalAsync<T>(DataGetListRequest request, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetFileByIdAsync<T>(FileGetByIdRequest request, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<DataResponse<T>, ApilaneError>> GetFilesAsync<T>(FileGetListRequest request, JsonSerializerOptions? customJsonSerializerOptions = null, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetStatsAggregateAsync(StatsAggregateRequest request, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetStatsAggregateAsync<T>(StatsAggregateRequest request, CancellationToken cancellationToken = default);
        Task<Either<string, ApilaneError>> GetStatsDistinctAsync(StatsDistinctRequest request, CancellationToken cancellationToken = default);
        Task<Either<T, ApilaneError>> GetStatsDistinctAsync<T>(StatsDistinctRequest request, CancellationToken cancellationToken = default);
        Task<Either<long, ApilaneError>> HealthCheckAsync(CancellationToken cancellationToken = default);
        Task<Either<long[], ApilaneError>> PostDataAsync(DataPostRequest request, object data, CancellationToken cancellationToken = default);
        Task<Either<long?, ApilaneError>> PostFileAsync(FilePostRequest request, byte[] data, CancellationToken cancellationToken = default);
        Task<Either<int, ApilaneError>> PutDataAsync(DataPutRequest request, object data, CancellationToken cancellationToken = default);
        Task<Either<OutTransactionData, ApilaneError>> TransactionDataAsync(DataTransactionRequest request, InTransactionData data, CancellationToken cancellationToken = default);
        string UrlFor_Account_Manage_ForgotPassword();
        string UrlFor_Email_ForgotPassword(string email);
        string UrlFor_Email_RequestConfirmation(string email);
    }
}
