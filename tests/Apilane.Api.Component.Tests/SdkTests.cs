using Apilane.Common.Enums;
using Apilane.Data.Utilities;
using Apilane.Net.Abstractions;
using Apilane.Net.Extensions;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Microsoft.Extensions.DependencyInjection;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    /// <summary>
    /// Behaviour of the .NET SDK itself against a real API host: signing, typed custom endpoint results,
    /// the stats sort direction, the exception of throw-on-error and the client of the DI registration.
    /// </summary>
    public class SdkTests : AppicationTestsBase
    {
        public SdkTests() : base(SuiteContext.Shared)
        {
        }

        private class SdkUser : ApiUser
        {
            public string? Nickname { get; set; }
        }

        private class SdkItem : DataItem
        {
            public const string EntityName = "SdkItem";
            public string? Name { get; set; }
            public long? Amount { get; set; }
        }

        private class AmountByName
        {
            public string? Name { get; set; }
            public decimal Amount_sum { get; set; }
        }

        private static string Quote(DatabaseType dbType, string identifier)
        {
            return SqlUtilis.QuoteIdentifier(identifier, dbType);
        }

        /// <summary>
        /// Creates SdkItem (Name, Amount) with anonymous post and get rules on both properties.
        /// </summary>
        private async Task AddItemEntityAsync()
        {
            await AddEntityAsync(SdkItem.EntityName);
            await AddStringPropertyAsync(SdkItem.EntityName, nameof(SdkItem.Name), required: true);
            await AddNumberPropertyAsync(SdkItem.EntityName, nameof(SdkItem.Amount));

            var properties = new List<string> { nameof(SdkItem.Name), nameof(SdkItem.Amount) };
            AddSecurity(SdkItem.EntityName, actionType: SecurityActionType.post, properties: properties);
            AddSecurity(SdkItem.EntityName, actionType: SecurityActionType.get, properties: properties);
        }

        private async Task PostItemAsync(string name, long amount)
        {
            var postResult = await ApilaneService.PostDataAsync(
                DataPostRequest.New(SdkItem.EntityName),
                new { Name = name, Amount = amount });

            postResult.Match(r => r.Single(), e => throw new Exception($"Post failed | {e.Code} | {e.Message}"));
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Signed_AccountUpdate_Should_Update_The_User(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);
            await AddStringPropertyAsync("Users", nameof(SdkUser.Nickname));

            // One login name per case: sign-in attempts are limited per login name
            var email = $"update{(int)dbType}{(useDiffEntity ? "d" : string.Empty)}@test.com";
            var password = "password";

            var registerResult = await ApilaneService.AccountRegisterAsync(
                AccountRegisterRequest.New(new RegisterItem { Email = email, Username = email, Password = password }));
            registerResult.Match(r => r, e => throw new Exception($"Register failed | {e.Code} | {e.Message}"));

            var login = (await ApilaneService.AccountLoginAsync<SdkUser>(
                AccountLoginRequest.New(new LoginItem { Email = email, Password = password })))
                .Match(r => r, e => throw new Exception($"Login failed | {e.Code} | {e.Message}"));

            // The signature covers the hash of the body, so the body must be in place before the request is signed
            var updateResult = await ApilaneService.AccountUpdateAsync<SdkUser>(
                AccountUpdateRequest.New().WithSigning(login.AuthTokenID, login.AuthToken),
                new { Nickname = "Ann" });

            var updated = updateResult.Match(r => r, e => throw new Exception($"Signed update failed | {e.Code} | {e.Message}"));
            Assert.Equal("Ann", updated.Nickname);

            var userData = await ApilaneService.GetAccountUserDataAsync<SdkUser>(
                AccountUserDataRequest.New().WithSigning(login.AuthTokenID, login.AuthToken));

            Assert.Equal("Ann", userData.Match(r => r.User, e => throw new Exception($"Signed user data failed | {e.Code} | {e.Message}")).Nickname);
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Typed_CustomEndpoint_With_Fewer_Result_Sets_Should_Read_The_Missing_Ones_As_Empty(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);
            await AddItemEntityAsync();
            await PostItemAsync("first", 1);

            // An UPDATE answers no result set, one SELECT answers one
            AddCustomEndpoint("RenameItems", $"UPDATE {Quote(dbType, SdkItem.EntityName)} SET {Quote(dbType, nameof(SdkItem.Name))} = 'renamed'");
            AddCustomEndpoint("SelectItems", $"SELECT {Quote(dbType, "ID")}, {Quote(dbType, nameof(SdkItem.Name))} FROM {Quote(dbType, SdkItem.EntityName)}");
            AddSecurity("RenameItems", type: SecurityTypes.CustomEndpoint);
            AddSecurity("SelectItems", type: SecurityTypes.CustomEndpoint);

            var renamedList = await ApilaneService.GetCustomEndpointAsync<List<SdkItem>>(CustomEndpointRequest.New("RenameItems"));
            Assert.Empty(renamedList.Match(r => r, e => throw new Exception($"Typed list call failed | {e.Code} | {e.Message}")));

            var renamedRow = await ApilaneService.GetCustomEndpointAsync<SdkItem>(CustomEndpointRequest.New("RenameItems"));
            Assert.Null(renamedRow.Match(r => r, e => throw new Exception($"Typed row call failed | {e.Code} | {e.Message}")));

            var selected = await ApilaneService.GetCustomEndpointAsync<List<SdkItem>, List<SdkItem>>(CustomEndpointRequest.New("SelectItems"));
            var (items, missing) = selected.Match(r => r, e => throw new Exception($"Typed two-set call failed | {e.Code} | {e.Message}"));

            Assert.Equal("renamed", Assert.Single(items).Name);
            Assert.Empty(missing);
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Stats_Aggregate_WithSort_Should_Order_The_Groups(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);
            await AddItemEntityAsync();
            await PostItemAsync("large", 10);
            await PostItemAsync("small", 1);
            await PostItemAsync("middle", 5);

            async Task<string?[]> GroupsAsync(Func<StatsAggregateRequest, StatsAggregateRequest> sort)
            {
                var request = StatsAggregateRequest.New(SdkItem.EntityName)
                    .WithProperty(nameof(SdkItem.Amount), StatsAggregateRequest.DataAggregates.Sum)
                    .WithGroupBy(nameof(SdkItem.Name));

                var result = await ApilaneService.GetStatsAggregateAsync<List<AmountByName>>(sort(request));

                return result.Match(r => r.Select(x => x.Name).ToArray(), e => throw new Exception($"Aggregate failed | {e.Code} | {e.Message}"));
            }

            Assert.Equal(new string?[] { "small", "middle", "large" }, await GroupsAsync(x => x.WithSort(true)));
            Assert.Equal(new string?[] { "large", "middle", "small" }, await GroupsAsync(x => x.WithSort(false)));

            // Largest first is also the default
            Assert.Equal(new string?[] { "large", "middle", "small" }, await GroupsAsync(x => x));
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task ThrowOnError_Should_Throw_ApilaneErrorException_With_The_Error_And_Status(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);
            await AddEntityAsync(SdkItem.EntityName);

            // No rule lets an anonymous caller read the entity
            var exception = await Assert.ThrowsAsync<ApilaneErrorException>(() =>
                ApilaneService.GetDataAsync<SdkItem>(DataGetListRequest.New(SdkItem.EntityName).OnErrorThrowException(true)));

            Assert.Equal(ValidationError.UNAUTHORIZED, exception.Error.Code);
            Assert.Equal(401, exception.StatusCode);
            Assert.Equal(exception.Error.BuildErrorMessage(), exception.Message);
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Service_Of_UseApilane_Should_Send_The_Application_Token_On_Each_Request(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);
            await AddItemEntityAsync();
            await PostItemAsync("first", 1);

            var services = new ServiceCollection();
            services.UseApilane(ApiConfiguration.Url, TestApplication.Token);

            // The clients of the factory reach the in-memory API host
            services.AddHttpClient("Apilane").ConfigurePrimaryHttpMessageHandler(() => SuiteContext.Shared.Factory.Server.CreateHandler());

            await using var serviceProvider = services.BuildServiceProvider();
            var apilane = serviceProvider.GetRequiredService<IApilaneService>();

            // The singleton takes a client per request: each one must carry the application token
            for (var i = 0; i < 2; i++)
            {
                var result = await apilane.GetDataAsync<SdkItem>(DataGetListRequest.New(SdkItem.EntityName));

                Assert.Equal("first", Assert.Single(result.Match(r => r.Data, e => throw new Exception($"Get failed | {e.Code} | {e.Message}"))).Name);
            }
        }

        [Theory]
        [InlineData(1700000000L, "2023-11-14 22:13:20")]
        [InlineData(1700000000000L, "2023-11-14 22:13:20")]
        [InlineData(631152000000L, "1990-01-01 00:00:00")]
        [InlineData(999999999L, "1970-01-12 13:46:39.999")]
        [InlineData(0L, "1970-01-01 00:00:00")]
        [InlineData(-86400000L, "1969-12-31 00:00:00")]
        public void UnixTimestampToDatetime_Should_Read_The_Value_As_The_Api_Does(long value, string expected)
        {
            // A positive number of exactly 10 digits is seconds, every other value milliseconds
            var expectedDate = DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

            Assert.Equal<DateTime?>(expectedDate, value.UnixTimestampToDatetime());
        }

        [Fact]
        public void UnixTimestampToDatetime_Should_Read_Back_A_Date_Before_1970_And_Refuse_One_Out_Of_Range()
        {
            var date = new DateTime(1960, 5, 6, 7, 8, 9, 10, DateTimeKind.Utc);

            Assert.Equal<DateTime?>(date, date.ToUnixTimestampMilliseconds().UnixTimestampToDatetime());
            Assert.Null(long.MaxValue.UnixTimestampToDatetime());
            Assert.Null(long.MinValue.UnixTimestampToDatetime());
        }
    }
}
