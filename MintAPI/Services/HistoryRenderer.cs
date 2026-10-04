using System.Globalization;
using System.Net;
using System.Text;
using MintOsuApi.Models;

namespace MintAPI.Services;

public sealed class HistoryRenderer(IRenderService renderer)
{
    private static string E(string text) => WebUtility.HtmlEncode(text);
    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Page(string title, string content) => "<!doctype html><html><meta charset='utf-8'><style>"
        + "body{margin:0;padding:36px;background:#f5f5f7;color:#202124;font:16px Arial,sans-serif}h1{font-size:28px}"
        + "section{background:white;border-radius:18px;padding:24px;margin:18px 0}table{width:100%;border-collapse:collapse}"
        + "td,th{text-align:left;padding:12px 8px;border-bottom:1px solid #eee}small{color:#666}svg{width:100%}</style>"
        + $"<h1>{E(title)}</h1>{content}</html>";

    public Task<byte[]> RenderTrendAsync(string title, IReadOnlyList<HistoryPoint> points, CancellationToken ct)
    {
        var content = new StringBuilder();
        foreach (var rank in new[] { false, true })
        {
            var values = points.Select(p => rank ? (double)p.GlobalRank : p.Pp).ToArray();
            var min = values.Min(); var max = values.Max(); var range = Math.Max(max - min, 1);
            var first = points[0].Date.DayNumber; var span = Math.Max(1, points[^1].Date.DayNumber - first);
            var coordinates = points.Select((p, i) =>
                $"{N(50 + (p.Date.DayNumber - first) * 800.0 / span)},{N(rank ? 40 + (values[i] - min) * 170 / range : 210 - (values[i] - min) * 170 / range)}");
            content.Append($"<section><h2>{(rank ? "全球排名" : "PP")}</h2><small>范围 {N(min)} – {N(max)} · 最新 {N(values[^1])}</small>"
                + $"<svg viewBox='0 0 900 260'><path d='M50 30V220H850' fill='none' stroke='#ddd'/>"
                + $"<polyline points='{string.Join(" ", coordinates)}' fill='none' stroke='{(rank ? "#9258dd" : "#007aff")}' stroke-width='3'/>"
                + (points.Count == 1 ? $"<circle cx='50' cy='{N(rank ? 40 : 210)}' r='4' fill='#007aff'/>" : "")
                + $"<text x='50' y='250'  font-size='14'>{points[0].Date:yyyy-MM-dd}</text><text x='750' y='250' font-size='14'>{points[^1].Date:yyyy-MM-dd}</text></svg></section>");
        }
        content.Append($"<small>{points.Count} 个每日数据点 · 来源：{E(string.Join(" + ", points.Select(p => p.Source).Distinct()))} · 缺失日期没有补值</small>");
        return renderer.RenderHtmlAsync(Page(title, content.ToString()), 1000, 1040, ct);
    }

    public Task<byte[]> RenderScoresAsync(string title, string notice, IReadOnlyList<Score> scores, int offset, int total, CancellationToken ct)
    {
        var content = new StringBuilder($"<small>{E(notice)} · 共 {total} 条</small><section><table><tr><th># / 时间（UTC）</th><th>Mods / 等级</th><th>分数</th><th>准确率</th><th>Combo</th><th>PP</th></tr>");
        for (var i = 0; i < scores.Count; i++)
        {
            var s = scores[i];
            var mods = string.Join(" ", s.Mods?.Select(m => m.Acronym) ?? []);
            content.Append($"<tr><td>{offset + i + 1}<br><small>{s.EndedAt.UtcDateTime:yyyy-MM-dd HH:mm}</small></td>"
                + $"<td>{E(mods.Length == 0 ? "NM" : mods)} / {E(s.Rank.ToString())}{(s.Passed ? "" : " · FAIL")}</td>"
                + $"<td>{s.TotalScore}</td><td>{N(s.Accuracy * 100)}%</td><td>{s.MaxCombo}x</td><td>{(s.Pp.HasValue ? N(s.Pp.Value) : "—")}</td></tr>");
        }
        content.Append("</table></section>");
        return renderer.RenderHtmlAsync(Page(title, content.ToString()), 1100, 300 + scores.Count * 72, ct);
    }
}
