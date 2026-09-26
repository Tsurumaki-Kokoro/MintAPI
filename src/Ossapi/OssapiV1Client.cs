using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ossapi.Mods;

namespace Ossapi.V1;

/// <summary>
/// Async osu! API v1 client. Uses a legacy API key for authentication.
/// v1 is read-only and exposes beatmaps, users, scores, replays, and matches.
/// </summary>
public class OssapiV1Client : IDisposable
{
    private const string BaseUrl    = "https://osu.ppy.sh/api/";
    private const string UserAgent  = "Ossapi.NET/1.0";
    private const int    Timeout    = 15;

    private readonly HttpClient _http;
    private readonly string     _apiKey;
    private bool _disposed;

    public OssapiV1Client(string apiKey)
    {
        _apiKey = apiKey;
        _http   = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout     = TimeSpan.FromSeconds(Timeout),
        };
        _http.DefaultRequestHeaders.Add("User-Agent", UserAgent);
    }

    // -------------------------------------------------------------------------
    // Endpoints
    // -------------------------------------------------------------------------

    /// <summary>
    /// Get beatmaps. At least one filter parameter should be provided.
    /// </summary>
    public Task<List<V1Beatmap>> GetBeatmapsAsync(
        DateTimeOffset? since    = null,
        int? beatmapsetId        = null,
        int? beatmapId           = null,
        string? user             = null,
        string? userType         = null,
        int? mode                = null,
        bool? includeConverts    = null,
        string? beatmapHash      = null,
        int? limit               = null,
        Mod? mods                = null,
        CancellationToken ct     = default)
        => GetListAsync<V1Beatmap>("get_beatmaps", new()
        {
            ["since"] = since.HasValue ? since.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
            ["s"]     = beatmapsetId,
            ["b"]     = beatmapId,
            ["u"]     = user,
            ["type"]  = userType,
            ["m"]     = mode,
            ["a"]     = includeConverts.HasValue ? (includeConverts.Value ? 1 : 0) : null,
            ["h"]     = beatmapHash,
            ["limit"] = limit,
            ["mods"]  = mods.HasValue ? (int?)mods.Value.Value : null,
        }, ct);

    /// <summary>Get a match by id.</summary>
    public Task<V1MatchInfo> GetMatchAsync(int matchId, CancellationToken ct = default)
        => GetObjectAsync<V1MatchInfo>("get_match", new() { ["mp"] = matchId }, ct);

    /// <summary>Get scores on a beatmap.</summary>
    public Task<List<V1Score>> GetScoresAsync(
        int beatmapId,
        string? user         = null,
        int? mode            = null,
        Mod? mods            = null,
        string? userType     = null,
        int? limit           = null,
        CancellationToken ct = default)
        => GetListAsync<V1Score>("get_scores", new()
        {
            ["b"]     = beatmapId,
            ["u"]     = user,
            ["m"]     = mode,
            ["mods"]  = mods.HasValue ? (int?)mods.Value.Value : null,
            ["type"]  = userType,
            ["limit"] = limit,
        }, ct, injectBeatmapId: beatmapId);

    /// <summary>
    /// Get the base64-encoded replay data for a score.
    /// Returns the raw base64 string as returned by the API.
    /// </summary>
    public async Task<string> GetReplayAsync(
        int? beatmapId       = null,
        string? user         = null,
        int? mode            = null,
        long? scoreId        = null,
        string? userType     = null,
        Mod? mods            = null,
        CancellationToken ct = default)
    {
        var data = await GetObjectAsync<JObject>("get_replay", new()
        {
            ["b"]    = beatmapId,
            ["u"]    = user,
            ["m"]    = mode,
            ["s"]    = scoreId,
            ["type"] = userType,
            ["mods"] = mods.HasValue ? (int?)mods.Value.Value : null,
        }, ct);
        return data["content"]?.Value<string>()
            ?? throw new OssapiV1Exception("Replay response missing 'content' field.");
    }

    /// <summary>Get a user by id or username.</summary>
    public async Task<V1User?> GetUserAsync(
        string user,
        int? mode            = null,
        string? userType     = null,
        int? eventDays       = null,
        CancellationToken ct = default)
    {
        var list = await GetListAsync<V1User>("get_user", new()
        {
            ["u"]          = user,
            ["m"]          = mode,
            ["type"]       = userType,
            ["event_days"] = eventDays,
        }, ct);
        return list.Count > 0 ? list[0] : null;
    }

    /// <summary>Get a user's best scores.</summary>
    public Task<List<V1Score>> GetUserBestAsync(
        string user,
        int? mode            = null,
        int? limit           = null,
        string? userType     = null,
        CancellationToken ct = default)
        => GetListAsync<V1Score>("get_user_best", new()
        {
            ["u"]     = user,
            ["m"]     = mode,
            ["limit"] = limit,
            ["type"]  = userType,
        }, ct);

    /// <summary>Get a user's recent scores.</summary>
    public Task<List<V1Score>> GetUserRecentAsync(
        string user,
        int? mode            = null,
        int? limit           = null,
        string? userType     = null,
        CancellationToken ct = default)
        => GetListAsync<V1Score>("get_user_recent", new()
        {
            ["u"]     = user,
            ["m"]     = mode,
            ["limit"] = limit,
            ["type"]  = userType,
        }, ct);

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private async Task<T> GetObjectAsync<T>(string endpoint,
        Dictionary<string, object?> queryParams, CancellationToken ct)
    {
        var url  = BuildUrl(endpoint, queryParams);
        var body = await FetchAsync(url, ct);
        return JsonConvert.DeserializeObject<T>(body)
            ?? throw new OssapiV1Exception("API returned null response.");
    }

    private async Task<List<T>> GetListAsync<T>(string endpoint,
        Dictionary<string, object?> queryParams, CancellationToken ct,
        int? injectBeatmapId = null)
    {
        var url  = BuildUrl(endpoint, queryParams);
        var body = await FetchAsync(url, ct);

        var arr = JsonConvert.DeserializeObject<JArray>(body)
            ?? throw new OssapiV1Exception("Expected a JSON array.");

        if (injectBeatmapId.HasValue)
            foreach (var item in arr.OfType<JObject>())
                item["beatmap_id"] = injectBeatmapId.Value.ToString();

        return arr.ToObject<List<T>>()
            ?? [];
    }

    private async Task<string> FetchAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // v1 always returns 200; errors live inside the JSON body
        if (body.TrimStart().StartsWith('{'))
        {
            var obj = JsonConvert.DeserializeObject<JObject>(body);
            if (obj?["error"] is JToken err)
            {
                var msg = err.Value<string>() ?? "Unknown error";
                if (msg.Contains("Replay not available"))
                    throw new V1ReplayUnavailableException("Replay not available.");
                if (msg.Contains("Replay retrieval failed"))
                    throw new V1ReplayUnavailableException("Replay retrieval failed.");
                if (msg.Contains("valid API key"))
                    throw new OssapiV1Exception("Invalid API key.");
                throw new OssapiV1Exception($"API error: {msg}");
            }
        }

        return body;
    }

    private string BuildUrl(string endpoint, Dictionary<string, object?> queryParams)
    {
        var parts = new List<string> { $"k={Uri.EscapeDataString(_apiKey)}" };
        foreach (var (key, value) in queryParams)
        {
            if (value is null) continue;
            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.ToString()!)}");
        }
        return endpoint + "?" + string.Join("&", parts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}

