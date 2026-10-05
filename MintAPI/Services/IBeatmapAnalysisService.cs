using MintAPI.rosu_pp;

namespace MintAPI.Services;

public record AccuracyPpReference(double Accuracy, double Pp, string? Label = null, string? Detail = null,
    uint Misses = 0, uint TinyMisses = 0, uint N320 = 0, uint N200 = 0);
public record BeatmapModReference(string Name, double Stars, double Pp, double Ar, double Od,
    double GreatHitWindow = 0, double OkHitWindow = 0);
public record BeatmapStrainSeries(string Name, string Color, double[] Values);
public record BeatmapAnalysis(PpResult Ss, AccuracyPpReference[] AccuracyReferences,
    BeatmapModReference[] Mods, OsuAnalysisAttributes? Skills, OsuStrainTimeline Strains,
    RulesetAnalysisAttributes Ruleset, double? DifficultyPp, BeatmapStrainSeries[] Curves)
{
    public byte Mode => Ruleset.Mode;
    public BeatmapBpmSegment[] BpmSegments { get; init; } = [];
}

public interface IBeatmapAnalysisService
{
    /// <summary>NM/lazer analysis of the map's native ruleset; empty maps return null.</summary>
    BeatmapAnalysis? Calculate(string osuFilePath);
}
