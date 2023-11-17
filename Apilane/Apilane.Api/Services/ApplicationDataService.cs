using Apilane.Api.Abstractions;
using Apilane.Api.Enums;
using Apilane.Api.Exceptions;
using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Apilane.Data.Abstractions;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Apilane.Api.Services
{
    public class ApplicationDataService : IApplicationDataService
    {
        private readonly IApplicationDataStoreFactory _dataStore;
        private readonly IApplicationService _applicationService;
        private readonly IApplicationHelperService _applicationHelperService;
        private readonly IEntityHistoryAPI _entityHistoryAPI;

        public ApplicationDataService(
            IApplicationDataStoreFactory dataStore,
            IApplicationService applicationService,
            IApplicationHelperService applicationHelperService,
            IEntityHistoryAPI entityHistoryAPI)
        {
            _dataStore = dataStore;
            _applicationService = applicationService;
            _applicationHelperService = applicationHelperService;
            _entityHistoryAPI = entityHistoryAPI;
        }

        public async Task<Dictionary<string, object?>> GetByIDAsync(
            string appToken,
            bool userHasFullAccess,
            DBWS_Entity entity,
            string? differentiationEntity,
            string applicationEncryptionKey,
            long id,
            string? properties,
            (Users? User, List<DBWS_Security> Security) Data_UserSecurity)
        {
            var filter = GetSystemFilters(userHasFullAccess, differentiationEntity, entity, Data_UserSecurity);

            filter.Add(new FilterData(Globals.PrimaryKeyColumn, FilterData.FilterOperators.equal, id, PropertyType.Number));

            var result = await GetAsync(
                appToken,
                differentiationEntity,
                applicationEncryptionKey,
                entity,
                1,
                1,
                new FilterData(FilterData.FilterLogic.AND, filter),
                null,
                properties,
                Data_UserSecurity,
                false);

            if (result.Data.Count == 1)
            {
                return result.Data[0];
            }
            else
            {
                throw new ApilaneException(AppErrors.NOT_FOUND, $"Record not found");
            }
        }

        public async Task<DataTotalResponse> GetHistoryByIdAsync(
            string appToken,
            bool userHasFullAccess,
            string? differentiationEntity,
            string applicationEncryptionKey,
            DBWS_Entity entity,
            long id,
            int? pageIndex,
            int? pageSize,
            (Users? User, List<DBWS_Security> Security) Data_UserSecurity)
        {
            var resultData = new List<Dictionary<string, object?>>();

            var record = await GetByIDAsync(appToken, userHasFullAccess, entity, differentiationEntity, applicationEncryptionKey, id, null, Data_UserSecurity);

            if (record != null)
            {
                var allowedProperties = GetAllowedProperties(entity.Properties, entity, Data_UserSecurity.Security);

                var history = await _entityHistoryAPI.GetPagedAsync(appToken, id, entity.Name, pageIndex, pageSize);

                foreach (var dr in history.Data)
                {
                    var jObject = JsonObject.Parse(Utils.GetString(dr[Globals.EntityHistoryDataColumn]))?.AsObject()
                        ?? throw new Exception("Data column is null");

                    var obj = new Dictionary<string, object?>
                    {
                        ["History_Record_Created"] = dr[Globals.CreatedColumn],
                        ["History_Record_Owner"] = dr[Globals.OwnerColumn]
                    };

                    foreach (var x in jObject)
                    {
                        string property = x.Key;
                        if (allowedProperties.Any(p => p.Name.Equals(property)))
                        {
                            obj[property] = x.Value;
                        }
                    }

                    resultData.Add(obj);
                }

                return new DataTotalResponse() { Data = resultData, Total = history.Total };
            }

            return new DataTotalResponse() { Data = new List<Dictionary<string, object?>>(), Total = 0 };
        }

        public async Task<DataResponse> GetAsync(
            string appToken,
            string? differentiationEntity,
            string applicationEncryptionKey,
            DBWS_Entity entity,
            int pageIndex,
            int pageSize,
            FilterData? filter,
            List<SortData>? sort,
            string? properties,
            (Users? User, List<DBWS_Security> Security) Data_UserSecurity,
            bool getTotal)
        {
            // First get the properties that the user requested
            List<DBWS_EntityProperty> entityProperties =
                string.IsNullOrWhiteSpace(properties)
                ?
                entity.Properties
                :
                entity.Properties.Where(x => x.IsPrimaryKey || Utils.GetString(properties).ToLower().Split(',').Select(y => Utils.GetString(y)).Contains(x.Name.ToLower())).ToList();

            // Then get the properties that the user has access to

            entityProperties = GetAllowedProperties(entityProperties, entity, Data_UserSecurity.Security);

            var resultData = await _dataStore.GetPagedDataAsync(
                entity.Name,
                entityProperties.Select(x => x.Name).ToList(),
                pageIndex: pageIndex,
                pageSize: pageSize,
                filter: filter,
                sort: sort);

            var encryptedProperties = entityProperties.Where(x => x.Encrypted);

            if (encryptedProperties.Any())
            {
                // Decrypt encrypted properties before sending
                var appEncryptionKey = applicationEncryptionKey.Decrypt(Globals.EncryptionKey);

                foreach (var prop in encryptedProperties)
                {
                    foreach (var record in resultData)
                    {
                        if (Encryptor.TryDecrypt(record[prop.Name] != DBNull.Value ? Utils.GetString(record[prop.Name]) : null, appEncryptionKey, out string? output))
                        {
                            record[prop.Name] = output;
                        }
                        else
                        {
                            record[prop.Name] = null;
                        }
                    }
                }
            }

            if (getTotal)
            {
                var resultCount = await _dataStore.GetDataCountAsync(entity.Name, filter: filter);

                return new DataTotalResponse() { Data = resultData, Total = resultCount };
            }

            return new DataResponse() { Data = resultData };
        }

        public async Task<List<long>> PostAsync(
            string appToken,
            DBWS_Entity entity,
            DatabaseType databaseType,
            string? differentiationEntity,
            string applicationEncryptionKey,
            object item,
            (Users? User, List<DBWS_Security> Security) Data_UserSecurity)
        {
            if (string.IsNullOrWhiteSpace(Utils.GetString(item)))
            {
                throw new ApilaneException(AppErrors.EMPTY_BODY, entity: entity.Name);
            }

            if (!entity.AllowPost())
            {
                throw new ApilaneException(AppErrors.ERROR, "Not allowed", entity: entity.Name);
            }

            // Get the properties that are allowed to edit
            var propertiesAllowedToEdit = entity.Properties.Where(x => x.AllowEdit(differentiationEntity, entity.HasDifferentiationProperty)).ToList();

            // Then get the properties that the user has not access to
            List<DBWS_EntityProperty> notAllowedProperties = GetEntityNotAllowedProperties(entity, Data_UserSecurity.Security);

            // Remove the properties that the user has not access to
            propertiesAllowedToEdit = propertiesAllowedToEdit.Where(x => notAllowedProperties.Select(y => y.Name).Contains(x.Name) == false).ToList();

            if (propertiesAllowedToEdit.Count == 0)
            {
                throw new ApilaneException(AppErrors.NO_PROPERTIES_PROVIDED, entity: entity.Name);
            }

            var token = JsonNode.Parse(Utils.GetString(item));

            if (token is JsonArray array)
            {
                var ids = new List<long>();

                foreach (var obj in array)
                {
                    if (obj is not null)
                    {
                        ids.AddRange(await PostAsync(appToken, entity, databaseType, differentiationEntity, applicationEncryptionKey, obj, Data_UserSecurity));
                    }
                }

                return ids;
            }
            else if (token is JsonObject newObject)
            {
                var propertyValues = new Dictionary<string, object?>();

                // General properties allowed to edit/update from the user
                foreach (var prop in propertiesAllowedToEdit)
                {
                    var propValue = GetPropertyValue(differentiationEntity, applicationEncryptionKey, entity, prop, newObject, Data_UserSecurity.User);
                    propertyValues[prop.Name] = propValue;
                }

                // System properties
                propertyValues[Globals.CreatedColumn] = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow);
                if (entity.HasOwnerColumn())
                {
                    propertyValues[Globals.OwnerColumn] = Data_UserSecurity.User?.ID;
                }
                if (!string.IsNullOrWhiteSpace(differentiationEntity) && entity.HasDifferentiationProperty)
                {
                    propertyValues[differentiationEntity.GetDifferentiationPropertyName()] = Data_UserSecurity.User?.DifferentiationPropertyValue;
                }

                var newID = await _dataStore.CreateDataAsync(entity.Name, propertyValues, false);

                if (!newID.HasValue)
                {
                    throw new Exception("Could not create record | New record id cannot be null");
                }

                return new List<long>() { newID.Value };
            }

            throw new InvalidOperationException();
        }

        public async Task<long> PutAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            DatabaseType databaseType,
            string? differentiationEntity,
            string applicationEncryptionKey,
            object item,
            (Users? User, List<DBWS_Security> Security) Data_UserSecurity)
        {
            if (string.IsNullOrWhiteSpace(Utils.GetString(item)))
            {
                throw new ApilaneException(AppErrors.EMPTY_BODY, entity: entity.Name);
            }

            if (!entity.AllowPut())
            {
                throw new ApilaneException(AppErrors.ERROR, "Not allowed", entity: entity.Name);
            }

            var token = JsonNode.Parse(Utils.GetString(item));

            if (token is JsonArray array)
            {
                long affected = 0;

                foreach (var obj in array)
                {
                    if (obj is not null)
                    {
                        affected += await PutAsync(appToken, entity, userHasFullAccess, databaseType, differentiationEntity, applicationEncryptionKey, obj, Data_UserSecurity);
                    }
                }

                return affected;
            }
            else if (token is JsonObject newObject)
            {
                var propertiesToUpdate = new List<string>();
                foreach (KeyValuePair<string, JsonNode?> prop in newObject)
                {
                    if (!propertiesToUpdate.Contains(prop.Key))
                    {
                        propertiesToUpdate.Add(prop.Key);
                    }
                }

                // Update all properties except ID
                List<DBWS_EntityProperty> properties = entity.Properties.Where(x => x.AllowEdit(differentiationEntity, entity.HasDifferentiationProperty)).ToList();

                // If the user decided not to update all properties
                properties = properties.Where(x => propertiesToUpdate.Select(y => y.ToLower().Trim()).Contains(x.Name.ToLower())).ToList();

                // Then get the properties that the user has access to
                properties = GetAllowedProperties(properties, entity, Data_UserSecurity.Security);

                if (properties.Count == 0)
                {
                    throw new ApilaneException(AppErrors.NO_PROPERTIES_PROVIDED, entity: entity.Name);
                }

                long ID = Utils.GetLong(newObject.GetObjectProperty(Globals.PrimaryKeyColumn));

                if (ID <= 0)
                {
                    throw new ApilaneException(AppErrors.NO_ID_PROVIDED, entity: entity.Name);
                }

                Dictionary<string, object?>? prevData = null;

                if (entity.RequireChangeTracking)
                {
                    prevData = await _dataStore.GetDataByIdAsync(entity.Name, ID, null);
                }

                var propertyValues = new Dictionary<string, object?>();

                foreach (var prop in properties)
                {
                    propertyValues[prop.Name] = GetPropertyValue(differentiationEntity, applicationEncryptionKey, entity, prop, newObject, Data_UserSecurity.User);
                }

                var filter = GetSystemFilters(userHasFullAccess, differentiationEntity, entity, Data_UserSecurity);
                filter.Add(new FilterData(Globals.PrimaryKeyColumn, FilterData.FilterOperators.equal, ID, PropertyType.Number));

                var rowsAffected = await _dataStore.UpdateDataAsync(
                    entity.Name,
                    propertyValues,
                    new FilterData(FilterData.FilterLogic.AND, filter));

                // Call SaveHistoryForDataRow after succesfull update, if prev data exists and row was updated
                if (prevData != null && rowsAffected > 0)
                {
                    await _applicationHelperService.CreateHistoryAsync(appToken, entity.Name, ID, Data_UserSecurity.User?.ID, prevData);
                }

                return rowsAffected;
            }

            throw new InvalidOperationException();
        }

        public async Task<List<long>> DeleteAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            string? differentiationEntity,
            string applicationEncryptionKey,
            string Ids,
            (Users? User, List<DBWS_Security> Security) userSecurity)
        {
            if (!entity.AllowDelete())
            {
                throw new ApilaneException(AppErrors.ERROR, $"Not allowed", entity: entity.Name);
            }

            if (string.IsNullOrWhiteSpace(Ids))
            {
                throw new ApilaneException(AppErrors.NO_RECORDS_FOUND_TO_DELETE, entity: entity.Name);
            }

            List<int> IDS = Ids.Split(',').Select(x => Utils.GetInt(x)).Where(x => x > 0).ToList();

            if (IDS.Count == 0)
            {
                throw new ApilaneException(AppErrors.NO_RECORDS_FOUND_TO_DELETE, entity: entity.Name);
            }

            var filter = GetSystemFilters(userHasFullAccess, differentiationEntity, entity, userSecurity);

            filter.Add(new FilterData(Globals.PrimaryKeyColumn, FilterData.FilterOperators.contains, string.Join(",", IDS), PropertyType.Number));

            // Perform a get, to get records that the user has access to
            var recordsAllowedToDelete = await GetAsync(
                appToken,
                differentiationEntity,
                applicationEncryptionKey,
                entity,
                -1,
                -1,
                new FilterData(FilterData.FilterLogic.AND, filter),
                null,
                Globals.PrimaryKeyColumn,
                userSecurity,
                false);

            if (recordsAllowedToDelete.Data.Count == 0)
            {
                return new List<long>();
            }

            var listIDsToDelete = recordsAllowedToDelete.Data.AsEnumerable().Select(x => Utils.GetLong(x[Globals.PrimaryKeyColumn])).ToList();

            await _applicationHelperService.DeleteHistoryAsync(appToken, entity.Name, listIDsToDelete);

            var deleteFilter = new FilterData(Globals.PrimaryKeyColumn, FilterData.FilterOperators.contains, string.Join(",", listIDsToDelete), PropertyType.Number);

            await _dataStore.DeleteDataAsync(entity.Name, deleteFilter);

            return listIDsToDelete;
        }

        public List<FilterData> GetSystemFilters(
            bool userHasFullAccess,
            string? differentiationEntity,
            DBWS_Entity entity,
            (Users? User, List<DBWS_Security> Security) User_Security)
        {
            var filters = new List<FilterData>();

            if (userHasFullAccess)
            {
                return filters;
            }

            if (entity.HasOwnerColumn() &&
                User_Security.User is not null &&
                User_Security.Security.Any(x => x.Record == (int)EndpointRecordAuthorization.Owned))
            {
                // If he can see only his records

                filters.Add(new(Globals.OwnerColumn, FilterData.FilterOperators.equal, User_Security.User.ID, PropertyType.Number));
            }

            // Append global differentiation filter if exists
            if (!string.IsNullOrWhiteSpace(differentiationEntity) && entity.HasDifferentiationProperty)
            {
                var diffPropertyName = differentiationEntity.GetDifferentiationPropertyName();

                filters.Add(new(diffPropertyName, FilterData.FilterOperators.equal, User_Security.User?.DifferentiationPropertyValue, PropertyType.Number));
            }

            return filters;
        }

        public object? GetPropertyValue(
            string? differentiationEntity,
            string applicationEncryptionKey,
            DBWS_Entity entity,
            DBWS_EntityProperty property,
            JsonObject Object,
            Users? user)
        {
            // If this the the differentiation property, the value should be the user's value
            if (property.IsSystem &&
                !string.IsNullOrWhiteSpace(differentiationEntity) &&
                entity.HasDifferentiationProperty &&
                differentiationEntity.GetDifferentiationPropertyName().Equals(property.Name, StringComparison.OrdinalIgnoreCase) &&
                user is not null)
            {
                return user.DifferentiationPropertyValue;
            }

            // All other properties
            foreach (KeyValuePair<string, JsonNode?> item in Object)
            {
                if (item.Key.Equals(property.Name))
                {
                    if (item.Value is null)
                    {
                        return null;
                    }

                    switch (property.TypeID_Enum)
                    {
                        case PropertyType.String:
                            {
                                var strValue = Utils.GetString(item.Value);
                                if (property.Minimum.HasValue && strValue.Length < property.Minimum.Value)
                                {
                                    throw new ApilaneException(AppErrors.VALIDATION, $"Minimum {property.Minimum.Value} characters", property: property.Name, entity: entity.Name);
                                }

                                if (property.Maximum.HasValue && strValue.Length > property.Maximum.Value)
                                {
                                    throw new ApilaneException(AppErrors.VALIDATION, $"Maximum {property.Maximum.Value} characters", property: property.Name, entity: entity.Name);
                                }

                                if (!string.IsNullOrWhiteSpace(property.ValidationRegex))
                                {
                                    if (!Utils.IsValidRegexMatch(strValue, property.ValidationRegex))
                                    {
                                        throw new ApilaneException(AppErrors.VALIDATION, $"The value provided does not match regex {property.ValidationRegex}", property: property.Name, entity: entity.Name);
                                    }
                                }

                                if (property.Encrypted)
                                {
                                    string appEncryptionKey = applicationEncryptionKey.Decrypt(Globals.EncryptionKey);
                                    strValue = Encryptor.Encrypt(strValue, appEncryptionKey);
                                }

                                return strValue;
                            }
                        case PropertyType.Number:
                            {
                                var strValue = Utils.GetString(item.Value);

                                // Validate
                                if (!decimal.TryParse(strValue, out decimal n))
                                {
                                    if (!decimal.TryParse(strValue, NumberStyles.Float, null, out n))
                                    {
                                        throw new ApilaneException(AppErrors.VALIDATION, $"Invalid number", property: property.Name, entity: entity.Name);
                                    }
                                }

                                decimal number = Utils.GetDecimal(strValue, 0);

                                if (property.Minimum.HasValue && number < property.Minimum.Value)
                                {
                                    throw new ApilaneException(AppErrors.VALIDATION, $"Minimum allowed value {property.Minimum.Value}", property: property.Name, entity: entity.Name);
                                }

                                if (property.Maximum.HasValue && number > property.Maximum.Value)
                                {
                                    throw new ApilaneException(AppErrors.VALIDATION, $"Maximum allowed value {property.Maximum.Value}", property: property.Name, entity: entity.Name);
                                }

                                return number.Truncate((byte)Utils.GetInt(property.DecimalPlaces, 0));
                            }
                        case PropertyType.Boolean:
                            {
                                var strValue = Utils.GetString(item.Value);

                                return Utils.GetBool(strValue);
                            }
                        case PropertyType.Date:
                            {
                                var strValue = Utils.GetString(item.Value);

                                DateTime? dateResult = Utils.ParseDate(strValue);

                                if (!dateResult.HasValue)
                                {
                                    throw new ApilaneException(AppErrors.VALIDATION, $"The value provided is not a valid unix timestamp or date in format {Globals.DateTimeMsFormat}", property: property.Name, entity: entity.Name);
                                }

                                return Utils.GetUnixTimestampMilliseconds(dateResult.Value);
                            }
                        default:
                            throw new NotImplementedException();
                    }
                }
            }

            return null;
        }

        public List<DBWS_EntityProperty> GetEntityNotAllowedProperties(DBWS_Entity entity, List<DBWS_Security> data_UserSecurity)
        {
            List<DBWS_EntityProperty> allowedProperties = GetAllowedProperties(entity.Properties, entity, data_UserSecurity);

            return entity.Properties.Where(x => allowedProperties.Select(y => y.Name).Contains(x.Name) == false).ToList();
        }

        public List<DBWS_EntityProperty> GetAllowedProperties(List<DBWS_EntityProperty> currentProperties, DBWS_Entity entity, List<DBWS_Security> data_UserSecurity)
        {
            // Combine all properties for all securities for this user
            var currentUserAllowdProperies = GetSecurityPropertiesAsync(data_UserSecurity).Select(x => x.ToLower());

            return currentProperties.Where(x => x.IsPrimaryKey || currentUserAllowdProperies.Contains(x.Name.ToLower())).ToList();
        }

        public static List<string> GetSecurityPropertiesAsync(List<DBWS_Security> securities)
        {
            var result = new List<string>();
            securities.ForEach(x => result.AddRange(x.GetProperties()));
            return result.Distinct().ToList();
        }

        public FilterData? GetFilterData(
            DBWS_Entity entity,
            string? filter,
            List<DBWS_Security> security)
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                try
                {
                    var notAllowedProperties = GetEntityNotAllowedProperties(entity, security)
                        .Select(x => x.Name)
                        .ToList();

                    var result = FilterData.Parse(filter);

                    if (result is not null)
                    {
                        // Search for properties that are not allowed on filtering
                        if (notAllowedProperties is not null)
                        {
                            foreach (var item in notAllowedProperties)
                            {
                                if (CheckFilterPropertyExists(result, item))
                                {
                                    throw new FormatException($"Property '{item}' is not allowed on this filter");
                                }
                            }
                        }

                        // Fill filter with properties types
                        FillFilterPropertiesTypeRecursively(entity, result);
                    }

                    return result;
                }
                catch (FormatException ex)
                {
                    throw new ApilaneException(AppErrors.INVALID_FILTER_PARAMETER, ex.Message, entity: entity.Name);
                }
                catch (Exception)
                {
                    throw new ApilaneException(AppErrors.INVALID_FILTER_PARAMETER, entity: entity.Name);
                }
            }

            return null;
        }

        private static void FillFilterPropertiesTypeRecursively(
            DBWS_Entity entity,
            FilterData filterItem)
        {
            // Fill filter with properties types
            if (!string.IsNullOrWhiteSpace(filterItem.Property))
            {
                var entityProperty = entity.Properties.Single(x => x.Name.Equals(filterItem.Property, StringComparison.OrdinalIgnoreCase));

                filterItem.Type = entityProperty.TypeID_Enum;
            }

            // Fill child filters
            if (filterItem.Filters is not null)
            {
                foreach (var filter in filterItem.Filters)
                {
                    // Fill grand child filters recursively
                    FillFilterPropertiesTypeRecursively(entity, filter);
                }
            }
        }

        public List<SortData>? GetSortData(
            DBWS_Entity entity,
            string? sortString,
            List<DBWS_Security> security)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(sortString))
                {
                    var notallowedProperties = GetEntityNotAllowedProperties(entity, security);

                    var token = JsonNode.Parse(sortString);

                    if (token is JsonArray array)
                    {
                        var sortDataList = SortData.ParseList(sortString) ?? throw new FormatException("Invalid parameter 'sort'");

                        sortDataList = sortDataList
                            .Where(s => entity.Properties.Select(x => x.Name.ToLower()).Contains(s.Property.ToLower()));

                        foreach (var item in sortDataList)
                        {
                            if (notallowedProperties.Select(x => x.Name).Contains(item.Property))
                            {
                                throw new FormatException($"Invalid sort parameter. Property '{item.Property}' is not allowed");
                            }
                        }

                        return sortDataList.ToList();
                    }
                    else if (token is JsonObject newObject)
                    {
                        var sortDataItem = SortData.Parse(sortString) ?? throw new FormatException("Invalid parameter 'sort'");

                        if (entity.Properties.Any(p => p.Name.Equals(sortDataItem.Property, StringComparison.OrdinalIgnoreCase)))
                        {
                            if (notallowedProperties.Select(x => x.Name).Contains(sortDataItem.Property))
                            {
                                throw new FormatException($"Invalid sort parameter. Property '{sortDataItem.Property}' is not allowed");
                            }

                            return new List<SortData>() { sortDataItem };
                        }
                    }
                }

                return null;
            }
            catch (FormatException ex)
            {
                throw new ApilaneException(AppErrors.INVALID_SORT_PARAMETER, ex.Message, entity: entity.Name);
            }
            catch (JsonException)
            {
                throw new ApilaneException(AppErrors.INVALID_SORT_PARAMETER, "Misconfigured json parameter 'sort'", entity: entity.Name);
            }
            catch 
            {
                throw new ApilaneException(AppErrors.INVALID_SORT_PARAMETER, entity: entity.Name);
            }
        }

        private bool CheckFilterPropertyExists(FilterData filter, string property)
        {
            bool result = false;

            if (filter.Property != null && filter.Property.ToLower().Equals(property.ToLower()))
            {
                result = true;
            }

            if (filter.Filters != null)
            {
                foreach (var item in filter.Filters)
                {
                    result = result == false && CheckFilterPropertyExists(item, property);
                }
            }

            return result;
        }

        #region USERS

        public async Task<Dictionary<string, object?>?> GetUserByIdAsync(
            string? appToken,
            long userID)
        {
            var result = await _dataStore.GetPagedDataAsync(
                nameof(Users),
                null,
                new(nameof(Users.ID), FilterData.FilterOperators.equal, userID, PropertyType.Number),
                null, 1, 1);

            return await ClearUserDataAsync(appToken, result?.Count == 1 ? result.Single() : null);
        }

        private async Task<Dictionary<string, object?>?> ClearUserDataAsync(string? appToken, Dictionary<string, object?>? drUser)
        {
            if (drUser != null)
            {
                if (!string.IsNullOrWhiteSpace(appToken))
                {
                    var _application = await _applicationService.GetAsync(appToken);

                    var entity = _application.Entities.Single(x => x.Name.Equals(nameof(Users)));

                    foreach (var property in entity.Properties.Where(x => x.Encrypted))
                    {
                        var propertyValue = drUser[property.Name];
                        if (propertyValue is not null)
                        {
                            string appEncryptionKey = _application.EncryptionKey.Decrypt(Globals.EncryptionKey);
                            drUser[property.Name] = Encryptor.Decrypt(propertyValue.ToString(), appEncryptionKey);
                        }
                    }
                }

                // IMPORTANT
                drUser[nameof(Users.Password)] = null;
            }

            return drUser;
        }

        #endregion
    }
}
