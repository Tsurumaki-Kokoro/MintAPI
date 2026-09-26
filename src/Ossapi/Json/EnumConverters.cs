using Newtonsoft.Json;
using Ossapi.Enums;

namespace Ossapi.Json;

/// <summary>
/// Deserializes GameMode from its osu! API string value ("osu", "taiko", "fruits", "mania").
/// </summary>
public class GameModeConverter : JsonConverter<GameMode>
{
    public override GameMode ReadJson(JsonReader reader, Type objectType, GameMode existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var s = reader.Value?.ToString();
        return s switch
        {
            "osu"    => GameMode.Osu,
            "taiko"  => GameMode.Taiko,
            "fruits" => GameMode.Catch,
            "mania"  => GameMode.Mania,
            _        => throw new JsonSerializationException($"Unknown GameMode: '{s}'"),
        };
    }

    public override void WriteJson(JsonWriter writer, GameMode value, JsonSerializer serializer)
    {
        var s = value switch
        {
            GameMode.Osu   => "osu",
            GameMode.Taiko => "taiko",
            GameMode.Catch => "fruits",
            GameMode.Mania => "mania",
            _              => throw new JsonSerializationException($"Unknown GameMode: {value}"),
        };
        writer.WriteValue(s);
    }
}

/// <summary>
/// Deserializes RankStatus from int or string ("ranked", "graveyard", etc.).
/// </summary>
public class RankStatusConverter : JsonConverter<RankStatus>
{
    public override RankStatus ReadJson(JsonReader reader, Type objectType, RankStatus existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Integer)
            return (RankStatus)Convert.ToInt32(reader.Value);

        return reader.Value?.ToString() switch
        {
            "graveyard" => RankStatus.Graveyard,
            "wip"       => RankStatus.Wip,
            "pending"   => RankStatus.Pending,
            "ranked"    => RankStatus.Ranked,
            "approved"  => RankStatus.Approved,
            "qualified" => RankStatus.Qualified,
            "loved"     => RankStatus.Loved,
            var s       => throw new JsonSerializationException($"Unknown RankStatus: '{s}'"),
        };
    }

    public override void WriteJson(JsonWriter writer, RankStatus value, JsonSerializer serializer)
        => writer.WriteValue((int)value);
}

/// <summary>
/// Deserializes Grade from its osu! API string value (SSH="XH", SS="X", SH="SH", etc.).
/// </summary>
public class GradeConverter : JsonConverter<Grade>
{
    public override Grade ReadJson(JsonReader reader, Type objectType, Grade existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        return reader.Value?.ToString() switch
        {
            "XH" => Grade.SSH,
            "X"  => Grade.SS,
            "SH" => Grade.SH,
            "S"  => Grade.S,
            "A"  => Grade.A,
            "B"  => Grade.B,
            "C"  => Grade.C,
            "D"  => Grade.D,
            "F"  => Grade.F,
            var s => throw new JsonSerializationException($"Unknown Grade: '{s}'"),
        };
    }

    public override void WriteJson(JsonWriter writer, Grade value, JsonSerializer serializer)
    {
        var s = value switch
        {
            Grade.SSH => "XH",
            Grade.SS  => "X",
            Grade.SH  => "SH",
            Grade.S   => "S",
            Grade.A   => "A",
            Grade.B   => "B",
            Grade.C   => "C",
            Grade.D   => "D",
            Grade.F   => "F",
            _         => throw new JsonSerializationException($"Unknown Grade: {value}"),
        };
        writer.WriteValue(s);
    }
}
