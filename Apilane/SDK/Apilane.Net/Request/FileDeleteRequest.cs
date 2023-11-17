using System.Collections.Generic;
using System.Collections.Specialized;

namespace Apilane.Net.Request
{
    public class FileDeleteRequest : ApilaneRequestBase
    {
        public static FileDeleteRequest New() => new();
        public static FileDeleteRequest New(List<long> Ids) => new(Ids);

        private List<long> _Ids = new();

        private FileDeleteRequest() : base(null, "Files", "Delete")
        {
        }

        private FileDeleteRequest(List<long> Ids) : base(null, "Files", "Delete")
        {
            _Ids = Ids;
        }

        public FileDeleteRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }

        public FileDeleteRequest AddIdToDelete(long id)
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
