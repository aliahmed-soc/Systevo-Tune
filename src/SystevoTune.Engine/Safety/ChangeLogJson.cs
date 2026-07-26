using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystevoTune.Engine.Safety;

/// <summary>
/// Serializer settings for the change log. Kept in one place so the on-disk shape
/// stays exactly as documented in docs/05-safety-layer.md section 5.2.
/// </summary>
internal static class ChangeLogJson
{
    /// <summary>One record per line, so writes append and a torn line costs one record.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new ChangeLogTimeConverter() },
    };
}

/// <summary>Writes and reads <c>"2026-07-26T14:03:22"</c> — local time, no offset, no fractions.</summary>
internal sealed class ChangeLogTimeConverter : JsonConverter<DateTime>
{
    internal const string Format = "yyyy-MM-ddTHH:mm:ss";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a time string in '{Format}' format.");
        }

        var text = reader.GetString();
        if (text is null ||
            !DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            throw new JsonException($"'{text}' is not a time in '{Format}' format.");
        }

        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}
