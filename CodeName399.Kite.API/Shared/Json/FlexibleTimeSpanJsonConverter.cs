using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Json
{
    /// <summary>
    /// Accepts both "HH:mm" and "HH:mm:ss" for TimeSpan configuration values.
    /// The API writes the canonical "c" TimeSpan format ("HH:mm:ss").
    /// This keeps the configuration endpoint compatible with Angular time inputs.
    /// </summary>
    public sealed class FlexibleTimeSpanJsonConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("TimeSpan must be supplied as a string in HH:mm or HH:mm:ss format.");

            var value = reader.GetString();

            if (string.IsNullOrWhiteSpace(value))
                throw new JsonException("TimeSpan value cannot be empty.");

            // Legacy Angular payloads have historically represented a duration
            // as total minutes followed by :00 (for example "900:00").  Treat
            // values whose first component exceeds 23 as total minutes instead
            // of 900 hours. This makes old saved configurations recoverable.
            var parts = value.Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var second)
                && first > 23
                && second is >= 0 and < 60)
            {
                return TimeSpan.FromMinutes(first).Add(TimeSpan.FromSeconds(second));
            }

            if (TimeSpan.TryParseExact(
                    value,
                    new[] { @"hh\:mm", @"hh\:mm\:ss", @"h\:mm", @"h\:mm\:ss" },
                    CultureInfo.InvariantCulture,
                    TimeSpanStyles.None,
                    out var result))
            {
                return result;
            }

            if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out result))
                return result;

            throw new JsonException(
                $"Invalid TimeSpan '{value}'. Expected HH:mm or HH:mm:ss.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            TimeSpan value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        }
    }
}
