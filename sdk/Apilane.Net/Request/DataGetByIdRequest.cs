using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Apilane.Net.Request
{
    public class DataGetByIdRequest : ApilaneRequestBase<DataGetByIdRequest>
    {
        public static DataGetByIdRequest New(string entity, long id) => new(entity, id);

        private long _id;
        private List<string>? _properties;

        private DataGetByIdRequest(string entity, long id) : base(entity, "Data", "GetByID")
        {
            _id = id;
        }

        public DataGetByIdRequest WithProperties(params string[] properties)
        {
            _properties = properties?.ToList();
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
