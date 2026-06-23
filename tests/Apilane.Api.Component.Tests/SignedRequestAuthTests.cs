using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class SignedRequestAuthTests : AppicationTestsBase
    {
        public SignedRequestAuthTests(SuiteContext suiteContext) : base(suiteContext)
        {
        }

        private class CustomEntityLight : DataItem
        {
            public const string EntityName = "CustomEntityLight";
            public string Custom_String_Required { get; set; } = null!;
        }

        /// <summary>
        /// Creates the entity, grants the authenticated role full CRUD access, registers + logs in a
        /// user, and returns the signing credentials (key id + token) from the login response.
        /// </summary>
        private async Task<(long KeyId, string Token)> SetupAndLoginAsync(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityLight.EntityName);
            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: true);

            var props = new System.Collections.Generic.List<string> { nameof(CustomEntityLight.Custom_String_Required) };
            AddSecurity(CustomEntityLight.EntityName, Globals.AUTHENTICATED, actionType: SecurityActionType.post, properties: props);
            AddSecurity(CustomEntityLight.EntityName, Globals.AUTHENTICATED, actionType: SecurityActionType.put, properties: props);
            AddSecurity(CustomEntityLight.EntityName, Globals.AUTHENTICATED, actionType: SecurityActionType.get, properties: props);
            AddSecurity(CustomEntityLight.EntityName, Globals.AUTHENTICATED, actionType: SecurityActionType.delete);

            var email = "signed@test.com";
            var password = "password";

            var registerResult = await ApilaneService.AccountRegisterAsync(
                AccountRegisterRequest.New(new RegisterItem { Email = email, Username = email, Password = password }));
            registerResult.Match(r => r, e => throw new Exception($"Register failed | {e.Code} | {e.Message}"));

            var login = (await ApilaneService.AccountLoginAsync<ApiUser>(
                AccountLoginRequest.New(new LoginItem { Email = email, Password = password })))
                .Match(r => r, e => throw new Exception($"Login failed | {e.Code} | {e.Message}"));

            Assert.True(login.AuthTokenID > 0);
            Assert.False(string.IsNullOrWhiteSpace(login.AuthToken));

            return (login.AuthTokenID, login.AuthToken);
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task SignedRequests_Perform_Full_Crud_Via_Sdk(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            var (keyId, token) = await SetupAndLoginAsync(dbType, connectionString, useDiffEntity);

            // POST — signed (also validates body-hash parity between the SDK and the server)
            var postResult = await ApilaneService.PostDataAsync(
                DataPostRequest.New(CustomEntityLight.EntityName).WithSigning(keyId, token),
                new { Custom_String_Required = "v1" });
            var id = postResult.Match(r => r.Single(), e => throw new Exception($"Signed post failed | {e.Code} | {e.Message}"));
            Assert.True(id > 0);

            // PUT — signed
            var putResult = await ApilaneService.PutDataAsync(
                DataPutRequest.New(CustomEntityLight.EntityName).WithSigning(keyId, token),
                new { ID = id, Custom_String_Required = "v2" });
            Assert.Equal(1, putResult.Match(r => r, e => throw new Exception($"Signed put failed | {e.Code} | {e.Message}")));

            // GET — signed; the authenticated user sees the updated value
            var getResult = await ApilaneService.GetDataAsync<CustomEntityLight>(
                DataGetListRequest.New(CustomEntityLight.EntityName).WithSigning(keyId, token).WithPageSize(20));
            var data = getResult.Match(r => r.Data, e => throw new Exception($"Signed get failed | {e.Code} | {e.Message}"));
            Assert.Contains(data, x => x.Custom_String_Required == "v2");

            // DELETE — signed
            var deleteResult = await ApilaneService.DeleteDataAsync(
                DataDeleteRequest.New(CustomEntityLight.EntityName).AddIdToDelete(id).WithSigning(keyId, token));
            var deletedIds = deleteResult.Match(r => r, e => throw new Exception($"Signed delete failed | {e.Code} | {e.Message}"));
            Assert.Contains(id, deletedIds);
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task SignedRequest_With_Invalid_Credentials_Is_Unauthorized(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            var (keyId, token) = await SetupAndLoginAsync(dbType, connectionString, useDiffEntity);

            // Right key id but wrong token -> the recomputed signature won't match -> unauthorized
            var wrongToken = await ApilaneService.GetDataAsync<CustomEntityLight>(
                DataGetListRequest.New(CustomEntityLight.EntityName).WithSigning(keyId, Guid.NewGuid().ToString()));
            wrongToken.Match(
                response => throw new Exception("We should not be here"),
                error => Assert.Equal(ValidationError.UNAUTHORIZED, error.Code));

            // Unknown key id -> unauthorized
            var unknownKey = await ApilaneService.GetDataAsync<CustomEntityLight>(
                DataGetListRequest.New(CustomEntityLight.EntityName).WithSigning(999999999, token));
            unknownKey.Match(
                response => throw new Exception("We should not be here"),
                error => Assert.Equal(ValidationError.UNAUTHORIZED, error.Code));
        }

        [Fact]
        public async Task SignedFileUpload_Throws_With_Clear_Message()
        {
            // Signing a file upload is not supported and must fail fast on the client, before any
            // request is sent (this is a pure SDK guard, so it needs no application/database setup).
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ApilaneService.PostFileAsync(
                    FilePostRequest.New().WithFileName("test.txt").WithSigning(1, Guid.NewGuid().ToString()),
                    new byte[] { 1, 2, 3 }));

            Assert.Equal("Request signing is not supported for file uploads. Use WithAuthToken for PostFileAsync.", ex.Message);
        }
    }
}