// ---------------------------------------------------------------------------
// V1 Models  (all API values arrive as JSON strings, hence string? properties)
// ---------------------------------------------------------------------------

public class V1Beatmap
{
    [JsonProperty("approved")]           public string? Approved          { get; set; }
    [JsonProperty("submit_date")]        public string? SubmitDate        { get; set; }
    [JsonProperty("approved_date")]      public string? ApprovedDate      { get; set; }
    [JsonProperty("last_update")]        public string? LastUpdate        { get; set; }
    [JsonProperty("artist")]             public string? Artist            { get; set; }
    [JsonProperty("beatmap_id")]         public string? BeatmapId         { get; set; }
    [JsonProperty("beatmapset_id")]      public string? BeatmapsetId      { get; set; }
    [JsonProperty("bpm")]                public string? Bpm               { get; set; }
    [JsonProperty("creator")]            public string? Creator           { get; set; }
    [JsonProperty("creator_id")]         public string? CreatorId         { get; set; }
    [JsonProperty("difficultyrating")]   public string? StarRating        { get; set; }
    [JsonProperty("diff_aim")]           public string? StarsAim          { get; set; }
    [JsonProperty("diff_speed")]         public string? StarsSpeed        { get; set; }
    [JsonProperty("diff_size")]          public string? CircleSize        { get; set; }
    [JsonProperty("diff_overall")]       public string? OverallDifficulty { get; set; }
    [JsonProperty("diff_approach")]      public string? ApproachRate      { get; set; }
    [JsonProperty("diff_drain")]         public string? Health            { get; set; }
    [JsonProperty("hit_length")]         public string? HitLength         { get; set; }
    [JsonProperty("source")]             public string? Source            { get; set; }
    [JsonProperty("genre_id")]           public string? GenreId           { get; set; }
    [JsonProperty("language_id")]        public string? LanguageId        { get; set; }
    [JsonProperty("title")]              public string? Title             { get; set; }
    [JsonProperty("total_length")]       public string? TotalLength       { get; set; }
    [JsonProperty("version")]            public string? Version           { get; set; }
    [JsonProperty("file_md5")]           public string? BeatmapHash       { get; set; }
    [JsonProperty("mode")]               public string? Mode              { get; set; }
    [JsonProperty("tags")]               public string? Tags              { get; set; }
    [JsonProperty("favourite_count")]    public string? FavouriteCount    { get; set; }
    [JsonProperty("rating")]             public string? Rating            { get; set; }
    [JsonProperty("playcount")]          public string? Playcount         { get; set; }
    [JsonProperty("passcount")]          public string? Passcount         { get; set; }
    [JsonProperty("count_normal")]       public string? CountHitcircles   { get; set; }
    [JsonProperty("count_slider")]       public string? CountSliders      { get; set; }
    [JsonProperty("count_spinner")]      public string? CountSpinners     { get; set; }
    [JsonProperty("max_combo")]          public string? MaxCombo          { get; set; }
    [JsonProperty("storyboard")]         public string? Storyboard        { get; set; }
    [JsonProperty("video")]              public string? Video             { get; set; }
    [JsonProperty("download_unavailable")] public string? DownloadUnavailable { get; set; }
    [JsonProperty("audio_unavailable")]  public string? AudioUnavailable  { get; set; }

