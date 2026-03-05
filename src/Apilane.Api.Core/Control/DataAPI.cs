﻿using Apilane.Api.Core.Abstractions;
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
using Orleans;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class DataAPI : IDataAPI
    {
        private readonly IClusterClient _clusterClient;
        private readonly IApplicationDataService _appDataService;
        private readonly ITransactionScopeService _transactionScopeService;
        private readonly ICustomAPI _customAPI;

        public DataAPI(
            IClusterClient clusterClient,
            IApplicationDataService appDataService,
            ITransactionScopeService transactionScopeService,
            ICustomAPI customAPI)
        {
            _clusterClient = clusterClient;
            _appDataService = appDataService;
            _transactionScopeService = transactionScopeService;
            _customAPI = customAPI;
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

        public async Task<OutTransactionOperationData> TransactionOperationsAsync(
            DBWS_Application application,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            DatabaseType databaseType,
            string? differentiationEntity,
            string applicationEncryptionKey,
            InTransactionOperationData data)
        {
            DBWS_Entity GetEntity(string entityName)
            {
                return application.Entities.SingleOrDefault(x => x.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ApilaneException(AppErrors.ERROR, $"Entity {entityName} does not exist");
            }

            if (data.Operations == null || data.Operations.Count == 0)
            {
                throw new ApilaneException(AppErrors.ERROR, "At least one operation is required");
            }

            var result = new OutTransactionOperationData();
            var resolvedResults = new Dictionary<string, List<long>>();

            using (var scope = _transactionScopeService.OpenTransactionScope(
                System.Transactions.TransactionScopeOption.Required,
                System.Transactions.IsolationLevel.ReadCommitted,
                TimeSpan.FromSeconds(20)))
            {
                foreach (var op in data.Operations)
                {
                    var opResult = new OutTransactionOperationResult
                    {
                        Action = op.Action,
                        Entity = op.Entity
                    };

                    switch (op.Action)
                    {
                        case TransactionAction.Post:
                        {
                            var resolvedData = op.Data != null
                                ? ResolveReferences(op.Data, resolvedResults)
                                : throw new ApilaneException(AppErrors.ERROR, "Post operation requires Data");

                            var ids = await PostAsync(
                                application.Token,
                                GetEntity(op.Entity),
                                userHasFullAccess,
                                appUser,
                                applicationSecurityList,
                                databaseType,
                                differentiationEntity,
                                applicationEncryptionKey,
                                resolvedData);

                            opResult.Created = ids;

                            if (!string.IsNullOrEmpty(op.Id))
                            {
                                resolvedResults[op.Id] = ids;
                            }

                            break;
                        }
                        case TransactionAction.Put:
                        {
                            var resolvedData = op.Data != null
                                ? ResolveReferences(op.Data, resolvedResults)
                                : throw new ApilaneException(AppErrors.ERROR, "Put operation requires Data");

                            var affected = await PutAsync(
                                application.Token,
                                GetEntity(op.Entity),
                                userHasFullAccess,
                                appUser,
                                applicationSecurityList,
                                databaseType,
                                differentiationEntity,
                                applicationEncryptionKey,
                                resolvedData);

                            opResult.Affected = affected;

                            break;
                        }
                        case TransactionAction.Delete:
                        {
                            var idsString = op.Ids
                                ?? throw new ApilaneException(AppErrors.ERROR, "Delete operation requires Ids");

                            var deleted = await DeleteAsync(
                                application.Token,
                                GetEntity(op.Entity),
                                userHasFullAccess,
                                appUser,
                                applicationSecurityList,
                                differentiationEntity,
                                applicationEncryptionKey,
                                idsString);

                            opResult.Deleted = deleted;

                            break;
                        }
                        case TransactionAction.Custom:
                        {
                            var resolvedData = op.Data != null
                                ? ResolveReferences(op.Data, resolvedResults)
                                : throw new ApilaneException(AppErrors.ERROR, "Custom operation requires Data with endpoint parameters");

                            var customEndpoint = application.CustomEndpoints.SingleOrDefault(x => x.Name.Equals(op.Entity, StringComparison.OrdinalIgnoreCase))
                                ?? throw new ApilaneException(AppErrors.ERROR, $"Custom endpoint '{op.Entity}' does not exist");

                            // Convert resolved data to Dictionary<string, string> for custom endpoint parameters
                            var uriParams = ExtractCustomEndpointParameters(resolvedData);

                            var customResult = await _customAPI.GetAsync(
                                application.Token,
                                userHasFullAccess,
                                appUser,
                                applicationSecurityList,
                                customEndpoint,
                                uriParams);

                            opResult.CustomResult = customResult;

                            break;
                        }
                        default:
                            throw new ApilaneException(AppErrors.ERROR, $"Unknown transaction action '{op.Action}'");
                    }

                    result.Results.Add(opResult);
                }

                scope.Complete();
            }

            return result;
        }

        private static readonly Regex RefPattern = new(@"^\$ref:(.+)$", RegexOptions.Compiled);

        private static object ResolveReferences(object data, Dictionary<string, List<long>> resolvedResults)
        {
            var json = JsonSerializer.Serialize(data);
            var node = JsonNode.Parse(json);
            if (node != null)
            {
                ResolveReferencesInNode(node, resolvedResults);
                return node;
            }
            return data;
        }

        private static void ResolveReferencesInNode(JsonNode node, Dictionary<string, List<long>> resolvedResults)
        {
            if (node is JsonObject obj)
            {
                foreach (var prop in obj.ToList())
                {
                    if (prop.Value is JsonValue val && val.TryGetValue<string>(out var str))
                    {
                        var match = RefPattern.Match(str);
                        if (match.Success)
                        {
                            var refId = match.Groups[1].Value;
                            if (resolvedResults.TryGetValue(refId, out var ids) && ids.Count > 0)
                            {
                                obj[prop.Key] = ids[0];
                            }
                            else
                            {
                                throw new ApilaneException(AppErrors.ERROR,
                                    $"Referenced operation '{refId}' has no results or does not exist");
                            }
                        }
                    }
                    else if (prop.Value is JsonObject or JsonArray)
                    {
                        ResolveReferencesInNode(prop.Value, resolvedResults);
                    }
                }
            }
            else if (node is JsonArray arr)
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue val && val.TryGetValue<string>(out var str))
                    {
                        var match = RefPattern.Match(str);
                        if (match.Success)
                        {
                            var refId = match.Groups[1].Value;
                            if (resolvedResults.TryGetValue(refId, out var ids) && ids.Count > 0)
                            {
                                arr[i] = ids[0];
                            }
                            else
                            {
                                throw new ApilaneException(AppErrors.ERROR,
                                    $"Referenced operation '{refId}' has no results or does not exist");
                            }
                        }
                    }
                    else if (arr[i] is JsonObject or JsonArray)
                    {
                        ResolveReferencesInNode(arr[i]!, resolvedResults);
                    }
                }
            }
        }

        private static Dictionary<string, string> ExtractCustomEndpointParameters(object resolvedData)
        {
            var result = new Dictionary<string, string>();
            var json = JsonSerializer.Serialize(resolvedData);
            var node = JsonNode.Parse(json);

            if (node is JsonObject obj)
            {
                foreach (var prop in obj)
                {
                    if (prop.Value is not null)
                    {
                        result[prop.Key] = prop.Value.ToString();
                    }
                }
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
