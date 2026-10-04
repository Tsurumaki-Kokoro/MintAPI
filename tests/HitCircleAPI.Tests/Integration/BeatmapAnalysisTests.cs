using HitCircleAPI.Services;

namespace HitCircleAPI.Tests.Integration;

public class BeatmapAnalysisTests
{
    [Fact]
    public void Standard_analysis_matches_real_3881559_calculations()
    {
        var result = new BeatmapAnalysisService().Calculate(Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu"));
        Assert.NotNull(result);
        Assert.Equal(2900u, result.Ss.MaxCombo);
        Assert.Equal(792.1304525583831, result.Ss.Pp, 4);
        Assert.Equal(291.80313, result.Ss.AimPp!.Value, 4);
        Assert.Equal(273.19586, result.Ss.SpeedPp!.Value, 4);
        Assert.Equal(201.92498, result.Ss.AccuracyPp!.Value, 4);
        Assert.Equal(new[] { 98d, 99d, 99.5d, 100d }, result.AccuracyReferences.Select(p => p.Accuracy));
        Assert.Equal(683.82901, result.AccuracyReferences[0].Pp, 4);
        Assert.Equal(result.Ss.Pp, result.AccuracyReferences[^1].Pp);
        Assert.Equal(new[] { "NM", "HD", "HR", "DT", "HDDT" }, result.Mods.Select(m => m.Name));
        Assert.Equal(2893.00174, result.Mods[^1].Pp, 4);
        Assert.Equal(10.86667, result.Mods[3].Ar, 4);
        Assert.Equal(10.97778, result.Mods[3].Od, 4);
        Assert.Equal(3.788678, result.Skills!.Value.Aim, 5);
        Assert.Equal(3.707546, result.Skills!.Value.Speed, 5);
        Assert.Equal(.9996641, result.Skills!.Value.SliderFactor, 6);
        Assert.Equal(839, result.Strains.Points.Length);
        var peak = result.Strains.Points.MaxBy(p => p.Aim * p.Aim + p.Speed * p.Speed);
        Assert.Equal(234800, peak.EndTimeMs);
        Assert.Equal(379.16987, peak.Aim, 4);
        Assert.All(result.Strains.Points, p => Assert.True(double.IsFinite(p.Aim) && double.IsFinite(p.Speed)));
    }

    [Theory]
    [InlineData("1028484.osu")]
    [InlineData("2118524.osu")]
    [InlineData("1638954.osu")]
    public void Nonstandard_maps_do_not_expose_standard_skills(string file)
    {
        var result = new BeatmapAnalysisService().Calculate(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        Assert.NotNull(result);
        Assert.Null(result.Skills);
        Assert.Null(result.Ss.AimPp);
        Assert.Null(result.Ss.SpeedPp);
        Assert.NotEmpty(result.Strains.Points);
        Assert.All(result.Curves, curve =>
        {
            Assert.Equal(result.Strains.Points.Length, curve.Values.Length);
            Assert.All(curve.Values, value => Assert.True(double.IsFinite(value) && value >= 0));
        });
    }

    [Fact]
    public void Taiko_uses_native_four_skills_and_additive_pp_components()
    {
        var result = Analyze("1028484.osu");
        Assert.Equal(1, result.Mode);
        Assert.Equal(289u, result.Ss.MaxCombo);
        Assert.Equal(130.334275305, result.Ss.Pp, 7);
        Assert.Equal(33.551925018, result.DifficultyPp!.Value, 7);
        Assert.Equal(result.Ss.Pp, result.DifficultyPp.Value + result.Ss.AccuracyPp!.Value, 8);
        Assert.Equal(2.053873997, result.Ruleset.Stamina, 7);
        Assert.Equal(.209107731, result.Ruleset.Rhythm, 7);
        Assert.Equal(.653306364, result.Ruleset.Color, 7);
        Assert.True(result.Ruleset.Reading > 0);
        Assert.Equal(new[] { "耐力", "节奏", "换色", "读图" }, result.Curves.Select(c => c.Name));
        Assert.Equal(34.5, result.Mods[0].GreatHitWindow);
        Assert.True(result.Mods[3].GreatHitWindow < result.Mods[0].GreatHitWindow);
        Assert.True(result.Mods[4].GreatHitWindow > result.Mods[0].GreatHitWindow);
    }

    [Fact]
    public void Catch_keeps_tiny_droplet_loss_separate_from_combo_breaks()
    {
        var result = Analyze("2118524.osu");
        Assert.Equal(2, result.Mode);
        Assert.Equal(728u, result.Ruleset.Fruits);
        Assert.Equal(2u, result.Ruleset.Droplets);
        Assert.Equal(263u, result.Ruleset.TinyDroplets);
        Assert.Equal(result.Ruleset.Fruits + result.Ruleset.Droplets, result.Ss.MaxCombo);
        Assert.Equal(113.859037144, result.Ss.Pp, 7);
        Assert.Null(result.DifficultyPp);
        Assert.Null(result.Ss.AccuracyPp);
        Assert.Single(result.Curves);
        Assert.Equal("移动", result.Curves[0].Name);
        var references = result.AccuracyReferences;
        Assert.Equal(result.Ss.Pp, references[0].Pp, 8);
        Assert.Equal(10u, references[1].TinyMisses);
        Assert.Equal(0u, references[1].Misses);
        Assert.Equal(100d * 983 / 993, references[1].Accuracy, 8);
        Assert.Equal(20u, references[2].TinyMisses);
        Assert.Equal(1u, references[3].Misses);
        Assert.Equal(0u, references[3].TinyMisses);
        Assert.True(references[1].Pp < references[0].Pp);
        Assert.True(references[2].Pp < references[1].Pp);
    }

    [Fact]
    public void Mania_references_fix_judgments_and_combo_does_not_change_pp()
    {
        var result = Analyze("1638954.osu");
        Assert.Equal(3, result.Mode);
        Assert.Equal(594u, result.Ruleset.Objects);
        Assert.Equal(121u, result.Ruleset.Holds);
        Assert.Equal(956u, result.Ss.MaxCombo);
        Assert.Equal(108.922974717, result.Ss.Pp, 7);
        Assert.Equal(result.Ss.Pp, result.DifficultyPp!.Value, 8);
        Assert.Null(result.Ss.AccuracyPp);
        Assert.Equal("综合", Assert.Single(result.Curves).Name);
        Assert.Equal(result.Ss.Pp, result.AccuracyReferences[0].Pp, 8);
        Assert.All(result.AccuracyReferences, row => Assert.Equal(715u, row.N320 + row.N200));
        Assert.All(result.AccuracyReferences, row => Assert.Equal(0u, row.Misses));
        Assert.All(result.AccuracyReferences, row => Assert.Equal(
            100d * (61d * row.N320 + 40d * row.N200) / (61d * 715), row.Accuracy, 8));
        for (var i = 1; i < result.AccuracyReferences.Length; i++)
            Assert.True(result.AccuracyReferences[i].Pp < result.AccuracyReferences[i - 1].Pp);
        using var map = HitCircleAPI.rosu_pp.Beatmap.FromPath(Path.Combine(AppContext.BaseDirectory, "Fixtures", "1638954.osu"));
        using var zeroCombo = new HitCircleAPI.rosu_pp.Performance(map).Lazer(true).Combo(0).Accuracy(100).Misses(0);
        Assert.Equal(result.Ss.Pp, zeroCombo.Calculate().Pp, 8);
    }

    private static BeatmapAnalysis Analyze(string file) =>
        new BeatmapAnalysisService().Calculate(Path.Combine(AppContext.BaseDirectory, "Fixtures", file))!;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    public void Empty_maps_skip_analysis_and_single_objects_keep_finite_data(int objectCount, int mode)
    {
        var path = Path.Combine(Path.GetTempPath(), "beatmap-analysis-" + Guid.NewGuid().ToString("N") + ".osu");
        try
        {
            File.WriteAllText(path, $"osu file format v14\n[General]\nMode:{mode}\n[Difficulty]\nCircleSize:5\nOverallDifficulty:5\n[HitObjects]\n" +
                (objectCount == 0 ? "" : "256,192,1000,1,0,0:0:0:0:\n"));
            var result = new BeatmapAnalysisService().Calculate(path);
            if (objectCount == 0)
            {
                Assert.Null(result);
                return;
            }
            Assert.NotNull(result);
            Assert.True(double.IsFinite(result.Ss.Pp));
            if (result.Skills is { } skills) Assert.True(double.IsFinite(skills.Aim));
            Assert.All(result.Curves, c => Assert.All(c.Values, v => Assert.True(double.IsFinite(v))));
            if (mode == 0) Assert.Single(result.Strains.Points);
            Assert.All(result.Strains.Points, p => Assert.True(double.IsFinite(p.Aim) && double.IsFinite(p.Speed)));
        }
        finally { File.Delete(path); }
    }
}
