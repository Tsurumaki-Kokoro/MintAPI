using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ossapi.Models;

namespace Ossapi.Json;

/// <summary>Normalizes legacy and current matches API scores without changing other score endpoints.</summary>
public sealed class MatchScoreConverter : JsonConverter<LegacyScore>
{
    public override LegacyScore ReadJson(JsonReader reader, Type objectType, LegacyScore? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var json = JObject.Load(reader);
        var score = json.ToObject<LegacyScore>(serializer)
            ?? throw new JsonSerializationException("Match score is empty.");
        if (json["total_score"] is { Type: not JTokenType.Null } total)
            score.Score = total.Value<long>();
        return score;
    }

    public override bool CanWrite => false;
    public override void WriteJson(JsonWriter writer, LegacyScore? value, JsonSerializer serializer)
        => throw new NotSupportedException();
}
