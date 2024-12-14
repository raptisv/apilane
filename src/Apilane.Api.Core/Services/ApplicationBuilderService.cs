using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Utilities;
using Apilane.Data.Abstractions;
using Apilane.Data.Repository;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Services
{
    public class ApplicationBuilderService : IApplicationBuilderService
    {
        private readonly ApiConfiguration _apiConfiguration;
        private readonly ILogger<ApplicationBuilderService> _logger;
        private readonly IApplicationDataStoreFactory _applicationDataStoreFactory;

        public ApplicationBuilderService(
            ApiConfiguration currentConfiguration,
            ILogger<ApplicationBuilderService> logger,
            IApplicationDataStoreFactory applicationDataStoreFactory)
        {
            _logger = logger;
            _apiConfiguration = currentConfiguration;
            _applicationDataStoreFactory = applicationDataStoreFactory;
        }

        public async Task BuildApplicationAsync(DBWS_Application application)
        {
            // Order by referenced entities, to avoid missing entities during constraint creation.
            var groups = application.GroupEntitesByFKReferences();

            // All entities should be present in property "groups.Flat" with a level, depending on the FK chain.
            // An entity might be preset multiple times on the list due to many FK relationships, so it is important to take the maximum level of all occurrences.
            var entitiesOrderedByFKReferences = application.Entities
                .OrderBy(e => groups.Flat.Where(x => x.ID.Equals(e.Name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Level).DefaultIfEmpty(0).Max());

            foreach (var entity in entitiesOrderedByFKReferences)
            {
                await GenerateEntityAsync(
                    application,
                    entity);
            }
        }

        public async Task DropApplicationDataAsync(
            DBWS_Application application,
            DatabaseType databaseType,
            string connectionString)
        {
            // Close all connections

            await _applicationDataStoreFactory.DisposeAsync();

            // !IMPORTANT. Let garbage collector finish

            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Delete files

            var filesRootDirectoryInfo = application.Token.GetFilesRootDirectoryInfo(_apiConfiguration.FilesPath);
            if (filesRootDirectoryInfo.Exists)
            {
                filesRootDirectoryInfo.Delete(true);
            }

            switch (databaseType)
            {
                case DatabaseType.SQLLite:
                    {
                        var mainDbFile = application.Token.GetApplicationFileInfo(_apiConfiguration.FilesPath);
                        if (mainDbFile.Exists)
                        {
                            try
                            {
                                // Sometimes this one fails
                                mainDbFile.Delete();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error trying to delete db file '{mainDbFile.FullName}'");
                            }
                        }
                    }
                    break;
                case DatabaseType.SQLServer:
                    {
                        // Drop ALL tables

                        await using (var ctx = new SQLServerDataStorageRepository(connectionString))
                        {
                            int attempt = 0;
                            while (attempt <= 100)
                            {
                                attempt++;

                                try
                                {
                                    // Run it multiple times to confirm all tables are dropped, due to errors from foreign keys.
                                    await ctx.ExecNQAsync($"EXEC sp_MSforeachtable 'DROP TABLE ?'");

                                    // Success, we can exit now.
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Error executing 'sp_MSforeachtable' | Retrying... | {ex.Message}");
                                }
                            }
                        }
                    }
                    break;
                case DatabaseType.MySQL:
                    {
                        // Drop ALL tables

                        await using (var ctx = new MySQLDataStorageRepository(connectionString))
                        {
                            var builder = new MySqlConnectionStringBuilder(connectionString);

                            var tables = await ctx.ExecTableAsync(@$"SELECT table_name as 'table' 
                                                                         FROM information_schema.tables WHERE table_schema = '{builder.Database}';");
                            var cmd = $@"SET FOREIGN_KEY_CHECKS = 0;
                                        {string.Join("", tables.ToDictionary().Select(x => $"DROP TABLE IF EXISTS `{x["table"]}`;"))}
                                        SET FOREIGN_KEY_CHECKS = 1;";

                            await ctx.ExecNQAsync(cmd);
                        }
                    }
                    break;
                default: throw new NotImplementedException();
            }
        }

        public async Task RenameEntityAsync(
            DBWS_Application application,
            long entityID,
            string newName)
        {
            var entity = application.Entities.Single(x => x.ID == entityID);

            if (entity.IsSystem)
            {
                throw new Exception("Cannot rename this Entity");
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new Exception("Required");
            }

            if (application.Entities.Any(x => x.ID != entityID && x.Name.ToLower().Equals(newName.ToLower())))
            {
                throw new Exception($"Entity named '{newName}' already exists");
            }

            var context = new ValidationContext(newName, null, null);
            var results = new List<ValidationResult>();
            var attributes = typeof(DBWS_Entity)
                .GetProperty(nameof(DBWS_Entity.Name))!
                .GetCustomAttributes(false)
                .OfType<ValidationAttribute>()
                .ToArray();

            var validationErrors = new List<string?>();
            if (!Validator.TryValidateValue(newName, context, results, attributes))
            {
                foreach (var result in results)
                {
                    validationErrors.Add(result.ErrorMessage);
                }
            }

            if (validationErrors.Count > 0)
                throw new Exception(string.Join(",", validationErrors));

            // Finally, only if the user has changed the entity name
            if (!entity.Name.Equals(newName))
            {
                await _applicationDataStoreFactory.RenameTableAsync(entity.Name, newName);
            }
        }

        public async Task RenameEntityPropertyAsync(
            DBWS_Application application,
            long propertyID,
            string newName)
        {
            DBWS_EntityProperty property = null!;

            foreach (var ent in application.Entities)
            {
                foreach (var prop in ent.Properties)
                {
                    if (prop.ID == propertyID)
                    {
                        property = (DBWS_EntityProperty)prop.Clone();
                    }
                }
            }

            if (property.IsSystem)
            {
                throw new Exception("Cannot rename this Property");
            }

            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new Exception("Required");
            }

            DBWS_Entity entity = application.Entities.Single(x => x.ID == property.EntityID);

            if (entity.Properties.Any(y => y.ID != propertyID && y.Name.ToLower().Equals(newName.ToLower())))
            {
                throw new Exception($"Property named '{newName}' already exists");
            }

            var context = new ValidationContext(newName, null, null);
            var results = new List<ValidationResult>();
            var attributes = typeof(DBWS_EntityProperty)
                .GetProperty(nameof(DBWS_EntityProperty.Name))!
                .GetCustomAttributes(false)
                .OfType<ValidationAttribute>()
                .ToArray();

            var validationErrors = new List<string?>();
            if (!Validator.TryValidateValue(newName, context, results, attributes))
            {
                foreach (var result in results)
                {
                    validationErrors.Add(result.ErrorMessage);
                }
            }

            if (validationErrors.Count > 0)
            {
                throw new Exception(string.Join(",", validationErrors));
            }

            // Finally, only if the user has changed the entity name
            if (!property.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
            { 
                await _applicationDataStoreFactory.RenameColumnAsync(entity.Name, property.Name, newName);
            }
        }

        public async Task GenerateEntityAsync(
            DBWS_Application application,
            DBWS_Entity entity)
        {
            var tableExists = await _applicationDataStoreFactory.ExistsTableAsync(entity.Name);

            if (tableExists)
            {
                throw new ApilaneException(AppErrors.ERROR, entity: entity.Name, message: $"Entity {entity.Name} already exists");
            }

            // Create table
            await _applicationDataStoreFactory.CreateTableWithPrimaryKeyAsync(entity.Name);

            // Generate properties
            foreach (DBWS_EntityProperty property in entity.Properties)
            {
                await GeneratePropertyAsync(
                    (DatabaseType)application.DatabaseType,
                    entity.Name,
                    property);
            }

            // Generate contraints after having created the properties
            await GenerateConstraintsAsync(
                entity,
                entity.Constraints,
                Enumerable.Empty<EntityConstraint>().ToList());
        }

        public async Task GeneratePropertyAsync(
            DatabaseType databaseType,
            string entityName,
            DBWS_EntityProperty property)
        {
            if (!property.IsPrimaryKey) // The primary key is created on table creation
            {
                if (await _applicationDataStoreFactory.ExistsColumnAsync(entityName, property.Name))
                {
                    throw new ApilaneException(AppErrors.ERROR, property: property.Name, entity: entityName, message: $"Property {property.Name} already exists");
                }

                await _applicationDataStoreFactory.CreateColumnAsync(
                    entityName,
                    property.Name,
                    property.TypeID_Enum,
                    property.Required,
                    property.DecimalPlaces,
                    property.Maximum);
            }
        }

        public async Task DegenerateEntityAsync(
            DBWS_Application application,
            string entityName)
        {
            var entity = application.Entities.SingleOrDefault(x => x.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                ?? throw new Exception($"Not found entity '{entityName}'");

            if (entity.IsSystem)
            {
                throw new Exception($"Cannot delete entity '{entityName}'");
            }

            if (await _applicationDataStoreFactory.ExistsTableAsync(entityName))
            {
                await _applicationDataStoreFactory.DropTableAsync(entityName);
            }
        }

        public async Task DegeneratePropertyAsync(
            DBWS_Application application,
            long propertyId)
        {
            var property = application.Entities.SelectMany(e => e.Properties).Single(p => p.ID == propertyId);

            if (property.IsSystem)
            {
                throw new ApilaneException(AppErrors.ERROR, $"Cannot delete property '{property.Name}'");
            }

            var entity = application.Entities.Single(x => x.ID == property.EntityID);

            if (await _applicationDataStoreFactory.ExistsColumnAsync(entity.Name, property.Name))
            {
                await _applicationDataStoreFactory.DropColumnAsync(entity.Name, property.Name);
            }
        }

        public Task GenerateConstraintsAsync(
            DBWS_Entity entity,
            List<EntityConstraint> incomingConstraints,
            List<EntityConstraint> currentConstraints)
        {
            return _applicationDataStoreFactory.SetConstraintsAsync(
                entity.Name,
                entity.Properties.Select(x => (x.Name, x.IsPrimaryKey, x.TypeID_Enum, x.Required, x.DecimalPlaces)).ToList(),
                incomingConstraints,
                currentConstraints);
        }

        public async Task<List<long>> ImportDataAsync(
            DBWS_Application application,
            DBWS_Entity entity,
            List<Dictionary<string, object?>> data)
        {
            var result = new List<long>();

            // Important! Import endpoint accepts all values as they arrive.
            // This is the place to encrypt any encrypted properties.
            var encryptedProperties = entity.Properties
                .Where(x => x.TypeID_Enum == PropertyType.String && x.Encrypted)
                .Select(x => x.Name);

            foreach (var item in data)
            {
                foreach(var property in item)
                {
                    if (encryptedProperties.Contains(property.Key) &&
                        property.Value is not null)
                    {
                        var appEncryptionKey = application.EncryptionKey.Decrypt(Globals.EncryptionKey);
                        item[property.Key] = Encryptor.Encrypt(property.Value.ToString() ?? string.Empty, appEncryptionKey);
                    }
                }

                var response = await _applicationDataStoreFactory.CreateDataAsync(
                    entity.Name,
                    item,
                    allowInsertIdentity: true);

                if (response.HasValue)
                {
                    result.Add(response.Value);
                }
            }

            return result;
        }

        public ValueTask DisposeAsync() => _applicationDataStoreFactory.DisposeAsync();
    }
}
