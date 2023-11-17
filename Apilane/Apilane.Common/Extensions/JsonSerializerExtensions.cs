using System.Text.Json;

namespace Apilane.Common.Extensions
{
    public static class JsonSerializerExtensions
    {
        public static T DeserializeAnonymous<T>(this string json, T anonymousTypeObject, JsonSerializerOptions? options = default)
            => JsonSerializer.Deserialize<T>(json, options)!;
    }
}
