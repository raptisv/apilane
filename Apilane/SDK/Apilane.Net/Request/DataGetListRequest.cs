using Apilane.Net.Models.Data;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;

namespace Apilane.Net.Request
{
    public class DataGetListRequest : ApilaneRequestBase<DataGetListRequest>
    {
        public static DataGetListRequest New(string entity) => new(entity);

        protected int _pageIndex = 1, _pageSize = 20;
        protected FilterItem? _filter = null;
        protected SortItem? _sort = null;
        private List<string>? _properties;
        private bool _geTtotal = false;

        private DataGetListRequest(string entity) : base(entity, "Data", "Get")
        {

        }

        public DataGetListRequest WithPageIndex(int pageIndex)
        {
            _pageIndex = pageIndex;
            return this;
        }

        public DataGetListRequest WithPageSize(int pageSize)
        {
            _pageSize = pageSize;
            return this;
        }

        public DataGetListRequest WithFilter(FilterItem filterItem)
        {
            _filter = filterItem;
            return this;
        }

        public DataGetListRequest WithSort(SortItem sortItem)
        {
            _sort = sortItem;
            return this;
        }

        public DataGetListRequest WithProperties(params string[] properties)
        {
            _properties = properties?.ToList();
            return this;
        }

        internal DataGetListRequest WithTotal(bool getTotal)
        {
            _geTtotal = getTotal;
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection
            {
                { "pageIndex", _pageIndex.ToString() },
                { "pageSize", _pageSize.ToString() },
                { "getTotal", _geTtotal.ToString().ToLower() }
            };

            if (_properties != null && _properties.Any())
            {
                extraParams.Add("properties", string.Join(",", _properties));
            }

            if (_filter != null)
            {
                extraParams.Add("filter", JsonSerializer.Serialize(_filter, JsonSerializerOptions));
            }

            if (_sort != null)
            {
                extraParams.Add("sort", JsonSerializer.Serialize(_sort, JsonSerializerOptions));
            }

            return extraParams;
        }
    }
}
