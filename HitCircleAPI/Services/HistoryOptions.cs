using System.Globalization;

namespace HitCircleAPI.Services;

public sealed class HistoryOptions
{
    public bool Enabled { get; set; } = true;
    public string TimeZone { get; set; } = "Asia/Shanghai";
    public string InfoTime { get; set; } = "00:00";
    public string ScoreTime { get; set; } = "02:00";
    public bool CatchUpOnStartup { get; set; } = true;
    public bool ScoreCollectionEnabled { get; set; } = true;
    public int RecentLimit { get; set; } = 200;
    public bool OsuTrackEnabled { get; set; } = true;
    public int OsuTrackDefaultDays { get; set; } = 365;

    public TimeZoneInfo Zone => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
    public DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);
    public static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
    public bool IsValid()
    {
        try
        {
            _ = Zone;
            return ParseTime(ScoreTime) >= ParseTime(InfoTime) && RecentLimit is >= 1 and <= 1000
                && OsuTrackDefaultDays is >= 1 and <= 3650;
        }
        catch (Exception ex) when (ex is FormatException or TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return false;
        }
    }
}
