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
using System.Linq;
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

        private async Task FillEntityWithDataAsync(int dataCount)
        {
            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
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

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole, properties: new List<string>() { nameof(CustomEntityLight.Custom_Integer_Required) }))
            {
                var result = await DistinctData_ShouldSucceed<List<CustomEntityLight>>(authtoken);
                Assert.Equal(distinctRecords, result.Count);
            }

            // Aggregate
            await AggregateData_Unauthorized_ShouldFail<object>(authtoken);

            using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
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

            getData.Match(response => throw new Exception($"We should not be here"),
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
            var registerResult = await ApilaneService.AccountRegisterAsync(new RegisterItem()
            {
                Username = userEmail,
                Email = userEmail,
                Password = userPassword
            });

            return registerResult.Match(newUserId =>
            {
                return newUserId;
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }

        private async Task<string> LoginUserAsync(string userEmail, string userPassword)
        {
            var loginResult = await ApilaneService.AccountLoginAsync<UserItem>(new LoginItem()
            {
                Email = userEmail,
                Password = userPassword
            });

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