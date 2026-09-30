using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ossapi.Mods;

namespace Ossapi.Json;

/// <summary>
/// Deserializes a Mod from either a legacy integer bitmask or a string acronym, or an array of acronyms or mod objects.
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
            JsonToken.StartArray => JArray.Load(reader).Aggregate(Mod.NM, (mods, token) => mods | ReadArrayMod(token)),
            JsonToken.Null    => Mod.NM,
            _ => throw new JsonSerializationException($"Unexpected token type for Mod: {reader.TokenType}"),
        };
    }

    private static Mod ReadArrayMod(JToken token)
    {
        var acronym = token.Type == JTokenType.String
            ? token.Value<string>()
            : token.Type == JTokenType.Object ? token["acronym"]?.Value<string>() : null;
        if (string.IsNullOrWhiteSpace(acronym))
            throw new JsonSerializationException("Mod array entries must be acronyms or objects with an acronym.");
        return Mod.Parse(acronym);
    }

    public override void WriteJson(JsonWriter writer, Mod value, JsonSerializer serializer)
        => writer.WriteValue(value.Value);
}
