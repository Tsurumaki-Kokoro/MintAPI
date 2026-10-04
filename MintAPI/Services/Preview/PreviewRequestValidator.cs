using System.Globalization;
using System.Text.RegularExpressions;

namespace MintAPI.Services.Preview;

public static partial class PreviewRequestValidator
{
    public static BeatmapPreviewRequest Normalize(BeatmapPreviewRequest request, BeatmapPreviewOptions options)
    {
        var format = request.Format.ToLowerInvariant();
        var selection = request.Selection.ToLowerInvariant();
        if (selection is not ("auto" or "hardest"))
            throw new PreviewValidationException("selection must be auto or hardest.");
        if (request.TimePoints.Length > 0) selection = "auto";
        if (selection == "hardest" && format != "gif")
            throw new PreviewValidationException("hardest supports native GIF only.");
        var convert = request.Convert?.ToLowerInvariant() switch
        {
            null or "" => null,
            "std" or "standard" => "standard",
            "ctb" or "catch" => "ctb",
            "taiko" => "taiko",
            "mania" => "mania",
            _ => throw new PreviewValidationException("Unsupported conversion mode.")
        };
        if (request.BeatmapId <= 0 || format is not ("png" or "gif" or "mp4"))
            throw new PreviewValidationException("A positive beatmap_id and png/gif/mp4 format are required.");
        if (request.TimePoints.Length > (format == "mp4" ? 1 : 4))
            throw new PreviewValidationException("Video supports one time point; images support at most four.");
        var points = request.TimePoints.Select(point =>
        {
            if (string.Equals(point, "preview", StringComparison.OrdinalIgnoreCase)) return "preview";
            if (!double.TryParse(point, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw new PreviewValidationException("Time points must be finite seconds or preview.");
            return value.ToString("R", CultureInfo.InvariantCulture);
        }).ToArray();
        var duration = request.DurationSeconds ?? (format == "mp4" ? 30 : format == "gif" ? 6 : (double?)null);
        if (duration.HasValue && (!double.IsFinite(duration.Value) || duration <= 0 ||
            duration > (format == "mp4" ? options.MaxVideoDurationSeconds : format == "gif" ? options.MaxGifDurationSeconds : 60)))
            throw new PreviewValidationException("Duration exceeds the permitted range.");
        if (format == "png" && duration.HasValue && points.Length != 1)
            throw new PreviewValidationException("A PNG interval requires exactly one time point.");
        var mods = request.Mods.Select(mod => mod.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (mods.Length > 12 || mods.Any(mod => !ModToken().IsMatch(mod)))
            throw new PreviewValidationException("Unsupported mod token.");
        if (mods.Any(mod => mod.StartsWith("dt")) && mods.Any(mod => mod.StartsWith("ht")) ||
            mods.Contains("ez") && mods.Contains("hr") || mods.Contains("tc") && mods.Contains("hd") ||
            mods.Contains("in") && mods.Contains("ho") || mods.Count(mod => mod.EndsWith('k')) > 1 ||
            mods.Any(mod => mod.StartsWith("da")) && (mods.Contains("ez") || mods.Contains("hr")))
            throw new PreviewValidationException("Conflicting mods.");
        foreach (var mod in mods.Where(mod => mod.StartsWith("dt") || mod.StartsWith("ht")))
        {
            if (mod.Length == 2) continue;
            var rate = double.Parse(mod[2..], CultureInfo.InvariantCulture);
            if (mod.StartsWith("dt") ? rate < 1.01 || rate > 2 : rate < .5 || rate > .99)
                throw new PreviewValidationException("Mod speed is outside the supported range.");
        }
        foreach (var mod in mods.Where(mod => mod.StartsWith("da")))
        {
            foreach (Match parameter in Regex.Matches(mod[2..], @"(cs|ar|od|hp)(-?\d+(?:\.\d+)?)"))
            {
                var value = double.Parse(parameter.Groups[2].Value, CultureInfo.InvariantCulture);
                var minimum = parameter.Groups[1].Value == "ar" ? -10 : 0;
                if (!double.IsFinite(value) || value < minimum || value > 11)
                    throw new PreviewValidationException("DA parameters are outside supported limits.");
            }
        }
        if (mods.Any(mod => mod.EndsWith('k')) && convert != "mania")
            throw new PreviewValidationException("Key count mods require convert=mania.");
        return request with { Format = format, Convert = convert, Mods = mods, TimePoints = points, DurationSeconds = duration, Selection = selection };
    }

    public static void ValidateMode(BeatmapPreviewRequest request, int sourceMode)
    {
        var targetMode = request.Convert switch { "standard" => 0, "taiko" => 1, "ctb" => 2, "mania" => 3, _ => sourceMode };
        if (request.Selection == "hardest" && targetMode != sourceMode)
            throw new PreviewValidationException("hardest does not support converted beatmaps.");
        if (sourceMode != 0 && targetMode != sourceMode)
            throw new PreviewValidationException("Only Standard beatmaps can be converted to another mode.");
        if (request.Format == "png" && targetMode == 0 && request.DurationSeconds.HasValue)
            throw new PreviewValidationException("Standard PNG does not accept duration.");
        if (request.Format == "png" && targetMode != 0 &&
            (request.TimePoints.Length > 1 || (request.TimePoints.Length == 1) != request.DurationSeconds.HasValue))
            throw new PreviewValidationException("Non-Standard PNG intervals require one time point and duration together.");
        var allowed = targetMode switch
        {
            0 => new[] { "ez", "hr", "hd", "da", "tc", "dt", "ht" },
            1 => new[] { "ez", "hr", "sw", "cs", "dt", "ht" },
            2 => new[] { "ez", "hr", "hd", "dt", "ht" },
            3 => new[] { "cs", "dt", "ht", "ds", "in", "ho" },
            _ => throw new PreviewValidationException("Unsupported beatmap mode.")
        };
        foreach (var mod in request.Mods)
        {
            var family = mod.StartsWith("dt") ? "dt" : mod.StartsWith("ht") ? "ht" : mod.StartsWith("da") ? "da" : mod;
            if (targetMode == 3 && mod.EndsWith('k')) continue;
            if (!allowed.Contains(family) || request.Format == "png" && (family is "dt" or "ht" or "cs" || targetMode == 2 && family == "hd"))
                throw new PreviewValidationException($"Mod {mod} is unsupported for this mode/format.");
        }
    }

    [GeneratedRegex(@"\A(?:ez|hr|hd|tc|sw|cs|ds|in|ho|(?:[1-9]|10)k|(?:dt|ht)(?:\d+(?:\.\d+)?)?|da(?:(?:cs|ar|od|hp)-?\d+(?:\.\d+)?)+)\z")]
    private static partial Regex ModToken();
}
