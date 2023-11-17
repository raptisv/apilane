using Apilane.Common;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Web.Api.Services
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

                return string.Empty;
            }
        }

        public string AppToken
        {
            get
            {
                // On manage and help controllers search for apptoken route value
                if (RouteController == "manage" || RouteController == "help")
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
        }

        public string IPAddress
        {
            get
            {
                return GetRequestIP() ?? string.Empty;
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
                    ? Utils.GetString(_actionContextAccessor.ActionContext?.RouteData?.Values["id"])
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
                if (item.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.Value))
                {
                    return Utils.GetString(item.Value);
                }
            }

            return string.Empty;
        }

        private string? GetRequestIP(bool tryUseXForwardHeader = true)
        {
            string? ip = null;

            // support new "Forwarded" header (2014) https://en.wikipedia.org/wiki/X-Forwarded-For

            // X-Forwarded-For (csv list):  Using the First entry in the list seems to work
            // for 99% of cases however it has been suggested that a better (although tedious)
            // approach might be to read each IP from right to left and use the first public IP.
            // http://stackoverflow.com/a/43554000/538763
            if (tryUseXForwardHeader)
            {
                ip = SplitCsv(GetHeaderValueAs<string?>("X-Forwarded-For"))?.FirstOrDefault();
            }

            // RemoteIpAddress is always null in DNX RC1 Update1 (bug).
            if (string.IsNullOrWhiteSpace(ip) && _actionContextAccessor.ActionContext?.HttpContext?.Connection?.RemoteIpAddress != null)
            {
                ip = _actionContextAccessor.ActionContext.HttpContext.Connection.RemoteIpAddress?.ToString();
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = GetHeaderValueAs<string>("REMOTE_ADDR");
            }

            // _httpContextAccessor.HttpContext?.Request?.Host this is the local host.

            return ip;
        }

        public T? GetHeaderValueAs<T>(string headerName)
        {
            StringValues values;

            if (_actionContextAccessor.ActionContext?.HttpContext?.Request?.Headers?.TryGetValue(headerName, out values) ?? false)
            {
                string rawValues = values.ToString();   // writes out as Csv when there are multiple.

                if (!string.IsNullOrWhiteSpace(rawValues))
                {
                    return (T)Convert.ChangeType(values.ToString(), typeof(T));
                }
            }
            return default(T);
        }

        public static List<string>? SplitCsv(string? csvList, bool nullOrWhitespaceInputReturnsNull = false)
        {
            if (string.IsNullOrWhiteSpace(csvList))
            {
                return nullOrWhitespaceInputReturnsNull ? null : new List<string>();
            }

            return csvList
                .TrimEnd(',')
                .Split(',')
                .AsEnumerable<string>()
                .Select(s => s.Trim())
                .ToList();
        }
    }
}
