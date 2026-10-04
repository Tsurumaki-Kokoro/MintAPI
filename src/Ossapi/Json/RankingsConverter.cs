using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ossapi.Models;

namespace Ossapi.Json;

/// <summary>Separates country statistics from player statistics in ranking responses.</summary>
public sealed class RankingsConverter : JsonConverter<Rankings>
{
    public override Rankings? ReadJson(JsonReader reader, Type objectType, Rankings? existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var json = JObject.Load(reader);
        var ranking = json["ranking"] as JArray ?? [];
        json.Remove("ranking");
        var result = new Rankings();
        using var remaining = json.CreateReader();
        serializer.Populate(remaining, result);
        if (ranking.First is JObject first && first.ContainsKey("code"))
            result.CountryRanking = ranking.ToObject<List<CountryStatistics>>(serializer) ?? [];
        else
            result.Ranking = ranking.ToObject<List<UserStatistics>>(serializer) ?? [];
        return result;
    }

    public override void WriteJson(JsonWriter writer, Rankings? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }
        serializer.Serialize(writer, new
        {
            ranking = value.CountryRanking.Count > 0 ? (object)value.CountryRanking : value.Ranking,
            cursor = value.Cursor,
            cursor_string = value.CursorString,
            beatmapsets = value.Beatmapsets,
            spotlight = value.SpotlightInfo,
            total = value.Total,
        });
    }
}
