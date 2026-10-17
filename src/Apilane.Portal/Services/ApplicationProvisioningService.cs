using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
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

        // A name in a system constraint: a property, an entity or an on-delete action (a name or a number).
        private const string SystemConstraintPartPattern = "^[a-zA-Z0-9_]+$";

        // The entities the API server gives every application (Modules.NewApplicationSystemEntities).
        private static readonly string[] SystemEntityNames = { "Users", "AuthTokens", "Files" };

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

            // Before anything is created, in the order of the file.
            CheckFile(application);

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

        /// <summary>
        /// What the Portal's own editors refuse, a file cannot bring in: a setting outside the range
        /// of its page, a client address the Security page refuses, a name or a property type that
        /// POST entities, properties or custom endpoints refuses, a constraint the schema import
        /// refuses and a rule that PUT security/rules refuses (<see cref="SecurityRuleChecks"/>).
        /// The first problem refuses the whole file, with its place in the file in the message.
        /// The flags no editor sets are not taken from the file: an entity is a system one when it
        /// is one every application has, and in any other entity the properties the API server adds
        /// are the system ones, ID the primary key. What the editors store in one form is stored in
        /// that form: the client addresses, the custom endpoint names and the rules. A rule of an item
        /// the file does not have, and a property of a rule that its entity does not have, are left
        /// out, as the Security page leaves them out and drops them on its next save.
        /// </summary>
        private static void CheckFile(DBWS_Application application)
        {
            CheckSettings(application);

            for (var i = 0; i < application.Entities.Count; i++)
            {
                CheckEntity(application, application.Entities[i], $"{nameof(DBWS_Application.Entities)}[{i}]");
            }

            CheckCustomEndpoints(application);

            // Last: the rules are checked against the entities and custom endpoints as they are stored.
            application.Security = CheckSecurity(application);
        }

        // The limits of POST applications and of the settings, security and e-mail pages.
        private static void CheckSettings(DBWS_Application application)
        {
            if (application.Name.Length is < 4 or > 100)
            {
                throw Refused(nameof(DBWS_Application.Name), "Must be 4 to 100 characters");
            }

            // Letters and underscore, as the entity it names; at most 40, as POST applications allows.
            if (!string.IsNullOrEmpty(application.DifferentiationEntity)
                && (application.DifferentiationEntity.Length > 40 || !SchemaImportService.IsMatch(application.DifferentiationEntity, EntityNameRules.Pattern)))
            {
                throw Refused(nameof(DBWS_Application.DifferentiationEntity), "Allowed characters are a-z, A-Z and _, at most 40 characters");
            }

            if (application.AuthTokenExpireMinutes < 1)
            {
                throw Refused(nameof(DBWS_Application.AuthTokenExpireMinutes), "Must be 1 or more");
            }

            if (application.MaxAllowedFileSizeInKB is < 1 or > 25600)
            {
                throw Refused(nameof(DBWS_Application.MaxAllowedFileSizeInKB), "Must be between 1 and 25600 (1 KB to 25 MB)");
            }

            if (!Enum.IsDefined(typeof(AppClientIPsLogics), application.ClientIPsLogic))
            {
                throw Refused(nameof(DBWS_Application.ClientIPsLogic), "Must be 0 (Block) or 1 (Allow)");
            }

            if (application.MailServerPort is < 1 or > 65535)
            {
                throw Refused(nameof(DBWS_Application.MailServerPort), "Must be between 1 and 65535");
            }

            if (application.EmailConfirmationRedirectUrl?.Length > 10000)
            {
                throw Refused(nameof(DBWS_Application.EmailConfirmationRedirectUrl), "Must be 10000 characters or fewer");
            }

            if (application.ClientIPsValue is null)
            {
                return;
            }

            // As the Security page stores them: each entry once, in the text the API server matches.
            var addresses = new List<string>();

            foreach (var entry in application.ClientIPsValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ClientIpFilter.TryNormalize(entry, out var normalized, out var problem))
                {
                    throw Refused(nameof(DBWS_Application.ClientIPsValue), $"'{entry}' {problem}");
                }

                if (!addresses.Contains(normalized))
                {
                    addresses.Add(normalized);
                }
            }

            application.ClientIPsValue = string.Join(",", addresses);
        }

        private static void CheckEntity(DBWS_Application application, DBWS_Entity entity, string path)
        {
            var isSystem = IsSystemEntity(application, entity.Name);

            entity.IsSystem = isSystem;

            if (!isSystem)
            {
                // An entity of the application's own, as POST entities creates it.
                entity.IsReadOnly = false;

                ThrowIfRefused($"{path}.{nameof(DBWS_Entity.Name)}", entity.Name, SchemaImportService.EntityNameError(entity.Name));

                if (entity.HasDifferentiationProperty && string.IsNullOrWhiteSpace(application.DifferentiationEntity))
                {
                    throw Refused($"{path}.{nameof(DBWS_Entity.HasDifferentiationProperty)}", "The application has no differentiation entity");
                }
            }

            var systemProperties = SystemPropertyNames(application, entity);

            for (var j = 0; j < entity.Properties.Count; j++)
            {
                var property = entity.Properties[j];
                var propertyPath = $"{path}.{nameof(DBWS_Entity.Properties)}[{j}]";

                if (!isSystem)
                {
                    property.IsSystem = systemProperties.Contains(property.Name, StringComparer.Ordinal);
                    property.IsPrimaryKey = property.Name == Globals.PrimaryKeyColumn;
                }

                // The API server names the system properties (ID, UID): letters and underscore, whatever their length.
                var nameError = property.IsSystem
                    ? (SchemaImportService.IsMatch(property.Name, PropertyRules.Pattern) ? null : PropertyRules.PatternMessage)
                    : SchemaImportService.PropertyNameError(property.Name);

                ThrowIfRefused($"{propertyPath}.{nameof(DBWS_EntityProperty.Name)}", property.Name, nameError);

                if (!Enum.IsDefined(typeof(PropertyType), property.TypeID))
                {
                    throw Refused($"{propertyPath}.{nameof(DBWS_EntityProperty.TypeID)}", SchemaImportService.PropertyTypeMessage);
                }
            }

            // Read as the API server reads them: a text that is not a list of constraints is none.
            var constraints = entity.Constraints;

            for (var k = 0; k < constraints.Count; k++)
            {
                var constraintPath = $"{path}.{nameof(DBWS_Entity.EntConstraints)}[{k}]";
                var constraint = constraints[k];

                if (constraint is null)
                {
                    throw Refused(constraintPath, "Required");
                }

                if (!Enum.IsDefined(typeof(ConstraintType), constraint.TypeID))
                {
                    throw Refused($"{constraintPath}.{nameof(EntityConstraint.TypeID)}", "Must be 1 (Unique) or 2 (ForeignKey)");
                }

                // A system constraint is one of the API server's own (a unique e-mail, the differentiation
                // key): names only. Any other must be one the schema import accepts.
                var error = constraint.IsSystem
                    ? ((constraint.Properties ?? string.Empty).Split(',', StringSplitOptions.TrimEntries).All(x => SchemaImportService.IsMatch(x, SystemConstraintPartPattern))
                        ? null
                        : "A system constraint can name only properties, entities and an on-delete action")
                    : SchemaImportConstraintChecks.ValidateAndCanonicalize(application, entity, new SchemaImportConstraint { TypeID = constraint.TypeID, Properties = constraint.Properties });

                if (error is not null)
                {
                    throw Refused($"{constraintPath}.{nameof(EntityConstraint.Properties)}", error);
                }
            }
        }

        // As POST custom-endpoints stores them: without the spaces around, unique whatever the letter case.
        private static void CheckCustomEndpoints(DBWS_Application application)
        {
            var customEndpoints = application.CustomEndpoints ?? new List<DBWS_CustomEndpoint>();

            for (var i = 0; i < customEndpoints.Count; i++)
            {
                var path = $"{nameof(DBWS_Application.CustomEndpoints)}[{i}].{nameof(DBWS_CustomEndpoint.Name)}";
                var name = Utils.GetString(customEndpoints[i].Name);

                if (!SchemaImportService.IsMatch(name, CustomEndpointNameRules.Pattern))
                {
                    throw Refused($"{path} '{name}'", CustomEndpointNameRules.PatternMessage);
                }

                var first = customEndpoints.FindIndex(0, i, x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

                if (first >= 0)
                {
                    throw Refused($"{path} '{name}'", $"Same name as {nameof(DBWS_Application.CustomEndpoints)}[{first}]");
                }

                customEndpoints[i].Name = name;
            }
        }

        /// <summary>
        /// The rules as PUT security/rules stores them, in the order of the file. A rule must pass
        /// what that endpoint checks; a rule for an entity or a custom endpoint the file does not
        /// have, spelled as the item is, is left out, and so is a property its entity does not have.
        /// A rate limit of the window None is no rate limit, as the Security page reads it.
        /// </summary>
        private static string? CheckSecurity(DBWS_Application application)
        {
            if (string.IsNullOrWhiteSpace(application.Security))
            {
                return application.Security;
            }

            List<DBWS_Security?>? rules;

            try
            {
                rules = JsonSerializer.Deserialize<List<DBWS_Security?>>(application.Security);
            }
            catch (JsonException)
            {
                throw Refused(nameof(DBWS_Application.Security), "The rules cannot be read");
            }

            if (rules is null)
            {
                throw Refused(nameof(DBWS_Application.Security), "The rules cannot be read");
            }

            var stored = new List<DBWS_Security>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < rules.Count; i++)
            {
                var path = $"{nameof(DBWS_Application.Security)}[{i}]";
                var rule = rules[i] ?? throw Refused(path, "Required");

                if (!Enum.IsDefined(typeof(SecurityTypes), rule.TypeID))
                {
                    throw Refused($"{path}.{nameof(DBWS_Security.TypeID)}", "Must be 0 (Entity), 1 (CustomEndpoint) or 2 (Schema)");
                }

                var type = (SecurityTypes)rule.TypeID;
                var name = rule.Name ?? string.Empty;
                var roleId = rule.RoleID ?? string.Empty;
                var action = (rule.Action ?? string.Empty).ToLowerInvariant();

                if ((type == SecurityTypes.Entity && SecurityRuleChecks.FindEntity(application, name, StringComparison.Ordinal) is null)
                    || (type == SecurityTypes.CustomEndpoint && !SecurityRuleChecks.HasCustomEndpoint(application, name, StringComparison.Ordinal)))
                {
                    continue;
                }

                ThrowIfRefused(SecurityRuleChecks.CheckItem(application, type, name, roleId, action, path, StringComparison.Ordinal, out var entity));

                if (!Enum.IsDefined(typeof(EndpointRecordAuthorization), rule.Record))
                {
                    throw Refused($"{path}.{nameof(DBWS_Security.Record)}", "Must be 0 (All) or 1 (Owned)");
                }

                var properties = rule.GetProperties()
                    .Where(x => entity is null || entity.Properties.Any(p => p.Name == x))
                    .ToList();

                ThrowIfRefused(SecurityRuleChecks.CheckProperties(application, entity, action, properties, path));

                DBWS_Security.RateLimitItem? rateLimit = null;

                if (rule.RateLimit is not null && rule.RateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    var rateLimitPath = $"{path}.{nameof(DBWS_Security.RateLimit)}";

                    if (!Enum.IsDefined(typeof(EndpointRateLimit), rule.RateLimit.TimeWindowType))
                    {
                        throw Refused($"{rateLimitPath}.{nameof(DBWS_Security.RateLimitItem.TimeWindowType)}", "Must be 1 (Per_Second), 2 (Per_Minute) or 3 (Per_Hour)");
                    }

                    if (rule.RateLimit.MaxRequests < 1)
                    {
                        throw Refused($"{rateLimitPath}.{nameof(DBWS_Security.RateLimitItem.MaxRequests)}", "Must be 1 or more");
                    }

                    rateLimit = DBWS_Security.RateLimitItem.New(rule.RateLimit.MaxRequests, (EndpointRateLimit)rule.RateLimit.TimeWindowType);
                }

                var key = $"{rule.TypeID}|{name}|{roleId}|{action}";

                if (seen.TryGetValue(key, out var first))
                {
                    throw Refused(path, $"Same type, name, role and action as {nameof(DBWS_Application.Security)}[{first}]");
                }

                seen.Add(key, i);

                stored.Add(new DBWS_Security
                {
                    Name = name,
                    TypeID = rule.TypeID,
                    RoleID = roleId,
                    Action = action,
                    Record = rule.Record,
                    // Entity rules store their list, empty included; other rules store none.
                    Properties = entity is null ? null : string.Join(",", properties),
                    RateLimit = rateLimit
                });
            }

            // DBWS_Security with the default serializer, as SecurityRulesService stores it.
            return JsonSerializer.Serialize(stored);
        }

        // The entities the API server gives every application, and its differentiation entity.
        private static bool IsSystemEntity(DBWS_Application application, string name)
        {
            return SystemEntityNames.Contains(name, StringComparer.Ordinal)
                || (!string.IsNullOrEmpty(application.DifferentiationEntity) && string.Equals(name, application.DifferentiationEntity, StringComparison.Ordinal));
        }

        // The properties the API server gives an entity of the application's own.
        private static List<string> SystemPropertyNames(DBWS_Application application, DBWS_Entity entity)
        {
            var names = new List<string> { Globals.PrimaryKeyColumn, Globals.OwnerColumn, Globals.CreatedColumn };

            if (entity.HasDifferentiationProperty && !string.IsNullOrWhiteSpace(application.DifferentiationEntity))
            {
                names.Add(application.DifferentiationEntity.GetDifferentiationPropertyName());
            }

            return names;
        }

        private static void ThrowIfRefused(string path, string name, string? message)
        {
            if (message is not null)
            {
                throw Refused($"{path} '{name}'", message);
            }
        }

        private static void ThrowIfRefused(ErrorDetail? error)
        {
            if (error is not null)
            {
                throw Refused(error.Property, error.Message);
            }
        }

        // A file an editor would refuse: where in the file, and what is wrong there.
        private static PortalException Refused(string path, string message)
        {
            return FileNotValid($"{path}: {message}");
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
