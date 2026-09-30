using Newtonsoft.Json;

namespace Ossapi.Json;

/// <summary>Parses osu! timestamps in ISO, date-only, or Unix-millisecond formats.</summary>
public class DateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
{
    public static readonly DateTimeOffsetConverter Instance = new();

    public override DateTimeOffset? ReadJson(JsonReader reader, Type objectType,
        DateTimeOffset? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        if (reader.TokenType == JsonToken.Integer)
        {
            var ms = Convert.ToInt64(reader.Value);
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }

        if (reader.TokenType is JsonToken.String or JsonToken.Date)
        {
            var s = reader.Value?.ToString();
            if (string.IsNullOrEmpty(s)) return null;

            if (DateTimeOffset.TryParse(s, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dto))
                return dto;

            // date-only: "2021-01-01"
            if (DateTime.TryParseExact(s, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return new DateTimeOffset(dt, TimeSpan.Zero);

            throw new JsonSerializationException($"Cannot parse datetime: '{s}'");
        }

        throw new JsonSerializationException($"Unexpected token type for DateTimeOffset: {reader.TokenType}");
    }

    public override void WriteJson(JsonWriter writer, DateTimeOffset? value, JsonSerializer serializer)
    {
        if (value is null) writer.WriteNull();
        else writer.WriteValue(value.Value.ToString("o"));
    }
}

/// <summary>Non-nullable variant for required datetime fields.</summary>
public class DateTimeOffsetRequiredConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset ReadJson(JsonReader reader, Type objectType,
        DateTimeOffset existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var nullable = DateTimeOffsetConverter.Instance.ReadJson(
            reader, typeof(DateTimeOffset?), null, false, serializer);
        return nullable ?? throw new JsonSerializationException("Required datetime field was null.");
    }

    public override void WriteJson(JsonWriter writer, DateTimeOffset value, JsonSerializer serializer)
        => writer.WriteValue(value.ToString("o"));
}
