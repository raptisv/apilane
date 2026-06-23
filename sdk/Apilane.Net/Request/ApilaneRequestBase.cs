using System.Collections.Specialized;
using System.Linq;
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

        public string GetUrl(string apiUrl)
        {
            var baseParams = new NameValueCollection();
            if (!string.IsNullOrWhiteSpace(_entity))
            {
                baseParams.Add("Entity", _entity);
            }

            var listOfQueryStringValues = (
                from key in baseParams.AllKeys.Where(x => baseParams.GetValues(x) != null)
                from value in baseParams.GetValues(key)
                select string.Format("{0}={1}", key, value)).ToList();

            var extraParams = GetExtraParams();

            if (extraParams != null &&
                extraParams.HasKeys())
            {
                var extraParamsList = (
                from key in extraParams.AllKeys.Where(x => extraParams.GetValues(x) != null)
                from value in extraParams.GetValues(key)
                select string.Format("{0}={1}", key, value)).ToList();

                listOfQueryStringValues.AddRange(extraParamsList);
            }

            return $"{apiUrl?.TrimEnd('/')}/api/{_controller}/{_action}?" + string.Join("&", listOfQueryStringValues);
        }
    }
}