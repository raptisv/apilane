using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Api.Filters;
using Microsoft.AspNetCore.Mvc;
using Orleans;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Api.Controllers
{
    /// <summary>
    /// User validation takes place on AuthHandler
    /// </summary>
    [ApiExplorerSettings(IgnoreApi = true)]
    [ApplicationOwnerAuthorize]
    public class ApplicationController : BaseApplicationApiController
    {
        private readonly IApplicationAPI _applicationAPI;

        public ApplicationController(
            ApiConfiguration apiConfiguration,
            IApplicationAPI applicationAPI,
            IClusterClient clusterClient) 
            : base(apiConfiguration, clusterClient)
        {
            _applicationAPI = applicationAPI;
        }

        [HttpGet]
        public JsonResult GetSystemPropertiesAndConstraints(bool entityHasDifferentiationProperty)
        {
            var data = Apilane.Api.Core.AppModules.Modules.NewEntityPropertiesConstraints(Application.DifferentiationEntity, entityHasDifferentiationProperty);

            return Json(new EntityPropertiesConstrainsDto()
            {
                Properties = data.Properties,
                Constraints = data.Constraints
            });
        }

        [HttpGet]
        public Task Rebuild() => _applicationAPI.RebuildAsync(Application);

        [HttpGet]
        public Task Degenerate() => _applicationAPI.DegenerateAsync(Application, (DatabaseType)Application.DatabaseType, Application.GetConnectionstring(ApiConfiguration.FilesPath));

        [HttpGet]
        public Task RenameEntity(long ID, string NewName) => _applicationAPI.RenameEntityAsync(Application, ID, NewName);

        [HttpGet]
        public Task RenameEntityProperty(long ID, string NewName) => _applicationAPI.RenameEntityPropertyAsync(Application, ID, NewName);

        [HttpPost]
        public Task GenerateEntity([FromBody] DBWS_Entity Item) => _applicationAPI.GenerateEntityAsync(Application, Item);

        [HttpGet]
        public Task DegenerateEntity(string Entity) => _applicationAPI.DegenerateEntityAsync(Application, Entity);

        [HttpPost]
        public Task GenerateProperty([FromBody]DBWS_EntityProperty Item, [FromQuery] string Entity) => _applicationAPI.GeneratePropertyAsync(Application.Token, (DatabaseType)Application.DatabaseType, Item, Entity);

        [HttpGet]
        public Task DegenerateProperty(long ID) => _applicationAPI.DegeneratePropertyAsync(Application, ID);

        [HttpPost]
        public Task GenerateConstraints([FromBody] List<EntityConstraint> Item, [FromQuery] string Entity) => _applicationAPI.GenerateConstraintsAsync(Application, Item, Entity);

        [HttpPost]
        public async Task<JsonResult> ImportData([FromBody] List<Dictionary<string, object?>> Data, [FromQuery] string Entity)
        {
            var result = await _applicationAPI.ImportDataAsync(Application, Data, Entity);

            return Json(result);
        }

        #region HELPERS

        [HttpGet]
        public async Task<IActionResult> ClearCache()
        {
            await _applicationAPI.ResetAppAsync(Application.Token);

            return Json("OK");
        }

        [HttpGet]
        public double GetStorageUsed() => _applicationAPI.GetStorageUsedInMB(Application.Token, (DatabaseType)Application.DatabaseType);

        [HttpGet]
        public IActionResult Export()
        {
            // Get folder that will be finally zipped
            var directoryToZip = Path.Combine(ApiConfiguration.FilesPath, Application.Token);

            // Hide application specifics
            Application.ID = 0;
            Application.ServerID = 0;
            Application.UserID = string.Empty;
            Application.Server = new DBWS_Server();
            Application.MailFromAddress = null;
            Application.MailFromDisplayName = null;
            Application.MailPassword = null;
            Application.MailServer = null;
            Application.MailServerPort = null;
            Application.MailUserName = null;
            Application.AdminEmail = null;
            Application.Collaborates = new List<DBWS_Collaborate>();
            Application.ConnectionString = null!;
            Application.DatabaseType = 0;
            Application.Entities?.ForEach(x => x.ID = 0);
            Application.Entities?.ForEach(x => x.AppID = 0);
            Application.CustomEndpoints?.ForEach(x => x.ID = 0);
            Application.CustomEndpoints?.ForEach(x => x.AppID = 0);
            Application.Reports?.ForEach(x => x.ID = 0);
            Application.Reports?.ForEach(x => x.AppID = 0);

            // !IMPORTANT! It is important to keep the same Encryption key since any encrypted data will not be able to be decrypted.

            // Serialize app info
            var jsonApp = JsonSerializer.Serialize(Application, new JsonSerializerOptions() { WriteIndented = true });

            // Write file to the to-be-zipped folder
            System.IO.File.WriteAllText(Path.Combine(directoryToZip, $"application.json"), jsonApp);

            var zipFolderFullPath = $"{directoryToZip}.zip";

            try
            {
                ZipFile.CreateFromDirectory(directoryToZip, zipFolderFullPath);

                var appBytes = System.IO.File.ReadAllBytes(zipFolderFullPath);

                return File(appBytes, "application/text", $"{Application.Token}.zip");
            }
            finally
            {
                if (System.IO.File.Exists(zipFolderFullPath))
                {
                    System.IO.File.Delete(zipFolderFullPath);
                }
            }
        }

        #endregion
    }
}
