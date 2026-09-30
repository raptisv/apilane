using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Apilane.Net.Services;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class StatsTests : AppicationTestsBase
    {
        public StatsTests(SuiteContext suiteContext) : base(suiteContext)
        {

        }

        private class UserItem : RegisterItem, IApiUser
        {
            public long ID { get; set; }
            public long Created { get; set; }
            public bool EmailConfirmed { get; set; }
            public long? LastLogin { get; set; }
            public string Roles { get; set; } = null!;

            public bool IsInRole(string role)
            {
                return !string.IsNullOrWhiteSpace(Roles)
                    && Roles.Split(',').Contains(role);
            }
        }

        private class RegisterItem : IRegisterItem
        {
            public string Email { get; set; } = null!;
            public string Username { get; set; } = null!;
            public string Password { get; set; } = null!;
        }

        private class CustomEntityLight : DataItem
        {
            public CustomEntityLight(int custom_Integer_Required = 1)
            {
                Custom_Integer_Required = custom_Integer_Required;
            }

            public const string EntityName = "CustomEntityLight";
            public int Custom_Integer_Required { get; set; }
        }

        private class CustomEntityWithDate : DataItem
        {
            public const string EntityName = "CustomEntityWithDate";
            public int Custom_Integer_Required { get; set; }
            public long Custom_Date { get; set; }
        }

        // Mid-month, mid-day UTC timestamps, so that no server/session time zone can move a record into another month or year.
        private static readonly List<(DateTimeOffset Date, int Value)> DateRecords = new()
        {
            (new DateTimeOffset(2020, 3, 15, 12, 0, 0, TimeSpan.Zero), 1),
            (new DateTimeOffset(2020, 3, 16, 12, 0, 0, TimeSpan.Zero), 2),
            (new DateTimeOffset(2020, 7, 15, 12, 0, 0, TimeSpan.Zero), 3),
            (new DateTimeOffset(2021, 3, 15, 12, 0, 0, TimeSpan.Zero), 4),
        };

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Entity_Aggregate_Distinct_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            // Add custom entity

            await AddEntityAsync(CustomEntityLight.EntityName);

            // Add custom properties

            await AddNumberPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_Integer_Required), required: false, decimalPlaces: 0);

            // Add some data

            var distinctRecords = 3;
            await FillEntityWithDataAsync(distinctRecords);

            // Assert anonymous access

            await Assert_With_AuthToken_Security_Async(null, Globals.ANONYMOUS, distinctRecords);

            // Assert authotized access

            var userPassword = "password";
            var userEmail = "test@test.com";
            var userId = await RegisterUserAsync(userEmail, userPassword); // Register 
            var authToken = await LoginUserAsync(userEmail, userPassword); // Login
            await Assert_With_AuthToken_Security_Async(authToken, Globals.AUTHENTICATED, distinctRecords);

            // Assert role access

            var roles = new List<string>() { "role1", "role2" };
            await SetUserRolesAsync(userId, roles, dbType);
            authToken = await LoginUserAsync(userEmail, userPassword); // Login again to reload the user grain
            foreach (var role in roles)
            {
                await Assert_With_AuthToken_Security_Async(authToken, role, distinctRecords);
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Entity_Aggregate_GroupBy_DatePart_And_Plain_Property_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeEntityWithDateAsync(dbType, connectionString, useDiffEntity);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityWithDate.EntityName,
                inRole: Globals.ANONYMOUS,
                properties: new List<string>() { nameof(CustomEntityWithDate.Custom_Integer_Required), nameof(CustomEntityWithDate.Custom_Date) }))
            {
                // Group by year -> key "Custom_Date_year"
                var byYear = await AggregateGroupBy_ShouldSucceedAsync("Custom_Date.year", StatsAggregateRequest.DataAggregates.Count);
                AssertResultKeys(byYear, "Custom_Integer_Required_count", "Custom_Date_year");
                Assert.Equal(
                    new[] { (2020, 3), (2021, 1) },
                    byYear.Select(x => (GetInt(x["Custom_Date_year"]), GetInt(x["Custom_Integer_Required_count"]))).OrderBy(x => x).ToArray());

                // Group by month -> key "Custom_Date_month"
                var byMonth = await AggregateGroupBy_ShouldSucceedAsync("Custom_Date.month", StatsAggregateRequest.DataAggregates.Sum);
                AssertResultKeys(byMonth, "Custom_Integer_Required_sum", "Custom_Date_month");
                Assert.Equal(
                    new[] { (3, 1 + 2 + 4), (7, 3) },
                    byMonth.Select(x => (GetInt(x["Custom_Date_month"]), GetInt(x["Custom_Integer_Required_sum"]))).OrderBy(x => x).ToArray());

                // Mixed case and multiple date parts -> the same canonical lowercase keys as before
                var byYearMonth = await AggregateGroupBy_ShouldSucceedAsync("Custom_Date.YEAR,Custom_Date.Month", StatsAggregateRequest.DataAggregates.Count);
                AssertResultKeys(byYearMonth, "Custom_Integer_Required_count", "Custom_Date_year", "Custom_Date_month");
                Assert.Equal(
                    new[] { (2020, 3, 2), (2020, 7, 1), (2021, 3, 1) },
                    byYearMonth.Select(x => (GetInt(x["Custom_Date_year"]), GetInt(x["Custom_Date_month"]), GetInt(x["Custom_Integer_Required_count"]))).OrderBy(x => x).ToArray());

                // Plain property (no suffix) -> key is the bare property name
                var byProperty = await AggregateGroupBy_ShouldSucceedAsync("Custom_Integer_Required", StatsAggregateRequest.DataAggregates.Count);
                AssertResultKeys(byProperty, "Custom_Integer_Required_count", "Custom_Integer_Required");
                Assert.Equal(
                    new[] { (1, 1), (2, 1), (3, 1), (4, 1) },
                    byProperty.Select(x => (GetInt(x["Custom_Integer_Required"]), GetInt(x["Custom_Integer_Required_count"]))).OrderBy(x => x).ToArray());
            }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Entity_Aggregate_GroupBy_Invalid_Suffix_Should_Fail(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeEntityWithDateAsync(dbType, connectionString, useDiffEntity);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityWithDate.EntityName,
                inRole: Globals.ANONYMOUS,
                properties: new List<string>() { nameof(CustomEntityWithDate.Custom_Integer_Required), nameof(CustomEntityWithDate.Custom_Date), nameof(DataItem.Created) }))
            {
                var invalidGroupBys = new List<string>()
                {
                    "Created.x from Users--", // SQL injected through the alias suffix
                    "Created.foo", // Unknown suffix, used to be accepted silently
                    "Custom_Date.year--",
                    "Custom_Date.year)",
                    "Custom_Integer_Required,Created.foo", // Only the second item is invalid
                };

                foreach (var groupBy in invalidGroupBys)
                {
                    await AggregateGroupBy_ShouldFailAsync(groupBy, ValidationError.INVALID_GROUPBY_PARAMETER);
                }

                // The same request with a valid suffix still works
                var byCreatedYear = await AggregateGroupBy_ShouldSucceedAsync("Created.year", StatsAggregateRequest.DataAggregates.Count);
                AssertResultKeys(byCreatedYear, "Custom_Integer_Required_count", "Created_year");
                Assert.Equal(DateRecords.Count, byCreatedYear.Sum(x => GetInt(x["Custom_Integer_Required_count"])));
            }
        }

        private async Task InitializeEntityWithDateAsync(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            await AddEntityAsync(CustomEntityWithDate.EntityName);
            await AddNumberPropertyAsync(CustomEntityWithDate.EntityName, nameof(CustomEntityWithDate.Custom_Integer_Required), required: false, decimalPlaces: 0);
            await AddDatePropertyAsync(CustomEntityWithDate.EntityName, nameof(CustomEntityWithDate.Custom_Date), required: false);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityWithDate.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityWithDate.Custom_Integer_Required), nameof(CustomEntityWithDate.Custom_Date) }))
            {
                foreach (var record in DateRecords)
                {
                    var postData = await ApilaneService.PostDataAsync(DataPostRequest.New(CustomEntityWithDate.EntityName), new CustomEntityWithDate()
                    {
                        Custom_Integer_Required = record.Value,
                        Custom_Date = record.Date.ToUnixTimeMilliseconds()
                    });

                    postData.Match(response =>
                    {
                        Assert.NotNull(response);
                        Assert.Single(response);
                        Assert.True(response.Single() > 0);
                    },
                    error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
                }
            }
        }

        private async Task<List<Dictionary<string, JsonElement>>> AggregateGroupBy_ShouldSucceedAsync(string groupBy, StatsAggregateRequest.DataAggregates aggregate)
        {
            var request = StatsAggregateRequest.New(CustomEntityWithDate.EntityName)
                .WithGroupBy(groupBy)
                .WithProperty(nameof(CustomEntityWithDate.Custom_Integer_Required), aggregate);

            var getData = await ApilaneService.GetStatsAggregateAsync(request);

            return getData.Match(response =>
            {
                Assert.NotNull(response);
                return JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(response)
                    ?? throw new Exception($"Invalid aggregate response '{response}'");
            },
            error => throw new Exception($"We should not be here | groupBy '{groupBy}' | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task AggregateGroupBy_ShouldFailAsync(string groupBy, ValidationError expectedError)
        {
            var request = StatsAggregateRequest.New(CustomEntityWithDate.EntityName)
                .WithGroupBy(groupBy)
                .WithProperty(nameof(CustomEntityWithDate.Custom_Integer_Required), StatsAggregateRequest.DataAggregates.Count);

            var getData = await ApilaneService.GetStatsAggregateAsync(request);

            getData.Match(response => throw new Exception($"We should not be here | groupBy '{groupBy}' returned '{response}'"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(expectedError, error.Code);
            });
        }

        private static void AssertResultKeys(List<Dictionary<string, JsonElement>> rows, params string[] expectedKeys)
        {
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                Assert.Equal(
                    expectedKeys.OrderBy(x => x, StringComparer.Ordinal),
                    row.Keys.OrderBy(x => x, StringComparer.Ordinal));
            }
        }

        /// <summary>
        /// Group values come back as numbers or strings depending on the database (e.g. SQLite strftime returns "03").
        /// </summary>
        private static int GetInt(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => (int)element.GetDecimal(),
                JsonValueKind.String => int.Parse((element.GetString() ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture),
                _ => throw new Exception($"Unexpected aggregate value '{element}'")
            };
        }

        private async Task FillEntityWithDataAsync(int dataCount)
        {
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: Globals.ANONYMOUS,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_Integer_Required) }))
            {
                for (int i = 0; i < dataCount; i++)
                {
                    var request = DataPostRequest.New(CustomEntityLight.EntityName);

                    var postData = await ApilaneService.PostDataAsync(request, new CustomEntityLight(i + 1));

                    postData.Match(response =>
                    {
                        Assert.NotNull(response);
                        Assert.Single(response);
                        Assert.True(response.Single() > 0);
                    },
                    error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
                }
            }
        }

        private async Task Assert_With_AuthToken_Security_Async(string? authtoken, string securityRole, int distinctRecords)
        {
            // Distinct
            await DistinctData_Unauthorized_ShouldFail<object>(authtoken);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole, properties: new List<string>() { nameof(CustomEntityLight.Custom_Integer_Required) }))
            {
                var result = await DistinctData_ShouldSucceed<List<CustomEntityLight>>(authtoken);
                Assert.Equal(distinctRecords, result.Count);
            }

            // Aggregate
            await AggregateData_Unauthorized_ShouldFail<object>(authtoken);

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole, properties: new List<string>() { nameof(CustomEntityLight.Custom_Integer_Required) }))
            {
                await AggregateData_ShouldSucceed(authtoken, distinctRecords);
            }
        }

        private async Task<T> DistinctData_ShouldSucceed<T>(string? authToken)
        {
            var request = StatsDistinctRequest.New(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_Integer_Required));

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getData = await ApilaneService.GetStatsDistinctAsync<T>(request);

            return getData.Match(response =>
            {
                Assert.NotNull(response);
                return response;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task DistinctData_Unauthorized_ShouldFail<T>(string? authToken)
        {
            var request = StatsDistinctRequest.New(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_Integer_Required));

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getData = await ApilaneService.GetStatsDistinctAsync<T>(request);

            getData.Match(response =>
            {
                throw new Exception($"We should not be here");
            },
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task AggregateData_ShouldSucceed(string? authToken, int distinctRecords)
        {
            // Count
            var requestCount = StatsAggregateRequest.New(CustomEntityLight.EntityName)
                .WithGroupBy(nameof(CustomEntityLight.Custom_Integer_Required))
                .WithProperty(nameof(CustomEntityLight.Custom_Integer_Required), StatsAggregateRequest.DataAggregates.Count);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                requestCount = requestCount.WithAuthToken(authToken);
            }

            var getDataCount = await ApilaneService.GetStatsAggregateAsync(requestCount);

            getDataCount.Match(response =>
            {
                Assert.NotNull(response);
                var objResponse = response.DeserializeAnonymous(new [] { new { Custom_Integer_Required_count = 0, Custom_Integer_Required = 0 } });
                Assert.NotNull(objResponse);
                Assert.Equal(distinctRecords, objResponse.Count());
                foreach(var item in objResponse)
                {
                    Assert.Equal(1, item.Custom_Integer_Required_count);
                }
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Max
            var requestMax = StatsAggregateRequest.New(CustomEntityLight.EntityName)
                .WithGroupBy(nameof(CustomEntityLight.Custom_Integer_Required))
                .WithProperty(nameof(CustomEntityLight.Custom_Integer_Required), StatsAggregateRequest.DataAggregates.Max);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                requestMax = requestMax.WithAuthToken(authToken);
            }

            var getDataMax = await ApilaneService.GetStatsAggregateAsync(requestMax);

            getDataMax.Match(response =>
            {
                Assert.NotNull(response);
                var objResponse = response.DeserializeAnonymous(new[] { new { Custom_Integer_Required_max = 0, Custom_Integer_Required = 0 } });
                Assert.NotNull(objResponse);
                Assert.Equal(distinctRecords, objResponse.Count());
                foreach (var item in objResponse)
                {
                    Assert.Equal(item.Custom_Integer_Required, item.Custom_Integer_Required_max);
                }
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Min
            var requestMin = StatsAggregateRequest.New(CustomEntityLight.EntityName)
                .WithGroupBy(nameof(CustomEntityLight.Custom_Integer_Required))
                .WithProperty(nameof(CustomEntityLight.Custom_Integer_Required), StatsAggregateRequest.DataAggregates.Min);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                requestMin = requestMin.WithAuthToken(authToken);
            }

            var getDataMin = await ApilaneService.GetStatsAggregateAsync(requestMin);

            getDataMin.Match(response =>
            {
                Assert.NotNull(response);
                var objResponse = response.DeserializeAnonymous(new[] { new { Custom_Integer_Required_min = 0, Custom_Integer_Required = 0 } });
                Assert.NotNull(objResponse);
                Assert.Equal(distinctRecords, objResponse.Count());
                foreach (var item in objResponse)
                {
                    Assert.Equal(item.Custom_Integer_Required, item.Custom_Integer_Required_min);
                }
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Avg
            var requestAvg = StatsAggregateRequest.New(CustomEntityLight.EntityName)
                .WithGroupBy(nameof(CustomEntityLight.Custom_Integer_Required))
                .WithProperty(nameof(CustomEntityLight.Custom_Integer_Required), StatsAggregateRequest.DataAggregates.Avg);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                requestAvg = requestAvg.WithAuthToken(authToken);
            }

            var getDataAvg = await ApilaneService.GetStatsAggregateAsync(requestAvg);

            getDataAvg.Match(response =>
            {
                Assert.NotNull(response);
                var objResponse = response.DeserializeAnonymous(new[] { new { Custom_Integer_Required_avg = 0m, Custom_Integer_Required = 0 } });
                Assert.NotNull(objResponse);
                Assert.Equal(distinctRecords, objResponse.Count());
                foreach (var item in objResponse)
                {
                    Assert.Equal(item.Custom_Integer_Required, item.Custom_Integer_Required_avg);
                }
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Sum
            var requestSum = StatsAggregateRequest.New(CustomEntityLight.EntityName)
                .WithGroupBy(nameof(CustomEntityLight.Custom_Integer_Required))
                .WithProperty(nameof(CustomEntityLight.Custom_Integer_Required), StatsAggregateRequest.DataAggregates.Sum);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                requestSum = requestSum.WithAuthToken(authToken);
            }

            var getDataSum = await ApilaneService.GetStatsAggregateAsync(requestSum);

            getDataSum.Match(response =>
            {
                Assert.NotNull(response);
                var objResponse = response.DeserializeAnonymous(new[] { new { Custom_Integer_Required_sum = 0, Custom_Integer_Required = 0 } });
                Assert.NotNull(objResponse);
                Assert.Equal(distinctRecords, objResponse.Count());
                foreach (var item in objResponse)
                {
                    Assert.Equal(item.Custom_Integer_Required, item.Custom_Integer_Required_sum);
                }
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task AggregateData_Unauthorized_ShouldFail<T>(string? authToken)
        {
            var request = StatsAggregateRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            var getData = await ApilaneService.GetStatsAggregateAsync<T>(request);

            getData.Match(response => throw new Exception($"We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED,error.Code);
            });
        }

        private async Task<long> RegisterUserAsync(string userEmail, string userPassword)
        {
            var registerResult = await ApilaneService.AccountRegisterAsync(AccountRegisterRequest.New(new RegisterItem()
            {
                Username = userEmail,
                Email = userEmail,
                Password = userPassword
            }));

            return registerResult.Match(newUserId =>
            {
                return newUserId;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task<string> LoginUserAsync(string userEmail, string userPassword)
        {
            var loginResult = await ApilaneService.AccountLoginAsync<UserItem>(AccountLoginRequest.New(new LoginItem()
            {
                Email = userEmail,
                Password = userPassword
            }));

            return loginResult.Match(success =>
            {
                Assert.NotNull(success);
                Assert.NotNull(success.AuthToken);
                Assert.NotNull(success.User);
                Assert.NotEmpty(success.AuthToken);
                Assert.Equal(userEmail, success.User.Email);
                Assert.False(success.User.EmailConfirmed);

                return success.AuthToken;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }
    }
}