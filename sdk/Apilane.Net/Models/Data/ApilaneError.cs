using Apilane.Net.Models.Enums;
using System.Text.Json.Serialization;

namespace Apilane.Net.Models.Data
{
    public class ApilaneError
    {
        [JsonPropertyName("Code")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ValidationError Code { get; set; } = ValidationError.ERROR;

        [JsonPropertyName("Message")]
        public string Message { get; set; } = null!;

        [JsonPropertyName("Property")]
        public string Property { get; set; } = null!;

        [JsonPropertyName("Entity")]
        public string Entity { get; set; } = null!;
    }
}