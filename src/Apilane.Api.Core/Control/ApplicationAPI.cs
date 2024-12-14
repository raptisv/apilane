using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Grains;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Repository;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class ApplicationAPI : IApplicationAPI
    {
        private readonly ILogger<ApplicationAPI> _logger;
        private readonly IClusterClient _clusterClient;
        private readonly ApiConfiguration _apiConfiguration;
        private readonly IApplicationService _applicationService;
        private readonly IApplicationBuilderService _applicationBuilder;
        private readonly ITransactionScopeService _transactionScopeService;        

        public ApplicationAPI(
            ILogger<ApplicationAPI> logger,
            IClusterClient clusterClient,
            ApiConfiguration apiConfiguration,
            IApplicationService applicationService,
            IApplicationBuilderService applicationBuilder,
            ITransactionScopeService transactionScopeService)
        {
            _logger = logger;
            _clusterClient = clusterClient;
            _apiConfiguration = apiConfiguration;
            _applicationService = applicationService;
            _applicationBuilder = applicationBuilder;
            _transactionScopeService = transactionScopeService;
        }

        public async Task RebuildAsync(DBWS_Application application)
        {
            // First drop all data
            await _applicationBuilder.DropApplicationDataAsync(application,
                (DatabaseType)application.DatabaseType,
                application.GetConnectionstring(_apiConfiguration.FilesPath));

            // Create the database or just confirm database exists (for SQL Server)

            switch (application.DatabaseType)
            {
                case (int)DatabaseType.SQLLite:
                    var fileInfo = application.Token.GetApplicationFileInfo(_apiConfiguration.FilesPath);

                    application.ConnectionString = null;
                    if (fileInfo.Exists)
                        throw new ApilaneException(AppErrors.ERROR, "Database already exists!");

                    // Create the database file
                    SQLiteDataStorageRepository.GenerateDatabase(fileInfo.FullName);
                    break;
                case (int)DatabaseType.SQLServer:
                    SQLServerDataStorageRepository.ConfirmDatabaseExists(application.GetConnectionstring(_apiConfiguration.FilesPath));
                    break;
                case (int)DatabaseType.MySQL:
                    MySQLDataStorageRepository.ConfirmDatabaseExists(application.GetConnectionstring(_apiConfiguration.FilesPath));
                    break;
                default:
                    throw new NotImplementedException();
            }

            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                // Then rebuild the application
                await _applicationBuilder.BuildApplicationAsync(application);

                scope.Complete();
            }

            await ResetAppAsync(application.Token);
        }

        public async Task DegenerateAsync(
            DBWS_Application application,
            DatabaseType databaseType,
            string connectionString)
        {
            await _applicationBuilder.DisposeAsync();

            // Drop all data and delete database

            await _applicationBuilder.DropApplicationDataAsync(application, databaseType, connectionString);

            // Delete all application data

            var rootDirectory = application.Token.GetRootDirectoryInfo(_apiConfiguration.FilesPath);
            if (rootDirectory.Exists)
            {
                try
                {
                    // Sometimes this one fails
                    rootDirectory.Delete(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error trying to delete root directory '{rootDirectory.FullName}'");
                }
            }

            // Clear grain cache
            var grainRef = _clusterClient.GetGrain<IApplicationGrain>(new Guid(application.Token));
            await grainRef.ResetStateAsync();
        }

        public async Task RenameEntityAsync(
            DBWS_Application application,
            long entityID,
            string newName)
        {
            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                await _applicationBuilder.RenameEntityAsync(application, entityID, newName);

                scope.Complete();
            }

            await ResetAppAsync(application.Token);
        }

        public async Task RenameEntityPropertyAsync(
            DBWS_Application application,
            long propertyID,
            string newName)
        {
            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                await _applicationBuilder.RenameEntityPropertyAsync(application, propertyID, newName);

                scope.Complete();
            }

            await ResetAppAsync(application.Token);
        }

        public async Task GenerateEntityAsync(
            DBWS_Application application,
            DBWS_Entity entity)
        {
            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                await _applicationBuilder.GenerateEntityAsync(application, entity);

                scope.Complete();
            }

            await ResetAppAsync(application.Token);
        }

        public async Task DegenerateEntityAsync(
            DBWS_Application application,
            string entityName)
        {
            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                await _applicationBuilder.DegenerateEntityAsync(application, entityName);

                scope.Complete();
            }

            await ResetAppAsync(application.Token);
        }

        public async Task GeneratePropertyAsync(
            string appToken,
            DatabaseType databaseType,
            DBWS_EntityProperty property,
            string entityName)
        {
            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                await _applicationBuilder.GeneratePropertyAsync(databaseType, entityName, property);

                scope.Complete();
            }

            await ResetAppAsync(appToken);
        }

        public async Task DegeneratePropertyAsync(
            DBWS_Application application,
            long propertyID)
        {
            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                await _applicationBuilder.DegeneratePropertyAsync(application, propertyID);

                scope.Complete();
            }

            await ResetAppAsync(application.Token);
        }

        public async Task GenerateContraintsAsync(
            DBWS_Application application,
            List<EntityConstraint> incomingContraints,
            string entityName)
        {
            var entity = application.Entities.Single(x => x.Name.Equals(entityName));

            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                await _applicationBuilder.GenerateConstraintsAsync(entity, incomingContraints, entity.Constraints);

                scope.Complete();
            }

            await ResetAppAsync(application.Token);
        }

        public async Task<List<long>> ImportDataAsync(
            DBWS_Application application,
            List<Dictionary<string, object?>> data,
            string entityName)
        {
            var entity = application.Entities.Single(x => x.Name.Equals(entityName));

            List<long> result;
            using (var scope = _transactionScopeService.OpenTransactionScope())
            {
                result = await _applicationBuilder.ImportDataAsync(application, entity, data);

                scope.Complete();
            }

            return result;
        }

        public async Task ResetAppAsync(string appToken)
        {
            // Clear grain cache
            var grainRef = _clusterClient.GetGrain<IApplicationGrain>(new Guid(appToken));
            await grainRef.ResetStateAsync();
        }

        public double GetStorageUsedInMB(
            string appToken,
            DatabaseType databaseType)
        {
            // Get files size
            var dirFiles = appToken.GetFilesRootDirectoryInfo(_apiConfiguration.FilesPath);
            var filesSize = dirFiles.Exists
                ? dirFiles.EnumerateFiles("*.*", SearchOption.AllDirectories).Sum(fi => fi.Length) / 1024.0 / 1024.0
                : 0.0;

            // Get db size if it is SQLite
            if (databaseType == DatabaseType.SQLLite)
            {
                var dirInfo = appToken.GetRootDirectoryInfo(_apiConfiguration.FilesPath);
                FileInfo info = new FileInfo(Path.Combine(dirInfo.FullName, appToken) + ".db");
                filesSize += info != null && info.Exists
                    ? (info.Length / 1024.0 / 1024.0)
                    : 0.0;
            }

            return filesSize;
        }
    }
}
