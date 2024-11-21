using Apilane.Api.Component.Tests.Infrastructure;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Net.Extensions;
using Apilane.Net.Models.Account;
using Apilane.Net.Models.Enums;
using Apilane.Net.Request;
using Apilane.Net.Services;
using CasinoService.ComponentTests.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Apilane.Api.Component.Tests
{
    [Collection(nameof(ApilaneApiComponentTestsCollection))]
    public class AccountTests : AppicationTestsBase
    {
        public AccountTests(SuiteContext suiteContext) : base(suiteContext)
        {

        }

        private class UserItem : RegisterItem, IApiUser
        {
            public long ID { get; set; }
            public long Created { get; set; }
            public bool EmailConfirmed { get; set; }
            public long? LastLogin { get; set; }
            public string Roles { get; set; } = null!;

            // Differentiation property
            public long? Company_ID { get; set; } = null!;

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

            // Custom properties
            public string Custom_String { get; set; } = null!;
            public string Custom_String_two { get; set; } = null!;
            public int Custom_Integer { get; set; }
            public decimal Custom_Decimal { get; set; }
            public bool Custom_Bool { get; set; }
            public long Custom_Date { get; set; }
        }

        [Theory]
        [ClassData(typeof(StorageConfigurationTestData))]
        public async Task Register_Login_GetData_Update_Logout_Should_Work(DatabaseType dbType, string? connectionString, bool useDiffEntity)
        {
            await InitializeApplicationAsync(dbType, connectionString, useDiffEntity);

            var expectedUserId = 1;
            var userName = "test";
            var userPassword = "password";
            var userEmail = "test@test.com";
            var user_Custom_Integer = 1;
            var user_Custom_Decimal = 1.6m;
            var user_Custom_Bool = true;
            var user_Custom_Date = DateTime.UtcNow.ToUnixTimestampMilliseconds();
            var user_Custom_String = "test_first_name";
            var user_Custom_String2 = user_Custom_String + "_updated";

            // Add custom properties

            await AddStringPropertyAsync("Users", nameof(UserItem.Custom_String), required: true);
            await AddStringPropertyAsync("Users", nameof(UserItem.Custom_String_two), required: true);
            await AddNumberPropertyAsync("Users", nameof(UserItem.Custom_Integer), required: true);
            await AddNumberPropertyAsync("Users", nameof(UserItem.Custom_Decimal), required: true, decimalPlaces: 2);
            await AddBooleanPropertyAsync("Users", nameof(UserItem.Custom_Bool), required: true);
            await AddDatePropertyAsync("Users", nameof(UserItem.Custom_Date), required: true);

            // Invalid login with email

            var invalidLoginWithEmailResult = await ApilaneService.AccountLoginAsync<UserItem>(new LoginItem()
            {
                Email = userEmail,
                Password = userPassword
            });

            invalidLoginWithEmailResult.Match(success => throw new Exception("We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.ERROR, error.Code);
            });

            // Invalid login with username

            var invalidLoginWithUserNameResult = await ApilaneService.AccountLoginAsync<UserItem>(new LoginItem()
            {
                Username = userName,
                Password = userPassword
            });

            invalidLoginWithUserNameResult.Match(success => throw new Exception("We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.ERROR, error.Code);
            });

            // Register with required first name

            var registerResult = await ApilaneService.AccountRegisterAsync(new RegisterItem()
            {
                Username = userName,
                Email = userEmail,
                Password = userPassword,
                Custom_String = user_Custom_String,
                Custom_String_two = user_Custom_String,
                Custom_Integer = user_Custom_Integer,
                Custom_Decimal = user_Custom_Decimal,
                Custom_Bool = user_Custom_Bool,
                Custom_Date = user_Custom_Date
            });

            registerResult.Match(newUserId =>
            {
                Assert.Equal(expectedUserId, newUserId);
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Register without required first name

            var invalidRegisterResult = await ApilaneService.AccountRegisterAsync(new RegisterItem()
            {
                Username = userName,
                Email = userEmail,
                Password = userPassword,
                Custom_String = null!, // <=- this should fail
                Custom_String_two = null!, // <=- this should fail
                Custom_Integer = user_Custom_Integer,
                Custom_Decimal = user_Custom_Decimal,
                Custom_Bool = user_Custom_Bool,
                Custom_Date = user_Custom_Date
            });

            invalidRegisterResult.Match(newUserId => throw new Exception("We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.REQUIRED, error.Code);
            });

            // Attempt register again to force error

            var registerResult2 = await ApilaneService.AccountRegisterAsync(new RegisterItem()
            {
                Username = userName,
                Email = userEmail,
                Password = userPassword,
                Custom_String = user_Custom_String,
                Custom_String_two = user_Custom_String,
                Custom_Integer = user_Custom_Integer,
                Custom_Decimal = user_Custom_Decimal,
                Custom_Bool = user_Custom_Bool,
                Custom_Date = user_Custom_Date
            });

            registerResult2.Match(newUserId => throw new Exception("We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNIQUE_CONSTRAINT_VIOLATION, error.Code);
            });

            // Login with email

            var loginWithEmailResult = await ApilaneService.AccountLoginAsync<UserItem>(new LoginItem()
            {
                Email = userEmail,
                Password = userPassword
            });

            loginWithEmailResult.Match(success =>
            {
                Assert.NotNull(success);
                Assert.NotNull(success.AuthToken);
                Assert.NotNull(success.User);
                Assert.NotEmpty(success.AuthToken);
                Assert.Equal(userEmail, success.User.Email);
                Assert.Equal(user_Custom_String, success.User.Custom_String);
                Assert.Equal(user_Custom_String, success.User.Custom_String_two);
                Assert.Equal(user_Custom_Integer, success.User.Custom_Integer);
                Assert.Equal(user_Custom_Decimal, success.User.Custom_Decimal);
                Assert.Equal(user_Custom_Bool, success.User.Custom_Bool);
                Assert.Equal(user_Custom_Date, success.User.Custom_Date);
                Assert.Null(success.User.Company_ID);
                Assert.False(success.User.EmailConfirmed);
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Login with username

            var loginWithUsernameResult = await ApilaneService.AccountLoginAsync<UserItem>(new LoginItem()
            {
                Username = userName,
                Password = userPassword
            });

            loginWithUsernameResult.Match(success =>
            {
                Assert.NotNull(success);
                Assert.NotNull(success.AuthToken);
                Assert.NotNull(success.User);
                Assert.NotEmpty(success.AuthToken);
                Assert.Equal(userEmail, success.User.Email);
                Assert.Equal(user_Custom_String, success.User.Custom_String);
                Assert.Equal(user_Custom_String, success.User.Custom_String_two);
                Assert.Equal(user_Custom_Integer, success.User.Custom_Integer);
                Assert.Equal(user_Custom_Decimal, success.User.Custom_Decimal);
                Assert.Equal(user_Custom_Bool, success.User.Custom_Bool);
                Assert.Equal(user_Custom_Date, success.User.Custom_Date);
                Assert.Null(success.User.Company_ID);
                Assert.False(success.User.EmailConfirmed);
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Update first name

            var updateFirstNameResult = await ApilaneService.AccountUpdateAsync<UserItem>(AccountUpdateRequest.New()
                .WithAuthToken(loginWithUsernameResult.Value.AuthToken),
                new
                {
                    Custom_String = user_Custom_String2
                });

            updateFirstNameResult.Match(success =>
            {
                Assert.NotNull(success);
                Assert.Equal(userEmail, success.Email);
                Assert.Equal(user_Custom_String2, success.Custom_String);
                Assert.False(success.EmailConfirmed);
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Login with username again with new first name

            var loginWithUsernameResult2 = await ApilaneService.AccountLoginAsync<UserItem>(new LoginItem()
            {
                Username = userName,
                Password = userPassword
            });

            loginWithUsernameResult2.Match(success =>
            {
                Assert.NotNull(success);
                Assert.NotNull(success.AuthToken);
                Assert.NotNull(success.User);
                Assert.NotEmpty(success.AuthToken);
                Assert.Equal(userEmail, success.User.Email);
                Assert.Equal(user_Custom_String2, success.User.Custom_String);
                Assert.Equal(user_Custom_String, success.User.Custom_String_two);
                Assert.Equal(user_Custom_Integer, success.User.Custom_Integer);
                Assert.Equal(user_Custom_Decimal, success.User.Custom_Decimal);
                Assert.Equal(user_Custom_Bool, success.User.Custom_Bool);
                Assert.Equal(user_Custom_Date, success.User.Custom_Date);
                Assert.Null(success.User.Company_ID);
                Assert.False(success.User.EmailConfirmed);
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Get user data invalid auth token

            var getUserDataWithInvalidAuthToken = await ApilaneService.GetAccountUserDataAsync<UserItem>(AccountUserDataRequest.New()
                .WithAuthToken(Guid.NewGuid().ToString()));

            getUserDataWithInvalidAuthToken.Match(success => throw new Exception("We should not be here"),
            error =>
            {
                Assert.NotNull(error);
                Assert.Equal(ValidationError.UNAUTHORIZED, error.Code);
            });

            // Get user data valid auth token

            var getUserDataWithValidAuthToken = await ApilaneService.GetAccountUserDataAsync<UserItem>(AccountUserDataRequest.New()
                .WithAuthToken(loginWithUsernameResult.Value.AuthToken));

            getUserDataWithValidAuthToken.Match(success =>
            {
                Assert.NotNull(success);
                Assert.NotNull(success.User);
                Assert.Equal(userEmail, success.User.Email);
                Assert.False(success.User.EmailConfirmed);
                Assert.Equal(user_Custom_String2, success.User.Custom_String);
                Assert.Equal(user_Custom_String, success.User.Custom_String_two);
                Assert.Equal(user_Custom_Integer, success.User.Custom_Integer);
                Assert.Equal(user_Custom_Decimal, success.User.Custom_Decimal);
                Assert.Equal(user_Custom_Bool, success.User.Custom_Bool);
                Assert.Equal(user_Custom_Date, success.User.Custom_Date);
                Assert.Null(success.User.Company_ID);
                Assert.NotNull(success.Security);
                Assert.Empty(success.Security); // There should be no security items at this point
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));

            // Set the dirrerentiation property and get user data valid auth token again

            if (useDiffEntity)
            {
                var newDiffPropertyValue = 5;
                var updateDiffPropertyEndpointName = "UpdateDiffProperty";

                var customEndpointQuery = dbType switch
                {
                    DatabaseType.SQLServer => $@"SET IDENTITY_INSERT [{DiffEntityName}] ON;
                                                INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES ({newDiffPropertyValue}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                                UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = {newDiffPropertyValue} WHERE [ID] = {expectedUserId};
                                                SET IDENTITY_INSERT [{DiffEntityName}] OFF;",
                    DatabaseType.MySQL => $@"INSERT INTO `{DiffEntityName}` (`ID`, `Created`) VALUES ({newDiffPropertyValue}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE `Users` SET `{DiffEntityName.GetDifferentiationPropertyName()}` = {newDiffPropertyValue} WHERE `ID` = {expectedUserId};",
                    _ => $@"INSERT INTO [{DiffEntityName}] ([ID], [Created]) VALUES ({newDiffPropertyValue}, {DateTime.UtcNow.ToUnixTimestampMilliseconds()});
                                               UPDATE [Users] SET [{DiffEntityName.GetDifferentiationPropertyName()}] = {newDiffPropertyValue} WHERE [ID] = {expectedUserId};",
                };

                AddCustomEndpoint(updateDiffPropertyEndpointName, customEndpointQuery);

                using (new WithSecurityAccess(ApplicationServiceMock, TestApplication, updateDiffPropertyEndpointName,
                    type: SecurityTypes.CustomEndpoint))
                {
                    await ApilaneService.GetCustomEndpointAsync(CustomEndpointRequest.New(updateDiffPropertyEndpointName));
                }

                var getUserDataWithValidAuthToken2 = await ApilaneService.GetAccountUserDataAsync<UserItem>(AccountUserDataRequest.New()
                    .WithAuthToken(loginWithUsernameResult.Value.AuthToken));

                getUserDataWithValidAuthToken2.Match(success =>
                {
                    Assert.NotNull(success);
                    Assert.NotNull(success.User);
                    Assert.Equal(userEmail, success.User.Email);
                    Assert.False(success.User.EmailConfirmed);
                    Assert.Equal(user_Custom_String2, success.User.Custom_String);
                    Assert.Equal(user_Custom_String, success.User.Custom_String_two);
                    Assert.Equal(user_Custom_Integer, success.User.Custom_Integer);
                    Assert.Equal(user_Custom_Decimal, success.User.Custom_Decimal);
                    Assert.Equal(user_Custom_Bool, success.User.Custom_Bool);
                    Assert.Equal(user_Custom_Date, success.User.Custom_Date);
                    Assert.Equal(newDiffPropertyValue, success.User.Company_ID);                    
                    Assert.NotNull(success.Security);
                    Assert.Empty(success.Security);
                },
                error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
            }

            // Logout

            var logoutResult = await ApilaneService.AccountLogoutAsync(AccountLogoutRequest.New(true)
                .WithAuthToken(loginWithUsernameResult.Value.AuthToken));

            logoutResult.Match(logoutCount =>
            {
                Assert.Equal(3, logoutCount); // User logged in 3 times, we expect 3 auth tokens to be deleted
            },
            error => throw new Exception($"We should not be here | {error.Code} | {error.Message} | {error.Property}"));
        }
    }
}