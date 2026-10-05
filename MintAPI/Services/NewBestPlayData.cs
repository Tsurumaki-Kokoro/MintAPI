using MintOsuApi.Models;

namespace MintAPI.Services;

public record NewBestPlayEntry(Score Score, int BpRank);
public record NewBestPlayReport(int Days, int First, int Total, List<NewBestPlayEntry> Entries);

public static class NewBestPlayData
{
    public static NewBestPlayReport Select(IReadOnlyList<Score> scores, DateTimeOffset now, int days,
        int first, int last, IReadOnlyCollection<string> mods)
    {
        var cutoff = now.AddDays(-days);
        var candidates = scores.Take(200).Select((score, index) => new NewBestPlayEntry(score, index + 1))
            .Where(entry => entry.Score.EndedAt > cutoff && entry.Score.EndedAt <= now && MatchesMods(entry.Score, mods))
            .ToList();
        return new NewBestPlayReport(days, first, candidates.Count, candidates.Skip(first - 1).Take(last - first + 1).ToList());
    }

    private static bool MatchesMods(Score score, IReadOnlyCollection<string> required)
    {
        if (required.Count == 0) return true;
        var actual = (score.Mods ?? []).Select(mod => mod.Acronym.ToUpperInvariant()).ToHashSet();
        actual.Remove("CL");
        if (required.Contains("NM")) return actual.Count == 0;
        return required.All(actual.Contains);
    }
}
