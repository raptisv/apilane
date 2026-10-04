using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Apilane.Portal.Abstractions;
using Apilane.Portal.Api;
using Apilane.Portal.Api.V1.Contracts;
using Apilane.Portal.Api.V1.Mapping;
using Apilane.Portal.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Portal.Services
{
    public class ApplicationProvisioningService : IApplicationProvisioningService
    {
        private const string EncryptionKeyCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private const int EncryptionKeyLength = 8;

        private readonly ApplicationDbContext _dbContext;
        private readonly IPortalAccessService _portalAccessService;
        private readonly IApplicationAccessService _applicationAccessService;
        private readonly IApiServerClient _apiServerClient;
        private readonly IApiServerCacheReset _apiServerCacheReset;
        private readonly ILogger<ApplicationProvisioningService> _logger;

        public ApplicationProvisioningService(
            ApplicationDbContext dbContext,
            IPortalAccessService portalAccessService,
            IApplicationAccessService applicationAccessService,
            IApiServerClient apiServerClient,
            IApiServerCacheReset apiServerCacheReset,
            ILogger<ApplicationProvisioningService> logger)
        {
            _dbContext = dbContext;
            _portalAccessService = portalAccessService;
            _applicationAccessService = applicationAccessService;
            _apiServerClient = apiServerClient;
            _apiServerCacheReset = apiServerCacheReset;
            _logger = logger;
        }

        public async Task<ApplicationResponse> CreateAsync(CreateApplicationRequest request)
        {
            var databaseType = ParseDatabaseType(request.DatabaseType);
            var connectionString = GetConnectionString(databaseType, request.ConnectionString);
            var differentiationEntity = Utils.GetString(request.DifferentiationEntity);

            var user = await _portalAccessService.GetCurrentUserAsync();
            var server = await FindServerAsync(request.ServerID);

            var entities = await _apiServerClient.GetSystemEntitiesAsync(server, differentiationEntity);

            // The API server numbers what it returns; the Portal database gives the real IDs.
            foreach (var entity in entities)
            {
                entity.ID = 0;
                entity.Properties.ForEach(x => x.ID = 0);
            }

            var application = new DBWS_Application
            {
                Token = Guid.NewGuid().ToString(),
                EncryptionKey = RandomNumberGenerator.GetString(EncryptionKeyCharacters, EncryptionKeyLength).Encrypt(Globals.EncryptionKey),
                Name = request.Name,
                Server = server,
                ServerID = server.ID,
                DatabaseType = (int)databaseType,
                ConnectionString = connectionString,
                DifferentiationEntity = differentiationEntity,
                UserID = user.Id,
                AdminEmail = user.Email,
                Online = true,
                AllowLoginUnconfirmedEmail = true,
                AllowUserRegister = true,
                AuthTokenExpireMinutes = 60,
                MaxAllowedFileSizeInKB = 100,
                Entities = entities,
                Reports = new List<DBWS_ReportPanel> { CreateDefaultReport() }
            };

            await GenerateAndSaveAsync(server, application);

            // Clears anything the API server cached about the token before the application existed.
            await _apiServerCacheReset.ResetAfterWriteAsync(application);

            return await ToResponseAsync(application, user);
        }

        public async Task<ApplicationResponse> ImportAsync(ImportApplicationRequest request)
        {
            var databaseType = ParseDatabaseType(request.DatabaseType);
            var connectionString = GetConnectionString(databaseType, request.ConnectionString);

            var application = await ReadApplicationAsync(request.File);

            // The token comes from the file. The API server finds an application by the value of this
            // GUID, however it is written, so it is stored in the one spelling the Portal issues and
            // compared by value: another spelling of an existing token would otherwise become a second
            // row, owned by the importer, for the same application.
            if (!Guid.TryParse(application.Token, out var token))
            {
                throw FileNotValid("Application token is not valid");
            }

            application.Token = token.ToString();

            var existingTokens = await _dbContext.Applications.Select(x => x.Token).ToListAsync();

            if (existingTokens.Any(x => Guid.TryParse(x, out var existing) && existing == token))
            {
                throw PortalException.Conflict($"Application token '{application.Token}' already exists", "Application");
            }

            var user = await _portalAccessService.GetCurrentUserAsync();
            var server = await FindServerAsync(request.ServerID);

            // Nothing of where the file came from is kept: IDs, owner, collaborators and server are
            // those of this import. Entities and properties are added in the order they were created.
            application.ID = 0;
            application.Entities = application.Entities.OrderBy(x => x.ID).ToList();

            foreach (var entity in application.Entities)
            {
                entity.ID = 0;
                entity.AppID = -1;
                entity.Properties = entity.Properties.OrderBy(x => x.ID).ToList();

                foreach (var property in entity.Properties)
                {
                    property.ID = 0;
                    property.EntityID = -1;
                }
            }

            foreach (var customEndpoint in application.CustomEndpoints ?? Enumerable.Empty<DBWS_CustomEndpoint>())
            {
                customEndpoint.ID = 0;
                customEndpoint.AppID = -1;
            }

            foreach (var report in application.Reports ?? Enumerable.Empty<DBWS_ReportPanel>())
            {
                report.ID = 0;
                report.AppID = -1;

                // An ID taken from the file could be the ID of a row that exists here.
                foreach (var series in report.Series ?? Enumerable.Empty<DBWS_ReportSeries>())
                {
                    series.ID = 0;
                    series.PanelID = 0;
                }
            }

            application.Collaborates = new List<DBWS_Collaborate>();
            application.Server = server;
            application.ServerID = server.ID;
            application.UserID = user.Id;
            application.AdminEmail = user.Email;
            application.DatabaseType = (int)databaseType;
            application.ConnectionString = connectionString;

            // The encryption key stays as it is in the file: data encrypted with it could not be read with another.

            await GenerateAndSaveAsync(server, application);

            return await ToResponseAsync(application, user);
        }

        /// <summary>
        /// The application is created on the API server first; the Portal row is saved only when
        /// that worked, so a refused connection string leaves nothing behind.
        /// </summary>
        private async Task GenerateAndSaveAsync(DBWS_Server server, DBWS_Application application)
        {
            _dbContext.Applications.Add(application);

            try
            {
                await _apiServerClient.GenerateAsync(server, application);
            }
            catch (PortalException exception) when (exception.Code == PortalErrorCode.Validation
                && application.ConnectionString is not null
                && string.IsNullOrWhiteSpace(exception.Property))
            {
                // Without a property of its own, a refusal at this point is most likely about the database it was told to use.
                throw PortalException.Validation(nameof(CreateApplicationRequest.ConnectionString), exception.Message);
            }

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException exception)
            {
                // The API server has the application now and the Portal does not. Nothing is undone
                // here; the message tells the caller what state things are in.
                _logger.LogError(exception, "Application was generated on the API server but not saved in the Portal | {Token}", application.Token);

                throw new PortalException(
                    StatusCodes.Status500InternalServerError,
                    PortalErrorCode.Error,
                    "The application was created on the API server but could not be saved in the Portal.");
            }
        }

        private async Task<ApplicationResponse> ToResponseAsync(DBWS_Application application, ApplicationUser user)
        {
            // Read back the way GET /applications reads it, so both answers are the same.
            var saved = await _applicationAccessService.GetApplicationAsync(application.Token);

            return saved.ToResponse(user.Id);
        }

        private async Task<DBWS_Server> FindServerAsync(long? id)
        {
            // [Required] on the contract already answers 400 for a missing value.
            var serverId = id ?? throw PortalException.Validation(nameof(CreateApplicationRequest.ServerID), "Required");

            return await _dbContext.Servers.FirstOrDefaultAsync(x => x.ID == serverId)
                ?? throw PortalException.NotFound("Server");
        }

        // Internal: a clone (ApplicationCloneService) takes the same two values under the same rules.
        internal static DatabaseType ParseDatabaseType(string value)
        {
            // By name only: Enum.Parse would also take "2" and "sqlserver".
            if (!Enum.GetNames<DatabaseType>().Contains(value))
            {
                throw PortalException.Validation(
                    nameof(CreateApplicationRequest.DatabaseType),
                    $"Must be one of: {string.Join(", ", Enum.GetNames<DatabaseType>())}");
            }

            return Enum.Parse<DatabaseType>(value);
        }

        /// <summary>
        /// SQLite databases are files the API server names itself, so a connection string is not kept for them.
        /// </summary>
        internal static string? GetConnectionString(DatabaseType databaseType, string? connectionString)
        {
            if (databaseType == DatabaseType.SQLLite)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw PortalException.Validation(nameof(CreateApplicationRequest.ConnectionString), "Required");
            }

            return connectionString;
        }

        private static async Task<DBWS_Application> ReadApplicationAsync(IFormFile? file)
        {
            if (file is null || file.Length == 0)
            {
                throw FileNotValid("No file found to upload");
            }

            DBWS_Application? application;

            try
            {
                using var stream = file.OpenReadStream();
                application = await JsonSerializer.DeserializeAsync<DBWS_Application>(stream);
            }
            catch (JsonException)
            {
                throw FileNotValid("The file is not the application.json of an exported application");
            }

            if (application is null)
            {
                throw FileNotValid("Application json cannot be null");
            }

            // A file that fails here is not an export. Later code relies on these values, and the
            // Portal database cannot store a row without them: the application would be created on
            // the API server and then not be saved here.
            if (string.IsNullOrWhiteSpace(application.Name)
                || string.IsNullOrWhiteSpace(application.EncryptionKey)
                || application.Entities is null
                || application.Entities.Any(x => !IsComplete(x))
                || application.CustomEndpoints?.Any(x => x is null || IsBlank(x.Name, x.Query)) == true
                || application.Reports?.Any(x => !IsComplete(x)) == true)
            {
                throw FileNotValid("The file is not the application.json of an exported application");
            }

            return application;
        }

        private static bool IsComplete(DBWS_Entity? entity)
        {
            return entity is not null
                && !IsBlank(entity.Name)
                && entity.Properties is not null
                && entity.Properties.All(x => x is not null && !IsBlank(x.Name));
        }

        private static bool IsComplete(DBWS_ReportPanel? report)
        {
            // A report without a Series list is fine: it has none.
            return report is not null
                && !IsBlank(report.Title)
                && report.Series?.Any(x => x is null || IsBlank(x.Label, x.Entity, x.GroupBy, x.Property)) != true;
        }

        private static bool IsBlank(params string?[] values)
        {
            return values.Any(string.IsNullOrWhiteSpace);
        }

        private static PortalException FileNotValid(string message)
        {
            return PortalException.Validation(nameof(ImportApplicationRequest.File), message);
        }

        private static DBWS_ReportPanel CreateDefaultReport()
        {
            return new DBWS_ReportPanel
            {
                ID = 0,
                AppID = -1,
                Title = "User registrations per day",
                DateModified = DateTime.UtcNow,
                MaxRecords = 1000,
                X = 0,
                Y = 0,
                W = 12,
                H = 4,
                TypeID = (int)ReportType.Line,
                Series = new List<DBWS_ReportSeries>
                {
                    new DBWS_ReportSeries
                    {
                        ID = 0,
                        Label = "Registrations",
                        Entity = "Users",
                        GroupBy = "Created.Year,Created.Month,Created.Day",
                        Property = $"ID.{AggregateData.DataAggregates.Count}",
                        Filter = null,
                        Order = 0,
                        DateModified = DateTime.UtcNow
                    }
                }
            };
        }
    }
}
