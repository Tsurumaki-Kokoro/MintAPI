using Newtonsoft.Json;
using Ossapi.Enums;
using Ossapi.Json;
using Xunit;

namespace Ossapi.Tests.Unit;

public class JsonConverterTests
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        Converters =
        {
            new GameModeConverter(),
            new RankStatusConverter(),
            new GradeConverter(),
        },
    };

    private static T Deserialize<T>(string json) =>
        JsonConvert.DeserializeObject<T>(json, Settings)!;

    // GameMode
    [Theory]
    [InlineData("\"osu\"",    GameMode.Osu)]
    [InlineData("\"taiko\"",  GameMode.Taiko)]
    [InlineData("\"fruits\"", GameMode.Catch)]
    [InlineData("\"mania\"",  GameMode.Mania)]
    public void GameMode_deserializes_from_api_string(string json, GameMode expected)
        => Assert.Equal(expected, Deserialize<GameMode>(json));

    [Fact]
    public void GameMode_unknown_throws()
        => Assert.Throws<JsonSerializationException>(() => Deserialize<GameMode>("\"osu!\""));

    // Grade
    [Theory]
    [InlineData("\"XH\"", Grade.SSH)]
    [InlineData("\"X\"",  Grade.SS)]
    [InlineData("\"SH\"", Grade.SH)]
    [InlineData("\"S\"",  Grade.S)]
    [InlineData("\"A\"",  Grade.A)]
    [InlineData("\"F\"",  Grade.F)]
    public void Grade_deserializes_from_api_string(string json, Grade expected)
        => Assert.Equal(expected, Deserialize<Grade>(json));

    // RankStatus — int form
    [Theory]
    [InlineData("-2", RankStatus.Graveyard)]
    [InlineData("0",  RankStatus.Pending)]
    [InlineData("1",  RankStatus.Ranked)]
    [InlineData("4",  RankStatus.Loved)]
    public void RankStatus_deserializes_from_int(string json, RankStatus expected)
        => Assert.Equal(expected, Deserialize<RankStatus>(json));

    // RankStatus — string form
    [Theory]
    [InlineData("\"graveyard\"", RankStatus.Graveyard)]
    [InlineData("\"ranked\"",    RankStatus.Ranked)]
    [InlineData("\"loved\"",     RankStatus.Loved)]
    public void RankStatus_deserializes_from_string(string json, RankStatus expected)
        => Assert.Equal(expected, Deserialize<RankStatus>(json));

    // DateTimeOffset
    [Fact]
    public void DateTimeOffset_deserializes_iso8601()
    {
        var converter = new DateTimeOffsetConverter();
        var settings = new JsonSerializerSettings { Converters = { converter } };
        var dto = JsonConvert.DeserializeObject<DateTimeOffset?>("\"2021-06-15T10:30:00+00:00\"", settings);
        Assert.NotNull(dto);
        Assert.Equal(2021, dto!.Value.Year);
    }

    [Fact]
    public void DateTimeOffset_deserializes_unix_ms()
    {
        var converter = new DateTimeOffsetConverter();
        var settings = new JsonSerializerSettings { Converters = { converter } };
        var dto = JsonConvert.DeserializeObject<DateTimeOffset?>("1609459200000", settings);
        Assert.NotNull(dto);
        Assert.Equal(2021, dto!.Value.Year);
    }

    [Fact]
    public void DateTimeOffset_deserializes_date_only()
    {
        var converter = new DateTimeOffsetConverter();
        var settings = new JsonSerializerSettings { Converters = { converter } };
        var dto = JsonConvert.DeserializeObject<DateTimeOffset?>("\"2021-06-15\"", settings);
        Assert.NotNull(dto);
        Assert.Equal(15, dto!.Value.Day);
    }

    [Fact]
    public void DateTimeOffset_null_returns_null()
    {
        var converter = new DateTimeOffsetConverter();
        var settings = new JsonSerializerSettings { Converters = { converter } };
        var dto = JsonConvert.DeserializeObject<DateTimeOffset?>("null", settings);
        Assert.Null(dto);
    }
}
