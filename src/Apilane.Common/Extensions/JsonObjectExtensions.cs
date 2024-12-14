using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Apilane.Common.Extensions
{
    public static class JsonObjectExtensions
    {
        public static string? GetObjectProperty(this JsonObject obj, string propertyName)
        {
            foreach (KeyValuePair<string, JsonNode?> item in obj)
            {
                if (item.Key.Equals(propertyName))
                {
                    return Utils.GetString(item.Value);
                }
            }

            return null;
        }
    }
}
