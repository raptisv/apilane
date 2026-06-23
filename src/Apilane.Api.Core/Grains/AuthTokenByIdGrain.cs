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
    public interface IAuthTokenByIdGrain : IGrainWithIntegerKey
    {
        /// <summary>
        /// Verifies a signed request for this grain's AuthTokens.ID and resolves the user.
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
    }

    /// <summary>
    /// Signed-request authentication grain, keyed by AuthTokens.ID. It caches the immutable
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

            // 4) Resolve the user via the GUID-keyed grain (which handles expiry + sliding)
            return await GrainFactory
                .GetGrain<IAuthTokenUserGrain>(guidAuthToken)
                .GetAsync(applicationDbInfo, authTokenExpireMinutes);
        }

        public Task ResetAsync()
        {
            _token = null;
            DeactivateOnIdle();
            return Task.CompletedTask;
        }

        private async Task<string?> LoadTokenAsync(ApplicationDbInfoDto applicationDbInfo)
        {
            if (_token is null)
            {
                var id = this.GetPrimaryKeyLong();

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
