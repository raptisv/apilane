using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apilane.Net.JsonConverters
{
    internal class NumericToNullIntegerConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    if (int.TryParse(reader.GetString(), out int result))
                    {
                        return result;
                    }

                    return null;
                case JsonTokenType.Number:
                    {
                        if (reader.TryGetInt32(out var intResult))
                        {
                            return intResult;
                        }
                        else if (reader.TryGetInt64(out var longResult))
                        {
                            return Convert.ToInt32(longResult);
                        }
                        else if (reader.TryGetDecimal(out var decimalResult))
                        {
                            return Convert.ToInt32(decimalResult);
                        }
                        else if (reader.TryGetDouble(out var doubleResult))
                        {
                            return Convert.ToInt32(doubleResult);
                        }

                        return null;
                    }
                default:
                    {
                        return null;
                    }
            }
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
}
