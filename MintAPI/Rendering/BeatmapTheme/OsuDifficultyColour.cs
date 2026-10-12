namespace MintAPI.Rendering.BeatmapTheme;

/// <summary>osu! web difficulty badge colours, including gamma-correct interpolation.</summary>
internal static class OsuDifficultyColour
{
    // https://github.com/ppy/osu-web/blob/master/resources/js/utils/beatmap-helper.ts
    private static readonly (double Stars, int Rgb)[] BackgroundStops =
    [
        (.1, 0x4290fb), (1.25, 0x4fc0ff), (2, 0x4fffd5), (2.5, 0x7cff4f),
        (3.3, 0xf6f05c), (4.2, 0xff8068), (4.9, 0xff4e6f), (5.8, 0xc645b8),
        (6.7, 0x6563de), (7.7, 0x18158e), (9, 0x000000)
    ];
    private static readonly (double Stars, int Rgb)[] TextStops =
    [
        (9, 0xf6f05c), (9.9, 0xff8068), (10.6, 0xff4e6f),
        (11.5, 0xc645b8), (12.4, 0x6563de)
    ];

    public static string Background(double stars) => stars < .1 ? "#aaaaaa" : Sample(BackgroundStops, stars);
    public static string Text(double stars) => stars < 6.5 ? "#000000" : stars < 9 ? "#f6f05c" : Sample(TextStops, stars);

    private static string Sample((double Stars, int Rgb)[] stops, double stars)
    {
        if (!double.IsFinite(stars)) return "#aaaaaa";
        if (stars <= stops[0].Stars) return $"#{stops[0].Rgb:x6}";
        for (var i = 1; i < stops.Length; i++)
        {
            if (stars > stops[i].Stars) continue;
            var from = stops[i - 1];
            var to = stops[i];
            var fraction = (stars - from.Stars) / (to.Stars - from.Stars);
            int Channel(int shift)
            {
                var start = (from.Rgb >> shift) & 255;
                var end = (to.Rgb >> shift) & 255;
                var value = Math.Pow(Math.Pow(start, 2.2) * (1 - fraction) + Math.Pow(end, 2.2) * fraction, 1 / 2.2);
                return (int)Math.Floor(value + .5);
            }
            return $"#{Channel(16):x2}{Channel(8):x2}{Channel(0):x2}";
        }
        return $"#{stops[^1].Rgb:x6}";
    }
}
