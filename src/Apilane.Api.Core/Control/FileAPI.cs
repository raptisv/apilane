using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Grains;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Api.Core.Models.AppModules.Files;
using Apilane.Api.Core.Services;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Orleans;
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
        private readonly IClusterClient _clusterClient;
        private IApplicationDataService _appDataService;
        private IApplicationService _applicationService;
        private IApplicationDataStoreFactory _dataStore;

        public FileAPI(
            ApiConfiguration apiConfiguration,
            IClusterClient clusterClient,
            IApplicationDataService appDataService,
            IApplicationService applicationService,
            IApplicationDataStoreFactory dataStore)
        {
            _apiConfiguration = apiConfiguration;
            _clusterClient = clusterClient;
            _appDataService = appDataService;
            _applicationService = applicationService;
            _dataStore = dataStore;
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
                     Public = Utils.GetBool(result[nameof(Files.Public)]),
                };
            }

            return null;
        }

        public async Task<Files?> GetFileItemAsync(string fileUID)
        {
            var resultList = await _dataStore.GetPagedDataAsync(
                nameof(Files),
                new List<string>() { nameof(Files.ID), nameof(Files.Owner), nameof(Files.Created), nameof(Files.UID), nameof(Files.Size), nameof(Files.Name), nameof(Files.Public) },
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
                    Public = Utils.GetBool(result[nameof(Files.Public)]),
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

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), entityFiles.Name, SecurityActionType.get);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: entityFiles.Name, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            if (pageIndex <= 0)
            {
                pageIndex = 1;
            }

            if (pageSize < 0 || pageSize > 1000)
            {
                pageSize = 1000;
            }

            return await _appDataService.GetAsync(
                appToken,
                differentiationEntity,
                applicationEncryptionKey,
                entityFiles,
                pageIndex,
                pageSize,
                _appDataService.GetFilterData(entityFiles, filter, userSecurity),
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
            byte[] buffer,
            string fileName,
            string fileUID,
            bool isPublic)
        {
            var userSecurity = userHasFullAccess
                ? EntityAccess.GetFull(nameof(Files), filesEntity.Properties, SecurityActionType.post)
                : EntityAccess.GetMaximum(appUser, applicationSecurityList, nameof(Files), SecurityTypes.Entity, SecurityActionType.post);

            if (userSecurity.Count == 0)
            {
                throw new ApilaneException(AppErrors.UNAUTHORIZED, entity: nameof(Files));
            }

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), filesEntity.Name, SecurityActionType.post);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: filesEntity.Name, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
            }

            fileUID = (appUser?.ID.ToString() ?? string.Empty)
                + "_"
                + (string.IsNullOrWhiteSpace(fileUID) ? Guid.NewGuid().ToString() : fileUID);

            if (!Utils.IsValidRegexMatch(fileUID, "^[a-zA-Z0-9_-]+$"))
            {
                throw new Exception("UID accepts only a-z, A-Z, 0-9, _, -");
            }

            var newFile = new Files()
            {
                ID = -1,
                Name = fileName,
                UID = fileUID,
                Owner = appUser?.ID,
                Public = isPublic,
                Created = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow),
                Size = buffer.Length / 1024.0 / 1024.0
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

            // Create the file object
            var result = await _appDataService.PostAsync(
                appToken,
                filesEntity,
                databaseType,
                differentiationEntity,
                applicationEncryptionKey,
                JsonSerializer.Serialize(newFile),
                (appUser, userSecurity));

            // First create the file object in database (to apply security) and then save the file
            File.WriteAllBytes(Path.Combine(currentRoot.FullName, newFile.UID), buffer);

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

            if (userSecurity.Select(x => x.RateLimit).IsRateLimited(out int maxRequests, out TimeSpan timeWindow))
            {
                // Check rate limit
                var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(maxRequests, timeWindow, appUser?.ID.ToString(), filesEntity.Name, SecurityActionType.delete);
                var rateLimitGrainRef = _clusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(appToken), rateLimitGrainKeyExt, null);
                var rateLimitResult = await rateLimitGrainRef.IsRequestAllowedAsync();
                if (!rateLimitResult.IsRequestAllowed)
                {
                    throw new ApilaneException(AppErrors.RATE_LIMIT_EXCEEDED, entity: filesEntity.Name, message: $"Try again in {rateLimitResult.TimeToWait.GetTimeRemainingString()}");
                }
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
                    // First delete to validate access
                    result = await _appDataService.DeleteAsync(appToken, filesEntity, userHasFullAccess, differentiationEntity, applicationEncryptionKey, finalIdsToDelete, (appUser, userSecurity));

                    // Then delete the files
                    foreach (var file in fileItems)
                    {
                        var fileInfo = new FileInfo($"{appToken.GetFilesRootDirectoryInfo(_apiConfiguration.FilesPath).FullName}\\{file[nameof(Files.UID)]}");
                        if (fileInfo.Exists)
                        {
                            fileInfo.Delete();
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
