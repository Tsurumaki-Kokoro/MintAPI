using Newtonsoft.Json;
using Ossapi.Enums;
using Ossapi.Json;

namespace Ossapi.Models;

public class UserCompact
{
    [JsonProperty("current_user_attributes")] public Newtonsoft.Json.Linq.JObject? CurrentUserAttributes { get; set; }
    [JsonProperty("matchmaking_stats")] public List<MatchmakingStatistics>? MatchmakingStats { get; set; }
    // required fields
    [JsonProperty("avatar_url")]      public string AvatarUrl     { get; set; } = "";
    [JsonProperty("country_code")]    public string CountryCode   { get; set; } = "";
    [JsonProperty("id")]              public int    Id            { get; set; }
    [JsonProperty("is_active")]       public bool   IsActive      { get; set; }
    [JsonProperty("is_bot")]          public bool   IsBot         { get; set; }
    [JsonProperty("is_deleted")]      public bool   IsDeleted     { get; set; }
    [JsonProperty("is_online")]       public bool   IsOnline      { get; set; }
    [JsonProperty("is_supporter")]    public bool   IsSupporter   { get; set; }
    [JsonProperty("pm_friends_only")] public bool   PmFriendsOnly { get; set; }
    [JsonProperty("username")]        public string Username      { get; set; } = "";

