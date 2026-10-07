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
using Orleans.Placement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Grains
{
    /// <summary>
    /// Caches the user behind an authentication token. Keyed by the token plus the application token (the
    /// key extension), because a token is only valid in the application that issued it. Resolve it with
    /// <see cref="Apilane.Api.Core.Extensions.AuthTokenGrainFactoryExtensions.GetAuthTokenUserGrain"/>.
    /// </summary>
    public interface IAuthTokenUserGrain : IGrainWithGuidCompoundKey
    {
        Task<Users?> GetAsync(ApplicationDbInfoDto applicationDbInfo, int authTokenExpireMinutes);
        Task DeleteAsync(ApplicationDbInfoDto applicationDbInfo);
        Task ResetAsync();
        Task ResetUserCacheAsync();
    }

    // Prefer activating on the calling silo so that, when resolved from AuthTokenByIdGrain
    // (signed requests) or directly from the API controller (bearer), it tends to be co-located
    // with its caller and avoids an extra network hop.
    [PreferLocalPlacement]
    public class AuthTokenUserGrain : Grain, IAuthTokenUserGrain
    {
        private readonly ILogger<AuthTokenUserGrain> _logger;

        private Users? _user;
        private AuthTokens? _authToken;

        public AuthTokenUserGrain(ILogger<AuthTokenUserGrain> logger)
        {
            _logger = logger;
        }

        public async Task<Users?> GetAsync(ApplicationDbInfoDto applicationDbInfo, int authTokenExpireMinutes)
        {
            // The application is part of this grain's key. An activation reached without it (a silo still
            // running a version that keyed by token alone) is shared between applications: authenticate nobody.
            this.GetPrimaryKey(out var appToken);
            if (string.IsNullOrWhiteSpace(appToken))
            {
                return null;
            }

            await LoadStateAsync(applicationDbInfo);

            if (_authToken is null)
            {
                // The token does not exist, and LoadStateAsync has dropped any user cached while it did
                return null;
            }

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
                    // The filter names the token as well as its id: ids start again after an application
                    // is rebuilt, so the id alone could extend a newer token that reuses it.
                    await dataStore.UpdateDataAsync(
                        nameof(AuthTokens),
                        new Dictionary<string, object?>()
                        {
                            { nameof(AuthTokens.Created), Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow) }
                        },
                        new FilterData(FilterData.FilterLogic.AND, new List<FilterData>()
                        {
                            new FilterData(nameof(AuthTokens.ID), FilterData.FilterOperators.equal, _authToken.ID, PropertyType.Number),
                            new FilterData(nameof(AuthTokens.Token), FilterData.FilterOperators.equal, _authToken.Token, PropertyType.String)
                        }));
                }

                // Read the token and its user again. A row removed behind this grain (the user was
                // deleted, the owner removed it with SQL, the application was rebuilt) ends here, and
                // changes to the user (roles) take effect.
                _authToken = null;
                await LoadStateAsync(applicationDbInfo);

                if (_authToken is null)
                {
                    return null;
                }
            }

            return _user;
        }

        public Task ResetUserCacheAsync()
        {
            _user = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Drops both cached records after a bulk token revocation. Await this on every revoked token
        /// before reporting success: waiting for the normal refresh would keep a logged-out user authenticated.
        /// </summary>
        public Task ResetAsync()
        {
            _authToken = null;
            _user = null;
            DeactivateOnIdle();
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

            await ResetAsync();
        }

        private async Task LoadStateAsync(ApplicationDbInfoDto applicationDbInfo)
        {
            // Auth token
            if (_authToken is null)
            {
                // The user is read again whenever the token is. A token that no longer exists then has no
                // user, and changes made behind this grain (roles, a deleted user) stop being served from
                // the cache at the same point.
                _user = null;

                var authToken = this.GetPrimaryKey(out _).ToString();

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
