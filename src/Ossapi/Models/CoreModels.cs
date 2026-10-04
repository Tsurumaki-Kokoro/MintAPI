using Newtonsoft.Json;
using Ossapi.Enums;
using Ossapi.Json;
using Ossapi.Mods;

namespace Ossapi.Models;

// ---------------------------------------------------------------------------
// Cursor
// ---------------------------------------------------------------------------

/// <summary>Endpoint-specific pagination cursor.</summary>
public class Cursor : Dictionary<string, object?>
{
    public Cursor() { }

    public Cursor(IDictionary<string, object?> data) : base(data) { }
}

// ---------------------------------------------------------------------------
// Statistics
// ---------------------------------------------------------------------------

public class LegacyStatistics
{
    [JsonProperty("count_50")]   public int? Count50   { get; set; }
    [JsonProperty("count_100")]  public int? Count100  { get; set; }
    [JsonProperty("count_300")]  public int? Count300  { get; set; }
    [JsonProperty("count_geki")] public int? CountGeki { get; set; }
    [JsonProperty("count_katu")] public int? CountKatu { get; set; }
    [JsonProperty("count_miss")] public int? CountMiss { get; set; }
}

public class Statistics
{
    [JsonProperty("miss")]                  public int? Miss               { get; set; }
    [JsonProperty("meh")]                   public int? Meh                { get; set; }
    [JsonProperty("ok")]                    public int? Ok                 { get; set; }
    [JsonProperty("good")]                  public int? Good               { get; set; }
    [JsonProperty("great")]                 public int? Great              { get; set; }
    [JsonProperty("perfect")]               public int? Perfect            { get; set; }
    [JsonProperty("small_tick_miss")]       public int? SmallTickMiss      { get; set; }
    [JsonProperty("small_tick_hit")]        public int? SmallTickHit       { get; set; }
    [JsonProperty("large_tick_miss")]       public int? LargeTickMiss      { get; set; }
    [JsonProperty("large_tick_hit")]        public int? LargeTickHit       { get; set; }
    [JsonProperty("small_bonus")]           public int? SmallBonus         { get; set; }
    [JsonProperty("large_bonus")]           public int? LargeBonus         { get; set; }
    [JsonProperty("ignore_miss")]           public int? IgnoreMiss         { get; set; }
    [JsonProperty("ignore_hit")]            public int? IgnoreHit          { get; set; }
    [JsonProperty("combo_break")]           public int? ComboBreak         { get; set; }
    [JsonProperty("slider_tail_hit")]       public int? SliderTailHit      { get; set; }
    [JsonProperty("legacy_combo_increase")] public int? LegacyComboIncrease { get; set; }
}

// ---------------------------------------------------------------------------
// Failtimes / Covers / Weight
// ---------------------------------------------------------------------------

public class Failtimes
{
    [JsonProperty("exit")] public List<int>? Exit { get; set; }
    [JsonProperty("fail")] public List<int>? Fail { get; set; }
}

public class Covers
{
    [JsonProperty("cover")]       public string Cover      { get; set; } = "";
    [JsonProperty("cover@2x")]    public string Cover2x    { get; set; } = "";
    [JsonProperty("card")]        public string Card       { get; set; } = "";
    [JsonProperty("card@2x")]     public string Card2x     { get; set; } = "";
    [JsonProperty("list")]        public string List       { get; set; } = "";
    [JsonProperty("list@2x")]     public string List2x     { get; set; } = "";
    [JsonProperty("slimcover")]   public string Slimcover  { get; set; } = "";
    [JsonProperty("slimcover@2x")] public string Slimcover2x { get; set; } = "";
}

public class Weight
{
    [JsonProperty("percentage")] public double Percentage { get; set; }
    [JsonProperty("pp")]         public double Pp         { get; set; }
}

// ---------------------------------------------------------------------------
// BeatmapOwner
// ---------------------------------------------------------------------------

