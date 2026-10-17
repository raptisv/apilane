using Apilane.Api.Component.Tests.Extensions;
using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common.Enums;
using CasinoService.ComponentTests.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// How the API server hosts the applications: the application every request of it shares, and the
    /// error body and headers its endpoints answer with.
    /// </summary>
    public class ApiHostTests : AppicationTestsBase
    {
        public ApiHostTests() : base(SuiteContext.Shared)
        {
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Export_Should_Not_Break_The_Next_Data_Call(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // The test host hands every request of the application the same instance, as the cache of a real server does
            var databaseType = TestApplication.DatabaseType;
            var applicationConnectionString = TestApplication.ConnectionString;
            var adminEmail = TestApplication.AdminEmail;

            using (new WithApplicationOwnerAccess(TestApplication.Token, PortalInfoServiceMock))
            {
                var export = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/Application/Export?appToken={TestApplication.Token}");
                Assert.Equal(HttpStatusCode.OK, export.StatusCode);

                // The export hides these in what it writes, not in the application the requests use
                Assert.Equal(databaseType, TestApplication.DatabaseType);
                Assert.Equal(applicationConnectionString, TestApplication.ConnectionString);
                Assert.Equal(adminEmail, TestApplication.AdminEmail);

                var data = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/Data/Get?appToken={TestApplication.Token}&entity=Users");
                Assert.Equal(HttpStatusCode.OK, data.StatusCode);
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task CountDataAndHistory_Unauthorized_Should_Answer_The_Standard_Error_Body(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Not the owner (the Portal says no), and no rule lets anyone else read Users
            var ownerOnly = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/Stats/CountDataAndHistory?appToken={TestApplication.Token}&entity=Users");
            var data = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/Data/Get?appToken={TestApplication.Token}&entity=Users");

            Assert.Equal(HttpStatusCode.Unauthorized, ownerOnly.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, data.StatusCode);

            var ownerOnlyBody = JsonSerializer.Deserialize<JsonElement>(await ownerOnly.Content.ReadAsStringAsync());
            var dataBody = JsonSerializer.Deserialize<JsonElement>(await data.Content.ReadAsStringAsync());

            Assert.Equal("UNAUTHORIZED", ownerOnlyBody.GetProperty("Code").GetString());
            Assert.Equal("Unauthorized", ownerOnlyBody.GetProperty("Message").GetString());

            // The same body as every other endpoint: Code, Message, Entity and Property
            Assert.Equal(
                dataBody.EnumerateObject().Select(x => x.Name).OrderBy(x => x),
                ownerOnlyBody.EnumerateObject().Select(x => x.Name).OrderBy(x => x));
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Download_Error_Should_Not_Be_Cached(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var response = await HttpClient.RequestAsync(HttpMethod.Get, $"/api/Files/Download?appToken={TestApplication.Token}&fileID=999999");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.CacheControl);
        }
    }
}
