using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apilane.Common.Models
{
    public class SortData
    {
        private static JsonSerializerOptions _sortStringJsonSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        [JsonPropertyName("Property")]
        public required string Property { get; set; } = null!;

        [JsonPropertyName("Direction")]
        public required string Direction { get; set; } = null!;

        public static IEnumerable<SortData>? ParseList(string? sort)
        {
            if (string.IsNullOrWhiteSpace(sort))
            {
                return null;
            }

            return JsonSerializer.Deserialize<List<SortData>>(sort, _sortStringJsonSerializerOptions);
        }

        public static SortData? Parse(string? sort)
        {
            if (string.IsNullOrWhiteSpace(sort))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SortData>(sort, _sortStringJsonSerializerOptions);
        }
    }
}