public class BeatmapOwner
{
    [JsonProperty("id")]       public int    Id       { get; set; }
    [JsonProperty("username")] public string Username { get; set; } = "";
}

// ---------------------------------------------------------------------------
// NonLegacyMod (new API score mods)
// ---------------------------------------------------------------------------

public class NonLegacyMod
{
    [JsonProperty("acronym")]  public string              Acronym  { get; set; } = "";
    [JsonProperty("settings")] public Dictionary<string, object?>? Settings { get; set; }
}

// ---------------------------------------------------------------------------
// BeatmapCompact / Beatmap
// ---------------------------------------------------------------------------

public class BeatmapCompact
{
    [JsonProperty("difficulty_rating")] public double     DifficultyRating { get; set; }
    [JsonProperty("id")]                public int        Id               { get; set; }
    [JsonProperty("mode")]
    [JsonConverter(typeof(GameModeConverter))]
    public GameMode Mode { get; set; }

    [JsonProperty("status")]
    [JsonConverter(typeof(RankStatusConverter))]
    public RankStatus Status { get; set; }

    [JsonProperty("total_length")] public int     TotalLength  { get; set; }
    [JsonProperty("version")]      public string  Version      { get; set; } = "";
    [JsonProperty("user_id")]      public int     UserId       { get; set; }
    [JsonProperty("beatmapset_id")] public int    BeatmapsetId { get; set; }

    // optional
    [JsonProperty("beatmapset")]   public BeatmapsetCompact? Beatmapset { get; set; }
    [JsonProperty("checksum")]     public string?            Checksum   { get; set; }
    [JsonProperty("failtimes")]    public Failtimes?         Failtimes  { get; set; }
    [JsonProperty("max_combo")]    public int?               MaxCombo   { get; set; }
}

public class Beatmap : BeatmapCompact
{
    [JsonProperty("accuracy")]       public double    Accuracy     { get; set; }
    [JsonProperty("ar")]             public double    Ar           { get; set; }
    [JsonProperty("bpm")]            public double?   Bpm          { get; set; }
    [JsonProperty("convert")]        public bool      Convert      { get; set; }
    [JsonProperty("count_circles")]  public int       CountCircles { get; set; }
    [JsonProperty("count_sliders")]  public int       CountSliders { get; set; }
    [JsonProperty("count_spinners")] public int       CountSpinners { get; set; }
    [JsonProperty("cs")]             public double    Cs           { get; set; }
    [JsonProperty("drain")]          public double    Drain        { get; set; }
    [JsonProperty("hit_length")]     public int       HitLength    { get; set; }
    [JsonProperty("is_scoreable")]   public bool      IsScoreable  { get; set; }
    [JsonProperty("mode_int")]       public int       ModeInt      { get; set; }
    [JsonProperty("rating")]         public double    Rating       { get; set; }
    [JsonProperty("passcount")]      public int       Passcount    { get; set; }
    [JsonProperty("playcount")]      public int       Playcount    { get; set; }
    [JsonProperty("url")]            public string    Url          { get; set; } = "";

