using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.AppModules;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Exceptions;
using Apilane.Common;
using Apilane.Common.Models;
using Apilane.Common.Security;
using Apilane.Api.Filters;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Apilane.Api.Controllers
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
        public async Task<bool> Generate([FromBody] DBWS_Application application)
        {
            // The portal sends the installation key only in the x-installation-key header, never in the URL.
            var providedKey = Request.Headers[Globals.InstallationKeyHeaderName].ToString();

            if (!SecureCompare.AreEqual(_apiConfiguration.InstallationKey, providedKey))
            {
                throw new ApilaneException(Apilane.Api.Core.Enums.AppErrors.UNAUTHORIZED);
            }

            return await _applicationNewAPI.CreateApplicationAsync(application);
        }

        [HttpGet]
        public JsonResult GetSystemEntities(string differentiationEntity) => Json(Modules.NewApplicationSystemEntities(differentiationEntity));
    }
}
