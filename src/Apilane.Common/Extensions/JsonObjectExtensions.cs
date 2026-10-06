using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Apilane.Common.Extensions
{
    public static class JsonObjectExtensions
    {
        // The name is matched without regard to case, like every other property name of the API
        public static string? GetObjectProperty(this JsonObject obj, string propertyName)
        {
            foreach (KeyValuePair<string, JsonNode?> item in obj)
            {
                if (item.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return Utils.GetString(item.Value);
                }
            }

            return null;
        }
    }
}
