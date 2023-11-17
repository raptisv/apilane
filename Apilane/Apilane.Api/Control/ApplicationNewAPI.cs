using Apilane.Api.Abstractions;
using Apilane.Api.Configuration;
using Apilane.Api.Enums;
using Apilane.Api.Exceptions;
using Apilane.Api.Services;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Abstractions;
using Apilane.Data.Repository;
using Apilane.Data.Repository.Factory;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace Apilane.Api
{
    public class ApplicationNewAPI : IApplicationNewAPI
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<ApplicationService> _logger;
        private readonly ApiConfiguration _apiConfiguration;
        private readonly ITransactionScopeService _transactionScopeService;
        private readonly IApplicationHelperService _applicationHelperService;

        public ApplicationNewAPI(
            ApiConfiguration currentConfiguration,
            ILogger<ApplicationService> loggerService,
            ILoggerFactory loggerFactory,
            ITransactionScopeService transactionScopeService,
            IApplicationHelperService applicationHelperService)
        {
            _logger = loggerService;
            _apiConfiguration = currentConfiguration;
            _loggerFactory = loggerFactory;
            _transactionScopeService = transactionScopeService;
            _applicationHelperService = applicationHelperService;
        }

        public async Task<bool> CreateApplicationAsync(DBWS_Application newApplication)
        {
            if (newApplication.DatabaseType != (int)DatabaseType.SQLLite
               && string.IsNullOrWhiteSpace(newApplication.ConnectionString))
            {
                throw new ApilaneException(AppErrors.ERROR, "This database provider requires a connection string");
            }

            if (newApplication.DatabaseType == (int)DatabaseType.SQLLite
               && !string.IsNullOrWhiteSpace(newApplication.ConnectionString))
            {
                throw new ApilaneException(AppErrors.ERROR, "This database provider does not require a connection string");
            }

            // Create the application directory
            DirectoryInfo rootAppDirectory = newApplication.Token.GetRootDirectoryInfo(_apiConfiguration.FilesPath);
            if (!rootAppDirectory.Exists)
            {
                rootAppDirectory.Create();
            }

            // Create the sqlite database or just confirm database exists (for SQL Server and MySql)
            switch (newApplication.DatabaseType)
            {
                case (int)DatabaseType.SQLLite:
                    var fileInfo = newApplication.Token.GetApplicationFileInfo(_apiConfiguration.FilesPath);

                    newApplication.ConnectionString = null;
                    if (fileInfo.Exists)
                    {
                        throw new ApilaneException(AppErrors.ERROR, "Database already exists!");
                    }

                    // Create the database file
                    SQLiteDataStorageRepository.GenerateDatabase(fileInfo.FullName);
                    break;
                case (int)DatabaseType.SQLServer:
                    SQLServerDataStorageRepository.ConfirmDatabaseExists(newApplication.GetConnectionstring(_apiConfiguration.FilesPath));
                    break;
                case (int)DatabaseType.MySQL:
                    MySQLDataStorageRepository.ConfirmDatabaseExists(newApplication.GetConnectionstring(_apiConfiguration.FilesPath));
                    break;
                default:
                    throw new NotImplementedException();
            }

            var dataStoreFactory = new ApplicationDataStoreFactory(_apiConfiguration.FilesPath, new Lazy<Task<DBWS_Application>>(Task.Run(() => newApplication)));

            var applicationBuilder = new ApplicationBuilderService(
                _apiConfiguration,
                _loggerFactory.CreateLogger<ApplicationBuilderService>(),
                dataStoreFactory);

            try
            {
                // Build app
                using (var scope = _transactionScopeService.OpenTransactionScope(timeout: TimeSpan.FromMinutes(10)))
                {
                    await applicationBuilder.BuildApplicationAsync(newApplication);

                    scope.Complete();
                }

                // Build helper
                await _applicationHelperService.EnsureHelperDatabaseAsync(newApplication.Token);

                return true;
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
            finally
            {
                await applicationBuilder.DisposeAsync();
            }
        }

        //public async Task ImportApplicationAsync(MemoryStream zipFileStream)
        //{
        //    DBWS_Application importedApplication = null;

        //    using (var zip = new ZipArchive(zipFileStream, ZipArchiveMode.Read))
        //    {
        //        Json file
        //        var appJsonFileEntry = zip.Entries.SingleOrDefault(x => x.Name.Equals("application.json"));

        //        if (appJsonFileEntry is null)
        //            throw new Exception("Application json file not found to import");

        //        using (var stream = appJsonFileEntry.Open())
        //        using (var reader = new StreamReader(stream))
        //            importedApplication = JsonSerializer.Deserialize<DBWS_Application>(reader.ReadToEnd());

        //        Create the application directory
        //        DirectoryInfo rootAppDirectory = importedApplication.GetRootDirectoryInfo(_apiConfiguration.FilesPath);
        //        if (rootAppDirectory.Exists == false)
        //            rootAppDirectory.Create();

        //        Extractor all to directory
        //        zip.ExtractToDirectory(rootAppDirectory.FullName);

        //        Db file
        //        var appJsonDbEntry = zip.Entries.SingleOrDefault(x => x.Name.Equals($"{importedApplication.Token}.db"));

        //        if (appJsonDbEntry is null)
        //            throw new Exception("Application db file not found to import");
        //    }

        //    if (importedApplication is null)
        //    {
        //        throw new Exception("Application not found to import");
        //    }

        //    Finally...
        //    var appService = new ApplicationServiceCachedForNewApp(_loggerService, _apiConfiguration, _queryDataService, _configuration, _clientFactory, _cacheService, importedApplication);

        //    var _applicationBuilder = new ApplicationBuilderService(
        //        _apiConfiguration,
        //        _loggerService,
        //        new ApplicationDataStoreFactory(_apiConfiguration, appService, _loggerService),
        //        appService,
        //        _configuration);

        //    try
        //    {
        //        await _applicationBuilder.BuildApplication(importedApplication);
        //    }
        //    catch
        //    {
        //        throw;
        //    }
        //    finally
        //    {
        //    IMPORTANT: Dispose the context as this is used outside the default context from DI
        //        _applicationBuilder.Dispose();
        //    }
        //}
    }
}