    [JsonProperty("deleted_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonProperty("last_updated")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset LastUpdated { get; set; }

    [JsonProperty("ranked")]
    [JsonConverter(typeof(RankStatusConverter))]
    public RankStatus Ranked { get; set; }

    // The diff owner (named "user" in JSON, but conflicts with beatmapset user)
    [JsonProperty("user")]   public UserCompact? Owner  { get; set; }
    [JsonProperty("owners")] public List<BeatmapOwner>? Owners { get; set; }

    [JsonProperty("current_user_tag_ids")] public List<int>? CurrentUserTagIds { get; set; }
    [JsonProperty("top_tag_ids")]          public List<Newtonsoft.Json.Linq.JToken>? TopTagIds { get; set; }
    [JsonProperty("current_user_playcount")] public int?     CurrentUserPlaycount { get; set; }
}

// ---------------------------------------------------------------------------
// BeatmapsetCompact / Beatmapset
// ---------------------------------------------------------------------------

public class BeatmapsetCompact
{
    [JsonProperty("anime_cover")]          public bool    AnimeCover         { get; set; }
    [JsonProperty("artist")]               public string  Artist             { get; set; } = "";
    [JsonProperty("artist_unicode")]       public string  ArtistUnicode      { get; set; } = "";
    [JsonProperty("covers")]               public Covers  Covers             { get; set; } = new();
    [JsonProperty("current_user_playcount")] public int   CurrentUserPlaycount { get; set; }
    [JsonProperty("creator")]              public string  Creator            { get; set; } = "";
    [JsonProperty("favourite_count")]      public int     FavouriteCount     { get; set; }
    [JsonProperty("id")]                   public int     Id                 { get; set; }
    [JsonProperty("nsfw")]                 public bool    Nsfw               { get; set; }
    [JsonProperty("offset")]               public int     Offset             { get; set; }
    [JsonProperty("play_count")]           public int     PlayCount          { get; set; }
    [JsonProperty("preview_url")]          public string  PreviewUrl         { get; set; } = "";
    [JsonProperty("source")]               public string  Source             { get; set; } = "";
    [JsonProperty("spotlight")]            public bool    Spotlight          { get; set; }
    [JsonProperty("title")]                public string  Title              { get; set; } = "";
    [JsonProperty("title_unicode")]        public string  TitleUnicode       { get; set; } = "";
    [JsonProperty("user_id")]              public int     UserId             { get; set; }
    [JsonProperty("video")]                public bool    Video              { get; set; }
    [JsonProperty("hype")]                 public Hype?   Hype               { get; set; }

    [JsonProperty("status")]
    [JsonConverter(typeof(RankStatusConverter))]
    public RankStatus Status { get; set; }

    // optional expanded fields
    [JsonProperty("ranked_date")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? RankedDate { get; set; }

    [JsonProperty("beatmaps")]              public List<Beatmap>?      Beatmaps             { get; set; }
    [JsonProperty("current_nominations")]   public List<Nomination>?   CurrentNominations   { get; set; }
    [JsonProperty("genre_id")]              public int?                GenreId              { get; set; }
    [JsonProperty("has_favourited")]        public bool?               HasFavourited        { get; set; }
    [JsonProperty("language_id")]           public int?                LanguageId           { get; set; }
    [JsonProperty("pack_tags")]             public List<string>?       PackTags             { get; set; }
    [JsonProperty("track_id")]              public int?                TrackId              { get; set; }
    [JsonProperty("user")]                  public UserCompact?         SetUser              { get; set; }
}

public class Availability
{
    [JsonProperty("download_disabled")] public bool    DownloadDisabled { get; set; }
    [JsonProperty("more_information")]  public string? MoreInformation  { get; set; }
}

public class Hype
{
    [JsonProperty("current")]  public int Current  { get; set; }
    [JsonProperty("required")] public int Required { get; set; }
}

public class NominationsRequired
{
    [JsonProperty("main_ruleset")]     public int MainRuleset    { get; set; }
    [JsonProperty("non_main_ruleset")] public int NonMainRuleset { get; set; }
}

public class Nominations
{
    [JsonProperty("current")]       public int               Current              { get; set; }
    [JsonProperty("required_meta")] public NominationsRequired RequiredMeta       { get; set; } = new();
    [JsonProperty("eligible_main_rulesets")] public List<GameMode>? EligibleMainRulesets { get; set; }
}

public class Nomination
{
    [JsonProperty("beatmapset_id")] public int         BeatmapsetId { get; set; }
    [JsonProperty("rulesets")]      public List<GameMode> Rulesets  { get; set; } = [];
    [JsonProperty("reset")]         public bool        Reset        { get; set; }
    [JsonProperty("user_id")]       public int         UserId       { get; set; }
}

public class BeatmapTag
{
    [JsonProperty("description")] public string Description { get; set; } = "";
    [JsonProperty("id")]          public int    Id          { get; set; }
    [JsonProperty("name")]        public string Name        { get; set; } = "";
    [JsonProperty("ruleset_id")]  public int    RulesetId   { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonProperty("updated_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class Beatmapset : BeatmapsetCompact
{
    [JsonProperty("availability")]       public Availability  AvailabilityInfo  { get; set; } = new();
    [JsonProperty("bpm")]                public double        Bpm               { get; set; }
    [JsonProperty("can_be_hyped")]       public bool          CanBeHyped        { get; set; }
    [JsonProperty("discussion_enabled")] public bool          DiscussionEnabled { get; set; }
    [JsonProperty("discussion_locked")]  public bool          DiscussionLocked  { get; set; }
    [JsonProperty("is_scoreable")]       public bool          IsScoreable       { get; set; }
    [JsonProperty("legacy_thread_url")]  public string?       LegacyThreadUrl   { get; set; }
    [JsonProperty("nominations_summary")] public Nominations  NominationsSummary { get; set; } = new();
    [JsonProperty("rating")]             public double        Rating            { get; set; }
    [JsonProperty("storyboard")]         public bool          Storyboard        { get; set; }
    [JsonProperty("tags")]               public string        Tags              { get; set; } = "";
    [JsonProperty("related_tags")]       public List<BeatmapTag> RelatedTags   { get; set; } = [];
    [JsonProperty("version_count")]      public int           VersionCount      { get; set; }

    [JsonProperty("ranked")]
    [JsonConverter(typeof(RankStatusConverter))]
    public RankStatus Ranked { get; set; }

    [JsonProperty("deleted_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonProperty("last_updated")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset LastUpdated { get; set; }

    [JsonProperty("submitted_date")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? SubmittedDate { get; set; }
}

// ---------------------------------------------------------------------------
// Score
// ---------------------------------------------------------------------------

public class ScoreMatchInfo
{
    [JsonProperty("slot")]  public int    Slot { get; set; }
    [JsonProperty("team")]  public string Team { get; set; } = "";
    [JsonProperty("pass")]  public bool   Pass { get; set; }
}

public class LegacyScore
{
    [JsonProperty("id")]          public long?  Id        { get; set; }
    [JsonProperty("best_id")]     public long?  BestId    { get; set; }
    [JsonProperty("user_id")]     public int   UserId    { get; set; }
    [JsonProperty("accuracy")]    public double Accuracy { get; set; }
    [JsonProperty("score")]       public long  Score     { get; set; }
    [JsonProperty("max_combo")]   public int   MaxCombo  { get; set; }
    [JsonProperty("perfect")]     public bool  Perfect   { get; set; }
    [JsonProperty("pp")]          public double? Pp      { get; set; }
    [JsonProperty("replay")]      public bool  Replay    { get; set; }
    [JsonProperty("passed")]      public bool  Passed    { get; set; }
    [JsonProperty("mode_int")]    public int   ModeInt   { get; set; }
    [JsonProperty("type")]        public string Type     { get; set; } = "";
    [JsonProperty("rank_country")] public int? RankCountry { get; set; }
    [JsonProperty("rank_global")]  public int? RankGlobal  { get; set; }

    [JsonProperty("mods")]
    [JsonConverter(typeof(ModConverter))]
    public Mod Mods { get; set; }

    [JsonProperty("rank")]
    [JsonConverter(typeof(GradeConverter))]
    public Grade Rank { get; set; }

    [JsonProperty("mode")]
    [JsonConverter(typeof(GameModeConverter))]
    public GameMode Mode { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("statistics")]   public Statistics?          Statistics { get; set; }
    [JsonProperty("beatmap")]      public Beatmap?             Beatmap    { get; set; }
    [JsonProperty("beatmapset")]   public BeatmapsetCompact?   Beatmapset { get; set; }
    [JsonProperty("weight")]       public Weight?              Weight     { get; set; }
    [JsonProperty("user")]         public UserCompact?          User       { get; set; }
    [JsonProperty("match")]        public ScoreMatchInfo?       Match      { get; set; }
}

public class Score
{
    [JsonProperty("id")]                     public long?   Id                      { get; set; }
    [JsonProperty("best_id")]                public long?   BestId                  { get; set; }
    [JsonProperty("user_id")]                public int     UserId                  { get; set; }
    [JsonProperty("accuracy")]               public double  Accuracy                { get; set; }
    [JsonProperty("max_combo")]              public int     MaxCombo                { get; set; }
    [JsonProperty("pp")]                     public double? Pp                      { get; set; }
    [JsonProperty("passed")]                 public bool    Passed                  { get; set; }
    [JsonProperty("classic_total_score")]    public long    ClassicTotalScore       { get; set; }
    [JsonProperty("processed")]              public bool    Processed               { get; set; }
    [JsonProperty("replay")]                 public bool    Replay                  { get; set; }
    [JsonProperty("ruleset_id")]             public int     RulesetId               { get; set; }
    [JsonProperty("ranked")]                 public bool    Ranked                  { get; set; }
    [JsonProperty("preserve")]               public bool    Preserve                { get; set; }
    [JsonProperty("beatmap_id")]             public int     BeatmapId               { get; set; }
    [JsonProperty("build_id")]               public int?    BuildId                 { get; set; }
    [JsonProperty("has_replay")]             public bool    HasReplay               { get; set; }
    [JsonProperty("is_perfect_combo")]       public bool    IsPerfectCombo          { get; set; }
    [JsonProperty("total_score")]            public long    TotalScore              { get; set; }
    [JsonProperty("total_score_without_mods")] public long?  TotalScoreWithoutMods  { get; set; }
    [JsonProperty("legacy_perfect")]         public bool    LegacyPerfect           { get; set; }
    [JsonProperty("legacy_score_id")]        public long?   LegacyScoreId           { get; set; }
    [JsonProperty("legacy_total_score")]     public long    LegacyTotalScore        { get; set; }
    [JsonProperty("type")]                   public string  Type                    { get; set; } = "";
    [JsonProperty("rank_country")]           public int?    RankCountry             { get; set; }
    [JsonProperty("rank_global")]            public int?    RankGlobal              { get; set; }

    [JsonProperty("rank")]
    [JsonConverter(typeof(GradeConverter))]
    public Grade Rank { get; set; }

    [JsonProperty("ended_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset EndedAt { get; set; }

    [JsonProperty("started_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonProperty("mods")]            public List<NonLegacyMod>?  Mods             { get; set; }
    [JsonProperty("statistics")]      public Statistics?           Statistics       { get; set; }
    [JsonProperty("maximum_statistics")] public Statistics?        MaximumStatistics { get; set; }
    [JsonProperty("beatmap")]         public Beatmap?              Beatmap          { get; set; }
    [JsonProperty("beatmapset")]      public BeatmapsetCompact?    Beatmapset       { get; set; }
    [JsonProperty("weight")]          public Weight?               Weight           { get; set; }
    [JsonProperty("user")]            public UserCompact?           User             { get; set; }
    [JsonProperty("match")]           public ScoreMatchInfo?        Match            { get; set; }
}

// ---------------------------------------------------------------------------
// Score wrapper models
// ---------------------------------------------------------------------------

public class BeatmapUserScore
{
    [JsonProperty("position")] public int   Position { get; set; }
    [JsonProperty("score")]    public Score Score    { get; set; } = new();
}

public class BeatmapUserScores
{
    [JsonProperty("scores")] public List<Score> Scores { get; set; } = [];
}

public class BeatmapScores
{
    [JsonProperty("scores")]      public List<Score>     Scores     { get; set; } = [];
    [JsonProperty("score_count")] public int             ScoreCount { get; set; }
    [JsonProperty("userScore")]   public BeatmapUserScore? UserScore  { get; set; }
}

// ---------------------------------------------------------------------------
// StatisticsVariant / UserStatistics (correct from models.py)
// ---------------------------------------------------------------------------

public class StatisticsVariant
{
    [JsonProperty("mode")]
    [JsonConverter(typeof(GameModeConverter))]
    public GameMode Mode { get; set; }

    [JsonProperty("variant")]
    [JsonConverter(typeof(VariantConverter))]
    public Variant Variant { get; set; }

    [JsonProperty("country_rank")] public int?   CountryRank { get; set; }
    [JsonProperty("global_rank")]  public int?   GlobalRank  { get; set; }
    [JsonProperty("pp")]           public double Pp          { get; set; }
}

// ---------------------------------------------------------------------------
// BeatmapsetEvent comment hierarchy
// ---------------------------------------------------------------------------

public class BeatmapsetEventComment
{
    [JsonProperty("beatmap_discussion_id")]      public int BeatmapDiscussionId     { get; set; }
    [JsonProperty("beatmap_discussion_post_id")] public int BeatmapDiscussionPostId { get; set; }
}

public class BeatmapsetEventCommentNoPost
{
    [JsonProperty("beatmap_discussion_id")]      public int  BeatmapDiscussionId     { get; set; }
    [JsonProperty("beatmap_discussion_post_id")] public int? BeatmapDiscussionPostId { get; set; }
}

public class BeatmapsetEventCommentNone
{
    [JsonProperty("beatmap_discussion_id")]      public int? BeatmapDiscussionId     { get; set; }
    [JsonProperty("beatmap_discussion_post_id")] public int? BeatmapDiscussionPostId { get; set; }
}

// Used for genre_edit, language_edit, nsfw_toggle — old/new values can be string or bool
public class BeatmapsetEventCommentChange : BeatmapsetEventCommentNone
{
    [JsonProperty("old")] public object? Old { get; set; }
    [JsonProperty("new")] public object? New { get; set; }
}

public class BeatmapsetEventCommentLovedRemoval : BeatmapsetEventCommentNone
{
    [JsonProperty("reason")] public string Reason { get; set; } = "";
}

public class BeatmapsetEventCommentKudosuChange : BeatmapsetEventCommentNoPost
{
    [JsonProperty("new_vote")] public KudosuVote  NewVote { get; set; } = new();
    [JsonProperty("votes")]    public List<KudosuVote> Votes { get; set; } = [];
}

public class BeatmapsetEventCommentKudosuRecalculate : BeatmapsetEventCommentNoPost
{
    [JsonProperty("new_vote")] public KudosuVote? NewVote { get; set; }
}

public class BeatmapsetEventCommentOwnerChange : BeatmapsetEventCommentNone
{
    [JsonProperty("beatmap_id")]          public int          BeatmapId       { get; set; }
    [JsonProperty("beatmap_version")]     public string       BeatmapVersion  { get; set; } = "";
    [JsonProperty("new_user_id")]         public int          NewUserId       { get; set; }
    [JsonProperty("new_user_username")]   public string       NewUserUsername { get; set; } = "";
    [JsonProperty("new_users")]           public List<int>    NewUsers        { get; set; } = [];
}

public class BeatmapsetEventCommentNominate
{
    [JsonProperty("modes", ItemConverterType = typeof(GameModeConverter))]
    public List<GameMode> Modes { get; set; } = [];
}

public class BeatmapsetEventCommentWithNominators : BeatmapsetEventCommentNoPost
{
    [JsonProperty("beatmap_ids")]    public List<int>? BeatmapIds    { get; set; }
    [JsonProperty("nominator_ids")]  public List<int>? NominatorIds  { get; set; }
}

public class BeatmapsetEventCommentWithSourceUser : BeatmapsetEventCommentNoPost
{
    [JsonProperty("source_user_id")]       public int    SourceUserId       { get; set; }
    [JsonProperty("source_user_username")] public string SourceUserUsername { get; set; } = "";
}