    // Typed convenience accessors
    public int?    BeatmapIdInt      => int.TryParse(BeatmapId, out var v)      ? v : null;
    public int?    BeatmapsetIdInt   => int.TryParse(BeatmapsetId, out var v)   ? v : null;
    public double? StarRatingDouble  => double.TryParse(StarRating, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public bool?   HasStoryboard     => Storyboard == "1";
    public bool?   HasVideo          => Video      == "1";
    public DateTimeOffset? SubmitDateOffset    => ParseV1Date(SubmitDate);
    public DateTimeOffset? ApprovedDateOffset  => ParseV1Date(ApprovedDate);
    public DateTimeOffset? LastUpdateOffset    => ParseV1Date(LastUpdate);

    private static DateTimeOffset? ParseV1Date(string? s) =>
        s is not null && DateTimeOffset.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
}

public class V1User
{
    [JsonProperty("user_id")]              public string? UserId           { get; set; }
    [JsonProperty("username")]             public string? Username         { get; set; }
    [JsonProperty("join_date")]            public string? JoinDate         { get; set; }
    [JsonProperty("count300")]             public string? Count300         { get; set; }
    [JsonProperty("count100")]             public string? Count100         { get; set; }
    [JsonProperty("count50")]              public string? Count50          { get; set; }
    [JsonProperty("playcount")]            public string? Playcount        { get; set; }
    [JsonProperty("ranked_score")]         public string? RankedScore      { get; set; }
    [JsonProperty("total_score")]          public string? TotalScore       { get; set; }
    [JsonProperty("pp_rank")]              public string? Rank             { get; set; }
    [JsonProperty("level")]                public string? Level            { get; set; }
    [JsonProperty("pp_raw")]               public string? PpRaw            { get; set; }
    [JsonProperty("accuracy")]             public string? Accuracy         { get; set; }
    [JsonProperty("count_rank_ss")]        public string? CountRankSs      { get; set; }
    [JsonProperty("count_rank_ssh")]       public string? CountRankSsh     { get; set; }
    [JsonProperty("count_rank_s")]         public string? CountRankS       { get; set; }
    [JsonProperty("count_rank_sh")]        public string? CountRankSh      { get; set; }
    [JsonProperty("count_rank_a")]         public string? CountRankA       { get; set; }
    [JsonProperty("country")]              public string? Country          { get; set; }
    [JsonProperty("total_seconds_played")] public string? SecondsPlayed    { get; set; }
    [JsonProperty("pp_country_rank")]      public string? CountryRank      { get; set; }
    [JsonProperty("events")]               public List<V1Event> Events     { get; set; } = [];

