using Apilane.Portal.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Net.Mime;
using System.Threading.Tasks;

namespace Apilane.Portal.Api.V1
{
    [Route(RoutePrefix + "/backup")]
    public class AdminBackupController : PortalAdminApiControllerBase
    {
        public const string FileName = "Apilane.db";

        private readonly IBackupService _backupService;

        public AdminBackupController(IBackupService backupService)
        {
            _backupService = backupService;
        }

        /// <summary>
        /// Downloads a backup of the Portal database as the SQLite file 'Apilane.db'. The file holds
        /// everything, including password hashes and the secrets of every application: keep it safe.
        /// Every download is written to the audit log. Admin only.
        /// </summary>
        [HttpGet]
        // A Stream response is documented as a binary file: see PortalFileOperationFilter.
        [ProducesResponseType(typeof(Stream), StatusCodes.Status200OK)]
        public async Task<FileStreamResult> Download()
        {
            return File(await _backupService.CreateAsync(), MediaTypeNames.Application.Octet, FileName);
        }
    }
}