    [JsonProperty("last_visit")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? LastVisit { get; set; }

    [JsonProperty("profile_colour")] public string? ProfileColour { get; set; }

    // optional expanded fields
    [JsonProperty("account_history")]            public List<UserAccountHistory>? AccountHistory          { get; set; }
    [JsonProperty("active_tournament_banner")]   public ProfileBanner?            ActiveTournamentBanner  { get; set; }
    [JsonProperty("active_tournament_banners")]  public List<ProfileBanner>?      ActiveTournamentBanners { get; set; }
    [JsonProperty("badges")]                     public List<UserBadge>?          Badges                  { get; set; }
    [JsonProperty("beatmap_playcounts_count")]   public int?                      BeatmapPlaycountsCount  { get; set; }
    [JsonProperty("blocks")]                     public List<UserRelation>?       Blocks                  { get; set; }
    [JsonProperty("country")]                    public Country?                  Country                 { get; set; }
    [JsonProperty("cover")]                      public Cover?                    Cover                   { get; set; }
    [JsonProperty("default_group")]              public string?                   DefaultGroup            { get; set; }
    [JsonProperty("favourite_beatmapset_count")] public int?                      FavouriteBeatmapsetCount { get; set; }
    [JsonProperty("follow_user_mapping")]        public List<int>?                FollowUserMapping       { get; set; }
    [JsonProperty("follower_count")]             public int?                      FollowerCount           { get; set; }
    [JsonProperty("friends")]                    public List<UserRelation>?       Friends                 { get; set; }
    [JsonProperty("graveyard_beatmapset_count")] public int?                      GraveyardBeatmapsetCount { get; set; }
    [JsonProperty("groups")]                     public List<UserGroup>?          Groups                  { get; set; }
    [JsonProperty("guest_beatmapset_count")]     public int?                      GuestBeatmapsetCount    { get; set; }
    [JsonProperty("is_restricted")]              public bool?                     IsRestricted            { get; set; }
    [JsonProperty("is_silenced")]                public bool?                     IsSilenced              { get; set; }
    [JsonProperty("loved_beatmapset_count")]     public int?                      LovedBeatmapsetCount    { get; set; }
    [JsonProperty("global_rank")]                public UserGlobalRank?           GlobalRank              { get; set; }
    [JsonProperty("mapping_follower_count")]      public int?                     MappingFollowerCount    { get; set; }
    [JsonProperty("monthly_playcounts")]         public List<UserMonthlyPlaycount>? MonthlyPlaycounts     { get; set; }
    [JsonProperty("page")]                       public UserPage?                 Page                    { get; set; }
    [JsonProperty("pending_beatmapset_count")]   public int?                      PendingBeatmapsetCount  { get; set; }
    [JsonProperty("previous_usernames")]         public List<string>?             PreviousUsernames       { get; set; }
    [JsonProperty("rankHistory")]                public RankHistory?              RankHistoryLegacy       { get; set; }
    [JsonProperty("rank_history")]               public RankHistory?              RankHistory             { get; set; }
    [JsonProperty("ranked_and_approved_beatmapset_count")] public int?            RankedAndApprovedBeatmapsetCount { get; set; }
    [JsonProperty("ranked_beatmapset_count")]    public int?                      RankedBeatmapsetCount   { get; set; }
    [JsonProperty("replays_watched_counts")]     public List<UserReplaysWatchedCount>? ReplaysWatchedCounts { get; set; }
    [JsonProperty("scores_best_count")]          public int?                      ScoresBestCount         { get; set; }
    [JsonProperty("scores_first_count")]         public int?                      ScoresFirstCount        { get; set; }
    [JsonProperty("scores_recent_count")]        public int?                      ScoresRecentCount       { get; set; }
    [JsonProperty("statistics")]                 public UserStatistics?           Statistics              { get; set; }
    [JsonProperty("statistics_rulesets")]        public UserStatisticsRulesets?   StatisticsRulesets      { get; set; }
    [JsonProperty("support_level")]              public int?                      SupportLevel            { get; set; }
    [JsonProperty("unranked_beatmapset_count")]  public int?                      UnrankedBeatmapsetCount { get; set; }
    [JsonProperty("unread_pm_count")]            public int?                      UnreadPmCount           { get; set; }
    [JsonProperty("user_achievements")]          public List<UserAchievement>?    UserAchievements        { get; set; }
    [JsonProperty("user_preferences")]           public UserProfileCustomization? UserPreferences         { get; set; }
    [JsonProperty("session_verified")]           public bool?                     SessionVerified         { get; set; }
    [JsonProperty("team")]                       public Team?                     Team                    { get; set; }
}

public class User : UserCompact
{
    [JsonProperty("comments_count")]         public int     CommentsCount        { get; set; }
    [JsonProperty("cover_url")]              public string  CoverUrl             { get; set; } = "";
    [JsonProperty("discord")]               public string? Discord              { get; set; }
    [JsonProperty("has_supported")]         public bool    HasSupported         { get; set; }
    [JsonProperty("interests")]             public string? Interests            { get; set; }
    [JsonProperty("max_blocks")]            public int     MaxBlocks            { get; set; }
    [JsonProperty("max_friends")]           public int     MaxFriends           { get; set; }
    [JsonProperty("occupation")]            public string? Occupation           { get; set; }
    [JsonProperty("playmode")]              public string  Playmode             { get; set; } = "";
    [JsonProperty("post_count")]            public int     PostCount            { get; set; }
    [JsonProperty("profile_hue")]           public int?    ProfileHue           { get; set; }
    [JsonProperty("title")]                 public string? Title                { get; set; }
    [JsonProperty("title_url")]             public string? TitleUrl             { get; set; }
    [JsonProperty("twitter")]              public string? Twitter              { get; set; }
    [JsonProperty("website")]              public string? Website              { get; set; }
    [JsonProperty("scores_pinned_count")]   public int     ScoresPinnedCount    { get; set; }
    [JsonProperty("nominated_beatmapset_count")] public int NominatedBeatmapsetCount { get; set; }

    [JsonProperty("join_date")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset JoinDate { get; set; }

    [JsonProperty("kudosu")]           public Kudosu         Kudosu           { get; set; } = new();
    [JsonProperty("location")]         public string?        Location         { get; set; }
    [JsonProperty("profile_order")]    public List<string>   ProfileOrder     { get; set; } = [];
    [JsonProperty("playstyle")]        public List<string>?  Playstyle        { get; set; }
    [JsonProperty("daily_challenge_user_stats")] public DailyChallengeUserStats? DailyChallengeUserStats { get; set; }
    [JsonProperty("rank_highest")]     public RankHighest?   RankHighest      { get; set; }
    [JsonProperty("current_season_stats")] public SeasonStatistics? CurrentSeasonStats { get; set; }
}

/// <summary>Wrapper for /users endpoint that returns a list of UserCompact.</summary>
public class Users
{
    [JsonProperty("users")] public List<UserCompact> UserList { get; set; } = [];
}
