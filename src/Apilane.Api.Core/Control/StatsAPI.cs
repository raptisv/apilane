using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Data.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class StatsAPI : IStatsAPI
    {
        private IApplicationHelperService _applicationHelperService;
        private IApplicationDataService _appDataService;
        private IApplicationDataStoreFactory _dataStore;

        public StatsAPI(
            IApplicationDataService appDataService,
            IApplicationDataStoreFactory dataStore,
            IApplicationHelperService applicationHelperService)
        {
            _dataStore = dataStore;
            _appDataService = appDataService;
            _applicationHelperService = applicationHelperService;
        }

        public async Task<List<Dictionary<string, object?>>> AggregateAsync(
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            string? differentiationEntity,
            string properties,
            int pageIndex,
            int pageSize,
            string? filter,
            string? groupBy,
            string orderDirection = "DESC")
        {
            if (pageIndex <= 0)
            {
                pageIndex = 1;
            }

            if (pageSize < 0 || pageSize > 1000)
            {
                pageSize = 1000;
            }

            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            // Validate that property parameter exists
            if (properties is null)
            {
                throw new ApilaneException(AppErrors.ERROR, "Properties not provided");
            }

            var aggregateData = GetAggregateData(userHasFullAccess, entity, properties, (appUser, userSecurity));
            var groupData = GetGroupData(userHasFullAccess, entity, groupBy, (appUser, userSecurity));

            var systemFilters = _appDataService.GetSystemFilters(userHasFullAccess, differentiationEntity, entity, (appUser, userSecurity));
            var filterData = _appDataService.GetFilterData(entity, filter, userSecurity);
            if (filterData is not null)
            {
                systemFilters.Add(filterData);
            }

            return await _dataStore.AggregateDataAsync(
                entity.Name,
                aggregateData,
                new FilterData(FilterData.FilterLogic.AND, systemFilters),
                groupData,
                Utils.GetString(orderDirection).Equals("asc", StringComparison.OrdinalIgnoreCase),
                pageIndex,
                pageSize);
        }

        public async Task<List<Dictionary<string, object?>>> DistinctAsync(
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            string? differentiationEntity,
            string propertyName,
            string? filter)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entity.Name, entity.Properties, SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entity.Name, SecurityTypes.Entity, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entity.Name);
            }

            if (propertyName == null)
            {
                throw new ApilaneException(AppErrors.ERROR, "Property not provided");
            }

            var property = ValidateAccessToProperty(userHasFullAccess, entity, propertyName, (appUser, userSecurity));

            var systemFilters = _appDataService.GetSystemFilters(userHasFullAccess, differentiationEntity, entity, (appUser, userSecurity));
            var filterData = _appDataService.GetFilterData(entity, filter, userSecurity);
            if (filterData is not null)
            {
                systemFilters.Add(filterData);
            }

            return await _dataStore.DistinctDataAsync(
                entity.Name,
                property.Name,
                new FilterData(FilterData.FilterLogic.AND, systemFilters));
        }

        public async Task<CountDataHistoryDto> CountDataAndHistoryAsync(
            string appToken,
            DBWS_Entity entity)
        {
            var data = await _dataStore.GetDataCountAsync(entity.Name, null);

            var historyRecords = await _applicationHelperService
                .GetHistoryCountForEntityAsync(appToken, entity.Name);

            return new CountDataHistoryDto()
            {
                Data = Utils.GetInt(data, 0),
                History = historyRecords
            };
        }

        private AggregateData GetAggregateData(
            bool userHasFullAccess,
            DBWS_Entity entity,
            string properties,
            (Users? User, List<DBWS_Security> Security) User_Security)
        {
            var result = new List<AggregateData.AggregateProperty>();

            var propAggrArr = properties.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            for (int i = 0; i < propAggrArr.Count; i++)
            {
                string[] temp = propAggrArr[i].Split('.');
                if (temp.Length != 2)
                {
                    throw new ApilaneException(AppErrors.ERROR, $"Error on AggregateProperties '{propAggrArr[i]}'", entity: entity.Name);
                }

                var property = ValidateAccessToProperty(userHasFullAccess, entity, temp[0], User_Security);
                var aggregate = AggregateData.ConvertToType(temp[1]);

                result.Add(new AggregateData.AggregateProperty()
                {
                    Name = property.Name,
                    Alias = $"{property.Name}_{aggregate.ToString().ToLower()}",
                    Aggregate = aggregate
                });
            }

            return new AggregateData()
            {
                Properties = result
            };
        }

        private DBWS_EntityProperty ValidateAccessToProperty(
            bool userHasFullAccess,
            DBWS_Entity entity,
            string property,
            (Users? User, List<DBWS_Security> Security) User_Security)
        {
            var prop = entity.Properties.SingleOrDefault(x => x.Name.Equals(property, StringComparison.OrdinalIgnoreCase))
                ?? throw new ApilaneException(AppErrors.ERROR, $"Property {property} does not exist", entity: entity.Name);

            // Check only if it is not admin
            if (!userHasFullAccess)
            {
                if (_appDataService.GetEntityNotAllowedProperties(entity, User_Security.Security).Select(x => x.Name).Contains(prop.Name))
                {
                    throw new ApilaneException(AppErrors.UNAUTHORIZED, null, property, entity: entity.Name);
                }
            }

            return prop;
        }

        private GroupData? GetGroupData(
            bool userHasFullAccess,
            DBWS_Entity entity,
            string? groupByString,
            (Users? User, List<DBWS_Security> Security) user_Security)
        {
            if (string.IsNullOrWhiteSpace(groupByString))
            {
                return null;
            }

            var properties = groupByString.Trim()
                .Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();

            if (!properties.Any())
            {
                return null;
            }

            // First, validate access to properties if it is not an administrator
            var notallowedProperties = userHasFullAccess
                ? new string[0]
                : _appDataService.GetEntityNotAllowedProperties(entity, user_Security.Security).Select(x => x.Name);

            var propertiesResult = properties.Select(prop =>
            {
                string[] parts = prop.Split('.');

                if (parts.Length != 1 && parts.Length != 2)
                {
                    throw new ApilaneException(AppErrors.INVALID_GROUPBY_PARAMETER, null, prop, entity: entity.Name);
                }

                var entityProperty = entity.Properties.FirstOrDefault(x => x.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                    ?? throw new FormatException($"Property '{parts[0]}' does not exist");

                if (notallowedProperties.Any(x => x.Equals(entityProperty.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ApilaneException(AppErrors.UNAUTHORIZED, null, entityProperty.Name, entity: entity.Name);
                }

                var strType = parts.Length == 2 ? parts[1] : string.Empty;

                return new GroupData.GroupProperty()
                {
                    Name = entityProperty.Name,
                    Alias = $"{entityProperty.Name}{(string.IsNullOrWhiteSpace(strType) ? string.Empty : $"_{strType.ToLower()}")}",
                    Type = GroupData.ConvertToType(strType)
                };
            }).ToList();

            return new GroupData()
            {
                Properties = propertiesResult
            };
        }
    }
}
