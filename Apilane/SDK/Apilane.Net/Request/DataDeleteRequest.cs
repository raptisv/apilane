using System.Collections.Generic;
using System.Collections.Specialized;

namespace Apilane.Net.Request
{
    public class DataDeleteRequest : ApilaneRequestBase<DataDeleteRequest>
    {
        public static DataDeleteRequest New(string entity) => new(entity);
        public static DataDeleteRequest New(string entity, List<long> Ids) => new(entity, Ids);

        private List<long> _Ids = new();

        private DataDeleteRequest(string entity) : base(entity, "Data", "Delete")
        {
        }

        private DataDeleteRequest(string entity, List<long> Ids) : base(entity, "Data", "Delete")
        {
            _Ids = Ids;
        }

        public DataDeleteRequest AddIdToDelete(long id)
        {
            _Ids ??= new List<long>();

            _Ids.Add(id);

            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection
            {
                { "ids", string.Join(",", _Ids) }
            };

            return extraParams;
        }
    }
}
