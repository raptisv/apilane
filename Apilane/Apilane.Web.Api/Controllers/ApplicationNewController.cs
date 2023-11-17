using Apilane.Api.Abstractions;
using Apilane.Api.AppModules;
using Apilane.Api.Configuration;
using Apilane.Api.Exceptions;
using Apilane.Common.Models;
using Apilane.Web.Api.Filters;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Web.Api.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    [ServiceFilter(typeof(ApiExceptionFilter))]
    [Route("api/[controller]/[action]")]
    public class ApplicationNewController : Controller
    {
        private readonly ApiConfiguration _apiConfiguration;
        private readonly IApplicationNewAPI _applicationNewAPI;

        public ApplicationNewController(
            ApiConfiguration apiConfiguration,
            IApplicationNewAPI applicationNewAPI)
        {
            _apiConfiguration = apiConfiguration;
            _applicationNewAPI = applicationNewAPI;
        }

        [HttpPost]
        public async Task<bool> Generate([FromBody] DBWS_Application application, [FromQuery] string installationKey)
        {
            if (!_apiConfiguration.InstallationKey.Equals(installationKey))
            {
                throw new ApilaneException(Apilane.Api.Enums.AppErrors.UNAUTHORIZED);
            }

            return await _applicationNewAPI.CreateApplicationAsync(application);
        }

        [HttpGet]
        public JsonResult GetSystemEntities(string differentiationEntity) => Json(Modules.NewApplicationSystemEntities(differentiationEntity));
    }
}
