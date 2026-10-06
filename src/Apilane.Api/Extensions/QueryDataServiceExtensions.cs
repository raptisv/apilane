using Apilane.Api.Services;
using Apilane.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Api.Extensions
{
    public static class QueryDataServiceExtensions
    {
        private const string MaskedValue = "***";

        // A parameter whose name holds one of these carries a secret: the one-time token of the link in a
        // confirmation or a password reset mail (token), the session of a file download link (authToken), a
        // password or a signed link. A rule by name: a secret in a parameter named otherwise, or inside the
        // JSON of a filter, is not caught.
        private static readonly string[] SecretNameParts = { "token", "password", "secret", "signature" };

        /// <summary>
        /// The query string of the request as the logs get it: the values of the parameters that carry a
        /// secret are masked, as a sink such as Graylog stores the scope of an entry for anyone to read.
        /// <see cref="IQueryDataService.UriParams"/> is not changed: custom endpoints get the real values.
        /// </summary>
        public static Dictionary<string, string> GetUriParamsForLog(this IQueryDataService queryDataService)
        {
            return queryDataService.UriParams.ToDictionary(
                x => x.Key,
                x => IsSecret(x.Key) ? MaskedValue : x.Value);
        }

        private static bool IsSecret(string name)
        {
            // The application token is the public identifier of the application, in every URL and every log entry
            return !name.Equals(Globals.ApplicationTokenQueryParam, StringComparison.OrdinalIgnoreCase) &&
                SecretNameParts.Any(part => name.Contains(part, StringComparison.OrdinalIgnoreCase));
        }
    }
}
