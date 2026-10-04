using System.Net;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ossapi.Enums;
using Ossapi.Models;

namespace Ossapi.Tests.Unit;

public class ContractCompatibilityTests
{
    private static readonly JsonSerializer Serializer = JsonSerializer.Create(OssapiClient.BuildJsonSettings());

    private static JObject Samples()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Ossapi.Tests.Fixtures.contract_live_samples.json")!;
        using var reader = new StreamReader(stream);
        return JObject.Parse(reader.ReadToEnd());
    }

    [Theory]
    [InlineData("osu")]
    [InlineData("taiko")]
    [InlineData("mania")]
    [InlineData("fruits")]
    public void Live_difficulty_attributes_keep_ruleset_specific_values(string mode)
    {
        var raw = Samples()["difficulty_attributes"]![mode]!;
        var result = raw.ToObject<DifficultyAttributes>(Serializer)!.Attributes;
        Assert.Equal(raw["attributes"]!["star_rating"]!.Value<double>(), result.StarRating);
        Assert.Equal(raw["attributes"]!["max_combo"]!.Value<int>(), result.MaxCombo);
        if (mode == "osu")
        {
            Assert.Equal(raw["attributes"]!["aim_difficult_slider_count"]!.Value<double>(), result.AimDifficultSliderCount);
            Assert.Equal(raw["attributes"]!["aim_difficult_strain_count"]!.Value<double>(), result.AimDifficultStrainCount);
            Assert.Equal(raw["attributes"]!["speed_difficult_strain_count"]!.Value<double>(), result.SpeedDifficultStrainCount);
        }
        if (mode == "taiko")
            Assert.Equal(raw["attributes"]!["mono_stamina_factor"]!.Value<double>(), result.MonoStaminaFactor);
    }

    [Fact]
    public void Live_country_ranking_retains_country_fields_and_large_play_count()
    {
        var raw = Samples()["country_ranking"]!;
        var result = raw.ToObject<Rankings>(Serializer)!;
        var country = Assert.Single(result.CountryRanking);
        Assert.Empty(result.Ranking);
        Assert.Equal("US", country.Code);
        Assert.Equal(2807834329L, country.PlayCount);
        Assert.Equal(raw["ranking"]![0]!["ranked_score"]!.Value<long>(), country.RankedScore);
        Assert.Equal("United States", country.Country.Name);
    }

    [Fact]
    public void Live_stable_score_keeps_both_long_ids_and_null_started_at()
    {
        var raw = Samples()["stable_score"]!;
        var score = raw.ToObject<Score>(Serializer)!;
        Assert.Equal(1659863772L, score.Id);
        Assert.Equal(4343465566L, score.LegacyScoreId);
        Assert.Equal(raw["total_score"]!.Value<long>(), score.TotalScore);
        Assert.Equal(raw["legacy_total_score"]!.Value<long>(), score.LegacyTotalScore);
        Assert.Null(score.StartedAt);
        Assert.Equal(raw["statistics"]!["great"]!.Value<int>(), score.Statistics!.Great);
    }

    [Fact]
    public void Live_discussion_users_read_playmode_arrays()
    {
        var group = Samples()["user_group"]!.ToObject<UserGroup>(Serializer)!;
        Assert.Equal(GameMode.Taiko, Assert.Single(group.Playmodes!));
    }

    [Fact]
    public void Live_owner_change_retains_user_objects()
    {
        var item = Samples()["owner_change"]!.ToObject<BeatmapsetEvent>(Serializer)!;
        var comment = Assert.IsType<BeatmapsetEventCommentOwnerChange>(item.Comment);
        Assert.Equal(4, comment.NewUsers.Count);
        Assert.Equal(4026817, comment.NewUsers[0].Id);
        Assert.Equal("Xen", comment.NewUsers[0].Username);
        Assert.Null(comment.BeatmapDiscussionId);
    }

    [Fact]
    public void Live_match_keeps_normalized_score_and_full_mod_settings()
    {
        var raw = Samples()["modern_match"]!;
        var match = raw.ToObject<MatchResponse>(Serializer)!;
        var game = Assert.Single(match.EventList).Game!;
        var score = Assert.Single(game.Scores);
        Assert.Equal(raw["events"]![0]!["game"]!["scores"]![0]!["total_score"]!.Value<long>(), score.Score);
        Assert.NotNull(score.ModernScore);
        Assert.Equal(score.Score, score.ModernScore!.TotalScore);
        Assert.Equal(score.ModernScore.EndedAt, score.CreatedAt);
        Assert.True(JToken.DeepEquals(raw["events"]![0]!["game"]!["mods"], game.RawMods));
    }

    [Fact]
    public void Legacy_judgments_are_normalized_without_changing_current_scores()
    {
        var score = JsonConvert.DeserializeObject<LegacyScore>(
            """{"statistics":{"count_300":123,"count_100":9,"count_50":2,"count_geki":6,"count_katu":5,"count_miss":4}}""",
            OssapiClient.BuildJsonSettings())!;
        Assert.Equal(123, score.Statistics!.Great);
        Assert.Equal(9, score.Statistics.Ok);
        Assert.Equal(2, score.Statistics.Meh);
        Assert.Equal(6, score.Statistics.Perfect);
        Assert.Equal(5, score.Statistics.Good);
        Assert.Equal(4, score.Statistics.Miss);
    }

    [Fact]
    public void Multiplayer_score_preserves_nullable_fields_and_large_solo_id()
    {
        var score = JsonConvert.DeserializeObject<MultiplayerScore>(
            """{"id":5000000000,"solo_score_id":6645131974,"legacy_score_id":4343465566,"pp":null,"build_id":null,"started_at":null}""",
            OssapiClient.BuildJsonSettings())!;
        Assert.Equal(5000000000L, score.Id);
        Assert.Equal(6645131974L, score.SoloScoreId);
        Assert.Equal(4343465566L, score.LegacyScoreId);
        Assert.Null(score.Pp);
        Assert.Null(score.BuildId);
        Assert.Null(score.StartedAt);
    }

    [Fact]
    public void Blocks_are_relations_in_an_array()
    {
        var user = JsonConvert.DeserializeObject<UserCompact>(
            """{"blocks":[{"target_id":42,"relation_type":"block","mutual":false}]}""",
            OssapiClient.BuildJsonSettings())!;
        Assert.Equal(42, Assert.Single(user.Blocks!).TargetId);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Public_get_and_compute_preserve_http_error_status(HttpStatusCode status)
    {
        using var http = new HttpClient(new Handler(status)) { BaseAddress = new Uri("https://example.test/") };
        using var client = new OssapiClient(http);
        var get = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetBeatmapAsync(2075268));
        var compute = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetBeatmapAttributesAsync(2075268));
        Assert.Equal(status, get.StatusCode);
        Assert.Equal(status, compute.StatusCode);
    }

    [Fact]
    public async Task Injected_http_client_gets_version_and_correct_query_parameters()
    {
        var handler = new Handler(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        using var client = new OssapiClient(http);
        await client.GetBeatmapScoresAsync(2075268, type: BeatmapScoreRankingType.Friend);
        Assert.Equal("20241024", handler.Version);
        Assert.EndsWith("?type=friend", handler.Url);
        await client.GetWikiPageAsync("en", "Gameplay/Accuracy");
        Assert.EndsWith("wiki/en/Gameplay/Accuracy", handler.Url);
        await client.GetUsersAsync([1646397], includeVariantStatistics: true);
        Assert.Contains("include_variant_statistics=1", handler.Url);
    }

    [Fact]
    public async Task Difficulty_mods_and_event_filters_use_api_values()
    {
        var handler = new Handler(HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        using var client = new OssapiClient(http);
        await client.GetBeatmapAttributesAsync(2075268, Ossapi.Mods.Mod.DT, GameMode.Osu);
        var body = JObject.Parse(handler.Body!);
        Assert.Equal(JTokenType.Integer, body["mods"]!.Type);
        Assert.Equal(64, body["mods"]!.Value<int>());
        await client.GetBeatmapsetEventsAsync(types: [BeatmapsetEventType.OffsetEdit],
            minDate: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var url = Uri.UnescapeDataString(handler.Url!);
        Assert.Contains("types[]=offset_edit", url);
        Assert.Contains("min_date=2020-01-01T00:00:00.0000000+00:00", url);
        await client.GetBeatmapsetDiscussionsAsync(messageTypes: [MessageType.Problem]);
        Assert.Contains("message_types[]=problem", Uri.UnescapeDataString(handler.Url!));
    }

    [Fact]
    public void Unknown_beatmapset_events_keep_their_type_instead_of_becoming_approve()
    {
        var item = JsonConvert.DeserializeObject<BeatmapsetEvent>(
            """{"id":1,"type":"future_event","comment":{"new":true}}""",
            OssapiClient.BuildJsonSettings())!;
        Assert.Equal(BeatmapsetEventType.Unknown, item.Type);
        Assert.Equal("future_event", item.RawType);
        Assert.Equal("future_event", JObject.FromObject(item, Serializer)["type"]!.Value<string>());
    }

    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Version { get; private set; }
        public string? Url { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Version = string.Join(",", request.Headers.GetValues("x-api-version"));
            Url = request.RequestUri!.ToString();
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            { Content = new StringContent("{}"), RequestMessage = request };
        }
    }
}
