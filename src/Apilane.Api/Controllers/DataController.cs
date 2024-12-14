using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Abstractions.Metrics;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Files;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Api.Filters;
using Apilane.Api.Models.ViewModels;
using Apilane.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Api.Controllers
{
    [ServiceFilter(typeof(ApplicationLogActionFilter))]
    public class DataController : BaseApplicationApiController
    {
        private readonly IDataAPI _dataAPI;
        private readonly IEntityHistoryAPI _entityHistoryAPI;
        private readonly IMetricsService _metricsService; 

        public DataController(
            ApiConfiguration apiConfiguration,
            IDataAPI dataAPI,
            IEntityHistoryAPI entityHistoryAPI,
            IClusterClient clusterClient,
            IMetricsService metricsService) : base(apiConfiguration, clusterClient)
        {
            _dataAPI = dataAPI;
            _entityHistoryAPI = entityHistoryAPI;
            _metricsService = metricsService;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext actionContext, ActionExecutionDelegate next)
        {
            // Do not allow Files to be managed from Data controller

            var queryDataService = actionContext.HttpContext.RequestServices.GetService<IQueryDataService>() ?? throw new Exception("could not load IQueryDataService");

            if (queryDataService.RouteController.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                queryDataService.Entity.Equals(nameof(Files), StringComparison.OrdinalIgnoreCase))
            {
                throw new ApilaneException(AppErrors.ERROR, "Use 'Files' controller to access files. Refer to API Documentation for more info.");
            }

            await base.OnActionExecutionAsync(actionContext, next);
        }

        /// <summary>
        /// Use this endpoint to retrieve a record with the given id of the given entity.
        /// </summary>
        /// <param name="entity">The entity name</param>
        /// <param name="id">The record ID</param>
        /// <param name="properties">The properties to fetch, comma separated. Leave empty to fetch all properties.</param>
        /// <returns></returns>
        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(Dictionary<string, object?>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<object> GetByID(
            [BindRequired] long id,
            [BindRequired] string entity,
            string? properties = null)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("getbyid", entity, Application.Token);

            return await _dataAPI.GetByIDAsync(
                Application.Token,
                GetEntity(entity),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                id,
                properties);
        }        

        /// <summary>
        /// Use this endpoint to retrieve a record's historical data.
        /// </summary>
        /// <param name="entity">The entity name</param>
        /// <param name="id">The record ID</param>
        /// <param name="pageIndex">Default value 1. Used for data paging</param>
        /// <param name="pageSize">Default value 20. Range 0-1000. Used for data paging</param>
        /// <returns></returns>
        [HttpGet]
        [Produces("application/json")]        
        [ProducesResponseType(typeof(DataTotalResponse), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<object> GetHistoryByID(
            [BindRequired] long id,
            [BindRequired] string entity,
            int? pageIndex = 1,
            int? pageSize = 10)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("gethistorybyid", entity, Application.Token);
            
            return await _dataAPI.GetHistoryByIDAsync(
                Application.Token,
                GetEntity(entity),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                id,
                pageIndex,
                pageSize);
        }

        /// <summary>
        ///  Use this endpoint to retrieve records of the given entity.
        /// </summary>
        /// <param name="entity">The entity name</param>
        /// <param name="pageIndex">Default value 1. Used for data paging</param>
        /// <param name="pageSize">Default value 20. Range 0-1000. Used for data paging</param>
        /// <param name="filter">Default value NULL. Used for data fitlering</param>
        /// <param name="sort">Default value NULL. Used for data sorting</param>
        /// <param name="properties">The entity properties to fetch, comma separated. Leave empty to fetch all properties. Use it to limit consumed bandwidth.</param>
        /// <param name="getTotal">Default value false. Set it to true to get the count of records according to the specidified filtering </param>
        /// <returns>An object that consists of two properties. Data is an array of records. If GetTotal is set to True, Total is the count of records, according to the specidified filtering</returns>
        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(DataResponse), 200)]
        [ProducesResponseType(typeof(DataTotalResponse), 200)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<JsonResult> Get(
            [BindRequired] string entity,
            int pageIndex = 1,
            int pageSize = 20,
            string? filter = null,
            string? sort = null,
            string? properties = null,
            bool getTotal = false)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("get", entity, Application.Token);

            var result = await _dataAPI.GetAsync(
                Application.Token,
                GetEntity(entity),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                (DatabaseType)Application.DatabaseType,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                pageIndex,
                pageSize,
                filter,
                sort,
                properties,
                getTotal);

            return Json(result);
        }

        /// <summary>
        /// Use this endpoint to create new record(s) of the given entity.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="entity">Required. The entity name</param>
        /// <returns>Returns an array containing the ID(s) of the created record(s)</returns>
        [HttpPost]
        [Produces("application/json")]
        [ProducesResponseType(typeof(List<long>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<JsonResult> Post(
            [BindRequired] [FromBody]object item,
            [BindRequired] string entity)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("post", entity, Application.Token);

            var result = await _dataAPI.PostAsync(
                Application.Token,
                GetEntity(entity),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                (DatabaseType)Application.DatabaseType,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                item);

            return Json(result);
        }

        /// <summary>
        /// Use this endpoint to update existing record(s) of the given entity.
        /// </summary>
        /// <param name="entity">Required. The entity name</param>
        /// <param name="item"></param>
        /// <returns>A count of records affected</returns>
        [HttpPut]
        [Produces("application/json")]
        [ProducesResponseType(typeof(long), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<long> Put(
            [BindRequired][FromBody]object item,
            [BindRequired] string entity)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("put", entity, Application.Token);
            
            return await _dataAPI.PutAsync(
                Application.Token,
                GetEntity(entity),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                (DatabaseType)Application.DatabaseType,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                item);
        }

        /// <summary>
        /// Use this endpoint to delete existing records of the given entity.
        /// </summary>
        /// <param name="entity">Required. The entity name</param>
        /// <param name="ids">Required. The IDs of the records to delete comma separated (e.g. "1,2,3")</param>
        /// <returns>A list containing the IDs deleted</returns>
        [HttpDelete]
        [Produces("application/json")]
        [ProducesResponseType(typeof(List<long>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<List<long>> Delete(
            [BindRequired] string entity,
            [BindRequired] string ids)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("delete", entity, Application.Token);

            return await _dataAPI.DeleteAsync(
                Application.Token,
                GetEntity(entity),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                ids);
        }

        /// <summary>
        /// Use this endpoint to create/update/delete multiple records inside a transaction scope.
        /// </summary>
        [HttpPost]
        [Produces("application/json")]
        [ProducesResponseType(typeof(OutTransactionData), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<OutTransactionData> Transaction(
            [FromBody] InTransactionData transactionData)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("transaction", string.Empty, Application.Token);
            
            return await _dataAPI.TransactionAsync(
                Application,
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                (DatabaseType)Application.DatabaseType,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                transactionData);
        }

        /// <summary>
        /// Use this endpoint to retrieve the application schema.
        /// </summary>
        /// <param name="encryptionKey">The application encryption key</param>
        /// <returns></returns>
        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ApplicationSchemaVm), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<JsonResult> Schema()
        {
            using var metricsTimer = _metricsService.RecordDataDuration("schema", string.Empty, Application.Token);

            var allowGetSchema = await _dataAPI.AllowGetSchemaAsync(
                Application.Token,
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List);

            if (allowGetSchema)
            {
                var schemaResult = ApplicationSchemaVm.GetFromApplication(Application);
                return Json(schemaResult);
            }
            else
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, "Unauthorized");
            }
        }
    }
}
