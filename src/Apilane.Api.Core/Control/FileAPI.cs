using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Models.AppModules.Files;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class FileAPI : IFileAPI
    {
        private ApiConfiguration _apiConfiguration;
        private IApplicationDataService _appDataService;
        private IApplicationService _applicationService;
        private IApplicationDataStoreFactory _dataStore;
        private ICloudStorageProvider _storageProvider;
        private readonly ILogger<FileAPI> _logger;

        public FileAPI(
            ApiConfiguration apiConfiguration,
            IApplicationDataService appDataService,
            IApplicationService applicationService,
            IApplicationDataStoreFactory dataStore,
            ICloudStorageProvider storageProvider,
            ILogger<FileAPI> logger)
        {
            _apiConfiguration = apiConfiguration;
            _appDataService = appDataService;
            _applicationService = applicationService;
            _dataStore = dataStore;
            _storageProvider = storageProvider;
            _logger = logger;
        }

        public async Task<Files?> GetFileItemAsync(long fileID)
        {
            var result = await _dataStore.GetDataByIdAsync(
                nameof(Files),
                fileID,
                null);

            if (result is not null)
            {
                return new Files()
                {
                     ID = Utils.GetLong(result[nameof(Files.ID)], 0),
                     Owner = Utils.GetNullLong(result[nameof(Files.Owner)]),
                     Created = Utils.GetLong(result[nameof(Files.Created)], 0),
                     UID = Utils.GetString(result[nameof(Files.UID)]),
                     Size = Utils.GetDouble(result[nameof(Files.Size)], 0),
                     Name = Utils.GetString(result[nameof(Files.Name)]),
                };
            }

            return null;
        }

        public async Task<Files?> GetFileItemAsync(string fileUID)
        {
            var resultList = await _dataStore.GetPagedDataAsync(
                nameof(Files),
            new List<string>() { nameof(Files.ID), nameof(Files.Owner), nameof(Files.Created), nameof(Files.UID), nameof(Files.Size), nameof(Files.Name) },
                new FilterData(nameof(Files.UID), FilterData.FilterOperators.equal, fileUID, PropertyType.String),
                null, 1, 1);

            if (resultList is not null && resultList.Count == 1)
            {
                var result = resultList.Single();
                return new Files()
                {
                    ID = Utils.GetLong(result[nameof(Files.ID)], 0),
                    Owner = Utils.GetNullLong(result[nameof(Files.Owner)]),
                    Created = Utils.GetLong(result[nameof(Files.Created)], 0),
                    UID = Utils.GetString(result[nameof(Files.UID)]),
                    Size = Utils.GetDouble(result[nameof(Files.Size)], 0),
                    Name = Utils.GetString(result[nameof(Files.Name)]),
                };
            }

            return null;
        }

        public async Task<object> GetByIdAsync(
            string appToken,
            DBWS_Entity entity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            string? differentiationEntity,
            string applicationEncryptionKey,
            long id,
            string? properties)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(nameof(Files), new List<DBWS_EntityProperty>(), SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, nameof(Files), SecurityTypes.Entity, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: nameof(Files));
            }

            return await _appDataService.GetByIDAsync(
                appToken,
                userHasFullAccess,
                entity,
                differentiationEntity,
                applicationEncryptionKey,
                id,
                properties,
                (appUser, userSecurity));
        }

        public async Task<DataResponse> GetAsync(
            string appToken,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            string? differentiationEntity,
            string applicationEncryptionKey,
            int pageIndex,
            int pageSize,
            string? properties,
            string? filter,
            string? sort,
            bool getTotal)
        {
            var application = await _applicationService.GetAsync(appToken);

            var entityFiles = application.Entities.Single(x => x.Name.Equals(nameof(Files)));

            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(entityFiles.Name, entityFiles.Properties, SecurityActionType.get)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, entityFiles.Name, SecurityTypes.Entity, SecurityActionType.get);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: entityFiles.Name);
            }

            if (pageIndex <= 0)
            {
                pageIndex = 1;
            }

            if (pageSize < 0 || pageSize > 1000)
            {
                pageSize = 1000;
            }

            var systemFilters = _appDataService.GetSystemFilters(userHasFullAccess, differentiationEntity, entityFiles, (appUser, userSecurity));
            var filterData = _appDataService.GetFilterData(entityFiles, filter, userSecurity);
            if (filterData is not null)
            {
                systemFilters.Add(filterData);
            }

            return await _appDataService.GetAsync(
                appToken,
                differentiationEntity,
                applicationEncryptionKey,
                entityFiles,
                pageIndex,
                pageSize,
                new FilterData(FilterData.FilterLogic.AND, systemFilters),
                _appDataService.GetSortData(entityFiles, sort, userSecurity),
                properties,
                (appUser, userSecurity),
                getTotal);
        }

        public async Task<long> PostAsync(
            string appToken,
            DBWS_Entity filesEntity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            DatabaseType databaseType,
            string? differentiationEntity,
            string applicationEncryptionKey,
            int maxAllowedFileSizeInKB,
            Stream fileContent,
            long contentLength,
            string fileName)
        {
            var fullPropertyAccess = EntityAccess.GetFull(nameof(Files), filesEntity.Properties, SecurityActionType.post);

            // The file will be created with full access but at this point we need to confirm that the user has post access to Files.
            var userSecurity = userHasFullAccess
                ? fullPropertyAccess
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, nameof(Files), SecurityTypes.Entity, SecurityActionType.post);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: nameof(Files));
            }

            string fileUID = Guid.NewGuid().ToString();

            var newFile = new Files()
            {
                ID = -1,
                Name = fileName,
                UID = fileUID,
                Owner = appUser?.ID,
                Created = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow),
                Size = contentLength / 1024.0 / 1024.0,
            };

            if (_apiConfiguration.InvalidFilesExtentions.Contains(new FileInfo(newFile.Name).Extension.ToLower()))
            {
                throw new Exception($"The following extentions are not allowed: {string.Join(",", _apiConfiguration.InvalidFilesExtentions)}");
            }

            var currentRoot = appToken.GetFilesRootDirectoryInfo(_apiConfiguration.FilesPath);
            if (!currentRoot.Exists)
            {
                currentRoot.Create();
            }

            // Validate things like file size etc.
            await ValidateFileObjectAsync(newFile.Size, newFile.UID, currentRoot, maxAllowedFileSizeInKB);

            // Create the file object with full property access (Files properties are system-managed, not user-provided)
            var result = await _appDataService.PostAsync(
                appToken,
                filesEntity,
                databaseType,
                differentiationEntity,
                applicationEncryptionKey,
                JsonSerializer.Serialize(newFile),
                (appUser, fullPropertyAccess));

            // First create the file object in database (to apply security) and then save the file
            await _storageProvider.PutAsync(appToken, fileUID, fileContent, contentLength);

            return result.FirstOrDefault();
        }

        public async Task<List<long>> DeleteAsync(
            string appToken,
            DBWS_Entity filesEntity,
            bool userHasFullAccess,
            Users? appUser,
            List<DBWS_Security> applicationSecurityList,
            string? differentiationEntity,
            string applicationEncryptionKey,
            string ids)
        {
            var application = await _applicationService.GetAsync(appToken);

            var result = new List<long>();

            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(filesEntity.Name, filesEntity.Properties, SecurityActionType.delete)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, filesEntity.Name, SecurityTypes.Entity, SecurityActionType.delete);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: filesEntity.Name);
            }

            var listOfIds = Utils.GetString(ids).Split(',').Select(x => Utils.GetInt(x)).Where(x => x > 0).ToList();

            if (listOfIds.Count == 0)
            {
                throw new ApilaneException(AppErrors.NO_RECORDS_FOUND_TO_DELETE);
            }

            // Create the filter
            var filters = new FilterData(FilterData.FilterLogic.OR, new List<FilterData>());
            foreach (var id in listOfIds)
            {
                filters.Add(new FilterData(nameof(Files.ID), FilterData.FilterOperators.equal, id, PropertyType.Number));
            }

            // IMPORTANT!!!
            // Get the files from base controller, to append filter and prevent delete files that the user does not have access to
            var systemFilters = _appDataService.GetSystemFilters(userHasFullAccess, differentiationEntity, filesEntity, (appUser, userSecurity));
            systemFilters.Add(filters);

            var listFiles = await _appDataService.GetAsync(
                appToken,
                differentiationEntity,
                applicationEncryptionKey,
                filesEntity,
                -1,
                -1,
                new FilterData(FilterData.FilterLogic.AND, systemFilters),
                null,
                $"{nameof(Files.ID)},{nameof(Files.UID)}",
                (appUser, userSecurity),
                false);

            var finalIdsToDelete = string.Join(",", listFiles.Data.Select(x => x[nameof(Files.ID)]));

            if (!string.IsNullOrWhiteSpace(finalIdsToDelete))
            {
                // Get files explicitly. Do not remove this select command cause await base.Get returns only ID while we need UID also
                var fileItems = await _dataStore.GetPagedDataAsync(
                    nameof(Files),
                    new List<string>() { nameof(Files.ID), nameof(Files.UID) },
                    new FilterData(nameof(Files.ID), FilterData.FilterOperators.contains, finalIdsToDelete, PropertyType.Number),
                    null, 1, 10000);

                if (fileItems.Count > 0)
                {
                    var fileUidsToDelete = fileItems
                        .Select(x => Utils.GetString(x[nameof(Files.UID)]))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();

                    result = await _appDataService.DeleteAsync(appToken, filesEntity, userHasFullAccess, differentiationEntity, applicationEncryptionKey, finalIdsToDelete, (appUser, userSecurity));

                    foreach (var fileUid in fileUidsToDelete)
                    {
                        try
                        {
                            await _storageProvider.DeleteAsync(appToken, fileUid);
                        }
                        catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                        {
                            // File is already gone; the database delete has succeeded.
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete physical file {UID} for application {AppToken}", fileUid, appToken);
                        }
                    }
                }
            }

            return result;
        }

        public FileInfo GetFileInfoAsync(
            string appToken,
            string fileUID)
        {
            return new FileInfo(Path.Combine(appToken.GetFilesRootDirectoryInfo(_apiConfiguration.FilesPath).FullName, fileUID));
        }

        private async Task ValidateFileObjectAsync(
            double fileSize,
            string UID,
            DirectoryInfo currentRoot,
            int maxAllowedFileSizeInKB)
        {
            if (fileSize * 1024.0 > maxAllowedFileSizeInKB)
            {
                throw new ApilaneException(AppErrors.ERROR, $"Maximum file size {maxAllowedFileSizeInKB} KB");
            }

            // Override existing files
            await _dataStore.DeleteDataAsync(
                nameof(Files),
                new FilterData(nameof(Files.UID), FilterData.FilterOperators.equal, UID, PropertyType.String));

            IEnumerable<FileInfo> existingFiles = currentRoot.GetFiles().Where(x => x.Name.Equals(UID));
            foreach (var f in existingFiles)
            {
                f.Delete();
            }
        }
    }
}
