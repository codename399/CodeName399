using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Helpers;

/// <summary>
/// Accepts decimal values sent by browser clients as JSON numbers or strings.
/// JavaScript cannot represent decimal.MaxValue exactly. When a browser round-trips
/// decimal.MaxValue it can emit a rounded JSON number that is microscopically above
/// the .NET decimal range. Such values are treated as the configuration "unlimited"
/// sentinel and clamped to decimal.MaxValue.
/// </summary>
public sealed class FlexibleDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return decimal.MaxValue;

            case JsonTokenType.Number:
                {
                    if (reader.TryGetDecimal(out var decimalValue))
                        return decimalValue;

                    double doubleValue;
                    try
                    {
                        doubleValue = reader.GetDouble();
                    }
                    catch (FormatException)
                    {
                        throw new JsonException("The JSON number is not a valid decimal value.");
                    }

                    return ToDecimalOrMax(doubleValue);
                }

            case JsonTokenType.String:
                {
                    var text = reader.GetString();

                    if (string.IsNullOrWhiteSpace(text))
                        return decimal.MaxValue;

                    if (decimal.TryParse(
                            text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var parsed))
                    {
                        return parsed;
                    }

                    if (double.TryParse(
                            text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var parsedDouble))
                    {
                        return ToDecimalOrMax(parsedDouble);
                    }

                    throw new JsonException($"The value '{text}' is not a valid decimal value.");
                }

            default:
                throw new JsonException(
                    $"Token {reader.TokenType} cannot be converted to Decimal.");
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        decimal value,
        JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }

    private static decimal ToDecimalOrMax(double value)
    {
        if (double.IsNaN(value) ||
            double.IsInfinity(value) ||
            value >= (double)decimal.MaxValue)
        {
            return decimal.MaxValue;
        }

        if (value <= (double)decimal.MinValue)
            return decimal.MinValue;

        return (decimal)value;
    }
}
