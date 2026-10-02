using Apilane.Api.Core.Extensions;
using Apilane.Api.Core.Models.AppModules.Authentication;
using Apilane.Common;
using Apilane.Common.Enums;
using Apilane.Common.Models;
using Apilane.Common.Models.AppModules.Authentication;
using Apilane.Common.Models.Dto;
using Apilane.Common.Security;
using Apilane.Data.Repository.Factory;
using Orleans;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Apilane.Api.Core.Grains
{
    public interface IAuthTokenByIdGrain : IGrainWithIntegerCompoundKey
    {
        /// <summary>
        /// Verifies a signed request for this grain's application and AuthTokens.ID and resolves the user.
        /// Performs the timestamp-window check and the HMAC comparison using the cached secret, then
        /// delegates user resolution (and expiry/sliding) to <see cref="IAuthTokenUserGrain"/>.
        /// Returns the user on success, or a failure reason the caller can surface as UNAUTHORIZED.
        /// </summary>
        Task<Users?> VerifyAndGetUserAsync(
            ApplicationDbInfoDto applicationDbInfo,
            int authTokenExpireMinutes,
            long timestampMs,
            string canonicalString,
            string providedSignature);

        Task ResetAsync();

        /// <summary>
        /// Deletes the token behind this key id. A signed request does not carry the token, so this is how
        /// such a request logs out or renews. Call it only for a request whose signature was verified.
        /// </summary>
        Task DeleteAsync(ApplicationDbInfoDto applicationDbInfo);

        /// <summary>
        /// Drops the user cached for the token behind this key id, so that it is read again on the next request.
        /// </summary>
        Task ResetUserCacheAsync(ApplicationDbInfoDto applicationDbInfo);
    }

    /// <summary>
    /// Signed-request authentication grain, keyed by AuthTokens.ID plus the application token (the key
    /// extension): row ids start at 1 in every application, so the id alone would make applications share
    /// a grain and verify with each other's secrets. Resolve it with
    /// <see cref="AuthTokenGrainFactoryExtensions.GetAuthTokenByIdGrain"/>. It caches the immutable
    /// AuthTokens.ID -> token (secret) mapping so verification does not hit the database on every
    /// request, performs the HMAC verification (the secret never leaves the grain), and resolves the
    /// user via <see cref="IAuthTokenUserGrain"/> — which is [PreferLocalPlacement] so it tends to
    /// activate on this grain's silo, avoiding an extra network hop.
    /// </summary>
    public class AuthTokenByIdGrain : Grain, IAuthTokenByIdGrain
    {
        private string? _token;

        public async Task<Users?> VerifyAndGetUserAsync(
            ApplicationDbInfoDto applicationDbInfo,
            int authTokenExpireMinutes,
            long timestampMs,
            string canonicalString,
            string providedSignature)
        {
            // The application is part of this grain's key. An activation reached without it (a silo still
            // running a version that keyed by id alone) is shared between applications, so it verifies nothing.
            this.GetPrimaryKeyLong(out var appToken);
            if (string.IsNullOrWhiteSpace(appToken))
            {
                return null;
            }

            // 1) Reject stale/future requests outside the allowed clock-skew window (replay guard)
            var nowMs = Utils.GetUnixTimestampMilliseconds(DateTime.UtcNow);
            if (Math.Abs(nowMs - timestampMs) > Globals.SignedRequestClockSkewSeconds * 1000L)
            {
                return null;
            }

            // 2) Resolve the secret (the AuthToken GUID) from the public key id (cached)
            var token = await LoadTokenAsync(applicationDbInfo);
            if (string.IsNullOrWhiteSpace(token) || !Guid.TryParse(token, out var guidAuthToken))
            {
                return null;
            }

            // 3) Recompute the signature and compare in constant time
            var expectedSignature = RequestSignature.ComputeSignature(token!, canonicalString);
            if (!RequestSignature.SignaturesMatch(expectedSignature, providedSignature))
            {
                return null;
            }

            // 4) Resolve the user via the token grain of the same application (which handles expiry + sliding)
            return await GrainFactory
                .GetAuthTokenUserGrain(appToken, guidAuthToken)
                .GetAsync(applicationDbInfo, authTokenExpireMinutes);
        }

        public Task ResetAsync()
        {
            _token = null;
            DeactivateOnIdle();
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(ApplicationDbInfoDto applicationDbInfo)
        {
            var userGrain = await GetUserGrainAsync(applicationDbInfo);
            if (userGrain is not null)
            {
                await userGrain.DeleteAsync(applicationDbInfo);
            }

            _token = null;
            DeactivateOnIdle();
        }

        public async Task ResetUserCacheAsync(ApplicationDbInfoDto applicationDbInfo)
        {
            var userGrain = await GetUserGrainAsync(applicationDbInfo);
            if (userGrain is not null)
            {
                await userGrain.ResetUserCacheAsync();
            }
        }

        // The token grain of this key id, in the same application. The token itself stays inside the grains.
        private async Task<IAuthTokenUserGrain?> GetUserGrainAsync(ApplicationDbInfoDto applicationDbInfo)
        {
            this.GetPrimaryKeyLong(out var appToken);
            if (string.IsNullOrWhiteSpace(appToken))
            {
                return null;
            }

            var token = await LoadTokenAsync(applicationDbInfo);
            if (!Guid.TryParse(token, out var guidAuthToken))
            {
                return null;
            }

            return GrainFactory.GetAuthTokenUserGrain(appToken, guidAuthToken);
        }

        private async Task<string?> LoadTokenAsync(ApplicationDbInfoDto applicationDbInfo)
        {
            if (_token is null)
            {
                var id = this.GetPrimaryKeyLong(out _);

                await using var dataStore = new ApplicationDataStoreFactory(applicationDbInfo);

                var row = (await dataStore.GetPagedDataAsync(
                    nameof(AuthTokens),
                    null,
                    new FilterData(nameof(AuthTokens.ID), FilterData.FilterOperators.equal, id, PropertyType.Number),
                    null,
                    1,
                    1)).SingleOrDefault();

                if (row is not null)
                {
                    _token = Utils.GetString(row[nameof(AuthTokens.Token)]);
                }
            }

            return _token;
        }
    }
}
