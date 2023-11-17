using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Apilane.Net.Request
{
    public class CustomEndpointRequest : ApilaneRequestBase
    {
        public static CustomEndpointRequest New(string endpoint) => new(endpoint);

        private Dictionary<string, long?> _parameters = new();

        private CustomEndpointRequest(string endpoint) : base(null, "Custom", endpoint)
        {

        }

        public CustomEndpointRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }

        public CustomEndpointRequest(string endpoint, Dictionary<string, long?> parameters) : base(null, "Custom", endpoint)
        {
            _parameters = parameters;
        }

        public CustomEndpointRequest WithParameter(string key, long? value)
        {
            if (_parameters == null)
            {
                _parameters = new Dictionary<string, long?>();
            }

            _parameters.Add(key, value);
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection();

            if (_parameters != null)
            {
                foreach (var p in _parameters.AsEnumerable().Where(x => x.Value.HasValue))
                {
                    extraParams.Add(p.Key, p.Value!.Value.ToString());
                }
            }

            return extraParams;
        }
    }
}
