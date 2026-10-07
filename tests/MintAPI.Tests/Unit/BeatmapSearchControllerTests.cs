using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Controllers;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Unit;

public sealed class BeatmapSearchControllerTests
{
    [Theory]
    [InlineData(null, "any", "any", null, null)]
    [InlineData(" ", "any", "any", null, null)]
    [InlineData("song", "bad", "any", null, null)]
    [InlineData("song", "any", "mine", null, null)]
    [InlineData("song", "any", "any", "bad", null)]
    [InlineData("song", "any", "any", null, " ")]
    public async Task Invalid_parameters_do_not_call_upstream(string? query, string mode, string status, string? sort, string? cursor)
    {
        var api = new RecordingOsuApiService();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.IsType<BadRequestObjectResult>(await Create(api, cache).Search(query, mode, status, sort, cursor));
        Assert.Equal(0, api.CallCount);
    }

    [Fact]
    public async Task Search_preserves_filters_json_and_cursor_and_caches_each_page()
    {
        var api = new RecordingOsuApiService { SearchHandler = (query, mode, category, sort, cursor) =>
        {
            Assert.Equal("Freedom Dive star>=5", query);
            Assert.Equal(BeatmapsetSearchMode.Osu, mode);
            Assert.Equal(BeatmapsetSearchCategory.Ranked, category);
            Assert.Equal(BeatmapsetSearchSort.DifficultyDescending, sort);
            return new BeatmapsetSearchResult { Total = 2, CursorString = cursor is null ? "next" : null,
                Beatmapsets = [new Beatmapset { Id = 1, Beatmaps = [new Beatmap { Id = 10 }] }] };
        } };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Create(api, cache);
        var response = Assert.IsType<ContentResult>(await controller.Search(" Freedom Dive star>=5 ", "osu", "ranked", "difficulty_desc"));
        using var json = JsonDocument.Parse(response.Content!);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal(2, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("next", json.RootElement.GetProperty("cursor_string").GetString());
        Assert.Equal(10, json.RootElement.GetProperty("beatmapsets")[0].GetProperty("beatmaps")[0].GetProperty("id").GetInt32());
        await controller.Search("Freedom Dive star>=5", "osu", "ranked", "difficulty_desc");
        Assert.Equal(1, api.CallCount);
        await controller.Search("Freedom Dive star>=5", "osu", "ranked", "difficulty_desc", "next");
        Assert.Equal(2, api.CallCount);
    }

    [Fact]
    public async Task Default_search_includes_all_modes_and_statuses_and_empty_results()
    {
        var api = new RecordingOsuApiService { SearchHandler = (_, mode, category, sort, _) =>
        {
            Assert.Equal(BeatmapsetSearchMode.Any, mode);
            Assert.Equal(BeatmapsetSearchCategory.Any, category);
            Assert.Null(sort);
            return new BeatmapsetSearchResult();
        } };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var response = Assert.IsType<ContentResult>(await Create(api, cache).Search("unknown"));
        using var json = JsonDocument.Parse(response.Content!);
        Assert.Equal(0, json.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("beatmapsets").GetArrayLength());
    }

    [Fact]
    public async Task Upstream_failure_is_not_cached_and_quota_errors_propagate()
    {
        var api = new RecordingOsuApiService { SearchHandler = (_, _, _, _, _) => throw new HttpRequestException("secret") };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Create(api, cache);
        var result = Assert.IsType<ObjectResult>(await controller.Search("song"));
        Assert.Equal(502, result.StatusCode);
        Assert.DoesNotContain("secret", result.Value!.ToString());
        api.SearchHandler = (_, _, _, _, _) => throw new OsuQuotaExceededException();
        await Assert.ThrowsAsync<OsuQuotaExceededException>(() => controller.Search("song"));
        Assert.Equal(2, api.CallCount);
    }

    private static BeatmapSearchController Create(RecordingOsuApiService api, IMemoryCache cache) =>
        new(api, cache, NullLogger<BeatmapSearchController>.Instance);
}
