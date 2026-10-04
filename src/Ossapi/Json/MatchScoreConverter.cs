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
        {
            score.Score = total.Value<long>();
            var current = (JObject)json.DeepClone();
            if (current["mods"] is JArray mods)
                current["mods"] = new JArray(mods.Select(m => m.Type == JTokenType.String
                    ? new JObject { ["acronym"] = m.Value<string>(), ["settings"] = new JObject() }
                    : m.DeepClone()));
            score.ModernScore = current.ToObject<Score>(serializer);
            if (json["ended_at"] is { Type: not JTokenType.Null })
                score.CreatedAt = score.ModernScore!.EndedAt;
            if (json["ruleset_id"] is { Type: not JTokenType.Null } ruleset)
                score.Mode = (Ossapi.Enums.GameMode)ruleset.Value<int>();
        }
        if (json["statistics"] is JObject statistics && statistics.Properties().Any(p => p.Name.StartsWith("count_")))
        {
            var legacy = statistics.ToObject<LegacyStatistics>(serializer)!;
            score.LegacyStatistics = legacy;
        }
        return score;
    }

    public override bool CanWrite => true;
    public override void WriteJson(JsonWriter writer, LegacyScore? value, JsonSerializer serializer)
        => serializer.Serialize(writer, (object?)value?.ModernScore ?? value);
}
