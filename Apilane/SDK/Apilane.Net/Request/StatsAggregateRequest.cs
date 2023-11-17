using Apilane.Net.Models.Data;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json;

namespace Apilane.Net.Request
{
    public class StatsAggregateRequest : ApilaneRequestBase
    {
        public static StatsAggregateRequest New(string entity) => new(entity);

        private int _pageIndex = 1, _pageSize = 20;
        private FilterItem? _filter;
        private string? _groupBy;
        private bool _ascending;
        protected List<string> _properties;

        public enum DataAggregates
        {
            Min,
            Max,
            Count,
            Sum,
            Avg
        }

        private StatsAggregateRequest(string entity) : base(entity, "Stats", "Aggregate")
        {
            _properties = new List<string>();
        }

        public StatsAggregateRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }

        public StatsAggregateRequest WithPageIndex(int pageIndex)
        {
            _pageIndex = pageIndex;
            return this;
        }

        public StatsAggregateRequest WithPageSize(int pageSize)
        {
            _pageSize = pageSize;
            return this;
        }

        public StatsAggregateRequest WithFilter(FilterItem filterItem)
        {
            _filter = filterItem;
            return this;
        }

        public StatsAggregateRequest WithGroupBy(string groupBy)
        {
            _groupBy = groupBy;
            return this;
        }

        public StatsAggregateRequest WithSort(bool ascending)
        {
            _ascending = ascending;
            return this;
        }

        public StatsAggregateRequest WithProperty(string property, DataAggregates aggregate)
        {
            _properties.Add($"{property}.{aggregate}");
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection
            {
                { "groupBy", _groupBy },
                { "pageIndex", _pageIndex.ToString() },
                { "pageSize", _pageSize.ToString() },
                { "orderDirection", _ascending.ToString().ToLower() }
            };

            if (_filter != null)
            {
                extraParams.Add("filter", JsonSerializer.Serialize(_filter, JsonSerializerOptions));
            }

            if (_properties != null && _properties.Any())
            {
                extraParams.Add("properties", string.Join(",", _properties));
            }

            return extraParams;
        }
    }
}
