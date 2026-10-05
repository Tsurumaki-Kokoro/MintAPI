using MintOsuApi.Models;

namespace MintAPI.Services;

public record BpFixEntry(Score Score, int OldRank, int NewRank, PpResult Fixed);
public record BpFixReport(double CurrentPp, double FixedPp, double Gain, int CandidateCount,
    int SkippedCount, List<BpFixEntry> Entries);

public class BpFixService(IBeatmapFileService files, IPpCalculatorService calculator, ILogger<BpFixService> logger)
{
    public static bool IsCandidate(Score score)
    {
        if (!score.Passed || score.Beatmap is null || score.Pp is null || !double.IsFinite(score.Pp.Value) ||
            score.Rank.ToString().ToUpperInvariant() is "X" or "XH" or "SS" or "SSH") return false;
        var misses = score.Statistics?.Miss ?? 0;
        if (misses == 0) return !score.Beatmap.MaxCombo.HasValue || score.MaxCombo < score.Beatmap.MaxCombo;
        var map = score.Beatmap;
        var count = map.CountCircles + map.CountSliders + map.CountSpinners;
        var stats = score.Statistics ?? new Statistics();
        if (count == 0) count = score.RulesetId switch
        {
            2 => (stats.Great ?? 0) + (stats.LargeTickHit ?? 0) + (stats.SmallTickHit ?? 0) + (stats.SmallTickMiss ?? 0) + misses,
            3 => (stats.Perfect ?? 0) + (stats.Great ?? 0) + (stats.Good ?? 0) + (stats.Ok ?? 0) + (stats.Meh ?? 0) + misses,
            _ => (stats.Great ?? 0) + (stats.Ok ?? 0) + (stats.Meh ?? 0) + misses
        };
        return count > 0 && (double)misses / count <= .01;
    }

    public async Task<BpFixReport> AnalyzeAsync(User user, List<Score> scores, CancellationToken cancellationToken = default)
    {
        using var semaphore = new SemaphoreSlim(4);
        var candidates = scores.Select((score, index) => (score, index)).Where(x => IsCandidate(x.score)).ToList();
        var results = await Task.WhenAll(candidates.Select(async candidate =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var map = candidate.score.Beatmap!;
                var path = await files.GetOsuFilePathAsync(map.BeatmapsetId, map.Id);
                cancellationToken.ThrowIfCancellationRequested();
                var result = await Task.Run(() => calculator.CalculateFixed(candidate.score, path), cancellationToken);
                if ((candidate.score.Statistics?.Miss ?? 0) == 0 && candidate.score.MaxCombo >= result.MaxCombo)
                    return (candidate.index, result: (PpResult?)null, failed: false);
                return (candidate.index, result: (PpResult?)result, failed: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && ex is not RetryableException)
            {
                logger.LogWarning(ex, "Failed to calculate BP Fix for beatmap {BeatmapId}", candidate.score.Beatmap!.Id);
                return (candidate.index, result: (PpResult?)null, failed: true);
            }
            finally { semaphore.Release(); }
        }));
        return BuildReport(user, scores, results.Where(x => x.result is not null).ToDictionary(x => x.index, x => x.result!),
            candidates.Count, results.Count(x => x.failed));
    }

    public static BpFixReport BuildReport(User user, List<Score> scores, IReadOnlyDictionary<int, PpResult> fixedResults,
        int candidateCount, int skippedCount = 0)
    {
        var original = scores.Select(s => s.Pp ?? 0).ToArray();
        var reordered = original.Select((pp, index) => (pp: fixedResults.TryGetValue(index, out var result)
                ? Math.Max(pp, result.Pp) : pp, index)).OrderByDescending(x => x.pp).ToList();
        static double Weighted(IEnumerable<double> values) => values.Select((pp, index) => pp * Math.Pow(.95, index)).Sum();
        var gain = Math.Max(0, Weighted(reordered.Select(x => x.pp)) - Weighted(original));
        var current = user.Statistics?.Pp ?? Weighted(original);
        var ranks = reordered.Select((x, index) => (x.index, rank: index + 1)).ToDictionary(x => x.index, x => x.rank);
        var entries = fixedResults.Where(x => x.Value.Pp > original[x.Key] + .05 &&
                ((scores[x.Key].Statistics?.Miss ?? 0) > 0 || scores[x.Key].MaxCombo < x.Value.MaxCombo))
            .OrderByDescending(x => x.Value.Pp - original[x.Key]).Take(12)
            .Select(x => new BpFixEntry(scores[x.Key], x.Key + 1, ranks[x.Key], x.Value)).ToList();
        return new BpFixReport(current, current + gain, gain, candidateCount, skippedCount, entries);
    }
}
