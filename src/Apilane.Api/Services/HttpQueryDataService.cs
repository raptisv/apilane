using Apilane.Common;
using Apilane.Common.Utilities;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Api.Services
{
    public class HttpQueryDataService : IQueryDataService
    {
        private readonly IActionContextAccessor _actionContextAccessor;

        public HttpQueryDataService(
            IActionContextAccessor actionContextAccessor)
        {
            _actionContextAccessor = actionContextAccessor;
        }

        public string RouteController
        {
            get
            {
                return _actionContextAccessor.ActionContext?.RouteData.Values["controller"]?.ToString()?.ToLower() ?? string.Empty;
            }
        }

        public string RouteAction
        {
            get
            {
                var _actionName = _actionContextAccessor.ActionContext?.RouteData.Values["action"]?.ToString()?.ToLower() ?? string.Empty;
                return string.IsNullOrWhiteSpace(_actionName)
                    ? _actionContextAccessor.ActionContext?.HttpContext.Request.Method.ToLower() ?? string.Empty
                    : _actionName;
            }
        }

        public string AuthToken
        {
            get
            {
                // Header
                var tokenFromHeader = _actionContextAccessor.ActionContext?.HttpContext?.Request?.Headers?.Authorization.ToString();

                if (!string.IsNullOrWhiteSpace(tokenFromHeader))
                {
                    // Remove the "Bearer" part of the token
                    return tokenFromHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last();
                }

                // A plain navigation (e.g. a file download link built as an <a href>, which can't
                // set custom headers) falls back to the query string, same as AppToken does.
                return GetUriValue(Globals.AuthTokenQueryParam);
            }
        }

        public string AppToken
        {
            get
            {
                return ToCanonicalAppToken(GetRequestAppToken());
            }
        }

        private string GetRequestAppToken()
        {
            // On the manage controller search for apptoken route value
            if (RouteController == "manage")
            {
                return _actionContextAccessor.ActionContext?.RouteData.Values["apptoken"]?.ToString() ?? string.Empty;
            }

            // All other calls

            var tokenFromHeader = GetHeaderValue(Globals.ApplicationTokenHeaderName);

            if (!string.IsNullOrWhiteSpace(tokenFromHeader))
            {
                return tokenFromHeader;
            }

            return GetUriValue(Globals.ApplicationTokenQueryParam);
        }

        /// <summary>
        /// An application token is a GUID, and the application is found by its value, so upper case,
        /// braces or no dashes all reach the same application. Everything else that is keyed on the
        /// token (the Portal's ownership answer, rate limits, caches, logs) compares it as text, so the
        /// request's spelling is replaced by the one spelling the Portal issues and stores.
        /// </summary>
        private static string ToCanonicalAppToken(string appToken)
        {
            return Guid.TryParse(appToken, out var guid)
                ? guid.ToString()
                : appToken;
        }

        public string IPAddress
        {
            get
            {
                var httpContext = _actionContextAccessor.ActionContext?.HttpContext;

                // A header that was sent several times reads as one comma separated value.
                return ClientIpResolver.Resolve(
                    httpContext?.Request.Headers["X-Forwarded-For"].ToString(),
                    httpContext?.Connection.RemoteIpAddress?.ToString(),
                    httpContext?.Request.Headers["REMOTE_ADDR"].ToString()) ?? string.Empty;
            }
        }

        public string Entity
        {
            get
            {
                return RouteController.Equals("custom")
                    ? string.Empty
                    : GetUriValue("Entity");
            }
        }

        public string CustomEndpoint
        {
            get
            {
                return RouteController.Equals("custom")
                    ? Utils.GetString(_actionContextAccessor.ActionContext?.RouteData?.Values["name"])
                    : string.Empty;
            }
        }

        public Dictionary<string, string> UriParams
        {
            get
            {
                return _actionContextAccessor.ActionContext?.HttpContext.Request.
                    Query.ToDictionary(q => q.Key, q => q.Value).ToDictionary(x => x.Key, v => v.Value.ToString())
                    ?? new Dictionary<string, string>();
            }
        }

        public bool IsPortalRequest
        {
            get
            {
                var clientId = GetHeaderValue(Globals.ClientIdHeaderName);

                if (string.IsNullOrWhiteSpace(clientId))
                {
                    // Same fallback reasoning as AuthToken above.
                    clientId = GetUriValue(Globals.ClientIdQueryParam);
                }

                return !string.IsNullOrWhiteSpace(clientId) && clientId.Equals(Globals.ClientIdHeaderValuePortal, StringComparison.OrdinalIgnoreCase);
            }
        }

        private string GetHeaderValue(string key)
        {
            if (_actionContextAccessor.ActionContext?.HttpContext.Request.Headers.TryGetValue(key, out var value) ?? false)
            {
                return Utils.GetString(value);
            }

            return string.Empty;
        }

        private string GetUriValue(string key)
        {
            var queryString = _actionContextAccessor.ActionContext?.HttpContext.Request.Query.ToDictionary(q => q.Key, q => q.Value).ToList()
                ?? new List<KeyValuePair<string, StringValues>>();

            foreach (var item in queryString)
            {
                // A repeated parameter (?entity=a&entity=b) reaches the action as its first value. Use the
                // same one here: joined together ("a,b") it would name nothing, and the checks made on it
                // (rate limits) would be skipped for a request that still runs against "a".
                var value = item.Value.Count > 0 ? item.Value[0] : null;

                if (item.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
