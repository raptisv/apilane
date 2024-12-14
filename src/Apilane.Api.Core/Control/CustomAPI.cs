using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Grains;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Orleans;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class CustomAPI : ICustomAPI
    {
        private readonly ILogger<CustomAPI> _logger;
        private readonly IClusterClient _clusterClient; 
        private readonly IApplicationDataStoreFactory _dataStore;
        private readonly ITransactionScopeService _transactionScopeService;

        public CustomAPI(
            ILogger<CustomAPI> logger,
            IClusterClient clusterClient,
            IApplicationDataStoreFactory dataStore,
            ITransactionScopeService transactionScopeService)
        {
            _logger = logger;
            _dataStore = dataStore;
            _clusterClient = clusterClient;
            _transactionScopeService = transactionScopeService;
        }

        public async Task<List<List<Dictionary<string, object?>>>> GetAsync(
            string appToken,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            DBWS_CustomEndpoint customEndpoint,
            Dictionary<string, string> uriParams)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(customEndpoint.Name, new List<DBWS_EntityProperty>(), SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, customEndpoint.Name, SecurityTypes.CustomEndpoint, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: customEndpoint.Name);
            }

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), $"custom:{customEndpoint.Name}", SecurityActionType.get);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            string query = GetQueryFixed(customEndpoint, uriParams);

            // If authorized, replace owner
            if (appUser is not null)
            {
                query = query.Replace("{Owner}", appUser.ID.ToString());
            }

            try
            {
                using (var scope = _transactionScopeService.OpenTransactionScope(
                    System.Transactions.TransactionScopeOption.Required,
                    System.Transactions.IsolationLevel.ReadCommitted,
                    TimeSpan.FromSeconds(20)))
                {
                    var result = await _dataStore.ExecuteCustomAsync(query);

                    scope.Complete();

                    return result;
                }
            }
            catch (SqlException ex)
            {
                throw new ApilaneException(AppErrors.ERROR, ex.Message);
            }
            catch (SQLiteException ex)
            {
                throw new ApilaneException(AppErrors.ERROR, ex.Message);
            }
            catch (MySqlException ex)
            {
                throw new ApilaneException(AppErrors.ERROR, ex.Message);
            }
            catch (Exception)
            {
                throw new ApilaneException(AppErrors.ERROR, Globals.GeneralError);
            }
        }

        public async Task<List<List<Dictionary<string, object?>>>> TestQueryAsync(
            DBWS_CustomEndpoint item,
            Dictionary<string, string> uriParams)
        {
            try
            {
                using (var scope = _transactionScopeService.OpenTransactionScope(
                    System.Transactions.TransactionScopeOption.Required,
                    System.Transactions.IsolationLevel.ReadCommitted,
                    TimeSpan.FromSeconds(20)))
                {
                    return (await _dataStore.ExecuteCustomAsync(GetQueryFixed(item, uriParams)));

                    // IMPORTANT! This transaction should NOT be commited since it is test only
                    // scope.Complete();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, $"Exception of type SqlException in RunQuery: {ex.Message}");
                throw new ApilaneException(AppErrors.ERROR, ex.Message);
            }
            catch (SQLiteException ex)
            {
                _logger.LogError(ex, $"Exception of type SQLiteException in RunQuery: {ex.Message}");
                throw new ApilaneException(AppErrors.ERROR, ex.Message);
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, $"Exception of type MySqlException in RunQuery: {ex.Message}");
                throw new ApilaneException(AppErrors.ERROR, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception of type Exception in RunQuery: {ex.Message}");
                throw new ApilaneException(AppErrors.ERROR, null);
            }
        }

        public string GetQueryFixed(
            DBWS_CustomEndpoint item,
            Dictionary<string, string> uriParams)
        {
            StringBuilder result = new StringBuilder(item.Query);
            foreach (string param in item.GetParameters())
            {
                var uriParam = uriParams.ContainsKey(param) ? uriParams[param] : null;
                long? UriParamForSql = Utils.GetNullLong(uriParam, null);
                result = result.Replace($"{{{param}}}", UriParamForSql?.ToString() ?? "null");
            }

            return result.ToString();
        }
    }
}
