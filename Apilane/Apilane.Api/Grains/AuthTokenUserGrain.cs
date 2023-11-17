using Apilane.Api.Configuration;
using Apilane.Api.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.AppModules.Authentication;
using Apilane.Data.Repository.Factory;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Grains
{
    public interface IAuthTokenUserGrain : IGrainWithGuidKey
    {
        Task<Users?> GetAsync(DBWS_Application application);
        Task DeleteAsync(DBWS_Application application);
        Task ResetUserCacheAsync();
    }

    public class AuthTokenUserGrain : Grain, IAuthTokenUserGrain
    {
        private readonly ILogger<AuthTokenUserGrain> _logger;
        private readonly ApiConfiguration _apiConfiguration;

        private Users? _user;
        private AuthTokens? _authToken;

        public AuthTokenUserGrain(
            ILogger<AuthTokenUserGrain> logger,
            ApiConfiguration apiConfiguration)
        {
            _logger = logger;
            _apiConfiguration = apiConfiguration;
        }

        public async Task<Users?> GetAsync(DBWS_Application application)
        {
            await LoadStateAsync(application);

            if (_authToken is not null)
            {
                var created = Utils.GetDateFromUnixTimestamp(_authToken.Created.ToString())
                    ?? throw new Exception($"Could not convert to datetime | {_authToken.Created}");

                // Validate token expiration
                if ((DateTime.UtcNow - created).TotalMinutes >= application.AuthTokenExpireMinutes)
                {
                    await DeleteAsync(application);
                    return null;
                }

                // Update auth token to new expiration date
                var hasAuthTokenPassed10Percentile = (DateTime.UtcNow - created).TotalMinutes / (application.AuthTokenExpireMinutes * 1.0) > 0.1;
                if (hasAuthTokenPassed10Percentile)
                {
                    await using (var dataStore = new ApplicationDataStoreFactory(_apiConfiguration.FilesPath, new Lazy<Task<DBWS_Application>>(Task.Run(() => application))))
                    {
                        // Update is heavy so, do not update for the first 10% of the time passed.
                        await dataStore.UpdateDataAsync(
                            nameof(AuthTokens),
                            new Dictionary<string, object?>()
                            {
                                { nameof(AuthTokens.Created), Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) }
                            },
                            new FilterData(nameof(AuthTokens.ID), FilterData.FilterOperators.equal, _authToken.ID, PropertyType.Number));
                    }

                    // Set to null to force reload on next run
                    _authToken = null;
                }
            }

            return _user;
        }

        public Task ResetUserCacheAsync()
        {
            _user = null;
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(DBWS_Application application)
        {
            await LoadStateAsync(application);

            if (_authToken is not null)
            {
                await using (var dataStore = new ApplicationDataStoreFactory(_apiConfiguration.FilesPath, new Lazy<Task<DBWS_Application>>(Task.Run(() => application))))
                {
                    await dataStore.DeleteDataAsync(
                        nameof(AuthTokens),
                        new FilterData(nameof(AuthTokens.Token), FilterData.FilterOperators.equal, _authToken.Token, PropertyType.String));
                }
            }

            DeactivateOnIdle();
        }

        private async Task LoadStateAsync(DBWS_Application application)
        {
            // Auth token
            if (_authToken is null)
            {
                var authToken = this.GetPrimaryKey().ToString();

                await using (var dataStore = new ApplicationDataStoreFactory(_apiConfiguration.FilesPath, new Lazy<Task<DBWS_Application>>(Task.Run(() => application))))
                {
                    var authTokenRecord = (await dataStore.GetPagedDataAsync(
                        nameof(AuthTokens),
                        null,
                        new FilterData(nameof(AuthTokens.Token), FilterData.FilterOperators.equal, authToken, PropertyType.String),
                        null, 1, 1)).SingleOrDefault();

                    if (authTokenRecord is not null)
                    {
                        _authToken = new AuthTokens()
                        {
                            ID = Utils.GetLong(authTokenRecord[nameof(AuthTokens.ID)]),
                            Owner = Utils.GetLong(authTokenRecord[nameof(AuthTokens.Owner)]),
                            Created = Utils.GetLong(authTokenRecord[nameof(AuthTokens.Created)]),
                            Token = authToken
                        };
                    }
                }
            }

            // User
            if (_user is null &&
                _authToken is not null)
            {
                await using (var dataStore = new ApplicationDataStoreFactory(_apiConfiguration.FilesPath, new Lazy<Task<DBWS_Application>>(Task.Run(() => application))))
                {
                    var resultUser = await dataStore.GetDataByIdAsync(nameof(Users), _authToken.Owner, null);

                    if (resultUser is not null)
                    {
                        var diffPropertyValue = (long?)null;
                        if (!string.IsNullOrWhiteSpace(application.DifferentiationEntity))
                        {
                            var diffPropertyName = application.DifferentiationEntity.GetDifferentiationPropertyName();
                            diffPropertyValue = Utils.GetNullLong(resultUser[diffPropertyName]);
                        }

                        _user = new Users
                        {
                            ID = _authToken.Owner,
                            Created = Utils.GetLong(resultUser[nameof(Users.Created)], 0),
                            Email = Utils.GetString(resultUser[nameof(Users.Email)]),
                            EmailConfirmed = Utils.GetBool(resultUser[nameof(Users.EmailConfirmed)]),
                            LastLogin = Utils.GetLong(resultUser[nameof(Users.LastLogin)], 0),
                            Roles = Utils.GetString(resultUser[nameof(Users.Roles)]),
                            Username = Utils.GetString(resultUser[nameof(Users.Username)]),
                            Password = null!,
                            DifferentiationPropertyValue = diffPropertyValue
                        };
                    }
                }
            }
        }
    }
}
