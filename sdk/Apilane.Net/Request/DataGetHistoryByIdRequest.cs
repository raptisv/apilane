using System.Collections.Specialized;

namespace Apilane.Net.Request
{
    public class DataGetHistoryByIdRequest : ApilaneRequestBase<DataGetHistoryByIdRequest>
    {
        public static DataGetHistoryByIdRequest New(string entity, long id) => new(entity, id);

        private long _id;
        private int _pageIndex = 1, _pageSize = 10;

        private DataGetHistoryByIdRequest(string entity, long id) : base(entity, "Data", "GetHistoryByID")
        {
            _id = id;
        }

        public DataGetHistoryByIdRequest WithPageIndex(int pageIndex)
        {
            _pageIndex = pageIndex;
            return this;
        }

        public DataGetHistoryByIdRequest WithPageSize(int pageSize)
        {
            _pageSize = pageSize;
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            return new NameValueCollection
            {
                { "id", _id.ToString() },
                { "pageIndex", _pageIndex.ToString() },
                { "pageSize", _pageSize.ToString() }
            };
        }
    }
}
