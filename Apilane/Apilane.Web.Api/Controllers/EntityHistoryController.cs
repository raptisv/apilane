using Apilane.Api.Abstractions;
using Apilane.Api.Configuration;
using Apilane.Web.Api.Filters;
using Apilane.Web.Api.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Orleans;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Web.Api.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApplicationOwnerAuthorize]
    public class EntityHistoryController : BaseApplicationApiController
    {
        private readonly IEntityHistoryAPI _entityHistoryAPI;

        public EntityHistoryController(
            ApiConfiguration apiConfiguration,
            IEntityHistoryAPI entityHistoryAPI,
            IClusterClient clusterClient) : base(apiConfiguration, clusterClient)
        {
            _entityHistoryAPI = entityHistoryAPI;
        }

        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(object), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<object> Get(
            [BindRequired] long recordID,
            [BindRequired] string entity,
            int pageIndex = 1,
            int pageSize = 20)
        {
            return await _entityHistoryAPI.GetPagedAsync(Application.Token, recordID, entity, pageIndex, pageSize); 
        }

        [HttpDelete]
        [Produces("application/json")]
        [ProducesResponseType(typeof(string), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<JsonResult> Delete(
            [BindRequired] string entity,
            long? recordID = null)
        {
            await _entityHistoryAPI.DeleteAsync(Application.Token, entity, recordID is null ? null : new List<long>() { recordID.Value });

            return Json("OK");
        }
    }
}
