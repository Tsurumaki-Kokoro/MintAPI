using MintAPI.rosu_pp;

namespace MintAPI.Services;

public sealed class BeatmapAnalysisService : IBeatmapAnalysisService
{
    public BeatmapAnalysis? Calculate(string osuFilePath)
    {
        using var map = Beatmap.FromPath(osuFilePath);
        if (map.GetRulesetAnalysisAttributes() is not { } ruleset) return null;
        var skills = map.GetOsuAnalysisAttributes();
        using var difficulty = new Difficulty().Lazer(true);
        using var attributes = difficulty.Calculate(map);
        var ssResult = CalculatePerformance(map, 0, 100, attributes.MaxCombo);
        var ss = new PpResult(ssResult.Result.Pp, ssResult.Result.Stars, ssResult.Result.MaxCombo,
            Finite(ssResult.Result.PpAim), Finite(ssResult.Result.PpSpeed), Finite(ssResult.Result.PpAcc));
        var references = ruleset.Mode switch
        {
            2 => CatchReferences(map, ruleset, ss),
            3 => ManiaReferences(map, ruleset),
            _ => new[] { 98d, 99d, 99.5d, 100d }.Select(acc => new AccuracyPpReference(acc,
                acc == 100 ? ss.Pp : CalculatePerformance(map, 0, acc, attributes.MaxCombo).Result.Pp)).ToArray()
        };
        (string Name, uint Value)[] modOptions = ruleset.Mode switch
        {
            0 => [("NM", 0), ("HD", 8), ("HR", 16), ("DT", 64), ("HDDT", 72)],
            3 => [("NM", 0), ("DT", 64), ("HT", 256), ("EZ", 2), ("NF", 1)],
            _ => [("NM", 0), ("HD", 8), ("HR", 16), ("DT", 64), ("HT", 256)]
        };
        var mods = modOptions.Select(mod =>
        {
            var settings = mod.Value == 0 ? ruleset : map.GetRulesetAnalysisAttributes(mod.Value)!.Value;
            using var modDifficulty = new Difficulty().Mods(mod.Value).Lazer(true);
            using var modAttributes = modDifficulty.Calculate(map);
            var performance = mod.Value == 0 ? ssResult.Result : CalculatePerformance(map, mod.Value, 100, modAttributes.MaxCombo).Result;
            return new BeatmapModReference(mod.Name, performance.Stars, performance.Pp, settings.Ar, settings.Od,
                settings.GreatHitWindow, settings.OkHitWindow);
        }).ToArray();
        var strains = difficulty.GetPreviewStrains(map);
        var curveNames = ruleset.Mode switch
        {
            1 => new[] { "耐力", "节奏", "换色", "读图" },
            2 => new[] { "移动" },
            3 => new[] { "综合" },
            _ => new[] { "Aim", "Speed" }
        };
        var colors = new[] { "#0066cc", "#bf651d", "#53615c", "#8654a3" };
        var points = ruleset.Mode == 0
            ? strains.Points.Select(p => new AnalysisStrainPoint { EndTimeMs = p.EndTimeMs, First = p.Aim, Second = p.Speed }).ToArray()
            : difficulty.GetAnalysisStrains(map);
        var curves = curveNames.Select((name, i) => new BeatmapStrainSeries(name, colors[i], points.Select(p => p.Value(i)).ToArray())).ToArray();
        return new BeatmapAnalysis(ss, references, mods, skills, strains, ruleset, Finite(ssResult.PpDifficulty), curves);
    }

    private static AccuracyPpReference[] CatchReferences(Beatmap map, RulesetAnalysisAttributes counts, PpResult ss)
    {
        var total = counts.Fruits + counts.Droplets + counts.TinyDroplets;
        AccuracyPpReference Reference(uint missedTiny, uint misses, string label)
        {
            var missedFruit = Math.Min(counts.Fruits, misses);
            var missedDroplet = misses - missedFruit;
            var combo = ss.MaxCombo - misses;
            using var performance = new Performance(map).Lazer(true).Combo(combo)
                .N300(counts.Fruits - missedFruit).N100(counts.Droplets - missedDroplet)
                .N50(counts.TinyDroplets - missedTiny).NKatu(missedTiny).Misses(misses);
            var result = performance.CalculateAnalysis();
            var accuracy = total == 0 ? 100 : 100d * (total - misses - missedTiny) / total;
            var detail = misses == 0 ? $"漏小水滴 {missedTiny:N0}" : $"水果 / 水滴漏接 {misses:N0}";
            return new AccuracyPpReference(accuracy, result.Result.Pp, label, detail, misses, missedTiny);
        }
        return [Reference(0, 0, "SS"),
            Reference(Math.Min(counts.TinyDroplets, (uint)Math.Round(total * .01)), 0, "FC"),
            Reference(Math.Min(counts.TinyDroplets, (uint)Math.Round(total * .02)), 0, "FC"),
            Reference(0, Math.Min(1u, ss.MaxCombo), ss.MaxCombo > 0 ? "1 Miss" : "SS")];
    }

    private static AccuracyPpReference[] ManiaReferences(Beatmap map, RulesetAnalysisAttributes counts)
    {
        // Lazer judges both the head and tail of each hold note.
        var judgments = counts.Objects + counts.Holds;
        return new[] { 100d, 99d, 98d, 95d }.Select(target =>
        {
            // NM/lazer accuracy weights 320 at 61 and 200 at 40. Fix the judgments
            // rather than treating displayed accuracy as a unique Mania PP input.
            var n200 = Math.Min(judgments, (uint)Math.Round(judgments * (1 - target / 100) * 61 / 21));
            var n320 = judgments - n200;
            using var performance = new Performance(map).Lazer(true).NGeki(n320).N300(0).NKatu(n200).N100(0).N50(0).Misses(0);
            var result = performance.CalculateAnalysis();
            var accuracy = judgments == 0 ? 100 : 100d * (61d * n320 + 40d * n200) / (61d * judgments);
            return new AccuracyPpReference(accuracy, result.Result.Pp,
                FormattableString.Invariant($"{accuracy:0.##}%"), $"320 {n320:N0} · 200 {n200:N0}", N320: n320, N200: n200);
        }).ToArray();
    }

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;

    private static AnalysisPerformanceResult CalculatePerformance(Beatmap map, uint mods, double accuracy, uint combo)
    {
        using var performance = new Performance(map).Mods(mods).Lazer(true).Accuracy(accuracy).Combo(combo).Misses(0);
        return performance.CalculateAnalysis();
    }
}
