using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apilane.Net.Request
{
    public abstract class ApilaneRequestBase<TBuilder> where TBuilder : ApilaneRequestBase<TBuilder>
    {
        protected static JsonSerializerOptions JsonSerializerOptions = new()
        {
            Converters =
            {
                new JsonStringEnumConverter() // Required to serialize enumerations as strings
            }
        };

        protected string?
            _controller,
            _action,
            _entity,
            _authToken;

        protected long? _signingKeyId;
        protected string? _signingSecret;

        protected bool _throwOnError = false;

        protected ApilaneRequestBase(string? entity, string controller, string action)
        {
            _controller = controller;
            _action = action;
            _entity = entity;

            if (_entity != null &&
                _entity.Trim().ToLower().Equals("files"))
            {
                _entity = null;
                _controller = "Files";
            }
        }

        internal bool HasAuthToken(out string authToken)
        {
            authToken = _authToken ?? string.Empty;
            return !string.IsNullOrWhiteSpace(_authToken);
        }

        internal bool ShouldThrowExceptionOnError()
        {
            return _throwOnError;
        }

        /// <summary>
        /// Use this method to directly throw exception instead of checking for error on each request.
        /// The exception is an <see cref="Models.Data.ApilaneErrorException"/>, with the error and the HTTP status.
        /// </summary>
        public TBuilder OnErrorThrowException(bool throwOnError = false)
        {
            _throwOnError = throwOnError;
            return (TBuilder)this;
        }

        /// <summary>
        /// Authenticate this request with a bearer auth token.
        /// Mutually exclusive with <see cref="WithSigning"/>.
        /// </summary>
        public TBuilder WithAuthToken(string authToken)
        {
            if (_signingKeyId.HasValue)
            {
                throw new System.InvalidOperationException("A request cannot use both WithAuthToken and WithSigning. Choose one authentication method.");
            }

            _authToken = authToken;
            return (TBuilder)this;
        }

        /// <summary>
        /// Authenticate this request by signing it (HMAC proof-of-possession) instead of sending
        /// the token. Pass the values returned at login: <paramref name="keyId"/> is the AuthTokenID
        /// and <paramref name="secret"/> is the AuthToken itself. The SDK computes the signature
        /// locally from the token and never transmits the token.
        /// Mutually exclusive with <see cref="WithAuthToken"/>.
        /// </summary>
        /// <param name="keyId">The AuthTokenID returned at login.</param>
        /// <param name="secret">The AuthToken returned at login (used as the signing key; never sent).</param>
        public TBuilder WithSigning(long keyId, string secret)
        {
            if (!string.IsNullOrWhiteSpace(_authToken))
            {
                throw new System.InvalidOperationException("A request cannot use both WithSigning and WithAuthToken. Choose one authentication method.");
            }

            _signingKeyId = keyId;
            _signingSecret = secret;
            return (TBuilder)this;
        }

        internal bool HasSigning(out long keyId, out string secret)
        {
            keyId = _signingKeyId ?? 0;
            secret = _signingSecret ?? string.Empty;
            return _signingKeyId.HasValue && !string.IsNullOrWhiteSpace(_signingSecret);
        }

        protected virtual NameValueCollection? GetExtraParams()
        {
            return null;
        }

        /// <summary>
        /// A read whose address would be longer than this is sent with the HTTP QUERY method (RFC 10008) instead of
        /// GET, its parameters in a JSON body. The API refuses request lines above 8 KB, and proxies often refuse less.
        /// Shorter reads stay GET, which every proxy and firewall passes.
        /// </summary>
        public const int MaxGetUrlLength = 4096;

        internal static readonly HttpMethod QueryMethod = new HttpMethod("QUERY");

        /// <summary>
        /// The JSON body of this request when it is sent with the QUERY method, or null for a request that is always
        /// a GET. It holds the parameters of <see cref="GetExtraParams"/>, with the filter and the sort as JSON.
        /// </summary>
        protected virtual Dictionary<string, object?>? GetQueryBody()
        {
            return null;
        }

        /// <summary>
        /// The request of a read: a GET, or a QUERY when the address of the GET would be longer than
        /// <see cref="MaxGetUrlLength"/> and the endpoint accepts QUERY.
        /// </summary>
        internal HttpRequestMessage CreateReadRequest(string apiUrl)
        {
            var url = GetUrl(apiUrl);
            var body = url.Length > MaxGetUrlLength ? GetQueryBody() : null;

            if (body is null)
            {
                return new HttpRequestMessage(HttpMethod.Get, url);
            }

            return new HttpRequestMessage(QueryMethod, GetUrl(apiUrl, includeExtraParams: false))
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions), Encoding.UTF8, "application/json")
            };
        }

        public string GetUrl(string apiUrl)
        {
            return GetUrl(apiUrl, includeExtraParams: true);
        }

        private string GetUrl(string apiUrl, bool includeExtraParams)
        {
            var baseParams = new NameValueCollection();
            if (!string.IsNullOrWhiteSpace(_entity))
            {
                baseParams.Add("Entity", _entity);
            }

            // Keys and values are URL-encoded: unencoded, a value like "cust%41" reaches the API as "custA",
            // '#' cuts the query off and "%22" closes the JSON string of a filter.
            var listOfQueryStringValues = (
                from key in baseParams.AllKeys.Where(x => baseParams.GetValues(x) != null)
                from value in baseParams.GetValues(key)
                select string.Format("{0}={1}", EncodeQueryPart(key), EncodeQueryPart(value))).ToList();

            var extraParams = includeExtraParams ? GetExtraParams() : null;

            if (extraParams != null &&
                extraParams.HasKeys())
            {
                var extraParamsList = (
                from key in extraParams.AllKeys.Where(x => extraParams.GetValues(x) != null)
                from value in extraParams.GetValues(key)
                select string.Format("{0}={1}", EncodeQueryPart(key), EncodeQueryPart(value))).ToList();

                listOfQueryStringValues.AddRange(extraParamsList);
            }

            var url = $"{apiUrl?.TrimEnd('/')}/api/{_controller}/{_action}";

            return listOfQueryStringValues.Count == 0 ? url : url + "?" + string.Join("&", listOfQueryStringValues);
        }

        private static string EncodeQueryPart(string value)
        {
            // ',' and ':' are safe inside a query and are kept readable: escaping them only lengthens id lists
            // and JSON filters, and the API rejects request lines above 8 KB. A literal "%2C" in the value is
            // escaped to "%252C", so the replacements cannot change a value.
            return Uri.EscapeDataString(value).Replace("%2C", ",").Replace("%3A", ":");
        }
    }
}