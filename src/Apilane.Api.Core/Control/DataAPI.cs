﻿using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Models.AppModules.Files;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
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
        private readonly IApplicationDataService _appDataService;
        private readonly ITransactionScopeService _transactionScopeService;
        private readonly ICustomAPI _customAPI;
        private readonly IAccountAPI _accountAPI;

        public DataAPI(
            IApplicationDataService appDataService,
            ITransactionScopeService transactionScopeService,
            ICustomAPI customAPI,
            IAccountAPI accountAPI)
        {
            _appDataService = appDataService;
            _transactionScopeService = transactionScopeService;
            _customAPI = customAPI;
            _accountAPI = accountAPI;
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

            await EntityAccess.EnforceRateLimitAsync(appToken, appUser, userSecurity, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

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

            await EntityAccess.EnforceRateLimitAsync(appToken, appUser, userSecurity, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

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

            pageSize = Utils.GetPageSize(pageSize);

            // Load security
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            await EntityAccess.EnforceRateLimitAsync(appToken, appUser, userSecurity, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

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
                string.IsNullOrWhiteSpace(sort)
                    ? _appDataService.GetDefaultSortData(entity, userSecurity)
                    : _appDataService.GetSortData(entity, sort, userSecurity),
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

            await EntityAccess.EnforceRateLimitAsync(appToken, appUser, userSecurity, entity.Name, SecurityTypes.Entity, SecurityActionType.post);

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

            await EntityAccess.EnforceRateLimitAsync(appToken, appUser, userSecurity, entity.Name, SecurityTypes.Entity, SecurityActionType.put);

            var affected = await _appDataService.PutAsync(
                appToken,
                entity,
                userHasFullAccess,
                databaseType,
                differentiationEntity,
                applicationEncryptionKey,
                item,
                (appUser, userSecurity));

            // A password set through the data API ends the sessions of the user, as Account/ChangePassword does:
            // an owner who resets a compromised account must not leave the tokens already handed out working.
            if (affected > 0 && entity.Name.Equals(nameof(Users)))
            {
                var userIds = GetUsersWithNewPassword(entity, differentiationEntity, userSecurity, item);

                if (userIds.Count > 0)
                {
                    await _accountAPI.LogoutEverywhereAsync(
                        appToken,
                        await GetReachableIdsAsync(appToken, entity, userHasFullAccess, appUser, userSecurity, differentiationEntity, applicationEncryptionKey, userIds));
                }
            }

            return affected;
        }

        /// <summary>
        /// The users whose Password a put body sets: the objects that carry the property (in any case, as the put reads
        /// it) and an ID. None when the caller may not write the Password, since the put then left it unchanged.
        /// </summary>
        private List<long> GetUsersWithNewPassword(
            DBWS_Entity usersEntity,
            string? differentiationEntity,
            List<DBWS_Security> userSecurity,
            object item)
        {
            var passwordProperty = usersEntity.Properties
                .Where(x => x.Name.Equals(nameof(Users.Password)) && x.AllowEdit(differentiationEntity, usersEntity.HasDifferentiationProperty))
                .ToList();

            if (_appDataService.GetAllowedProperties(passwordProperty, usersEntity, userSecurity).Count == 0)
            {
                return new List<long>();
            }

            var objects = new List<JsonObject>();
            CollectObjects(JsonNode.Parse(Utils.GetString(item)), objects);

            return objects
                .Where(x => x.Any(p => p.Key.Equals(nameof(Users.Password), StringComparison.OrdinalIgnoreCase)))
                .Select(x => Utils.GetLong(x.GetObjectProperty(Globals.PrimaryKeyColumn)))
                .Where(x => x > 0)
                .Distinct()
                .ToList();
        }

        // The objects of a put body: one object, or an array of them (arrays nest, as the put reads them)
        private static void CollectObjects(JsonNode? node, List<JsonObject> objects)
        {
            if (node is JsonObject obj)
            {
                objects.Add(obj);
            }
            else if (node is JsonArray array)
            {
                foreach (var child in array)
                {
                    CollectObjects(child, objects);
                }
            }
        }

        /// <summary>
        /// Of the given record IDs, those the caller reaches: the records a put of theirs can change, after the
        /// filters of the system (own records, the differentiation property).
        /// </summary>
        private async Task<List<long>> GetReachableIdsAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> userSecurity,
            string? differentiationEntity,
            string applicationEncryptionKey,
            List<long> ids)
        {
            var filter = _appDataService.GetSystemFilters(userHasFullAccess, differentiationEntity, entity, (appUser, userSecurity));
            filter.Add(new FilterData(Globals.PrimaryKeyColumn, FilterData.FilterOperators.contains, string.Join(",", ids), PropertyType.Number));

            var records = await _appDataService.GetAsync(
                appToken,
                differentiationEntity,
                applicationEncryptionKey,
                entity,
                -1,
                -1,
                new FilterData(FilterData.FilterLogic.AND, filter),
                null,
                Globals.PrimaryKeyColumn,
                (appUser, userSecurity),
                false);

            return records.Data.Select(x => Utils.GetLong(x[Globals.PrimaryKeyColumn])).Where(x => x > 0).ToList();
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

            await EntityAccess.EnforceRateLimitAsync(appToken, appUser, userSecurity, entity.Name, SecurityTypes.Entity, SecurityActionType.delete);

            Task<List<long>> DeleteRecordsAsync()
            {
                return _appDataService.DeleteAsync(
                    appToken,
                    entity,
                    userHasFullAccess,
                    differentiationEntity,
                    applicationEncryptionKey,
                    ids,
                    (appUser, userSecurity));
            }

            // The sessions of a deleted user end with it, not when the token cache next checks the database
            if (entity.Name.Equals(nameof(Users)))
            {
                var userIds = Utils.GetString(ids).Split(',').Select(x => Utils.GetLong(x)).Where(x => x > 0).Distinct().ToList();

                return await _accountAPI.DeleteUsersAndEndSessionsAsync(appToken, userIds, DeleteRecordsAsync);
            }

            return await DeleteRecordsAsync();
        }

        // The entity of a transaction operation. File rows are created and removed only together with the
        // stored file, so the Files entity is refused here as it is on the Data controller.
        private static DBWS_Entity GetTransactionEntity(DBWS_Application application, string entityName)
        {
            if (nameof(Files).Equals(entityName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApilaneException(AppErrors.ERROR, "Use 'Files' controller to access files. Refer to API Documentation for more info.");
            }

            return application.Entities.SingleOrDefault(x => x.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ApilaneException(AppErrors.ERROR, $"Entity {entityName} does not exist");
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
                return GetTransactionEntity(application, entityName);
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
                return GetTransactionEntity(application, entityName);
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

            await EntityAccess.EnforceRateLimitAsync(appToken, appUser, userSecurity, Globals.SCHEMA, SecurityTypes.Schema, SecurityActionType.get);

            return true;
        }
    }
}
