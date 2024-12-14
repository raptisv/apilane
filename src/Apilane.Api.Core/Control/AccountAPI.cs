using Apilane.Api.Core.Abstractions;
using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Enums;
using Apilane.Api.Core.Exceptions;
using Apilane.Api.Core.Grains;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Helpers;
using Apilane.Common.Models;
using Apilane.Common.Models.AppModules.Authentication;
using Apilane.Common.Models.Dto;
using Apilane.Common.Utilities;
using Apilane.Data.Abstractions;
using Apilane.Data.Utilities;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Apilane.Api.Core
{
    public class AccountAPI : IAccountAPI
    {
        private readonly ILogger<AccountAPI> _logger;
        private readonly IApplicationHelperService _applicationHelperService;
        private readonly IApplicationDataService _appDataService;
        private readonly IApplicationDataStoreFactory _dataStore;
        private readonly IApplicationEmailService _appEmailService;
        private readonly IClusterClient _clusterClient; 
        private readonly ApiConfiguration _apiConfiguration;

        public AccountAPI(
            ILogger<AccountAPI> logger,
            IApplicationDataService appDataService,
            IApplicationDataStoreFactory dataStore,
            IApplicationEmailService appEmailService,
            ApiConfiguration currentConfiguration,
            IApplicationHelperService applicationHelperService,
            IClusterClient clusterClient)
        {
            _appDataService = appDataService;
            _logger = logger;
            _dataStore = dataStore;
            _appEmailService = appEmailService;
            _apiConfiguration = currentConfiguration;
            _applicationHelperService = applicationHelperService;
            _clusterClient = clusterClient;
        }

        public async Task<LoginResponseDto> LoginAsync(
            DBWS_Application application,
            DBWS_Entity usersEntity,
            string? username,
            string? email,
            string password)
        {
            var properties = usersEntity.Properties.Where(x => !x.IsPrimaryKey).ToList();

            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ApilaneException(AppErrors.REQUIRED, null, nameof(password));
            }

            Dictionary<string, object?>? drUser = null;

            if (!string.IsNullOrWhiteSpace(email))
            {
                if (!Utils.IsValidEmail(email))
                {
                    throw new ApilaneException(AppErrors.VALIDATION, "Email is not valid", nameof(email));
                }

                drUser = await GetUserByEmailAndPasswordAsync(application, email, password);
            }
            else if (!string.IsNullOrWhiteSpace(username))
            {
                drUser = await GetUserByNameAndPasswordAsync(application, username, password);
            }
            else // If both email and user name are missing
            {
                throw new ApilaneException(AppErrors.REQUIRED, "User name or email is required to login", nameof(email));
            }

            if (drUser is null)
            {
                throw new ApilaneException(AppErrors.ERROR, "Invalid login attempt");
            }

            if (!application.AllowLoginUnconfirmedEmail)
            {
                bool emailConfirmed = Utils.GetBool(drUser[nameof(Users.EmailConfirmed)]);
                if (!emailConfirmed)
                {
                    throw new ApilaneException(AppErrors.UNCONFIRMED_EMAIL);
                }
            }

            var userId = Utils.GetLong(drUser[nameof(Users.ID)]);

            // Clean up any expired auth tokens
            await DeleteExpiredAuthTokensAsync(application, userId);

            // Create and store a new AuthToken
            var authToken = Guid.NewGuid().ToString();

            await _dataStore.CreateDataAsync(
                nameof(AuthTokens),
                new Dictionary<string, object?>()
                {
                    { nameof(AuthTokens.Owner), userId },
                    { nameof(AuthTokens.Created), Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) },
                    { nameof(AuthTokens.Token), authToken }
                }, false);

            // Update last login
            await _dataStore.UpdateDataAsync(
                nameof(Users),
                new Dictionary<string, object?>()
                {
                    { nameof(Users.LastLogin), Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) }
                },
                new FilterData(nameof(Users.ID), FilterData.FilterOperators.equal, userId, PropertyType.Number));

            return new LoginResponseDto()
            {
                User = drUser,
                AuthToken = authToken
            };
        }

        private async Task DeleteExpiredAuthTokensAsync(
            DBWS_Application application,
            long userId)
        {
            FilterData expiredAuthTokensFilter;
            if (application.ForceSingleLogin)
            {
                // Delete all other tokens if required (force logout other logins)
                expiredAuthTokensFilter = new(nameof(AuthTokens.Owner), FilterData.FilterOperators.equal, userId, PropertyType.Number);
            }
            else
            {
                // Clean up any expired tokens
                var expiredTokensMinDate = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow.AddMinutes(-Math.Abs(application.AuthTokenExpireMinutes)));
                expiredAuthTokensFilter = new FilterData(FilterData.FilterLogic.AND, new List<FilterData>()
                {
                    new(nameof(AuthTokens.Owner), FilterData.FilterOperators.equal, userId, PropertyType.Number),
                    new(nameof(AuthTokens.Created), FilterData.FilterOperators.less, expiredTokensMinDate, PropertyType.Date)
                });
            }

            var expiredAuthTokens = await _dataStore.GetPagedDataAsync(
                nameof(AuthTokens),
                null,
                expiredAuthTokensFilter,
                null,
                1,
                1000);

            foreach (var expiredAuthToken in expiredAuthTokens)
            {
                // Delete auth token
                if (Guid.TryParse(Utils.GetString(expiredAuthToken[nameof(AuthTokens.Token)]), out var guidAuthToken))
                {
                    var authTokenGrainRef = _clusterClient.GetGrain<IAuthTokenUserGrain>(guidAuthToken);
                    await authTokenGrainRef.DeleteAsync(application.ToDbInfo(_apiConfiguration.FilesPath));
                }
            }
        }

        public async Task<string> RenewAuthTokenAsync(Users currentUser)
        {
            var newAuthToken = Guid.NewGuid().ToString();

            // Create the new token
            await _dataStore.CreateDataAsync(
                nameof(AuthTokens),
                new Dictionary<string, object?>()
                {
                    { nameof(AuthTokens.Owner), currentUser.ID },
                    { nameof(AuthTokens.Token), newAuthToken },
                    { nameof(AuthTokens.Created), Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) }
                }, false);

            return newAuthToken;
        }

        public async Task<UserDataDto> GetUserDataAsync(
            string appToken,
            Users currentUser,
            List<DBWS_Security> appSecurityList)
        {
            var drUser = await _appDataService.GetUserByIdAsync(appToken, currentUser.ID)
                ?? throw new ApilaneException(AppErrors.UNAUTHORIZED);

            return new UserDataDto()
            {
                User = drUser.ToDictionary(),
                Security = GetSecurityForUserRoles(Utils.GetString(currentUser.Roles), appSecurityList)
            };
        }

        private List<UserSecurityDto> GetSecurityForUserRoles(
            string roles,
            List<DBWS_Security> appSecurityList)
        {
            var rolesList = roles.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            rolesList.Add(Globals.ANONYMOUS);
            rolesList.Add(Globals.AUTHENTICATED);

            return appSecurityList
                .Where(x => rolesList.Contains(x.RoleID))
                .Select(x => new UserSecurityDto()
                {
                    Role = x.RoleID,
                    Name = x.Name,
                    Type = x.TypeID_Enum.ToString(),
                    Action = x.Action,
                    Properties = Utils.GetString(x.Properties).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                }).ToList();
        }

        public async Task<long> RegisterAsync(
            string appToken,
            DBWS_Entity usersEntity,
            DatabaseType databaseType,
            string applicationServerUrl,
            EmailSettings? emailSettings,
            string appEncryptionKey,
            string? differentiationEntity,
            JsonObject userJObject,
            bool allowUserRegister)
        {
            if (userJObject == null)
            {
                throw new ApilaneException(AppErrors.EMPTY_BODY);
            }

            if (!allowUserRegister)
            {
                throw new ApilaneException(AppErrors.ERROR, "Register is not allowed");
            }

            var usersProperties = usersEntity.Properties.Where(x => !x.IsPrimaryKey).ToList();

            var propertyValues = new Dictionary<string, object?>();

            foreach (var prop in usersProperties)
            {
                object? value = null;

                if (prop.Name.Equals(nameof(Users.Created)))
                {
                    value = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow).ToString();
                }
                else if (prop.Name.Equals(nameof(Users.LastLogin)))
                {
                    value = null;
                }
                else if (prop.Name.Equals(nameof(Users.EmailConfirmed)))
                {
                    value = false;
                }
                else if (!string.IsNullOrWhiteSpace(differentiationEntity) && usersEntity.HasDifferentiationProperty && prop.Name.Equals(differentiationEntity.GetDifferentiationPropertyName()))
                {
                    value = null;
                }
                else if (prop.Name.Equals(nameof(Users.Roles)))// IMPORTANT! DO NOT ALLOW SET ROLE ON REGISTER. Only users with put permission will update it.
                {
                    value = null;
                }
                else
                {
                    if (prop.Name.Equals(nameof(Users.Email)))
                    {
                        var newEmail = userJObject.GetObjectProperty(nameof(Users.Email));

                        if (!Utils.IsValidEmail(newEmail))
                        {
                            throw new ApilaneException(AppErrors.VALIDATION, "Email is not valid", "Email");
                        }
                    }

                    value = _appDataService.GetPropertyValue(differentiationEntity, appEncryptionKey, usersEntity, prop, userJObject, null);
                }

                propertyValues[prop.Name] = value;
            }

            var newUserID = await _dataStore.CreateDataAsync(
                usersEntity.Name,
                propertyValues,
                false);

            if (!newUserID.HasValue)
            {
                throw new Exception("Could not register user | New user id cannot be null");
            }

            var drUserThatAcceptsTheEmail = await _appDataService.GetUserByIdAsync(appToken, newUserID.Value);

            await _appEmailService.SendEmailFromApplication_FireAndForgetAsync(
                appToken,
                applicationServerUrl,
                emailSettings,
                EmailEventsCodes.UserRegisterConfirmation,
                drUserThatAcceptsTheEmail!,
                drUserThatAcceptsTheEmail!);

            return newUserID.Value;
        }

        public async Task<Dictionary<string, object?>> UpdateAsync(
            string appToken,
            DBWS_Entity usersEntity,
            Users currentUser,
            DatabaseType databaseType,
            string appEncryptionKey,
            string? differentiationEntity,
            JsonObject userJObject)
        {
            var nonSystemProperties = usersEntity.Properties.Where(x => !x.IsSystem).ToList();

            var propertiesRequestedToUpdate = new List<string>();
            foreach (KeyValuePair<string, JsonNode?> prop in userJObject)
            {
                if (!propertiesRequestedToUpdate.Contains(prop.Key))
                {
                    propertiesRequestedToUpdate.Add(prop.Key);
                }
            }

            nonSystemProperties = nonSystemProperties.Where(x => propertiesRequestedToUpdate.Select(y => y.ToLower().Trim()).Contains(x.Name.ToLower())).ToList();

            if (nonSystemProperties.Count == 0)
            {
                throw new ApilaneException(AppErrors.NO_PROPERTIES_PROVIDED, entity: nameof(Users));
            }

            if (usersEntity.RequireChangeTracking)
            {
                // Get the full user object, in case there are custom properties included
                var userData = await _dataStore.GetDataByIdAsync(nameof(Users), currentUser.ID, null);
                if (userData is not null)
                {
                    await _applicationHelperService.CreateHistoryAsync(appToken, usersEntity.Name, currentUser.ID, currentUser.ID, userData);
                }
            }

            var columnsAndValuesToUpdate = new Dictionary<string, object?>();
            foreach (var nonSystemProperty in nonSystemProperties)
            {
                columnsAndValuesToUpdate[nonSystemProperty.Name] = _appDataService.GetPropertyValue(differentiationEntity, appEncryptionKey, usersEntity, nonSystemProperty, userJObject, currentUser);
            }

            await _dataStore.UpdateDataAsync(
                nameof(Users),
                columnsAndValuesToUpdate,
                new FilterData(nameof(Users.ID), FilterData.FilterOperators.equal, currentUser.ID, PropertyType.Number));

            var drUser = await _appDataService.GetUserByIdAsync(appToken, currentUser.ID) ?? throw new Exception("Could not find user");

            return drUser.ToDictionary();
        }

        public async Task<string?> ConfirmAsync(
            string appToken,
            string confirmationToken,
            string appName,
            string? emailConfirmationRedirectUrl)
        {
            var userIdForToken = await _applicationHelperService.GetUserIdFromEmailConfitmationTokenAsync(appToken, confirmationToken);

            if (userIdForToken is null)
            {
                _logger.LogWarning($"Token not found | '{confirmationToken}'");
                return null;
            }

            var drUser = await _appDataService.GetUserByIdAsync(appToken, userIdForToken.Value);

            // Do not show that we did not find the user

            if (drUser is not null &&
                !Utils.GetBool(drUser[nameof(Users.EmailConfirmed)]))
            {
                // Confirm user
                await _dataStore.UpdateDataAsync(
                    nameof(Users),
                    new Dictionary<string, object?>()
                    {
                        { nameof(Users.EmailConfirmed), true }
                    },
                    new FilterData(nameof(Users.ID), FilterData.FilterOperators.equal, userIdForToken.Value, PropertyType.Number));
            }

            // Do not delete the token, it might be requested again.

            var redirectToUrl = string.IsNullOrWhiteSpace(emailConfirmationRedirectUrl) ?
                $"{_apiConfiguration.PortalUrl.Trim('/')}/Account/AppEmailConfirmed"
                : emailConfirmationRedirectUrl;

            return redirectToUrl;
        }

        public async Task<bool> ChangePasswordAsync(
            Users currentUser,
            string appEncryptionKey,
            string currentPassword,
            string newPassword)
        {
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                throw new ApilaneException(AppErrors.REQUIRED, null, nameof(currentPassword));
            }

            if (!currentUser.Password.Equals(appEncryptionKey.ApplicationEncrypt(currentPassword)))
            {
                throw new ApilaneException(AppErrors.VALIDATION, "Invalid password", nameof(currentPassword));
            }

            if (string.IsNullOrWhiteSpace(newPassword))
            {
                throw new ApilaneException(AppErrors.REQUIRED, null, nameof(newPassword));
            }

            if (newPassword.Length < 8)
            {
                throw new ApilaneException(AppErrors.VALIDATION, "Minimum 8 characters", nameof(newPassword));
            }

            if (newPassword.Length > 20)
            {
                throw new ApilaneException(AppErrors.VALIDATION, "Maximum 20 characters", nameof(newPassword));
            }

            var rowsAffected = await _dataStore.UpdateDataAsync(
                nameof(Users),
                new Dictionary<string, object?>()
                {
                    { nameof(Users.Password), SqlUtilis.GetString(appEncryptionKey.ApplicationEncrypt(newPassword)) }
                },
                new FilterData(nameof(Users.ID), FilterData.FilterOperators.equal, currentUser.ID, PropertyType.Number));

            return rowsAffected == 1;
        }

        public async Task<List<string>> GetAuthTokensAsync(long userId)
        {
            var filter = new FilterData(FilterData.FilterLogic.AND, new List<FilterData>()
            {
                new(nameof(AuthTokens.Owner), FilterData.FilterOperators.equal, userId, PropertyType.Number)
            });

            var result = await _dataStore.GetPagedDataAsync(nameof(AuthTokens), null, filter, null, 1, 1000);

            return result.Select(x => Utils.GetString(x[nameof(AuthTokens.Token)])).ToList();
        }

        private async Task<Dictionary<string, object?>?> GetUserByEmailAndPasswordAsync(
            DBWS_Application application,
            string userEmail,
            string userPassword)
        {
            var result = await _dataStore.GetPagedDataAsync(
                nameof(Users),
                null,
                new FilterData(FilterData.FilterLogic.AND, new List<FilterData>()
                {
                    new(nameof(Users.Email), FilterData.FilterOperators.equal, userEmail, PropertyType.String),
                    new(nameof(Users.Password), FilterData.FilterOperators.equal, application.EncryptionKey.ApplicationEncrypt(userPassword), PropertyType.String)
                }),
                null, 1, 1);

            return ClearUserData(application, result?.Count == 1 ? result.Single() : null);
        }

        private async Task<Dictionary<string, object?>?> GetUserByNameAndPasswordAsync(
            DBWS_Application application,
            string userName,
            string userPassword)
        {
            var result = await _dataStore.GetPagedDataAsync(
                nameof(Users),
                null,
                new FilterData(FilterData.FilterLogic.AND, new List<FilterData>()
                {
                    new(nameof(Users.Username), FilterData.FilterOperators.equal, userName, PropertyType.String),
                    new(nameof(Users.Password), FilterData.FilterOperators.equal, application.EncryptionKey.ApplicationEncrypt(userPassword), PropertyType.String)
                }),
                null, 1, 1);

            return ClearUserData(application, result?.Count == 1 ? result.Single() : null);
        }

        private Dictionary<string, object?>? ClearUserData(
            DBWS_Application application,
            Dictionary<string, object?>? drUser)
        {
            if (drUser != null)
            {
                var entity = application.Entities.Single(x => x.Name.Equals(nameof(Users)));

                foreach (var property in entity.Properties.Where(x => x.Encrypted))
                {
                    var propertyValue = drUser[property.Name];
                    if (propertyValue is not null)
                    {
                        string appEncryptionKey = application.EncryptionKey.Decrypt(Globals.EncryptionKey);
                        drUser[property.Name] = Encryptor.Decrypt(propertyValue.ToString(), appEncryptionKey);
                    }
                }

                // IMPORTANT
                drUser[nameof(Users.Password)] = null;
            }

            return drUser;
        }
    }
}
