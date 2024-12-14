using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Common.Models.Dto;
using Apilane.Api.Filters;
using Apilane.Api.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Orleans;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Api.Controllers
{
    public class StatsController : BaseApplicationApiController
    {
        private readonly IStatsAPI _statsAPI;

        public StatsController(
            ApiConfiguration apiConfiguration,
            IStatsAPI statsAPI,
            IClusterClient clusterClient) : base(apiConfiguration, clusterClient)
        {
            _statsAPI = statsAPI;
        }

        /// <summary>
        /// Use this endpoint to run aggregates functions agaisnt an Entity, depending on user access level
        /// </summary>
        /// <param name="entity">The entity name</param>
        /// <param name="properties">Accepts any property that the user has access to</param>
        /// <param name="pageIndex">Default value 1. Used for data paging</param>
        /// <param name="pageSize">Default value 20. Range 0-1000. Used for data paging</param>
        /// <param name="filter">Default value NULL. Used for data fitlering</param>
        /// <param name="orderDirection">Default value DESC. Used for data sort direction. Accepted values "ASC", "DESC"</param>
        /// <param name="groupBy">Default value NULL. Used for data grouping</param>
        /// <returns>The result of the aggregate function</returns>
        [HttpGet]
        [ServiceFilter(typeof(ApplicationLogActionFilter))]
        [Produces("application/json")]
        [ProducesResponseType(typeof(object), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<object> Aggregate(
            [BindRequired] string entity,
            [BindRequired] string properties,
            int pageIndex = 1,
            int pageSize = 20,
            string? filter = null,
            string? groupBy = null,
            string orderDirection = "DESC")
        {
            return await _statsAPI.AggregateAsync(
                GetEntity(entity),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity,
                properties,
                pageIndex,
                pageSize,
                filter,
                groupBy,
                orderDirection);
        }

        /// <summary>
        /// Use this endpoint to get the distinct values of any property of the given entity, depending on user access level
        /// </summary>
        /// <param name="entity">Required. The entity name</param>
        /// <param name="property">Required. The property can be on any type</param>
        /// <param name="filter">Optional. Default value NULL. Used for data fitlering</param>
        /// <returns>Returns the distinc values of the requested property</returns>
        [HttpGet]
        [ServiceFilter(typeof(ApplicationLogActionFilter))]
        [Produces("application/json")]
        [ProducesResponseType(typeof(object), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<object> Distinct(
            [BindRequired] string entity,
            [BindRequired] string property,
            string? filter = null)
        {
            return await _statsAPI.DistinctAsync(
                GetEntity(entity),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity, 
                property,
                filter);
        }

        [ApplicationOwnerAuthorize]
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet]
        public async Task<CountDataHistoryDto> CountDataAndHistory(string entity)
        {
            return await _statsAPI.CountDataAndHistoryAsync(
                Application.Token,
                GetEntity(entity));
        }
    }
}
