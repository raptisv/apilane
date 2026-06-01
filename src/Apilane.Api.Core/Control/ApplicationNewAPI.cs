using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Services;
using Apilane.Common.Abstractions;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Data.Repository;
using Apilane.Data.Repository.Factory;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class ApplicationNewAPI : IApplicationNewAPI
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<ApplicationService> _logger;
        private readonly ApiConfiguration _apiConfiguration;
        private readonly ITransactionScopeService _transactionScopeService;

        public ApplicationNewAPI(
            ApiConfiguration currentConfiguration,
            ILogger<ApplicationService> loggerService,
            ILoggerFactory loggerFactory,
            ITransactionScopeService transactionScopeService)
        {
            _logger = loggerService;
            _apiConfiguration = currentConfiguration;
            _loggerFactory = loggerFactory;
            _transactionScopeService = transactionScopeService;
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
                case (int)DatabaseType.PostgreSQL:
                    PostgreSQLDataStorageRepository.ConfirmDatabaseExists(newApplication.GetConnectionstring(_apiConfiguration.FilesPath));
                    break;
                default:
                    throw new NotImplementedException();
            }

            var dataStoreFactory = new ApplicationDataStoreFactory(newApplication.ToDbInfo(_apiConfiguration.FilesPath));

            var applicationBuilder = new ApplicationBuilderService(
                _apiConfiguration,
                _loggerFactory.CreateLogger<ApplicationBuilderService>(),
                dataStoreFactory);

            try
            {
                using (var scope = _transactionScopeService.OpenTransactionScope(timeout: TimeSpan.FromMinutes(10)))
                {
                    await applicationBuilder.BuildApplicationAsync(newApplication);
                    await applicationBuilder.EnsureSystemTablesAsync();

                    scope.Complete();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
            finally
            {
                await applicationBuilder.DisposeAsync();
            }
        }
    }
}
