using Apilane.Api.Core.Configuration;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Extensions;
using Apilane.Common.Models;
using Apilane.Common.Models.AppModules.Authentication;
using Apilane.Common.Models.Dto;
using Apilane.Data.Repository.Factory;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Grains
{
    public interface IAuthTokenUserGrain : IGrainWithGuidKey
    {
        Task<Users?> GetAsync(ApplicationDbInfoDto applicationDbInfo, int authTokenExpireMinutes);
        Task DeleteAsync(ApplicationDbInfoDto applicationDbInfo);
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

        public async Task<Users?> GetAsync(ApplicationDbInfoDto applicationDbInfo, int authTokenExpireMinutes)
        {
            await LoadStateAsync(applicationDbInfo);

            if (_authToken is not null)
            {
                var created = Utils.GetDateFromUnixTimestamp(_authToken.Created.ToString())
                    ?? throw new Exception($"Could not convert to datetime | {_authToken.Created}");

                // Validate token expiration
                if ((DateTime.UtcNow - created).TotalMinutes >= authTokenExpireMinutes)
                {
                    await DeleteAsync(applicationDbInfo);
                    return null;
                }

                // Update auth token to new expiration date
                var hasAuthTokenPassed10Percentile = (DateTime.UtcNow - created).TotalMinutes / (authTokenExpireMinutes * 1.0) > 0.1;
                if (hasAuthTokenPassed10Percentile)
                {
                    await using (var dataStore = new ApplicationDataStoreFactory(applicationDbInfo))
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

        public async Task DeleteAsync(ApplicationDbInfoDto applicationDbInfo)
        {
            await LoadStateAsync(applicationDbInfo);

            if (_authToken is not null)
            {
                await using (var dataStore = new ApplicationDataStoreFactory(applicationDbInfo))
                {
                    await dataStore.DeleteDataAsync(
                        nameof(AuthTokens),
                        new FilterData(nameof(AuthTokens.Token), FilterData.FilterOperators.equal, _authToken.Token, PropertyType.String));
                }
            }

            DeactivateOnIdle();
        }

        private async Task LoadStateAsync(ApplicationDbInfoDto applicationDbInfo)
        {
            // Auth token
            if (_authToken is null)
            {
                var authToken = this.GetPrimaryKey().ToString();

                await using (var dataStore = new ApplicationDataStoreFactory(applicationDbInfo))
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
                await using (var dataStore = new ApplicationDataStoreFactory(applicationDbInfo))
                {
                    var resultUser = await dataStore.GetDataByIdAsync(nameof(Users), _authToken.Owner, null);

                    if (resultUser is not null)
                    {
                        var diffPropertyValue = (long?)null;
                        if (!string.IsNullOrWhiteSpace(applicationDbInfo.DifferentiationEntity))
                        {
                            var diffPropertyName = applicationDbInfo.DifferentiationEntity.GetDifferentiationPropertyName();
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
