using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Api.Core.Grains;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Data;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Apilane.Net.Services;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class RateLimitTests : AppicationTestsBase
    {
        public RateLimitTests(SuiteContext suiteContext) : base(suiteContext)
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
            public CustomEntityLight(string custom_String_Required = "test")
            {
                Custom_String_Required = custom_String_Required;
            }

            public const string EntityName = "CustomEntityLight";
            public string Custom_String_Required { get; set; } = null!;
        }

        [Theory]
        [ClassData(typeof(RateLimitConfigurationTestData))]
        public async Task Entity_Get_Post_Put_Delete_RateLimited_Should_Work(int maxRequests, EndpointRateLimit rateLimitType)
        {
            await InitializeApplicationAsync(DatabaseType.SQLLite, null, false);

            // Add custom entity

            await AddEntityAsync(CustomEntityLight.EntityName);

            // Add custom properties

            await AddStringPropertyAsync(CustomEntityLight.EntityName, nameof(CustomEntityLight.Custom_String_Required), required: false);

            AddCustomEndpoint("test", "SELECT 1");

            // Assert anonymous access

            await Assert_CRUD_With_AuthToken_Security_Async(null, null, Globals.ANONYMOUS, DBWS_Security.RateLimitItem.New(maxRequests, rateLimitType));

            // Assert authotized access

            var userPassword = "password";
            var userEmail = "test@test.com";
            var userId = await RegisterUserAsync(userEmail, userPassword); // Register 
            var authToken = await LoginUserAsync(userEmail, userPassword); // Login
            await Assert_CRUD_With_AuthToken_Security_Async(userId, authToken, Globals.AUTHENTICATED, DBWS_Security.RateLimitItem.New(maxRequests, rateLimitType));

            // Assert role access

            var roles = new List<string>() { "role1", "role2" };
            await SetUserRolesAsync(userId, roles, DatabaseType.SQLLite);
            authToken = await LoginUserAsync(userEmail, userPassword); // Login again to reload the user grain
            foreach (var role in roles)
            {
                await Assert_CRUD_With_AuthToken_Security_Async(userId, authToken, role, DBWS_Security.RateLimitItem.New(maxRequests, rateLimitType));
            }
        }

        private async Task Assert_CRUD_With_AuthToken_Security_Async(
            long? userId,
            string? authtoken,
            string securityRole,
            DBWS_Security.RateLimitItem rateLimit)
        {
            // Get custom endpoint

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, "test", type: SecurityTypes.CustomEndpoint,
                inRole: securityRole, rateLimit: rateLimit))
            {
                await GetCustomEndpoint_ShouldSucceed<CustomEntityLight>(rateLimit, userId, authtoken);
            }

            // Get

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole, rateLimit: rateLimit))
            {
                await GetData_ShouldSucceed<CustomEntityLight>(rateLimit, userId, authtoken);
            }

            // Post

            long postedDataId = 0;
            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                rateLimit: rateLimit,
                actionType: SecurityActionType.post,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                postedDataId = await PostData_ShouldSucceed(rateLimit, userId, authtoken, new CustomEntityLight());
                Assert.True(postedDataId > 0);
            }

            // Put

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                rateLimit: rateLimit,
                actionType: SecurityActionType.put,
                properties: new() { nameof(CustomEntityLight.Custom_String_Required) }))
            {
                await PutData_ShouldSucceed(rateLimit, userId, authtoken, new CustomEntityLight() { ID = postedDataId });
            }

            // Delete

            using (new WithSecurityAccess(ApiConfiguration, ApplicationServiceMock, TestApplication, CustomEntityLight.EntityName,
                inRole: securityRole,
                rateLimit: rateLimit,
                actionType: SecurityActionType.delete))
            {
                await DeleteData_ShouldSucceed(rateLimit, userId, authtoken, new List<long>() { postedDataId });
            }
        }

        private async Task GetCustomEndpoint_ShouldSucceed<T>(DBWS_Security.RateLimitItem rateLimit, long? userId, string? authToken)
        {
            // Reset limits before next test
            var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(rateLimit.MaxRequests, rateLimit.TimeWindow, userId?.ToString(), $"custom:{"test"}", SecurityActionType.get);
            var rateLimitGrainRef = ClusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(TestApplication.Token), rateLimitGrainKeyExt, null);
            await rateLimitGrainRef.ResetLimitsAsync();

            var request = CustomEndpointRequest.New("test");

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            // First wave should succeed

            for (int i = 0; i < rateLimit.MaxRequests; i++)
            {
                var getData_First = await ApilaneService.GetCustomEndpointAsync(request);

                getData_First.Match(response =>
                {
                    Assert.NotNull(response);
                    return response;
                },
                error =>
                {
                    throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}");
                });
            }

            // Second wave should fail

            var getData_Second = await ApilaneService.GetCustomEndpointAsync(request);
            getData_Second.Match(response =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    throw new Exception($"We should not be here");
                }

                return 0;
            },
            error =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, error.Code);
                }

                return 0;
            });

            if (rateLimit.TimeWindowType == (int)EndpointRateLimit.Per_Second)
            {
                // Third wave should succeed if we wait for 1 second

                Thread.Sleep(1000);

                var getData_Third = await ApilaneService.GetCustomEndpointAsync(request);

                getData_Third.Match(response =>
                {
                    Assert.NotNull(response);
                    return response;
                },
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }
        }

        private async Task GetData_ShouldSucceed<T>(DBWS_Security.RateLimitItem rateLimit, long? userId, string? authToken)
        {
            // Reset limits before next test
            var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(rateLimit.MaxRequests, rateLimit.TimeWindow, userId?.ToString(), CustomEntityLight.EntityName, SecurityActionType.get);
            var rateLimitGrainRef = ClusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(TestApplication.Token), rateLimitGrainKeyExt, null);
            await rateLimitGrainRef.ResetLimitsAsync();

            var request = DataGetListRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            // First wave should succeed

            for(int i=0;i< rateLimit.MaxRequests; i++)
            {
                var getData_First = await ApilaneService.GetDataAsync<T>(request);

                getData_First.Match(response =>
                {
                    Assert.NotNull(response);
                    Assert.NotNull(response.Data);
                    return response.Data;
                },
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }

            // Second wave should fail

            var getData_Second = await ApilaneService.GetDataAsync<T>(request);
            getData_Second.Match(response =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    throw new Exception($"We should not be here");
                }

                return 0;
            },
            error =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, error.Code);
                }

                return 0;
            });

            if (rateLimit.TimeWindowType == (int)EndpointRateLimit.Per_Second)
            {
                // Third wave should succeed if we wait for 1 second

                Thread.Sleep(1000);

                var getData_Third = await ApilaneService.GetDataAsync<T>(request);

                getData_Third.Match(response =>
                {
                    Assert.NotNull(response);
                    Assert.NotNull(response.Data);
                    return response.Data;
                },
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }
        }

        private async Task<long> PostData_ShouldSucceed(DBWS_Security.RateLimitItem rateLimit, long? userId, string? authToken, object data)
        {
            // Reset limits before next test
            var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(rateLimit.MaxRequests, rateLimit.TimeWindow, userId?.ToString(), CustomEntityLight.EntityName, SecurityActionType.post);
            var rateLimitGrainRef = ClusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(TestApplication.Token), rateLimitGrainKeyExt, null);
            await rateLimitGrainRef.ResetLimitsAsync();

            var request = DataPostRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            long postItemId = 0;

            // First wave should succeed

            for (int i = 0; i < rateLimit.MaxRequests; i++)
            {
                var postDataFirst = await ApilaneService.PostDataAsync(request, data);

                postItemId = postDataFirst.Match(response =>
                {
                    Assert.NotNull(response);
                    Assert.Single(response);
                    return response.Single();
                },
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }

            // Second wave should fail

            var postDataSecond = await ApilaneService.PostDataAsync(request, data);

            postDataSecond.Match(response =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    throw new Exception($"We should not be here");
                }

                return 0;
            },
            error =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, error.Code);
                }

                return 0;
            });

            if (rateLimit.TimeWindowType == (int)EndpointRateLimit.Per_Second)
            {
                // Third wave should succeed if we wait for 1 second

                Thread.Sleep(1000);

                var postDataThird = await ApilaneService.PostDataAsync(request, data);

                return postDataThird.Match(response =>
                {
                    Assert.NotNull(response);
                    Assert.Single(response);
                    return response.Single();
                },
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }
            else
            {
                return postItemId;
            }
        }

        private async Task PutData_ShouldSucceed(DBWS_Security.RateLimitItem rateLimit, long? userId, string? authToken, object data)
        {
            // Reset limits before next test
            var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(rateLimit.MaxRequests, rateLimit.TimeWindow, userId?.ToString(), CustomEntityLight.EntityName, SecurityActionType.put);
            var rateLimitGrainRef = ClusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(TestApplication.Token), rateLimitGrainKeyExt, null);
            await rateLimitGrainRef.ResetLimitsAsync();

            var request = DataPutRequest.New(CustomEntityLight.EntityName);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            // First wave should succeed

            for (int i = 0; i < rateLimit.MaxRequests; i++)
            {
                var putDataFirst = await ApilaneService.PutDataAsync(request, data);
                putDataFirst.Match(response => response,
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }

            // Second wave should fail

            var putDataSecond = await ApilaneService.PutDataAsync(request, data);
            putDataSecond.Match(response =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    throw new Exception($"We should not be here");
                }

                return 0;
            },
            error =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, error.Code);
                }

                return 0;
            });

            if (rateLimit.TimeWindowType == (int)EndpointRateLimit.Per_Second)
            {
                // Third wave should succeed if we wait for 1 second

                Thread.Sleep(1000);

                var putDataThird = await ApilaneService.PutDataAsync(request, data);
                putDataThird.Match(response => response,
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }
        }

        private async Task DeleteData_ShouldSucceed(DBWS_Security.RateLimitItem rateLimit, long? userId, string? authToken, List<long> Ids)
        {
            // Reset limits before next test
            var rateLimitGrainKeyExt = SecurityExtensions.BuildRateLimitingGrainKeyExt(rateLimit.MaxRequests, rateLimit.TimeWindow, userId?.ToString(), CustomEntityLight.EntityName, SecurityActionType.delete);
            var rateLimitGrainRef = ClusterClient.GetGrain<IRateLimitSlidingWindowGrain>(Guid.Parse(TestApplication.Token), rateLimitGrainKeyExt, null);
            await rateLimitGrainRef.ResetLimitsAsync();

            var request = DataDeleteRequest.New(CustomEntityLight.EntityName, Ids);

            if (!string.IsNullOrWhiteSpace(authToken))
            {
                request = request.WithAuthToken(authToken);
            }

            // First wave should succeed

            for (int i = 0; i < rateLimit.MaxRequests; i++)
            {
                var deleteDataFirst = await ApilaneService.DeleteDataAsync(request);
                deleteDataFirst.Match(response => response,
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }

            // Second wave should fail

            var deleteDataSecond = await ApilaneService.DeleteDataAsync(request);
            deleteDataSecond.Match(response => 
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    throw new Exception($"We should not be here");
                }

                return 0;
            },
            error =>
            {
                if (rateLimit.TimeWindowType != (int)EndpointRateLimit.None)
                {
                    Assert.Equal(ValidationError.RATE_LIMIT_EXCEEDED, error.Code);
                }

                return 0;
            });

            if (rateLimit.TimeWindowType == (int)EndpointRateLimit.Per_Second)
            {
                // Third wave should succeed if we wait for 1 second

                Thread.Sleep(1000);

                var deleteDataThird = await ApilaneService.DeleteDataAsync(request);
                deleteDataThird.Match(response => response,
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }
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