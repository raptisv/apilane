using Apilane.Net.Models.Data;
using System.Collections.Specialized;
using System.Text.Json;

namespace Apilane.Net.Request
{
    public class StatsDistinctRequest : ApilaneRequestBase
    {
        public static StatsDistinctRequest New(string entity, string property) => new(entity, property);

        private FilterItem? _filter;
        protected string? _property;

        private StatsDistinctRequest(string entity, string property) : base(entity, "Stats", "Distinct")
        {
            _property = property;

            if (string.IsNullOrWhiteSpace(_property))
            {
                _property = "id"; // Default property
            }
        }

        public StatsDistinctRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }

        public StatsDistinctRequest WithFilter(FilterItem filterItem)
        {
            _filter = filterItem;
            return this;
        }

        protected override NameValueCollection GetExtraParams()
        {
            var extraParams = new NameValueCollection
            {
                { "property", _property }
            };

            if (_filter != null)
            {
                extraParams.Add("filter", JsonSerializer.Serialize(_filter, JsonSerializerOptions));
            }

            return extraParams;
        }
    }
}
