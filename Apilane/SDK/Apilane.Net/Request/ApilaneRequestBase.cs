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
        public TBuilder OnErrorThrowException()
        {
            _throwOnError = true;
            return (TBuilder)this;
        }

        /// <summary>
        /// Use this method to override the auth token provided from <see cref="Apilane.Net.Abstractions.IApilaneAuthTokenProvider"/>, if any.
        /// </summary>
        public TBuilder WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return (TBuilder)this;
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