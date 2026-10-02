using Apilane.Api.Core.Grains;
using Orleans;
using System;

namespace Apilane.Api.Core.Extensions
{
    public static class GrainFactoryExtensions
    {
        /// <summary>
        /// Resolves the grain that caches the user behind an authentication token. The grain is keyed by
        /// the application as well as the token: a token only means something inside the application that
        /// issued it, so a grain shared between applications would answer one application with the user
        /// cached for another.
        /// </summary>
        public static IAuthTokenUserGrain GetAuthTokenUserGrain(this IGrainFactory grainFactory, string appToken, Guid authToken)
        {
            return grainFactory.GetGrain<IAuthTokenUserGrain>(authToken, NormalizeAppToken(appToken), null);
        }

        /// <summary>
        /// Resolves the signed-request grain for an AuthTokens.ID. Row ids start at 1 in every application,
        /// so the grain is keyed by the application as well as the id.
        /// </summary>
        public static IAuthTokenByIdGrain GetAuthTokenByIdGrain(this IGrainFactory grainFactory, string appToken, long authTokenId)
        {
            return grainFactory.GetGrain<IAuthTokenByIdGrain>(authTokenId, NormalizeAppToken(appToken), null);
        }

        /// <summary>
        /// One spelling per application: application tokens are GUIDs and are resolved by value, so
        /// "ABC..." and "{abc...}" must lead to the same grains.
        /// </summary>
        private static string NormalizeAppToken(string appToken)
        {
            if (string.IsNullOrWhiteSpace(appToken))
            {
                throw new ArgumentException("The application token is required to resolve an auth token grain", nameof(appToken));
            }

            return Guid.TryParse(appToken, out var guid)
                ? guid.ToString()
                : appToken.Trim().ToLowerInvariant();
        }
    }
}
