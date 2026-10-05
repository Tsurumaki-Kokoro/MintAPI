using MintAPI.Controllers;
using MintAPI.Rendering.BeatmapTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using MintOsuApi;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Unit;

public class BeatmapRankedDateTests
{
    [Theory]
    [InlineData("default")]
    [InlineData("yaowan")]
    public async Task Ranked_embedded_set_date_is_shown_without_an_extra_set_request(string themeName)
    {
        var map = JsonConvert.DeserializeObject<Beatmap>("""
            {"id":1949106,"beatmapset_id":933630,"status":"ranked","mode_int":0,
             "beatmapset":{"id":933630,"user_id":6381153,"status":"ranked",
                           "ranked_date":"2019-05-07T09:40:07Z"}}
            """, OsuClient.BuildJsonSettings())!;
        var api = new RecordingOsuApiService
        {
            BeatmapHandler = _ => map,
            UserHandler = (id, _) => new User { Id = int.Parse(id), Username = "Mapper" },
            BeatmapsetHandler = _ => throw new InvalidOperationException("Embedded set already has the required data")
        };
        var renderer = new Capture();
        var result = await Create(api, renderer).GetBeatmapInfo(beatmap_id: 1949106, theme: themeName);
        Assert.IsType<FileContentResult>(result);
        Assert.Equal(2, api.CallCount);
        Assert.Contains("2019-05-07 17:40:07", renderer.Html);
        Assert.DoesNotContain("非上架", renderer.Html);
    }

    [Fact]
    public async Task Mapper_fallback_attaches_the_full_set_for_rendering()
    {
        var map = new Beatmap { Id = 1949106, BeatmapsetId = 933630, Status = RankStatus.Ranked };
        var set = new Beatmapset { Id = 933630, UserId = 6381153, RankedDate = DateTimeOffset.Parse("2019-05-07T09:40:07Z") };
        var api = new RecordingOsuApiService
        {
            BeatmapHandler = _ => map, BeatmapsetHandler = _ => set,
            UserHandler = (_, _) => new User { Id = 6381153, Username = "Mapper" }
        };
        var renderer = new Capture();
        Assert.IsType<FileContentResult>(await Create(api, renderer).GetBeatmapInfo(beatmap_id: 1949106));
        Assert.Same(set, map.Beatmapset);
        Assert.Equal(3, api.CallCount);
        Assert.Contains("2019-05-07 17:40:07", renderer.Html);
    }

    [Theory]
    [InlineData("default", RankStatus.Ranked)]
    [InlineData("yaowan", RankStatus.Ranked)]
    [InlineData("default", RankStatus.Pending)]
    public async Task Missing_date_never_implies_an_unranked_status(string themeName, RankStatus status)
    {
        var renderer = new Capture();
        var theme = Theme(renderer);
        var map = new Beatmap { Status = status, Beatmapset = new Beatmapset { Status = status } };
        await theme.RenderBeatmapAsync(map, new User(), [], "unused.osu", themeName);
        Assert.DoesNotContain("非上架", renderer.Html);
        Assert.Contains(status.ToString(), renderer.Html);
        Assert.Contains("—", renderer.Html);
        await theme.RenderBeatmapsetAsync(new Beatmapset { Status = status }, [], themeName);
        Assert.DoesNotContain("非ranked", renderer.Html);
        Assert.Contains("—", renderer.Html);
    }

    private static BeatmapController Create(RecordingOsuApiService api, Capture renderer) => new(
        api, new Files(), Theme(renderer), NullLogger<BeatmapController>.Instance);
    private static DefaultBeatmapTheme Theme(Capture renderer) => new(renderer,
        new AvatarCardImageCache([]), new Calculator(), NullLogger<DefaultBeatmapTheme>.Instance, new Analysis());
    private sealed class Capture : IRenderService
    {
        public string Html { get; private set; } = "";
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            return Task.FromResult(new byte[] { 137, 80, 78, 71 });
        }
    }
    private sealed class Analysis : IBeatmapAnalysisService
    {
        public BeatmapAnalysis? Calculate(string path) => null;
    }
    private sealed class Calculator : IPpCalculatorService
    {
        public PpResult CalculateFixed(Score score, string osuFilePath) => throw new NotSupportedException();
        public PpResult CalculateSs(string path, int rulesetId, uint mods = 0) => new(100, 5, 1000);
        public PpResult Calculate(Score score, string path) => throw new NotSupportedException();
        public (double IfPp, double SsPp) CalculateIfFcAndSs(Score score, string path) => throw new NotSupportedException();
        public (double NewPp, int Position) FindOptimalNewPp(List<double> ppList, double desiredIncrease) => throw new NotSupportedException();
    }
    private sealed class Files : IBeatmapFileService
    {
        public Task<string> GetOsuFilePathAsync(int setId, int mapId) => Task.FromResult("unused.osu");
        public Task<byte[]> GetMapBgAsync(int setId, int mapId, string? bgName = null) => Task.FromResult(Array.Empty<byte>());
        public string GetBgFilename(string path) => "bg.jpg";
        public Task<byte[]?> GetListCoverAsync(int setId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
