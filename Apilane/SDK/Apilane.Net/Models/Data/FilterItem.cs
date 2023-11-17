using Apilane.Net.Models.Enums;
using System.Collections.Generic;

namespace Apilane.Net.Models.Data
{
    public class FilterItem
    {
        public FilterItem(FilterLogic logic, List<FilterItem> filters)
        {
            Logic = logic;
            Filters = filters;
            Property = null;
            Operator = null;
            Value = null;
        }

        public FilterItem(string property, FilterOperator oper, object value)
        {
            Property = property;
            Operator = oper;
            Value = value;
            Logic = null;
            Filters = null;
        }

        public FilterLogic? Logic { get; set; }
        public List<FilterItem>? Filters { get; set; }
        public string? Property { get; set; }
        public FilterOperator? Operator { get; set; }
        public object? Value { get; set; }
    }
}
