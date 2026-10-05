using MintAPI.Services;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Unit;

public class BpFixTests
{
    private static Score Choke(int misses = 1) => new()
    {
        Passed = true, Pp = 100, Rank = Grade.A, RulesetId = 0, Accuracy = .98, MaxCombo = 90,
        Beatmap = new Beatmap { CountCircles = 100, MaxCombo = 100 },
        Statistics = new Statistics { Great = 100 - misses, Miss = misses }
    };

    [Fact]
    public void Candidates_require_small_chokes_and_exclude_full_combos_and_SS()
    {
        Assert.True(BpFixService.IsCandidate(Choke()));
        Assert.False(BpFixService.IsCandidate(Choke(2)));
        var score = Choke(0);
        Assert.True(BpFixService.IsCandidate(score));
        score.MaxCombo = 100;
        Assert.False(BpFixService.IsCandidate(score));
        score = Choke(); score.Rank = Grade.SS;
        Assert.False(BpFixService.IsCandidate(score));
        score = Choke(); score.Passed = false;
        Assert.False(BpFixService.IsCandidate(score));
    }

    [Fact]
    public void Report_reorders_all_BPs_preserves_bonus_and_limits_only_display()
    {
        var scores = Enumerable.Range(0, 14).Select(_ => Choke()).ToList();
        var fixedResults = Enumerable.Range(0, 14).ToDictionary(i => i, i => new PpResult(110 + i, 5, 100));
        var user = new User { Statistics = new UserStatistics { Pp = 5000 } };
        var report = BpFixService.BuildReport(user, scores, fixedResults, 14);
        Assert.Equal(12, report.Entries.Count);
        Assert.Equal(14, report.CandidateCount);
        Assert.Equal(14, report.Entries[0].OldRank);
        Assert.Equal(1, report.Entries[0].NewRank);
        var expected = Enumerable.Range(0, 14).Select(i => (123 - i - 100) * Math.Pow(.95, i)).Sum();
        Assert.Equal(expected, report.Gain, 8);
        Assert.Equal(5000 + expected, report.FixedPp, 8);
        Assert.Equal(100, scores[0].Pp);
    }

    [Theory]
    [InlineData("1638954.osu", 0)]
    [InlineData("2118524.osu", 3)]
    public void Native_FC_calculation_is_finite_and_uses_requested_ruleset(string fixture, int mode)
    {
        var score = Choke(); score.RulesetId = mode;
        var result = new PpCalculatorService().CalculateFixed(score, Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        Assert.True(double.IsFinite(result.Pp));
        Assert.True(result.Pp > 0);
        Assert.True(result.MaxCombo > 0);
    }
}
