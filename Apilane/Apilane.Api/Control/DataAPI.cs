using Apilane.Api.Abstractions;
using Apilane.Api.Enums;
using Apilane.Api.Exceptions;
using Apilane.Api.Grains;
using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Api.Services;
using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Orleans;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api
{
    public class DataAPI : IDataAPI
    {
        private readonly IClusterClient _clusterClient;
        private readonly IApplicationDataService _appDataService;
        private readonly ITransactionScopeService _transactionScopeService;

        public DataAPI(
            IClusterClient clusterClient,
            IApplicationDataService appDataService,
            ITransactionScopeService transactionScopeService)
        {
            _clusterClient = clusterClient;
            _appDataService = appDataService;
            _transactionScopeService = transactionScopeService;
        }

        public async Task<Dictionary<string, object?>> GetByIDAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            string? differentiationEntity,
            string applicationEncryptionKey,
            long id,
            string? properties)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), entity.Name, SecurityActionType.get);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: entity.Name, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            return await _appDataService.GetByIDAsync(
                appToken,
                userHasFullAccess,
                entity,
                differentiationEntity,
                applicationEncryptionKey,
                id,
                properties,
                (appUser, userSecurity));
        }

        public async Task<DataTotalResponse> GetHistoryByIDAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            string? differentiationEntity,
            string applicationEncryptionKey,
            long id,
            int? pageIndex,
            int? pageSize)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            return await _appDataService.GetHistoryByIdAsync(
                appToken,
                userHasFullAccess,
                differentiationEntity,
                applicationEncryptionKey,
                entity,
                id,
                pageIndex,
                pageSize,
                (appUser, userSecurity));
        }

        public async Task<DataResponse> GetAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            DatabaseType databaseType,
            string? differentiationEntity,
            string applicationEncryptionKey,
            int pageIndex,
            int pageSize,
            string? filter,
            string? sort,
            string? properties,
            bool getTotal)
        {
            if (pageIndex <= 0)
            {
                pageIndex = 1;
            }

            if (pageSize < 0 || pageSize > 1000)
            {
                pageSize = 1000;
            }

            // Load security
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), entity.Name, SecurityActionType.get);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: entity.Name, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            var systemFilters = _appDataService.GetSystemFilters(userHasFullAccess, differentiationEntity, entity, (appUser, userSecurity));
            var filterData = _appDataService.GetFilterData(entity, filter, userSecurity);
            if (filterData is not null)
            {
                systemFilters.Add(filterData);
            }

            return await _appDataService.GetAsync(
                appToken,
                differentiationEntity,
                applicationEncryptionKey,
                entity,
                pageIndex,
                pageSize,
                new FilterData(FilterData.FilterLogic.AND, systemFilters),
                _appDataService.GetSortData(entity, string.IsNullOrWhiteSpace(sort) ? entity.EntDefaultOrder : sort, userSecurity),
                properties,
                (appUser, userSecurity),
                getTotal);
        }

        public async Task<List<long>> PostAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            DatabaseType databaseType,
            string? differentiationEntity,
            string applicationEncryptionKey,
            object item)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.post)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.post);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), entity.Name, SecurityActionType.post);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: entity.Name, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            return await _appDataService.PostAsync(
                appToken,
                entity,
                databaseType,
                differentiationEntity,
                applicationEncryptionKey,
                item,
                (appUser, userSecurity));
        }

        public async Task<long> PutAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            DatabaseType databaseType,
            string? differentiationEntity,
            string applicationEncryptionKey,
            object item)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.put)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.put);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), entity.Name, SecurityActionType.put);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: entity.Name, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            return await _appDataService.PutAsync(
                appToken,
                entity,
                userHasFullAccess,
                databaseType,
                differentiationEntity,
                applicationEncryptionKey,
                item,
                (appUser, userSecurity));
        }

        public async Task<List<long>> DeleteAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            string? differentiationEntity,
            string applicationEncryptionKey,
            string ids)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.delete)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.delete);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), entity.Name, SecurityActionType.delete);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: entity.Name, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            return await _appDataService.DeleteAsync(
                appToken,
                entity,
                userHasFullAccess,
                differentiationEntity,
                applicationEncryptionKey,
                ids,
                (appUser, userSecurity));
        }

        public async Task<OutTransactionData> TransactionAsync(
            DBWS_Application application,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            DatabaseType databaseType,
            string? differentiationEntity,
            string applicationEncryptionKey,
            InTransactionData data)
        {
            DBWS_Entity GetEntity(string entityName)
            {
                return application.Entities.SingleOrDefault(x => x.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ApilaneException(AppErrors.ERROR, $"Entity {entityName} does not exist");
            }

            var result = new OutTransactionData();

            using (var scope = _transactionScopeService.OpenTransactionScope(
                System.Transactions.TransactionScopeOption.Required,
                System.Transactions.IsolationLevel.ReadCommitted,
                TimeSpan.FromSeconds(20)))
            {
                if (data.Post != null)
                {
                    foreach (var obj in data.Post)
                    {
                        result.Post.AddRange(await PostAsync(
                            application.Token,
                            GetEntity(obj.Entity),
                            userHasFullAccess,
                            appUser,
                            applicationSecurityList,
                            databaseType,
                            differentiationEntity,
                            applicationEncryptionKey,
                            obj.Data));
                    }
                }

                if (data.Put != null)
                {
                    foreach (var obj in data.Put)
                    {
                        result.Put += await PutAsync(
                            application.Token,
                            GetEntity(obj.Entity),
                            userHasFullAccess,
                            appUser,
                            applicationSecurityList,
                            databaseType,
                            differentiationEntity,
                            applicationEncryptionKey,
                            obj.Data);
                    }
                }

                if (data.Delete != null)
                {
                    foreach (var obj in data.Delete)
                    {
                        result.Delete.AddRange(await DeleteAsync(
                            application.Token,
                            GetEntity(obj.Entity),
                            userHasFullAccess,
                            appUser,
                            applicationSecurityList,
                            differentiationEntity,
                            applicationEncryptionKey,
                            obj.Ids));
                    }
                }

                scope.Complete();
            }

            return result;
        }

        public async Task<bool> AllowGetSchemaAsync(
            string appToken,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(Globals.SCHEMA, Enumerable.Empty<DBWS_EntityProperty>().ToList(), SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, Globals.SCHEMA, SecurityTypes.Schema, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: Globals.SCHEMA);
            }

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), Globals.SCHEMA + appToken, SecurityActionType.get);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: Globals.SCHEMA, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            return true;
        }
    }
}
