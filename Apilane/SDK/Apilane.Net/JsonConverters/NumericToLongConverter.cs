using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apilane.Net.JsonConverters
{
    internal class NumericToLongConverter : JsonConverter<long>
    {
        public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    if (long.TryParse(reader.GetString(), out long result))
                    {
                        return result;
                    }

                    throw new Exception($"Could not parse integer value");
                case JsonTokenType.Number:
                    {
                        if (reader.TryGetInt64(out var intResult))
                        {
                            return intResult;
                        }
                        else if (reader.TryGetInt32(out var longResult))
                        {
                            return Convert.ToInt64(longResult);
                        }
                        else if (reader.TryGetDecimal(out var decimalResult))
                        {
                            return Convert.ToInt64(decimalResult);
                        }
                        else if (reader.TryGetDouble(out var doubleResult))
                        {
                            return Convert.ToInt64(doubleResult);
                        }

                        throw new Exception($"Could not parse integer value");
                    }
                default:
                    {
                        return 0;
                    }
            }
        }

        public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }
}
