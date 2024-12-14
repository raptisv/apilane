using Apilane.Api.Abstractions;
using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Api.Configuration;
using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.Dto;
using Apilane.Common.Utilities;
using Apilane.Net;
using Apilane.Net.Request;
using Apilane.Net.Services;
using CasinoService.ComponentTests.Infrastructure;
using FakeItEasy;
using Orleans;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Apilane.Api.Component.Tests
{
    public abstract class AppicationTestsBase
    {
        private string _appToken = "11111111-1111-1111-1111-111111111111";
        protected string DiffEntityName = "Company";
        protected DBWS_Application TestApplication = null!;

        protected readonly HttpClient HttpClient;
        protected readonly IClusterClient ClusterClient;
        protected ApiConfiguration ApiConfiguration;
        protected ApilaneService ApilaneService;
        protected readonly IPortalInfoService PortalInfoServiceMock;
        protected readonly IApplicationService ApplicationServiceMock;

        public AppicationTestsBase(SuiteContext suiteContext)
        {
            HttpClient = suiteContext.HttpClient;
            ClusterClient = suiteContext.Fixture.ClusterClient;
            PortalInfoServiceMock = suiteContext.Fixture.MockIPortalInfoService;
            ApplicationServiceMock = suiteContext.Fixture.MockIApplicationService;
            ApiConfiguration = suiteContext.Fixture.ApiConfiguration;

            // Set the apilane service
            ApilaneService = new ApilaneService(HttpClient, new ApilaneConfiguration()
            {
                ApplicationApiUrl = suiteContext.Fixture.ApiConfiguration.Url,
                ApplicationToken = _appToken
            });
        }

        protected class RateLimitConfigurationTestData : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                yield return new object[] { 1, EndpointRateLimit.None, };
                yield return new object[] { 2, EndpointRateLimit.None, };
                yield return new object[] { 1, EndpointRateLimit.Per_Second, };
                yield return new object[] { 2, EndpointRateLimit.Per_Second, };
                yield return new object[] { 1, EndpointRateLimit.Per_Minute, };
                yield return new object[] { 2, EndpointRateLimit.Per_Minute, };
                yield return new object[] { 1, EndpointRateLimit.Per_Hour, };
                yield return new object[] { 2, EndpointRateLimit.Per_Hour, };
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        protected class StorageConfigurationTestData : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                yield return new object[] { DatabaseType.SQLLite, (string?)null!, false };
                yield return new object[] { DatabaseType.SQLLite, (string?)null!, true };
                yield return new object[] { DatabaseType.SQLServer, "Server=localhost,1433;Database=TestApp;User Id=sa;Password=12345678;TrustServerCertificate=true;", false };
                yield return new object[] { DatabaseType.SQLServer, "Server=localhost,1433;Database=TestApp;User Id=sa;Password=12345678;TrustServerCertificate=true;", true };
                yield return new object[] { DatabaseType.MySQL, "Server=127.0.0.1;Port=3306;Uid=root;Pwd=12345678;Database=testapp;UseXaTransactions=false;", false };
                yield return new object[] { DatabaseType.MySQL, "Server=127.0.0.1;Port=3306;Uid=root;Pwd=12345678;Database=testapp;UseXaTransactions=false;", true };
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        protected async Task AddEntityAsync(
            string entityName,
            bool hasDifferentiationProperty = false,
            bool requireChangeTracking = false)
        {
            var newEntity = new DBWS_Entity()
            {
                Name = entityName,
                HasDifferentiationProperty = hasDifferentiationProperty,
                RequireChangeTracking = requireChangeTracking,
                // Fixed
                ID = 0,
                EntDefaultOrder = null,
                Properties = new List<DBWS_EntityProperty>(),
                EntConstraints = null,
                IsReadOnly = false,
                AppID = TestApplication.ID,
                Application = TestApplication,
                IsSystem = false,
                Description = null,
                DateModified = DateTime.UtcNow,
            };

            using (new WithApplicationOwnerAccess(_appToken, PortalInfoServiceMock))
            {
                var httpResponse = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/Application/GetSystemPropertiesAndConstraints?appToken={TestApplication.Token}&entityHasDifferentiationProperty={hasDifferentiationProperty}");

                var strReponse = await httpResponse.Content.ReadAsStringAsync();

                var initialPropertiesAndConstraints = JsonSerializer.Deserialize<EntityPropertiesConstrainsDto>(strReponse)
                            ?? throw new Exception($"Invalid response from Api server | Json response '{strReponse}'");

                newEntity.Properties = initialPropertiesAndConstraints.Properties;
                newEntity.EntConstraints = JsonSerializer.Serialize(initialPropertiesAndConstraints.Constraints);
                newEntity.IsReadOnly = false;
                newEntity.IsSystem = false;
                newEntity.AppID = TestApplication.ID;
                newEntity.ID = 0;
                newEntity.Properties.ForEach(p => p.ID = 0);

                // Create the property 
                var responseMessage = await HttpClient.RequestAsync(HttpMethod.Post, $"/api/Application/GenerateEntity?appToken={TestApplication.Token}", newEntity);

                if (!responseMessage.IsSuccessStatusCode)
                {
                    throw new Exception(await responseMessage.Content.ReadAsStringAsync());
                }
            }

            TestApplication.Entities.Add(newEntity);

            MockApplicationService(TestApplication);
        }

        protected async Task AddBooleanPropertyAsync(
            string entityName,
            string propertyName,
            bool required = false)
        {
            var property = new DBWS_EntityProperty()
            {
                TypeID = (int)PropertyType.Boolean,
                Name = propertyName,
                Encrypted = false,
                Minimum = null,
                Maximum = null,
                Required = required,
                ValidationRegex = null,
                DecimalPlaces = 0,
                // Fixed
                ID = 0,
                Entity = null,
                IsPrimaryKey = false,
                IsSystem = false,
                EntityID = 0,
                Description = null,
                DateModified = DateTime.UtcNow,
            };

            await AddPropertyInnerAsync(entityName, property);
        }

        protected async Task AddDatePropertyAsync(
            string entityName,
            string propertyName,
            bool required = false)
        {
            var property = new DBWS_EntityProperty()
            {
                TypeID = (int)PropertyType.Date,
                Name = propertyName,
                Encrypted = false,
                Minimum = null,
                Maximum = null,
                Required = required,
                ValidationRegex = null,
                DecimalPlaces = 0,
                // Fixed
                ID = 0,
                Entity = null,
                IsPrimaryKey = false,
                IsSystem = false,
                EntityID = 0,
                Description = null,
                DateModified = DateTime.UtcNow,
            };

            await AddPropertyInnerAsync(entityName, property);
        }

        protected async Task AddNumberPropertyAsync(
            string entityName,
            string propertyName,
            bool required = false,
            long? minimum = null,
            long? maximum = null,
            int decimalPlaces = 0)
        {
            var property = new DBWS_EntityProperty()
            {
                TypeID = (int)PropertyType.Number,
                Name = propertyName,
                Encrypted = false,
                Minimum = minimum,
                Maximum = maximum,
                Required = required,
                ValidationRegex = null,
                DecimalPlaces = decimalPlaces,
                // Fixed
                ID = 0,
                Entity = null,
                IsPrimaryKey = false,
                IsSystem = false,
                EntityID = 0,
                Description = null,
                DateModified = DateTime.UtcNow,
            };

            await AddPropertyInnerAsync(entityName, property);
        }

        protected async Task AddStringPropertyAsync(
            string entityName,
            string propertyName,
            bool required = false,
            bool encrypted = false,
            long? minimum = null,
            long? maximum = null,
            string? validationRegex = null)
        {
            var property = new DBWS_EntityProperty()
            {
                TypeID = (int)PropertyType.String,
                Name = propertyName,
                Encrypted = encrypted,
                Minimum = minimum,
                Maximum = maximum,
                Required = required,
                ValidationRegex = validationRegex,
                DecimalPlaces = 0,
                // Fixed
                ID = 0,
                Entity = null,
                IsPrimaryKey = false,
                IsSystem = false,
                EntityID = 0,
                Description = null,
                DateModified = DateTime.UtcNow,
            };

            await AddPropertyInnerAsync(entityName, property);
        }

        protected async Task SetUserRolesAsync(long userId, List<string> roles, DatabaseType dbType)
        {
            var query = dbType switch
            {
                 DatabaseType.MySQL => $"UPDATE `{nameof(Users)}` SET `{nameof(Users.Roles)}` = '{string.Join(",", roles)}' WHERE `ID` = {userId}",
                 _ => $"UPDATE [{nameof(Users)}] SET [{nameof(Users.Roles)}] = '{string.Join(",", roles)}' WHERE [ID] = {userId}"
            };

            AddCustomEndpoint("AddUserToRole", query);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "AddUserToRole",
                    type: SecurityTypes.CustomEndpoint))
            {
                await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New("AddUserToRole"));
            }
        }

        protected void AddCustomEndpoint(
            string endpointName,
            string query)
        {
            var customEndpoint = new DBWS_CustomEndpoint()
            {
                Name = endpointName,
                AppID = TestApplication.ID,
                Application = TestApplication,
                Query = query,
                // Fixed
                ID = 0,
                Description = null,
                DateModified = DateTime.UtcNow,
            };

            TestApplication.CustomEndpoints ??= new List<DBWS_CustomEndpoint>();

            TestApplication.CustomEndpoints.Add(customEndpoint);

            MockApplicationService(TestApplication);
        }

        protected void AddSecurity(
            string entityOrEndpointName,
            string inRole = Globals.ANONYMOUS,
            SecurityTypes type = SecurityTypes.Entity,
            SecurityActionType actionType = SecurityActionType.get,
            EndpointRecordAuthorization recordAccess = EndpointRecordAuthorization.All,
            List<string>? properties = null)
        {
            var security = new DBWS_Security()
            {
                Name = entityOrEndpointName,
                RoleID = inRole,
                TypeID = (int)type,
                Action = actionType.ToString().ToLower(),
                Record = (int)recordAccess,
                Properties = properties is null ? null : string.Join(",", properties)
            };

            var currentSecurity = TestApplication.Security_List;
            currentSecurity.Add(security);
            TestApplication.Security = JsonSerializer.Serialize(currentSecurity);

            MockApplicationService(TestApplication);
        }

        protected void RemoveSecurity(
            string entityOrEndpointName,
            SecurityTypes type = SecurityTypes.Entity,
            SecurityActionType actionType = SecurityActionType.get)
        {
            var currentSecurity = TestApplication.Security_List;
            currentSecurity.RemoveAll(x => x.Name.Equals(entityOrEndpointName) && x.Action == actionType.ToString().ToLower() && x.TypeID == (int)type);
            TestApplication.Security = JsonSerializer.Serialize(currentSecurity);

            MockApplicationService(TestApplication);
        }

        private async Task AddPropertyInnerAsync(
            string entityName,
            DBWS_EntityProperty property)
        {
            var entity = TestApplication.Entities.Single(x => x.Name.Equals(entityName));

            // Set the entity id
            property.EntityID = entity.ID;

            using (new WithApplicationOwnerAccess(_appToken, PortalInfoServiceMock))
            {
                // Create the property 
                await HttpClient.RequestAsync(HttpMethod.Post, $"/api/Application/GenerateProperty?appToken={TestApplication.Token}&Entity={entityName}", property);
            }

            TestApplication.Entities.Single(x => x.Name.Equals(entityName)).Properties.Add(property);

            MockApplicationService(TestApplication);
        }

        private async Task<DBWS_Application> GetInitialApplicationAsync(
            DatabaseType databaseType,
            string? connectionString,
            bool useDiffProperty)
        {
            var useDiffEntity = useDiffProperty ? DiffEntityName : null;

            // Get the system entities
            var apiResponse = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/ApplicationNew/GetSystemEntities?differentiationEntity={useDiffEntity}");
            var strReponse = await apiResponse.Content.ReadAsStringAsync();
            var initialEntities = JsonSerializer.Deserialize<List<DBWS_Entity>>(strReponse)!;

            return new DBWS_Application()
            {
                // Basic data
                AdminEmail = "test@test.com",
                Entities = initialEntities,
                Name = "test_app",
                Token = _appToken,
                DatabaseType = (int)databaseType,
                EncryptionKey = "12345678".Encrypt(Globals.EncryptionKey),
                ConnectionString = connectionString,
                ServerID = 1,
                DifferentiationEntity = useDiffEntity,
                // Rest of default info
                UserID = Guid.NewGuid().ToString("N"),
                Online = true,
                AllowLoginUnconfirmedEmail = true,
                AllowUserRegister = true,
                AuthTokenExpireMinutes = 60,
                MaxAllowedFileSizeInKB = 100,
                Server = new DBWS_Server()
                {
                    ID = 1,
                    ServerUrl = "test",
                    Name = "test",
                }
            };
        }

        protected async Task<DBWS_Application> InitializeApplicationAsync(
            DatabaseType databaseType,
            string? connectionString,
            bool useDiffEntity)
        {
            using (new WithApplicationOwnerAccess(_appToken, PortalInfoServiceMock))
            {
                TestApplication = await GetInitialApplicationAsync(databaseType, connectionString, useDiffEntity);

                MockApplicationService(TestApplication);

                // Try drop application (if exists)
                var apiDegenerateResponse = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/Application/Degenerate?appToken={TestApplication.Token}");

                if (!apiDegenerateResponse.IsSuccessStatusCode)
                {
                    throw new Exception(await apiDegenerateResponse.Content.ReadAsStringAsync());
                }

                // Create the application
                var apiGenerateResponse = await HttpClient.RequestAsync<bool>(HttpMethod.Post, $"/api/ApplicationNew/Generate?installationKey={ApiConfiguration.InstallationKey}", TestApplication);
                
                if (!apiGenerateResponse)
                {
                    throw new Exception("Application not created succesfully");
                }

                MockApplicationService(TestApplication);

                return TestApplication;
            }
        }

        private void MockApplicationService(DBWS_Application application)
        {
            A.CallTo(() => ApplicationServiceMock.GetAsync(application.Token))
                    .Returns(application);

            A.CallTo(() => ApplicationServiceMock.GetDbInfoAsync(application.Token))
                .Returns(application.ToDbInfo(ApiConfiguration.FilesPath));
        }
    }
}