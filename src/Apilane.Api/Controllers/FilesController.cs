using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Abstractions.Metrics;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Files;
using Apilane.Api.Models.ViewModels;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Apilane.Api.Attributes;
using Apilane.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Orleans;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Apilane.Api.Controllers
{
    [ServiceFilter(typeof(ApplicationLogActionFilter))]
    public class FilesController : BaseApplicationApiController
    {
        private readonly IFileAPI _fileAPI;
        private readonly IMetricsService _metricsService;
        private readonly ICloudStorageProvider _storageProvider;

        public FilesController(
            ApiConfiguration apiConfiguration,
            IFileAPI fileAPI,
            IClusterClient clusterClient,
            IMetricsService metricsService,
            ICloudStorageProvider storageProvider) : base(apiConfiguration, clusterClient)
        {
            _fileAPI = fileAPI;
            _metricsService = metricsService;
            _storageProvider = storageProvider;
        }

        /// <summary>
        /// Use this endpoint to get the actual file, provided the ID. Client cache is set to 60 minutes.
        /// </summary>
        /// <param name="fileID">Required. The file's ID</param>
        /// <returns>Returns the actual file. You can use this endpoint on an HTML img tag</returns>
        [HttpGet]
        [RequiredParameter("FileID")]
        public async Task<IActionResult> Download(
            [BindRequired] long fileID)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("download", "files", Application.Token);

            var fileItem = await _fileAPI.GetFileItemAsync(fileID)
                ?? throw new ApilaneException(AppErrors.FILE_NOT_FOUND, null, null);

            return await GetGivenTheIDAsync(fileItem);
            
        }

        /// <summary>
        /// Use this endpoint to get the actual file, provided the UID. Client cache is set to 60 minutes.
        /// </summary>
        /// <param name="fileUID">Required. The file's UID</param>
        /// <returns>Returns the actual file. You can use this endpoint on an HTML img tag</returns>
        [HttpGet]
        [RequiredParameter("FileUID")]
        public async Task<IActionResult> Download(
            [BindRequired] string fileUID)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("download", "files", Application.Token);
            
            var fileItem = await _fileAPI.GetFileItemAsync(fileUID)
                ?? throw new ApilaneException(AppErrors.FILE_NOT_FOUND, null, null);

            return await GetGivenTheIDAsync(fileItem);
        }

        /// <summary>
        /// Use this endpoint to retrieve a file record with the given ID
        /// </summary>
        /// <param name="id">The file ID</param>
        /// <param name="properties">The properties to fetch, comma separated. Leave empty to fetch all properties.</param>
        /// <returns></returns>
        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(object), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<object> GetByID(
            [BindRequired] long id,
            string? properties = null)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("getbyid", "files", Application.Token);
            
            return await _fileAPI.GetByIdAsync(
                Application.Token,
                GetEntity(nameof(Files)),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                id,
                properties);
        }

        /// <summary>
        ///  Use this endpoint to retrieve file records, not the files
        /// </summary>
        /// <param name="pageIndex">Default value 1. Used for data paging</param>
        /// <param name="pageSize">Default value 20. Range 0-1000. Used for data paging</param>
        /// <param name="properties">The entity properties to fetch, comma separated. Leave empty to fetch all properties. Use it to limit consumed bandwidth.</param>
        /// <param name="filter">Default value NULL. Used for data fitlering</param>
        /// <param name="sort">Default value NULL. Used for data sorting</param>
        /// <param name="getTotal">Default value false. Set it to true to get the count of records according to the specidified filtering </param>
        /// <returns>An object that consists of two properties. Data is an array of records. Total is the count of records, according to the specidified filtering</returns>
        [HttpGet]
        [Produces("application/json")]
        [ProducesResponseType(typeof(object), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<DataResponse> Get(
            int pageIndex = 1,
            int pageSize = 20,
            string? properties = null,
            string? filter = null,
            string? sort = null,
            bool getTotal = false)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("get", "files", Application.Token);
            
            return await _fileAPI.GetAsync(
                Application.Token,
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                pageIndex,
                pageSize,
                properties,
                filter,
                sort,
                getTotal);
        }

        private async Task<IActionResult> GetGivenTheIDAsync(Files fileItem)
        {
            var item = await _fileAPI.GetByIdAsync(
                Application.Token,
                GetEntity(nameof(Files)),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                fileItem.ID,
                null);

            if (item == null)
            {
                return NotFound();
            }

            var stream = await _storageProvider.GetAsync(Application.Token, fileItem.UID);
            var contentType = MimeTypeDetector.GetMimeType(fileItem.Name);
            return new FileStreamResult(stream, contentType)
            {
                FileDownloadName = fileItem.Name
            };
        }

        /// <summary>
        ///  Use this endpoint to upload a file
        /// </summary>
        /// <param name="fileUpload"></param>
        /// <returns>Returns the new created file ID</returns>
        [HttpPost]
        [Produces("application/json")]
        [ProducesResponseType(typeof(long), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        [DisableRequestSizeLimit]
        public async Task<long> Post(IFormFile fileUpload)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("post", "files", Application.Token);

            if (fileUpload == null || fileUpload.Length == 0)
            {
                throw new ApilaneException(AppErrors.ERROR, "No file found to upload");
            }

            using var stream = fileUpload.OpenReadStream();
            return await _fileAPI.PostAsync(
                Application.Token,
                GetEntity(nameof(Files)),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                (DatabaseType)Application.DatabaseType,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                Application.MaxAllowedFileSizeInKB,
                stream,
                fileUpload.Length,
                fileUpload.FileName);
        }

        /// <summary>
        /// Use this endpoint to delete file(s)
        /// </summary>
        /// <param name="ids">Required. The IDs of the records to delete comma separated (e.g. "1,2,3")</param>
        /// <returns>A list containing the IDs deleted</returns>
        [HttpDelete]
        [Produces("application/json")]
        [ProducesResponseType(typeof(List<long>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ApiErrorVm), (int)HttpStatusCode.Unauthorized)]
        public async Task<List<long>> Delete(
            [BindRequired] string ids)
        {
            using var metricsTimer = _metricsService.RecordDataDuration("delete", "files", Application.Token);

            return await _fileAPI.DeleteAsync(
                Application.Token,
                GetEntity(nameof(Files)),
                UserHasFullAccess,
                ApplicationUser,
                Application.Security_List,
                Application.DifferentiationEntity,
                Application.EncryptionKey,
                ids);
        }
    }
}
