using System.Text.Json;
using System.Text.Json.Serialization;

namespace FleetFlow.Api.Errors;

public sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("An ISO 8601 timestamp with an offset is required.");
        var text = reader.GetString()!;
        var hasOffset = text.EndsWith('Z') || (text.Length >= 6 && text[^3] == ':' && text[^6] is '+' or '-');
        if (!hasOffset || !reader.TryGetDateTimeOffset(out var timestamp)) throw new JsonException("An ISO 8601 timestamp with an offset is required.");
        return timestamp.ToUniversalTime();
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime());
}
