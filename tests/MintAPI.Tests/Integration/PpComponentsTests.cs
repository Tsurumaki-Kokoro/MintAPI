using System.Runtime.InteropServices;
using MintAPI.rosu_pp;
using MintAPI.Services;
using MintOsuApi.Models;

namespace MintAPI.Tests.Integration;

public class PpComponentsTests
{
    // osu! API /beatmaps/3881559: max_combo=2900, checksum=07a866b9e2cae3ba4079b6230bd90160.
    [Theory]
    [InlineData(false, "NM")]
    [InlineData(false, "DT")]
    [InlineData(false, "NC")]
    [InlineData(true, "NM")]
    public void Epitaph_max_combo_is_the_full_map_combo_even_for_partial_scores(bool partial, string mod)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu");
        var checksum = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(path)));
        Assert.Equal("07A866B9E2CAE3BA4079B6230BD90160", checksum);
        var calculator = new PpCalculatorService();
        var score = new Score
        {
            RulesetId = 0, Accuracy = partial ? .9 : 1, MaxCombo = partial ? 123 : 2900,
            Statistics = new Statistics { Great = partial ? 100 : 2278, Miss = partial ? 10 : 0 },
            Mods = mod == "NM" ? [] : [new NonLegacyMod { Acronym = mod }]
        };
        Assert.Equal(2900u, calculator.Calculate(score, path).MaxCombo);
        Assert.Equal(2900u, calculator.CalculateSs(path, 0).MaxCombo);
    }

    [Fact]
    public void Original_native_export_keeps_its_legacy_result_layout()
    {
        var map = LegacyNative.beatmap_from_path(Path.Combine(AppContext.BaseDirectory, "Fixtures", "3881559.osu"));
        Assert.NotEqual(IntPtr.Zero, map);
        try
        {
            var performance = LegacyNative.performance_from_beatmap(map);
            var result = LegacyNative.performance_calculate(performance);
            Assert.Equal(24, Marshal.SizeOf<LegacyResult>());
            Assert.Equal(2900u, result.MaxCombo);
            Assert.Equal(0, result.Mode);
            Assert.True(result.Pp > 0);
        }
        finally { LegacyNative.beatmap_free(map); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LegacyResult
    {
        public double Pp;
        public double Stars;
        public uint MaxCombo;
        public byte Mode;
    }

    private static class LegacyNative
    {
        [DllImport("rosu_pp_ffi")] public static extern IntPtr beatmap_from_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
        [DllImport("rosu_pp_ffi")] public static extern IntPtr performance_from_beatmap(IntPtr map);
        [DllImport("rosu_pp_ffi")] public static extern LegacyResult performance_calculate(IntPtr handle);
        [DllImport("rosu_pp_ffi")] public static extern void beatmap_free(IntPtr map);
    }

    [Fact]
    public async Task Native_std_components_survive_the_Csharp_binding()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pp-components-{Guid.NewGuid():N}.osu");
        var objects = Enumerable.Range(0, 300).Select(i => $"{64 + i % 4 * 128},{64 + i % 3 * 128},{1000 + i * 150},1,0,0:0:0:0:");
        var map = """
            osu file format v14
            [General]
            Mode:0
            [Metadata]
            Title:Components fixture
            Artist:Test
            Creator:Test
            Version:Test
            [Difficulty]
            HPDrainRate:5
            CircleSize:4
            OverallDifficulty:8
            ApproachRate:9
            SliderMultiplier:1.4
            SliderTickRate:1
            [TimingPoints]
            0,500,4,2,0,100,1,0
            [HitObjects]
            """;
        try
        {
            await File.WriteAllTextAsync(path, map + "\n" + string.Join("\n", objects));
            var score = new Score { RulesetId = 0, Accuracy = 1, MaxCombo = 300, Statistics = new Statistics { Great = 300 } };
            var result = new PpCalculatorService().Calculate(score, path);
            Assert.Equal(48, Marshal.SizeOf<PerformanceResult>());
            Assert.True(result.AimPp is > 0);
            Assert.True(result.SpeedPp is > 0);
            Assert.True(result.AccuracyPp is > 0);
            var combined = Math.Pow(Math.Pow(result.AimPp!.Value, 1.1) + Math.Pow(result.SpeedPp!.Value, 1.1) + Math.Pow(result.AccuracyPp!.Value, 1.1), 1 / 1.1) * 1.14;
            Assert.Equal(combined, result.Pp, precision: 6);
        }
        finally { File.Delete(path); }
    }
}
