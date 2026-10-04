using Newtonsoft.Json;
using Ossapi.Enums;
using Ossapi.Json;

namespace Ossapi.Models;

public class Country
{
    [JsonProperty("code")]    public string Code { get; set; } = "";
    [JsonProperty("name")]    public string Name { get; set; } = "";
    [JsonProperty("display")] public int?   Display { get; set; }
}

public class Cover
{
    [JsonProperty("custom_url")] public string? CustomUrl { get; set; }
    [JsonProperty("url")]        public string  Url       { get; set; } = "";
    [JsonProperty("id")]         public string? Id        { get; set; }
}

public class ProfileBanner
{
    [JsonProperty("id")]            public int    Id           { get; set; }
    [JsonProperty("tournament_id")] public int    TournamentId { get; set; }
    [JsonProperty("image")]         public string Image        { get; set; } = "";
    [JsonProperty("image@2x")]      public string Image2x      { get; set; } = "";
}

public class UserAccountHistory
{
    [JsonProperty("description")] public string? Description { get; set; }
    [JsonProperty("id")]          public int     Id          { get; set; }
    [JsonProperty("length")]      public int     Length      { get; set; }
    [JsonProperty("permanent")]   public bool    Permanent   { get; set; }

    [JsonProperty("timestamp")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset Timestamp { get; set; }
}

public class UserBadge
{
    [JsonProperty("awarded_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset AwardedAt { get; set; }

    [JsonProperty("description")]  public string Description  { get; set; } = "";
    [JsonProperty("image_url")]    public string ImageUrl     { get; set; } = "";
    [JsonProperty("image@2x_url")] public string Image2xUrl  { get; set; } = "";
    [JsonProperty("url")]          public string Url          { get; set; } = "";
}

public class GroupDescription
{
    [JsonProperty("html")]     public string Html     { get; set; } = "";
    [JsonProperty("markdown")] public string Markdown { get; set; } = "";
}

public class UserGroup
{
    [JsonProperty("id")]              public int              Id            { get; set; }
    [JsonProperty("identifier")]      public string           Identifier    { get; set; } = "";
    [JsonProperty("name")]            public string           Name          { get; set; } = "";
    [JsonProperty("short_name")]      public string           ShortName     { get; set; } = "";
    [JsonProperty("colour")]          public string?          Colour        { get; set; }
    [JsonProperty("description")]     public GroupDescription? Description  { get; set; }
    [JsonProperty("is_probationary")] public bool             IsProbationary { get; set; }
    [JsonProperty("has_listing")]     public bool             HasListing    { get; set; }
    [JsonProperty("has_playmodes")]   public bool             HasPlaymodes  { get; set; }

    [JsonProperty("playmodes", ItemConverterType = typeof(GameModeConverter))]
    public List<GameMode>? Playmodes { get; set; }
}

public class UserRelation
{
    [JsonProperty("target_id")]  public int    TargetId  { get; set; }
    [JsonProperty("relation_type")] public string RelationType { get; set; } = "";
    [JsonProperty("mutual")]     public bool   Mutual    { get; set; }
}

public class RankHistory
{
    [JsonProperty("mode")] public string Mode { get; set; } = "";
    [JsonProperty("data")] public List<int> Data { get; set; } = [];
}

public class RankHighest
{
    [JsonProperty("rank")]       public int Rank { get; set; }

    [JsonProperty("updated_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UserMonthlyPlaycount
{
    [JsonProperty("start_date")] public string StartDate { get; set; } = "";
    [JsonProperty("count")]      public int    Count     { get; set; }
}

public class UserPage
{
    [JsonProperty("html")]     public string Html     { get; set; } = "";
    [JsonProperty("raw")]      public string Raw      { get; set; } = "";
}

public class UserLevel
{
    [JsonProperty("current")]  public int Current  { get; set; }
    [JsonProperty("progress")] public int Progress { get; set; }
}

public class UserGradeCounts
{
    [JsonProperty("ssh")] public int? Ssh { get; set; }
    [JsonProperty("sh")]  public int? Sh  { get; set; }
    [JsonProperty("ss")]  public int? Ss  { get; set; }
    [JsonProperty("s")]   public int? S   { get; set; }
    [JsonProperty("a")]   public int? A   { get; set; }
}

public class UserReplaysWatchedCount
{
    [JsonProperty("start_date")] public string StartDate { get; set; } = "";
    [JsonProperty("count")]      public int    Count     { get; set; }
}

public class UserAchievement
{
    [JsonProperty("achieved_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset AchievedAt { get; set; }

    [JsonProperty("achievement_id")] public int AchievementId { get; set; }
}

public class UserStatistics
{
    [JsonProperty("rank")] public UserStatisticsRank? Rank { get; set; }
    [JsonProperty("count_100")]         public int             Count100              { get; set; }
    [JsonProperty("count_300")]         public int             Count300              { get; set; }
    [JsonProperty("count_50")]          public int             Count50               { get; set; }
    [JsonProperty("count_miss")]        public int             CountMiss             { get; set; }
    [JsonProperty("level")]             public UserLevel       Level                 { get; set; } = new();
    [JsonProperty("global_rank")]       public int?            GlobalRank            { get; set; }
    [JsonProperty("global_rank_exp")]   public int?            GlobalRankExp         { get; set; }
    [JsonProperty("global_rank_percent")] public double?       GlobalRankPercent     { get; set; }
    [JsonProperty("pp")]                public double?         Pp                    { get; set; }
    [JsonProperty("pp_exp")]            public double          PpExp                 { get; set; }
    [JsonProperty("ranked_score")]      public long            RankedScore           { get; set; }
    [JsonProperty("hit_accuracy")]      public double          HitAccuracy           { get; set; }
    [JsonProperty("accuracy")]          public double          Accuracy              { get; set; }
    [JsonProperty("play_count")]        public int             PlayCount             { get; set; }
    [JsonProperty("play_time")]         public int?            PlayTime              { get; set; }
    [JsonProperty("total_score")]       public long            TotalScore            { get; set; }
    [JsonProperty("total_hits")]        public long            TotalHits             { get; set; }
    [JsonProperty("maximum_combo")]     public int             MaximumCombo          { get; set; }
    [JsonProperty("replays_watched_by_others")] public int     ReplaysWatched        { get; set; }
    [JsonProperty("is_ranked")]         public bool            IsRanked              { get; set; }
    [JsonProperty("grade_counts")]      public UserGradeCounts GradeCounts           { get; set; } = new();
    [JsonProperty("country_rank")]      public int?            CountryRank           { get; set; }
    [JsonProperty("rank_change_since_30_days")] public int?   RankChangeSince30Days  { get; set; }
    [JsonProperty("variants")]          public List<StatisticsVariant>? Variants     { get; set; }
    [JsonProperty("user")]              public UserCompact?    User                  { get; set; }
}

public class UserStatisticsRulesets
{
    [JsonProperty("osu")]   public UserStatistics? Osu   { get; set; }
    [JsonProperty("taiko")] public UserStatistics? Taiko { get; set; }
    [JsonProperty("fruits")] public UserStatistics? Catch { get; set; }
    [JsonProperty("mania")] public UserStatistics? Mania { get; set; }
}

public class Kudosu
{
    [JsonProperty("total")]     public int Total     { get; set; }
    [JsonProperty("available")] public int Available { get; set; }
}

public class UserGlobalRank
{
    [JsonProperty("rank")]      public int Rank      { get; set; }
    [JsonProperty("timestamp")] public long Timestamp { get; set; }
}

public class UserProfileCustomization
{
    [JsonProperty("audio_autoplay")]          public bool? AudioAutoplay         { get; set; }
    [JsonProperty("beatmapset_download")]     public string? BeatmapsetDownload  { get; set; }
    [JsonProperty("comments_show_deleted")]   public bool? CommentsShowDeleted   { get; set; }
    [JsonProperty("extra_playmodes")]         public List<string>? ExtraPlaymodes { get; set; }
    [JsonProperty("forum_posts_show_deleted")] public bool? ForumPostsShowDeleted { get; set; }
    [JsonProperty("user_list_view")]          public string? UserListView        { get; set; }
    [JsonProperty("user_list_filter")]        public string? UserListFilter      { get; set; }
    [JsonProperty("user_list_sort")]          public string? UserListSort        { get; set; }
}

public class DailyChallengeUserStats
{
    [JsonProperty("daily_streak_best")]    public int    DailyStreakBest    { get; set; }
    [JsonProperty("daily_streak_current")] public int   DailyStreakCurrent { get; set; }
    [JsonProperty("last_update")]          public string? LastUpdate        { get; set; }
    [JsonProperty("last_weekly_streak")]   public string? LastWeeklyStreak  { get; set; }
    [JsonProperty("playcount")]            public int    Playcount          { get; set; }
    [JsonProperty("top_10p_placements")]   public int    Top10pPlacements   { get; set; }
    [JsonProperty("top_50p_placements")]   public int    Top50pPlacements   { get; set; }
    [JsonProperty("user_id")]              public int    UserId             { get; set; }
    [JsonProperty("weekly_streak_best")]   public int    WeeklyStreakBest   { get; set; }
    [JsonProperty("weekly_streak_current")] public int   WeeklyStreakCurrent { get; set; }
}

public class Team
{
    [JsonProperty("flag_url")]   public string? FlagUrl   { get; set; }
    [JsonProperty("id")]         public int    Id        { get; set; }
    [JsonProperty("name")]       public string Name      { get; set; } = "";
    [JsonProperty("short_name")] public string ShortName { get; set; } = "";
    [JsonProperty("url")]        public string Url       { get; set; } = "";
}


public class UserStatisticsRank
{
    [JsonProperty("country")] public int? Country { get; set; }
}

public class MatchmakingStatistics
{
    [JsonProperty("first_placements")] public int FirstPlacements { get; set; }
    [JsonProperty("is_rating_provisional")] public bool IsRatingProvisional { get; set; }
    [JsonProperty("plays")] public int Plays { get; set; }
    [JsonProperty("pool_id")] public int PoolId { get; set; }
    [JsonProperty("rank")] public int? Rank { get; set; }
    [JsonProperty("rank_percent")] public double? RankPercent { get; set; }
    [JsonProperty("rating")] public double Rating { get; set; }
    [JsonProperty("total_points")] public long TotalPoints { get; set; }
    [JsonProperty("user_id")] public int UserId { get; set; }
    [JsonProperty("pool")] public MatchmakingPool? Pool { get; set; }
    [JsonProperty("recent_history")] public List<MatchmakingHistory>? RecentHistory { get; set; }
}

public class MatchmakingPool
{
    [JsonProperty("active")] public bool Active { get; set; }
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("name")] public string Name { get; set; } = "";
    [JsonProperty("ruleset_id")] public int RulesetId { get; set; }
    [JsonProperty("type")] public string Type { get; set; } = "";
    [JsonProperty("variant_id")] public int VariantId { get; set; }
}

public class MatchmakingHistory
{
    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }
    [JsonProperty("elo_after")] public double EloAfter { get; set; }
    [JsonProperty("id")] public long Id { get; set; }
    [JsonProperty("result")] public string Result { get; set; } = "";
    [JsonProperty("room_id")] public int RoomId { get; set; }
}
