using Apilane.Common.Enums;
using Apilane.Common.Utilities;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    public class FilterData
    {
        private static JsonSerializerOptions _filterStringJsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        [JsonConstructor]
        private FilterData()
        {

        }

        public FilterData(FilterLogic logic, List<FilterData> filters)
        {
            Logic = logic;
            Filters = filters;
        }

        public FilterData(string property, FilterOperators oper, object? value, PropertyType type)
        {
            Property = property;
            Operator = oper;
            Value = value;
            Type = type;
        }

        // Parent filter properties
        [JsonPropertyName("Logic")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FilterLogic? Logic { get; set; }

        [JsonPropertyName("Filters")]
        public List<FilterData>? Filters { get; set; }

        // Actual filter properties
        [JsonPropertyName("Property")]
        public string? Property { get; set; }

        [JsonPropertyName("Operator")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public FilterOperators? Operator { get; set; }

        [JsonPropertyName("Value")]
        public object? Value { get; set; }

        [JsonIgnore]
        public PropertyType Type { get; set; }

        public static List<(FilterOperators Operator, List<string> OperatorStrList)> AvailableFilterOperators = EnumProvider<FilterOperators>
            .GetValues(FilterOperators.equal)
            .Select(x => ((FilterOperators)x.Key, x.Value.ToString().Split(',').Select(y => y.Trim()).Where(y => !string.IsNullOrWhiteSpace(y)).ToList())).ToList();

        public void Add(FilterData filter)
        {
            Filters ??= new List<FilterData>();
            Filters.Add(filter);
        }

        public void Add(List<FilterData> filters)
        {
            Filters ??= new List<FilterData>();
            Filters.AddRange(filters);
        }

        public static FilterData? Parse(string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return null;
            }

            return JsonSerializer.Deserialize<FilterData>(filter, _filterStringJsonSerializerOptions);
        }

        public enum FilterOperators
        {
            [Display(Name = "equal, eq, ==, =")]
            equal,

            [Display(Name = "notequal, neq, !=, <>")]
            notequal,

            [Display(Name = "greater, g, >")]
            greater,

            [Display(Name = "greaterorequal, ge, >=")]
            greaterorequal,

            [Display(Name = "less, l, <")]
            less,

            [Display(Name = "lessorequal, le, <=")]
            lessorequal,

            [Display(Name = "startswith, sw")]
            startswith,

            [Display(Name = "endswith, ew")]
            endswith,

            [Display(Name = "contains, like")]
            contains,

            [Display(Name = "notcontains, nc")]
            notcontains
        }

        public enum FilterLogic
        {
            AND,
            OR
        }
    }
}