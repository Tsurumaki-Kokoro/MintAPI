using HitCircleAPI.rosu_pp;
using Ossapi.Models;
using RosuBeatmap = HitCircleAPI.rosu_pp.Beatmap;

namespace HitCircleAPI.Services;

public class PpCalculatorService : IPpCalculatorService
{
    private static uint GetModsValue(List<NonLegacyMod>? mods)
    {
        if (mods == null || mods.Count == 0) return 0;

        uint value = 0;
        foreach (var mod in mods)
        {
            value |= mod.Acronym switch
            {
                "NF" => 1u,
                "EZ" => 2u,
                "TD" => 4u,
                "HD" => 8u,
                "HR" => 16u,
                "SD" => 32u,
                "DT" => 64u,
                "RX" => 128u,
                "HT" => 256u,
                "NC" => 64u | 512u,
                "FL" => 1024u,
                "AT" => 2048u,
                "SO" => 4096u,
                "AP" => 8192u,
                "PF" => 32u | 16384u,
                "4K" => 32768u,
                "5K" => 65536u,
                "6K" => 131072u,
                "7K" => 262144u,
                "8K" => 524288u,
                "FI" => 1048576u,
                "RD" => 2097152u,
                "CN" => 4194304u,
                "TG" => 8388608u,
                "9K" => 16777216u,
                "KP" => 33554432u,
                "1K" => 67108864u,
                "3K" => 134217728u,
                "2K" => 268435456u,
                "V2" => 536870912u,
                "MR" => 1073741824u,
                _ => 0u
            };
        }
        return value;
    }

    public PpResult Calculate(Score score, string osuFilePath)
    {
        using var beatmap = RosuBeatmap.FromPath(osuFilePath);
        var mods = GetModsValue(score.Mods);
        var stats = score.Statistics;

        var result = new Performance(beatmap)
            .Mods(mods)
            .Accuracy(score.Accuracy * 100)
            .Combo((uint)score.MaxCombo)
            .N300((uint)(stats?.Great ?? 0))
            .N100((uint)(stats?.Ok ?? 0))
            .N50((uint)(stats?.Meh ?? 0))
            .Misses((uint)(stats?.Miss ?? 0))
            .NGeki((uint)(stats?.Perfect ?? 0))
            .NKatu((uint)(stats?.Good ?? 0))
            .Calculate();

        return new PpResult(result.Pp, result.Stars, result.MaxCombo,
            result.Mode == 0 ? result.PpAim : null,
            result.Mode == 0 ? result.PpSpeed : null,
            result.Mode == 0 ? result.PpAcc : null);
    }

    public (double IfPp, double SsPp) CalculateIfFcAndSs(Score score, string osuFilePath)
    {
        using var beatmap = RosuBeatmap.FromPath(osuFilePath);
        var mods = GetModsValue(score.Mods);
        var stats = score.Statistics;

        var great = (uint)(stats?.Great ?? 0);
        var misses = (uint)(stats?.Miss ?? 0);

        var ifResult = new Performance(beatmap)
            .Mods(mods)
            .Accuracy(score.Accuracy * 100)
            .N100((uint)(stats?.Ok ?? 0))
            .N50((uint)(stats?.Meh ?? 0))
            .N300(great + misses)
            .NGeki((uint)(stats?.Perfect ?? 0))
            .NKatu((uint)(stats?.Good ?? 0))
            .Calculate();

        double ifPp = double.IsNaN(ifResult.Pp) ? 0 : ifResult.Pp;

        using var beatmap2 = RosuBeatmap.FromPath(osuFilePath);
        var ssResult = new Performance(beatmap2)
            .Mods(mods)
            .Accuracy(100)
            .Calculate();

        double ssPp = double.IsNaN(ssResult.Pp) ? 0 : ssResult.Pp;

        return (ifPp, ssPp);
    }

    public PpResult CalculateSs(string osuFilePath, int rulesetId, uint mods = 0)
    {
        using var beatmap = RosuBeatmap.FromPath(osuFilePath);
        var result = new Performance(beatmap)
            .Mods(mods)
            .Accuracy(100)
            .Calculate();

        return new PpResult(result.Pp, result.Stars, result.MaxCombo,
            result.Mode == 0 ? result.PpAim : null,
            result.Mode == 0 ? result.PpSpeed : null,
            result.Mode == 0 ? result.PpAcc : null);
    }

    public (double NewPp, int Position) FindOptimalNewPp(List<double> ppList, double desiredIncrease)
    {
        static double CalculateTotalPp(List<double> list)
        {
            double total = 0;
            for (int i = 0; i < list.Count; i++)
                total += list[i] * Math.Pow(0.95, i);
            return total;
        }

        var sorted = ppList.OrderByDescending(x => x).ToList();

        if (sorted.Count < 100)
        {
            sorted.Add(desiredIncrease);
            sorted.Sort((a, b) => b.CompareTo(a));
            int pos = sorted.IndexOf(desiredIncrease) + 1;
            return (desiredIncrease, pos);
        }

        double totalPp = CalculateTotalPp(sorted);
        double bp100 = sorted[99];

        double low = bp100, high = bp100 + desiredIncrease + 100;
        double optimalNewPp = high;

        while (low <= high)
        {
            double mid = (low + high) / 2;
            var newList = new List<double>(sorted) { mid };
            newList.Sort((a, b) => b.CompareTo(a));
            if (newList.Count > 100) newList.RemoveAt(newList.Count - 1);

            double newTotal = CalculateTotalPp(newList);
            if (newTotal - totalPp >= desiredIncrease)
            {
                optimalNewPp = mid;
                high = mid - 0.01;
            }
            else
            {
                low = mid + 0.01;
            }
        }

        var finalList = new List<double>(sorted) { optimalNewPp };
        finalList.Sort((a, b) => b.CompareTo(a));
        int position = finalList.IndexOf(optimalNewPp) + 1;

        return (optimalNewPp, position);
    }
}