    public int?    UserIdInt    => int.TryParse(UserId, out var v)    ? v : null;
    public double? PpRawDouble  => double.TryParse(PpRaw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public double? AccuracyDouble => double.TryParse(Accuracy, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    public DateTimeOffset? JoinDateOffset => ParseV1Date(JoinDate);

    private static DateTimeOffset? ParseV1Date(string? s) =>
        s is not null && DateTimeOffset.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
}

public class V1Event
{
    [JsonProperty("display_html")]   public string? DisplayHtml   { get; set; }
    [JsonProperty("beatmap_id")]     public string? BeatmapId     { get; set; }
    [JsonProperty("beatmapset_id")]  public string? BeatmapsetId  { get; set; }
    [JsonProperty("date")]           public string? Date          { get; set; }
    [JsonProperty("epicfactor")]     public string? EpicFactor    { get; set; }
}

public class V1Score
{
    [JsonProperty("beatmap_id")]      public string? BeatmapId       { get; set; }
    [JsonProperty("score_id")]        public string? ScoreId         { get; set; }
    [JsonProperty("score")]           public string? Score           { get; set; }
    [JsonProperty("username")]        public string? Username        { get; set; }
    [JsonProperty("count300")]        public string? Count300        { get; set; }
    [JsonProperty("count100")]        public string? Count100        { get; set; }
    [JsonProperty("count50")]         public string? Count50         { get; set; }
    [JsonProperty("countmiss")]       public string? CountMiss       { get; set; }
    [JsonProperty("maxcombo")]        public string? MaxCombo        { get; set; }
    [JsonProperty("countkatu")]       public string? CountKatu       { get; set; }
    [JsonProperty("countgeki")]       public string? CountGeki       { get; set; }
    [JsonProperty("perfect")]         public string? Perfect         { get; set; }
    [JsonProperty("enabled_mods")]    public string? EnabledMods     { get; set; }
    [JsonProperty("user_id")]         public string? UserId          { get; set; }
    [JsonProperty("date")]            public string? Date            { get; set; }
    [JsonProperty("rank")]            public string? Rank            { get; set; }
    [JsonProperty("pp")]              public string? Pp              { get; set; }
    [JsonProperty("replay_available")] public string? ReplayAvailable { get; set; }

    public Mod? Mods => EnabledMods is not null && int.TryParse(EnabledMods, out var v) ? new Mod(v) : null;
    public bool IsPerfect => Perfect == "1";
    public DateTimeOffset? DateOffset => ParseV1Date(Date);

    private static DateTimeOffset? ParseV1Date(string? s) =>
        s is not null && DateTimeOffset.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
}

public class V1MatchInfo
{
    [JsonProperty("match")] public V1Match      Match { get; set; } = new();
    [JsonProperty("games")] public List<V1Game> Games { get; set; } = [];
}

public class V1Match
{
    [JsonProperty("match_id")]   public string? MatchId   { get; set; }
    [JsonProperty("name")]       public string? Name      { get; set; }
    [JsonProperty("start_time")] public string? StartTime { get; set; }
    [JsonProperty("end_time")]   public string? EndTime   { get; set; }

    public DateTimeOffset? StartTimeOffset => ParseV1Date(StartTime);
    public DateTimeOffset? EndTimeOffset   => ParseV1Date(EndTime);

    private static DateTimeOffset? ParseV1Date(string? s) =>
        s is not null && DateTimeOffset.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
}

public class V1Game
{
    [JsonProperty("game_id")]      public string? GameId      { get; set; }
    [JsonProperty("start_time")]   public string? StartTime   { get; set; }
    [JsonProperty("end_time")]     public string? EndTime     { get; set; }
    [JsonProperty("beatmap_id")]   public string? BeatmapId   { get; set; }
    [JsonProperty("play_mode")]    public string? PlayMode    { get; set; }
    [JsonProperty("match_type")]   public string? MatchType   { get; set; }
    [JsonProperty("scoring_type")] public string? ScoringType { get; set; }
    [JsonProperty("team_type")]    public string? TeamType    { get; set; }
    [JsonProperty("mods")]         public string? Mods        { get; set; }
    [JsonProperty("scores")]       public List<V1MatchScore> Scores { get; set; } = [];

    public Mod? EnabledMods => Mods is not null && int.TryParse(Mods, out var v) ? new Mod(v) : null;
    public DateTimeOffset? StartTimeOffset => ParseV1Date(StartTime);
    public DateTimeOffset? EndTimeOffset   => ParseV1Date(EndTime);

    private static DateTimeOffset? ParseV1Date(string? s) =>
        s is not null && DateTimeOffset.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : null;
}

public class V1MatchScore
{
    [JsonProperty("slot")]         public string? Slot       { get; set; }
    [JsonProperty("team")]         public string? Team       { get; set; }
    [JsonProperty("user_id")]      public string? UserId     { get; set; }
    [JsonProperty("score")]        public string? Score      { get; set; }
    [JsonProperty("maxcombo")]     public string? MaxCombo   { get; set; }
    [JsonProperty("rank")]         public string? Rank       { get; set; }
    [JsonProperty("count300")]     public string? Count300   { get; set; }
    [JsonProperty("count100")]     public string? Count100   { get; set; }
    [JsonProperty("count50")]      public string? Count50    { get; set; }
    [JsonProperty("countmiss")]    public string? CountMiss  { get; set; }
    [JsonProperty("countkatu")]    public string? CountKatu  { get; set; }
    [JsonProperty("countgeki")]    public string? CountGeki  { get; set; }
    [JsonProperty("perfect")]      public string? Perfect    { get; set; }
    [JsonProperty("pass")]         public string? Passed     { get; set; }
    [JsonProperty("enabled_mods")] public string? EnabledMods { get; set; }

    public Mod? Mods => EnabledMods is not null && int.TryParse(EnabledMods, out var v) ? new Mod(v) : null;
    public bool IsPerfect => Perfect == "1";
    public bool HasPassed => Passed  == "1";
}

// ---------------------------------------------------------------------------
// Exceptions
// ---------------------------------------------------------------------------

public class OssapiV1Exception : Exception
{
    public OssapiV1Exception(string message) : base(message) { }
}

public class V1ReplayUnavailableException : OssapiV1Exception
{
    public V1ReplayUnavailableException(string message) : base(message) { }
}
