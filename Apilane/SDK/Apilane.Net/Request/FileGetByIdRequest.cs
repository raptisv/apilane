using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Apilane.Net.Request
{
    public class FileGetByIdRequest : ApilaneRequestBase
    {
        public static FileGetByIdRequest New(long id) => new(id);

        private long _id;
        private List<string>? _properties;

        private FileGetByIdRequest(long id) : base(null, "Files", "GetByID")
        {
            _id = id;
        }

        public FileGetByIdRequest WithProperties(params string[] properties)
        {
            _properties = properties?.ToList();
            return this;
        }

        public FileGetByIdRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection
            {
                { "id", _id.ToString() }
            };

            if (_properties != null && _properties.Any())
            {
                extraParams.Add("properties", string.Join(",", _properties));
            }

            return extraParams;
        }
    }
}
