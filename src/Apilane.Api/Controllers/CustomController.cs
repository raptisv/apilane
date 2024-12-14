using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Abstractions.Metrics;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Common.Models;
using Apilane.Api.Filters;
using Apilane.Api.Models.ViewModels;
using Apilane.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Api.Controllers
{
    [Route("api/[controller]/{name}")]
    public class CustomController : BaseApplicationApiController
    {
        private readonly ICustomAPI _customAPI;
        private readonly IQueryDataService _queryDataService;
        private readonly IMetricsService _metricsService;

        public CustomController(
            ApiConfiguration apiConfiguration,
            ICustomAPI customAPI,
            IQueryDataService queryDataService,
            IClusterClient clusterClient,
            IMetricsService metricsService) : base(apiConfiguration, clusterClient)
        {
            _customAPI = customAPI;
            _queryDataService = queryDataService;
            _metricsService = metricsService;
        }

        /// <summary>
        /// Use this endpoint to make a call to a custom endpoint.
        /// </summary>
        /// <param name="name">The name of the custom endpoint.</param>
        /// <returns></returns>
        [HttpGet]
        [ServiceFilter(typeof(ApplicationLogActionFilter))]
        [Produces("application/json")]
        [ProducesResponseType(typeof(List<List<Dictionary<string, object?>>>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<JsonResult> Get(
            [BindRequired] string name)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("custom", name, Application.Token);

            var customEndpoint = Application.CustomEndpoints.SingleOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ApilaneException(AppErrors.NOT_FOUND, "Custom endpoint not found");

            var result = await _customAPI.GetAsync(
                Application.Token,
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                customEndpoint,
                _queryDataService.UriParams);

            return Json(result);
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost]
        public async Task<JsonResult> TestQuery([FromBody] DBWS_CustomEndpoint item)
        {
            var result = await _customAPI.TestQueryAsync(
                item,
                _queryDataService.UriParams);

            return Json(result);
        }
    }
}
