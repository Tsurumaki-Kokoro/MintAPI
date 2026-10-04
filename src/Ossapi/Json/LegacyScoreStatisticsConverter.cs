using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ossapi.Models;

namespace Ossapi.Json;

/// <summary>Normalizes legacy count_* judgments while retaining new score statistics.</summary>
public sealed class LegacyScoreStatisticsConverter : JsonConverter<Statistics>
{
    public override Statistics? ReadJson(JsonReader reader, Type objectType, Statistics? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var json = JObject.Load(reader);
        if (!json.Properties().Any(p => p.Name.StartsWith("count_")))
            return json.ToObject<Statistics>(serializer);
        var legacy = json.ToObject<LegacyStatistics>(serializer)!;
        return new Statistics
        {
            Great = legacy.Count300, Ok = legacy.Count100, Meh = legacy.Count50,
            Perfect = legacy.CountGeki, Good = legacy.CountKatu, Miss = legacy.CountMiss,
        };
    }

    public override bool CanWrite => false;
    public override void WriteJson(JsonWriter writer, Statistics? value, JsonSerializer serializer)
        => throw new NotSupportedException();
}
