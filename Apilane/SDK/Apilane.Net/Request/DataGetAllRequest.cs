using Apilane.Net.Models.Data;
using System.Collections.Generic;
using System.Linq;

namespace Apilane.Net.Request
{
    public class DataGetAllRequest
    {
        public static DataGetAllRequest New(string entity) => new(entity);

        protected string _entity;
        protected string? _authToken = null;
        private List<string>? _properties = null;
        protected FilterItem? _filter = null;
        protected SortItem? _sort = null;
        protected bool _throwOnError = false;

        public string Entity => _entity;
        public string? AuthToken => _authToken;
        public List<string>? Properties => _properties;
        public FilterItem? Filter => _filter;
        public SortItem? Sort => _sort;

        private DataGetAllRequest(string entity)
        {
            _entity = entity;
        }

        internal bool ShouldThrowExceptionOnError()
        {
            return _throwOnError;
        }

        /// <summary>
        /// Use this method to directly throw exception instead of checking for error on each request.
        /// </summary>
        public DataGetAllRequest OnErrorThrowException(bool throwOnError = false)
        {
            _throwOnError = throwOnError;
            return this;
        }

        public DataGetAllRequest WithAuthToken(string authToken)
        {
            _authToken = authToken;
            return this;
        }

        public DataGetAllRequest WithFilter(FilterItem filterItem)
        {
            _filter = filterItem;
            return this;
        }

        public DataGetAllRequest WithSort(SortItem sortItem)
        {
            _sort = sortItem;
            return this;
        }

        public DataGetAllRequest WithProperties(params string[] properties)
        {
            _properties = properties?.ToList();
            return this;
        }
    }
}
