using Newtonsoft.Json;
using Ossapi.Mods;

namespace Ossapi.Json;

/// <summary>
/// Deserializes a Mod from either a legacy integer bitmask or a string acronym.
/// </summary>
public class ModConverter : JsonConverter<Mod>
{
    public override Mod ReadJson(JsonReader reader, Type objectType, Mod existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        return reader.TokenType switch
        {
            JsonToken.Integer => new Mod(Convert.ToInt32(reader.Value)),
            JsonToken.String  => Mod.Parse(reader.Value!.ToString()!),
            JsonToken.Null    => Mod.NM,
            _ => throw new JsonSerializationException($"Unexpected token type for Mod: {reader.TokenType}"),
        };
    }

    public override void WriteJson(JsonWriter writer, Mod value, JsonSerializer serializer)
        => writer.WriteValue(value.Value);
}
