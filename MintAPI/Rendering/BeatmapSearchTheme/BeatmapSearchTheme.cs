using MintAPI.Configuration;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using MintAPI.Services;
using MintOsuApi.Models;

namespace MintAPI.Rendering.BeatmapSearchTheme;

public sealed class BeatmapSearchTheme(IRenderService renderer, IHttpClientFactory clients, IMemoryCache cache,
    ILogger<BeatmapSearchTheme> logger, IOptions<DownloadOptions>? downloads = null,
    IOptions<CachePolicyOptions>? cachePolicy = null, IOptions<ConcurrencyOptions>? concurrency = null)
{
    public async Task<byte[]> RenderAsync(IReadOnlyList<Beatmapset> sets, string query, string mode, string status,
        int page, int pageCount, int total, CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(concurrency?.Value.CoverDownload ?? 4);
        var backgrounds = await Task.WhenAll(sets.Select(async set =>
        {
            await gate.WaitAsync(ct);
            try { return await BackgroundAsync(set, ct); }
            finally { gate.Release(); }
        }));
        var rows = new StringBuilder();
        for (var i = 0; i < sets.Count; i++)
        {
            var set = sets[i];
            var title = string.IsNullOrWhiteSpace(set.TitleUnicode) ? set.Title : set.TitleUnicode;
            var artist = string.IsNullOrWhiteSpace(set.ArtistUnicode) ? set.Artist : set.ArtistUnicode;
            rows.Append($"<article class='row'><img class='background' src='{backgrounds[i]}' alt=''><div class='shade'></div><div class='number'>{(page - 1) * 5 + i + 1:00}</div><div class='info'><h2>{E(title)}</h2><p>{E(artist)} <span>· mapped by {E(set.Creator)}</span></p><div class='difficulties'>");
            var target = mode.ToLowerInvariant() switch { "osu" or "0" => 0, "taiko" or "1" => 1, "catch" or "2" => 2, "mania" or "3" => 3, _ => -1 };
            var maps = (set.Beatmaps ?? []).OrderBy(map => target >= 0 && map.ModeInt != target)
                .ThenBy(map => map.ModeInt).ThenBy(map => map.DifficultyRating).ToArray();
            foreach (var map in maps.Take(18))
            {
                var name = map.ModeInt switch { 1 => "taiko", 2 => "ctb", 3 => "mania", _ => "std" };
                var svg = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "wwwroot", "assets", "beatmap", "default", "icons", name + ".svg"), ct);
                var color = DifficultyColor(map.DifficultyRating);
                rows.Append($"<span class='difficulty {(target >= 0 && map.ModeInt != target ? "muted" : "")}' style='color:{color}' title='{E(map.Version)}'>{svg}</span>");
            }
            if (maps.Length > 18) rows.Append($"<span class='more'>+{maps.Length - 18}</span>");
            rows.Append($"</div></div><div class='status'>{E(set.Status.ToString().ToUpperInvariant())}</div><div class='set-id'>Set ID {set.Id}</div></article>");
        }
        if (sets.Count == 0) rows.Append("<div class='empty'>没有找到匹配的谱面<br><small>试试其他关键词或放宽筛选条件</small></div>");
        var template = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Rendering", "BeatmapSearchTheme", "templates", "search.html"), ct);
        var html = template.Replace("{{fonts}}", new Uri(Path.Combine(AppContext.BaseDirectory, "wwwroot", "fonts") + Path.DirectorySeparatorChar).AbsoluteUri)
            .Replace("{{query}}", E(query)).Replace("{{filters}}", E($"{mode} · {status}"))
            .Replace("{{rows}}", rows.ToString()).Replace("{{page}}", $"{page} / {pageCount}")
            .Replace("{{total}}", total.ToString(CultureInfo.InvariantCulture));
        html = RenderAttribution.Add(html, 900, 0);
        html = html.Replace("</head>", "<style>body .mint-attribution{position:absolute;right:36px;bottom:12px;margin:0;padding:0;line-height:18px;}</style></head>");
        return await renderer.RenderHtmlAsync(html, 900, 0, ct);
    }

    // osu-web resources/js/utils/beatmap-helper.ts: gamma-corrected RGB difficulty spectrum.
    internal static string DifficultyColor(double stars)
    {
        if (!double.IsFinite(stars) || stars < .1) return "#AAAAAA";
        if (stars >= 9) return "#000000";
        double[] stops = [.1, 1.25, 2, 2.5, 3.3, 4.2, 4.9, 5.8, 6.7, 7.7, 9];
        string[] colors = ["4290FB", "4FC0FF", "4FFFD5", "7CFF4F", "F6F05C", "FF8068", "FF4E6F", "C645B8", "6563DE", "18158E", "000000"];
        var index = 0;
        while (stars > stops[index + 1]) index++;
        var t = (stars - stops[index]) / (stops[index + 1] - stops[index]);
        var result = "#";
        for (var channel = 0; channel < 3; channel++)
        {
            var a = Convert.ToInt32(colors[index].Substring(channel * 2, 2), 16);
            var b = Convert.ToInt32(colors[index + 1].Substring(channel * 2, 2), 16);
            var value = Math.Pow(Math.Pow(a, 2.2) * (1 - t) + Math.Pow(b, 2.2) * t, 1 / 2.2);
            result += ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString("X2");
        }
        return result;
    }

    private async Task<string> BackgroundAsync(Beatmapset set, CancellationToken ct)
    {
        var policy = downloads?.Value ?? new DownloadOptions();
        var url = string.IsNullOrEmpty(set.Covers.Cover2x) ? set.Covers.Cover : set.Covers.Cover2x;
        if (string.IsNullOrEmpty(url) && set.Id > 0) url = DownloadOptions.Expand(policy.CoverUrlTemplate, setId: set.Id);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !policy.SearchCoverAllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) return "";
        if (cache.TryGetValue<string>(("search-cover", url), out var cached)) return cached!;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(policy.SearchCoverTimeoutSeconds));
            using var client = clients.CreateClient("Downloads");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is not ("image/jpeg" or "image/png" or "image/webp")) return "";
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + count > policy.MaxSearchCoverBytes) return "";
                output.Write(buffer, 0, count);
            }
            var data = $"data:{type};base64,{Convert.ToBase64String(output.ToArray())}";
            cache.Set(("search-cover", url), data, TimeSpan.FromMinutes(cachePolicy?.Value.SearchCoverMinutes ?? 30));
            return data;
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Search cover unavailable for set {SetId}", set.Id);
            return "";
        }
    }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
}
