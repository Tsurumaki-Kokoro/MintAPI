using Newtonsoft.Json;
using Ossapi.Enums;
using Ossapi.Json;
using Ossapi.Mods;

namespace Ossapi.Models;

// ---------------------------------------------------------------------------
// Comment / CommentBundle
// ---------------------------------------------------------------------------

public class CommentableMetaCurrentUserAttributes
{
    [JsonProperty("can_new_comment_reason")] public string? CanNewCommentReason { get; set; }
}

public class CommentableMeta
{
    [JsonProperty("id")]            public int?    Id           { get; set; }
    [JsonProperty("title")]         public string  Title        { get; set; } = "";
    [JsonProperty("type")]          public string? Type         { get; set; }
    [JsonProperty("url")]           public string? Url          { get; set; }
    [JsonProperty("owner_id")]      public int?    OwnerId      { get; set; }
    [JsonProperty("owner_title")]   public string? OwnerTitle   { get; set; }
    [JsonProperty("locked")]        public bool?   Locked       { get; set; }
    [JsonProperty("current_user_attributes")] public CommentableMetaCurrentUserAttributes? CurrentUserAttributes { get; set; }
}

public class Comment
{
    [JsonProperty("commentable_id")]   public int?   CommentableId   { get; set; }
    [JsonProperty("commentable_type")] public string? CommentableType { get; set; }
    [JsonProperty("edited_by_id")]     public int?   EditedById      { get; set; }
    [JsonProperty("id")]               public int    Id              { get; set; }
    [JsonProperty("legacy_name")]      public string? LegacyName      { get; set; }
    [JsonProperty("message")]          public string? Message         { get; set; }
    [JsonProperty("message_html")]     public string? MessageHtml     { get; set; }
    [JsonProperty("parent_id")]        public int?   ParentId        { get; set; }
    [JsonProperty("pinned")]           public bool   Pinned          { get; set; }
    [JsonProperty("replies_count")]    public int    RepliesCount    { get; set; }
    [JsonProperty("user_id")]          public int?   UserId          { get; set; }
    [JsonProperty("votes_count")]      public int    VotesCount      { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("deleted_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonProperty("edited_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? EditedAt { get; set; }

    [JsonProperty("updated_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset UpdatedAt { get; set; }
}

public class CommentBundle
{
    [JsonProperty("commentable_meta")]   public List<CommentableMeta> CommentableMeta    { get; set; } = [];
    [JsonProperty("comments")]           public List<Comment>         Comments           { get; set; } = [];
    [JsonProperty("cursor")]             public Cursor?               Cursor             { get; set; }
    [JsonProperty("cursor_string")]      public string?               CursorString       { get; set; }
    [JsonProperty("has_more")]           public bool                  HasMore            { get; set; }
    [JsonProperty("has_more_id")]        public int?                  HasMoreId          { get; set; }
    [JsonProperty("included_comments")]  public List<Comment>         IncludedComments   { get; set; } = [];
    [JsonProperty("pinned_comments")]    public List<Comment>?        PinnedComments     { get; set; }
    [JsonProperty("sort")]               public string                Sort               { get; set; } = "";
    [JsonProperty("top_level_count")]    public int?                  TopLevelCount      { get; set; }
    [JsonProperty("total")]              public int?                  Total              { get; set; }
    [JsonProperty("user_follow")]        public bool                  UserFollow         { get; set; }
    [JsonProperty("user_votes")]         public List<int>             UserVotes          { get; set; } = [];
    [JsonProperty("users")]              public List<UserCompact>     Users              { get; set; } = [];
}

// ---------------------------------------------------------------------------
// Forum
// ---------------------------------------------------------------------------

public class ForumPostBody
{
    [JsonProperty("html")] public string Html { get; set; } = "";
    [JsonProperty("raw")]  public string Raw  { get; set; } = "";
}

public class ForumPollText
{
    [JsonProperty("bbcode")] public string Bbcode { get; set; } = "";
    [JsonProperty("html")]   public string Html   { get; set; } = "";
}

public class ForumPollTitle
{
    [JsonProperty("bbcode")] public string Bbcode { get; set; } = "";
    [JsonProperty("html")]   public string Html   { get; set; } = "";
}

public class ForumPollOption
{
    [JsonProperty("id")]         public int          Id        { get; set; }
    [JsonProperty("text")]       public ForumPollText Text     { get; set; } = new();
    [JsonProperty("vote_count")] public int?         VoteCount { get; set; }
}

public class ForumPollModel
{
    [JsonProperty("allow_vote_change")]      public bool                AllowVoteChange       { get; set; }
    [JsonProperty("hide_incomplete_results")] public bool               HideIncompleteResults { get; set; }
    [JsonProperty("max_votes")]              public int                 MaxVotes              { get; set; }
    [JsonProperty("options")]               public List<ForumPollOption> Options              { get; set; } = [];
    [JsonProperty("title")]                 public ForumPollTitle       Title                 { get; set; } = new();
    [JsonProperty("total_vote_count")]      public int                 TotalVoteCount         { get; set; }

    [JsonProperty("ended_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? EndedAt { get; set; }

    [JsonProperty("last_vote_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? LastVoteAt { get; set; }

    [JsonProperty("started_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset StartedAt { get; set; }
}

public class Forum
{
    [JsonProperty("id")]          public int          Id          { get; set; }
    [JsonProperty("name")]        public string       Name        { get; set; } = "";
    [JsonProperty("description")] public string       Description { get; set; } = "";
    [JsonProperty("subforums")]   public List<Forum>? Subforums   { get; set; }
}

public class Forums
{
    [JsonProperty("forums")] public List<Forum> ForumList { get; set; } = [];
}

public class ForumPost
{
    [JsonProperty("edited_by_id")] public int?   EditedById { get; set; }
    [JsonProperty("forum_id")]     public int    ForumId    { get; set; }
    [JsonProperty("id")]           public int    Id         { get; set; }
    [JsonProperty("topic_id")]     public int    TopicId    { get; set; }
    [JsonProperty("user_id")]      public int    UserId     { get; set; }
    [JsonProperty("body")]         public ForumPostBody Body { get; set; } = new();

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("deleted_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonProperty("edited_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? EditedAt { get; set; }
}

public class ForumTopic
{
    [JsonProperty("first_post_id")] public int         FirstPostId { get; set; }
    [JsonProperty("forum_id")]      public int         ForumId     { get; set; }
    [JsonProperty("id")]            public int         Id          { get; set; }
    [JsonProperty("is_locked")]     public bool        IsLocked    { get; set; }
    [JsonProperty("last_post_id")]  public int         LastPostId  { get; set; }
    [JsonProperty("post_count")]    public int         PostCount   { get; set; }
    [JsonProperty("title")]         public string      Title       { get; set; } = "";
    [JsonProperty("user_id")]       public int         UserId      { get; set; }
    [JsonProperty("views")]         public int         Views       { get; set; }
    [JsonProperty("poll")]          public ForumPollModel? Poll    { get; set; }

    [JsonProperty("type")]
    [JsonConverter(typeof(ForumTopicTypeConverter))]
    public ForumTopicType Type { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("deleted_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonProperty("updated_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ForumTopicSearch
{
    [JsonProperty("sort")]  public string? Sort  { get; set; }
    [JsonProperty("limit")] public int?    Limit { get; set; }
    [JsonProperty("start")] public int?    Start { get; set; }
    [JsonProperty("end")]   public int?    End   { get; set; }
}

public class ForumTopicAndPosts
{
    [JsonProperty("cursor")]        public Cursor?          Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?          CursorString { get; set; }
    [JsonProperty("search")]        public ForumTopicSearch Search       { get; set; } = new();
    [JsonProperty("posts")]         public List<ForumPost>  Posts        { get; set; } = [];
    [JsonProperty("topic")]         public ForumTopic       Topic        { get; set; } = new();
}

public class ForumTopics
{
    [JsonProperty("forum")]         public Forum            ForumInfo     { get; set; } = new();
    [JsonProperty("topics")]        public List<ForumTopic> Topics        { get; set; } = [];
    [JsonProperty("pinned_topics")] public List<ForumTopic> PinnedTopics  { get; set; } = [];
}

public class CreateForumTopicResponse
{
    [JsonProperty("post")]  public ForumPost  Post  { get; set; } = new();
    [JsonProperty("topic")] public ForumTopic Topic { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Events
// ---------------------------------------------------------------------------

public class EventUser
{
    [JsonProperty("username")]         public string  Username         { get; set; } = "";
    [JsonProperty("url")]              public string  Url              { get; set; } = "";
    [JsonProperty("previousUsername")] public string? PreviousUsername { get; set; }
}

public class EventBeatmap
{
    [JsonProperty("title")] public string Title { get; set; } = "";
    [JsonProperty("url")]   public string Url   { get; set; } = "";
}

public class EventBeatmapset
{
    [JsonProperty("title")] public string Title { get; set; } = "";
    [JsonProperty("url")]   public string Url   { get; set; } = "";
}

public class EventAchievement
{
    [JsonProperty("instructions")] public string? Instructions { get; set; }
    [JsonProperty("mode")] public string? Mode { get; set; }
    [JsonProperty("icon_url")]        public string  IconUrl       { get; set; } = "";
    [JsonProperty("id")]              public int     Id            { get; set; }
    [JsonProperty("name")]            public string  Name          { get; set; } = "";
    [JsonProperty("grouping")]        public string  Grouping      { get; set; } = "";
    [JsonProperty("ordering")]        public int     Ordering      { get; set; }
    [JsonProperty("slug")]            public string  Slug          { get; set; } = "";
    [JsonProperty("description")]     public string  Description   { get; set; } = "";
    [JsonProperty("achieved_count")]  public int     AchievedCount { get; set; }
    [JsonProperty("achieved_percent")] public double AchievedPercent { get; set; }
}

public class Event
{
    [JsonProperty("createdAt")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? CreatedAtLegacy { get; set; }
    [JsonProperty("id")]   public int       Id   { get; set; }
    [JsonProperty("type")]
    [JsonConverter(typeof(EventTypeConverter))]
    public EventType Type { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }
}

public class AchievementEvent : Event
{
    [JsonProperty("achievement")] public EventAchievement Achievement { get; set; } = new();
    [JsonProperty("user")]        public EventUser         User        { get; set; } = new();
}

public class BeatmapPlaycountEvent : Event
{
    [JsonProperty("beatmap")] public EventBeatmap Beatmap { get; set; } = new();
    [JsonProperty("count")]   public int          Count   { get; set; }
}

public class BeatmapsetApproveEvent : Event
{
    [JsonProperty("approval")]   public string         Approval   { get; set; } = "";
    [JsonProperty("beatmapset")] public EventBeatmapset Beatmapset { get; set; } = new();
    [JsonProperty("user")]       public EventUser       User       { get; set; } = new();
}

public class BeatmapsetDeleteEvent  : Event { [JsonProperty("beatmapset")] public EventBeatmapset Beatmapset { get; set; } = new(); }
public class BeatmapsetReviveEvent  : Event { [JsonProperty("beatmapset")] public EventBeatmapset Beatmapset { get; set; } = new(); [JsonProperty("user")] public EventUser User { get; set; } = new(); }
public class BeatmapsetUpdateEvent  : Event { [JsonProperty("beatmapset")] public EventBeatmapset Beatmapset { get; set; } = new(); [JsonProperty("user")] public EventUser User { get; set; } = new(); }
public class BeatmapsetUploadEvent  : Event { [JsonProperty("beatmapset")] public EventBeatmapset Beatmapset { get; set; } = new(); [JsonProperty("user")] public EventUser User { get; set; } = new(); }

public class RankEvent : Event
{
    [JsonProperty("scoreRank")] public string       ScoreRank { get; set; } = "";
    [JsonProperty("rank")]      public int          Rank      { get; set; }
    [JsonProperty("beatmap")]   public EventBeatmap Beatmap   { get; set; } = new();
    [JsonProperty("user")]      public EventUser    User      { get; set; } = new();

    [JsonProperty("mode")]
    [JsonConverter(typeof(GameModeConverter))]
    public GameMode Mode { get; set; }
}

public class RankLostEvent : Event
{
    [JsonProperty("beatmap")] public EventBeatmap Beatmap { get; set; } = new();
    [JsonProperty("user")]    public EventUser    User    { get; set; } = new();

    [JsonProperty("mode")]
    [JsonConverter(typeof(GameModeConverter))]
    public GameMode Mode { get; set; }
}

public class UserSupportFirstEvent  : Event { [JsonProperty("user")] public EventUser User { get; set; } = new(); }
public class UserSupportAgainEvent  : Event { [JsonProperty("user")] public EventUser User { get; set; } = new(); }
public class UserSupportGiftEvent   : Event { [JsonProperty("user")] public EventUser User { get; set; } = new(); }
public class UsernameChangeEvent    : Event { [JsonProperty("user")] public EventUser User { get; set; } = new(); }

public class Events
{
    [JsonProperty("cursor")]        public Cursor?      Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?      CursorString { get; set; }
    [JsonProperty("events")]        public List<Event>  EventList    { get; set; } = [];
}

// ---------------------------------------------------------------------------
// Changelog
// ---------------------------------------------------------------------------

public class GithubUser
{
    [JsonProperty("display_name")]    public string  DisplayName    { get; set; } = "";
    [JsonProperty("github_username")] public string? GithubUsername { get; set; }
    [JsonProperty("github_url")]      public string? GithubUrl      { get; set; }
    [JsonProperty("id")]              public int?    Id             { get; set; }
    [JsonProperty("osu_username")]    public string? OsuUsername    { get; set; }
    [JsonProperty("user_id")]         public int?    UserId         { get; set; }
    [JsonProperty("user_url")]        public string? UserUrl        { get; set; }
}

public class ChangelogEntry
{
    [JsonProperty("category")]               public string     Category            { get; set; } = "";
    [JsonProperty("github_pull_request_id")] public int?       GithubPullRequestId { get; set; }
    [JsonProperty("github_url")]             public string?    GithubUrl           { get; set; }
    [JsonProperty("id")]                     public int?       Id                  { get; set; }
    [JsonProperty("major")]                  public bool       Major               { get; set; }
    [JsonProperty("message")]                public string?    Message             { get; set; }
    [JsonProperty("message_html")]           public string?    MessageHtml         { get; set; }
    [JsonProperty("repository")]             public string?    Repository          { get; set; }
    [JsonProperty("title")]                  public string?    Title               { get; set; }
    [JsonProperty("type")]                   public string     Type                { get; set; } = "";
    [JsonProperty("url")]                    public string?    Url                 { get; set; }
    [JsonProperty("github_user")]            public GithubUser GithubUser          { get; set; } = new();

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? CreatedAt { get; set; }
}

public class UpdateStream
{
    [JsonProperty("display_name")] public string? DisplayName  { get; set; }
    [JsonProperty("id")]           public int     Id           { get; set; }
    [JsonProperty("is_featured")]  public bool    IsFeatured   { get; set; }
    [JsonProperty("name")]         public string  Name         { get; set; } = "";
    [JsonProperty("latest_build")] public Build?  LatestBuild  { get; set; }
    [JsonProperty("user_count")]   public int?    UserCount    { get; set; }
}

public class Versions
{
    [JsonProperty("next")]     public Build? Next     { get; set; }
    [JsonProperty("previous")] public Build? Previous { get; set; }
}

public class Build
{
    [JsonProperty("display_version")]   public string          DisplayVersion   { get; set; } = "";
    [JsonProperty("id")]                public int             Id               { get; set; }
    [JsonProperty("users")]             public int             Users            { get; set; }
    [JsonProperty("version")]           public string?         Version          { get; set; }
    [JsonProperty("youtube_id")]        public string?         YoutubeId        { get; set; }
    [JsonProperty("update_stream")]     public UpdateStream?   UpdateStream     { get; set; }
    [JsonProperty("changelog_entries")] public List<ChangelogEntry>? ChangelogEntries { get; set; }
    [JsonProperty("versions")]          public Versions?       Versions         { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }
}

public class ChangelogSearch
{
    [JsonProperty("from")]   public string? From    { get; set; }
    [JsonProperty("limit")]  public int     Limit   { get; set; }
    [JsonProperty("max_id")] public int?    MaxId   { get; set; }
    [JsonProperty("stream")] public string? Stream  { get; set; }
    [JsonProperty("to")]     public string? To      { get; set; }
}

public class ChangelogListing
{
    [JsonProperty("builds")]  public List<Build>        BuildList { get; set; } = [];
    [JsonProperty("search")]  public ChangelogSearch    Search    { get; set; } = new();
    [JsonProperty("streams")] public List<UpdateStream> Streams   { get; set; } = [];
}

// ---------------------------------------------------------------------------
// News
// ---------------------------------------------------------------------------

public class NewsPost
{
    [JsonProperty("author")]        public string  Author      { get; set; } = "";
    [JsonProperty("edit_url")]      public string  EditUrl     { get; set; } = "";
    [JsonProperty("first_image")]   public string? FirstImage  { get; set; }
    [JsonProperty("first_image@2x")] public string? FirstImage2x { get; set; }
    [JsonProperty("id")]            public int     Id          { get; set; }
    [JsonProperty("slug")]          public string  Slug        { get; set; } = "";
    [JsonProperty("title")]         public string  Title       { get; set; } = "";
    [JsonProperty("content")]       public string? Content     { get; set; }
    [JsonProperty("preview")]       public string? Preview     { get; set; }
    [JsonProperty("navigation")]    public NewsNavigation? Navigation { get; set; }

    [JsonProperty("published_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset PublishedAt { get; set; }

    [JsonProperty("updated_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset UpdatedAt { get; set; }
}

public class NewsNavigation
{
    [JsonProperty("newer")] public NewsPost? Newer { get; set; }
    [JsonProperty("older")] public NewsPost? Older { get; set; }
}

public class NewsSidebar
{
    [JsonProperty("current_year")] public int           CurrentYear { get; set; }
    [JsonProperty("news_posts")]   public List<NewsPost> NewsPostList { get; set; } = [];
    [JsonProperty("years")]        public List<int>      Years       { get; set; } = [];
}

public class NewsSearch
{
    [JsonProperty("limit")] public int    Limit { get; set; }
    [JsonProperty("sort")]  public string Sort  { get; set; } = "";
    [JsonProperty("year")]  public int?   Year  { get; set; }
}

public class NewsListing
{
    [JsonProperty("cursor")]        public Cursor?      Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?      CursorString { get; set; }
    [JsonProperty("news_posts")]    public List<NewsPost> NewsPostList { get; set; } = [];
    [JsonProperty("news_sidebar")]  public NewsSidebar  NewsSidebar  { get; set; } = new();
    [JsonProperty("search")]        public NewsSearch   Search       { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Seasonal Backgrounds
// ---------------------------------------------------------------------------

public class SeasonalBackground
{
    [JsonProperty("url")]  public string      Url  { get; set; } = "";
    [JsonProperty("user")] public UserCompact User { get; set; } = new();
}

public class SeasonalBackgrounds
{
    [JsonProperty("backgrounds")] public List<SeasonalBackground> Backgrounds { get; set; } = [];

    [JsonProperty("ends_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset EndsAt { get; set; }
}

// ---------------------------------------------------------------------------
// Search / Wiki
// ---------------------------------------------------------------------------

public class SearchResult<T>
{
    [JsonProperty("data")]  public List<T> Data  { get; set; } = [];
    [JsonProperty("total")] public int     Total { get; set; }
}

public class WikiPage
{
    [JsonProperty("layout")]            public string       Layout           { get; set; } = "";
    [JsonProperty("locale")]            public string       Locale           { get; set; } = "";
    [JsonProperty("markdown")]          public string       Markdown         { get; set; } = "";
    [JsonProperty("path")]              public string       Path             { get; set; } = "";
    [JsonProperty("subtitle")]          public string?      Subtitle         { get; set; }
    [JsonProperty("tags")]              public List<Newtonsoft.Json.Linq.JToken> Tags { get; set; } = [];
    [JsonProperty("title")]             public string       Title            { get; set; } = "";
    [JsonProperty("available_locales")] public List<string> AvailableLocales { get; set; } = [];
}

public class Search
{
    [JsonProperty("user")]      public SearchResult<UserCompact>? Users     { get; set; }
    [JsonProperty("wiki_page")] public SearchResult<WikiPage>?    WikiPages { get; set; }
}

// ---------------------------------------------------------------------------
// Spotlight / Season
// ---------------------------------------------------------------------------

public class Spotlight
{
    [JsonProperty("id")]                public int    Id              { get; set; }
    [JsonProperty("mode_specific")]     public bool   ModeSpecific    { get; set; }
    [JsonProperty("participant_count")] public int?   ParticipantCount { get; set; }
    [JsonProperty("name")]              public string Name            { get; set; } = "";
    [JsonProperty("type")]              public string Type            { get; set; } = "";

    [JsonProperty("end_date")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset EndDate { get; set; }

    [JsonProperty("start_date")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset StartDate { get; set; }
}

public class Spotlights
{
    [JsonProperty("spotlights")] public List<Spotlight> SpotlightList { get; set; } = [];
}

public class SeasonDivision
{
    [JsonProperty("colour_tier")] public string ColourTier { get; set; } = "";
    [JsonProperty("id")]          public int    Id         { get; set; }
    [JsonProperty("image_url")]   public string ImageUrl   { get; set; } = "";
    [JsonProperty("name")]        public string Name       { get; set; } = "";
    [JsonProperty("threshold")]   public double Threshold  { get; set; }
}

public class Season
{
    [JsonProperty("name")]       public string Name      { get; set; } = "";
    [JsonProperty("room_count")] public int    RoomCount { get; set; }

    [JsonProperty("start_date")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset StartDate { get; set; }

    [JsonProperty("end_date")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? EndDate { get; set; }
}

public class SeasonStatistics
{
    [JsonProperty("division")]    public SeasonDivision Division   { get; set; } = new();
    [JsonProperty("season")]      public Season         SeasonInfo { get; set; } = new();
    [JsonProperty("rank")]        public int            Rank       { get; set; }
    [JsonProperty("total_score")] public double         TotalScore { get; set; }
}

// ---------------------------------------------------------------------------
// Rankings / CountryStatistics
// ---------------------------------------------------------------------------

public class CountryStatistics
{
    [JsonProperty("code")]         public string  Code        { get; set; } = "";
    [JsonProperty("active_users")] public int     ActiveUsers { get; set; }
    [JsonProperty("play_count")]   public long    PlayCount   { get; set; }
    [JsonProperty("ranked_score")] public long    RankedScore { get; set; }
    [JsonProperty("performance")]  public int     Performance { get; set; }
    [JsonProperty("country")]      public Country Country     { get; set; } = new();
}

[JsonConverter(typeof(RankingsConverter))]
public class Rankings
{
    /// <summary>Country leaderboard entries; Ranking contains player entries only.</summary>
    [JsonIgnore] public List<CountryStatistics> CountryRanking { get; set; } = [];
    [JsonProperty("beatmapsets")]   public List<Beatmapset>?    Beatmapsets  { get; set; }
    [JsonProperty("cursor")]        public Cursor?               Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?               CursorString { get; set; }
    [JsonProperty("ranking")]       public List<UserStatistics>  Ranking      { get; set; } = [];
    [JsonProperty("spotlight")]     public Spotlight?             SpotlightInfo { get; set; }
    [JsonProperty("total")]         public int?                  Total        { get; set; }
}

// ---------------------------------------------------------------------------
// Match
// ---------------------------------------------------------------------------

public class Match
{
    [JsonProperty("id")]   public int    Id   { get; set; }
    [JsonProperty("name")] public string Name { get; set; } = "";

    [JsonProperty("start_time")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset StartTime { get; set; }

    [JsonProperty("end_time")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? EndTime { get; set; }
}

public class Matches
{
    [JsonProperty("params")] public Newtonsoft.Json.Linq.JObject? Params { get; set; }
    [JsonProperty("matches")]       public List<Match> MatchList    { get; set; } = [];
    [JsonProperty("cursor")]        public Cursor?     Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?     CursorString { get; set; }
}

public class MatchGame
{
    [JsonProperty("id")]         public int             Id         { get; set; }
    [JsonProperty("mode_int")]   public int             ModeInt    { get; set; }
    [JsonProperty("beatmap")]    public BeatmapCompact? Beatmap    { get; set; }
    [JsonProperty("beatmap_id")] public int             BeatmapId  { get; set; }
    [JsonProperty("match_id")]   public int             MatchId    { get; set; }
    [JsonProperty("scores", ItemConverterType = typeof(MatchScoreConverter))]
    public List<LegacyScore> Scores     { get; set; } = [];

    [JsonProperty("mode")]
    [JsonConverter(typeof(GameModeConverter))]
    public GameMode Mode { get; set; }

    [JsonProperty("scoring_type")]
    [JsonConverter(typeof(ScoringTypeConverter))]
    public ScoringType ScoringType { get; set; }

    [JsonProperty("team_type")]
    [JsonConverter(typeof(TeamTypeConverter))]
    public TeamType TeamType { get; set; }

    private Mods.Mod _mods;
    private Newtonsoft.Json.Linq.JToken? _serializedMods;

    /// <summary>Legacy bitset view used by existing match calculations.</summary>
    [JsonIgnore]
    public Mods.Mod Mods
    {
        get => _mods;
        set { _mods = value; _serializedMods = null; }
    }

    /// <summary>Original API mods, including settings that cannot fit in a legacy bitset.</summary>
    [JsonIgnore] public Newtonsoft.Json.Linq.JToken? RawMods => _serializedMods?.DeepClone();

    [JsonProperty("mods")]
    private Newtonsoft.Json.Linq.JToken SerializedMods
    {
        get => _serializedMods ?? new Newtonsoft.Json.Linq.JValue(_mods.Value);
        set
        {
            _serializedMods = value;
            _mods = value.ToObject<Mods.Mod>(Newtonsoft.Json.JsonSerializer.Create(OssapiClient.BuildJsonSettings()))!;
        }
    }

    [JsonProperty("start_time")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset StartTime { get; set; }

    [JsonProperty("end_time")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? EndTime { get; set; }
}

public class MatchEventDetail
{
    [JsonProperty("type")]
    [JsonConverter(typeof(MatchEventTypeConverter))]
    public MatchEventType Type { get; set; }

    [JsonProperty("text")] public string? Text { get; set; }
}

public class MatchEvent
{
    [JsonProperty("id")]      public long             Id     { get; set; }
    [JsonProperty("detail")]  public MatchEventDetail Detail { get; set; } = new();
    [JsonProperty("user_id")] public int?             UserId { get; set; }
    [JsonProperty("game")]    public MatchGame?        Game   { get; set; }

    [JsonProperty("timestamp")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset Timestamp { get; set; }
}

public class MatchResponse
{
    [JsonProperty("match")]           public Match            MatchInfo      { get; set; } = new();
    [JsonProperty("events")]          public List<MatchEvent> EventList      { get; set; } = [];
    [JsonProperty("users")]           public List<UserCompact> Users         { get; set; } = [];
    [JsonProperty("first_event_id")]  public long             FirstEventId   { get; set; }
    [JsonProperty("latest_event_id")] public long             LatestEventId  { get; set; }
    [JsonProperty("current_game_id")] public int?             CurrentGameId  { get; set; }
}

// ---------------------------------------------------------------------------
// Rooms
// ---------------------------------------------------------------------------

public class RoomPlaylistItemMod
{
    [JsonProperty("acronym")]  public string                    Acronym  { get; set; } = "";
    [JsonProperty("settings")] public Dictionary<string, object?>? Settings { get; set; }
}

public class RoomPlaylistItem
{
    [JsonProperty("id")]            public int                   Id           { get; set; }
    [JsonProperty("room_id")]       public int                   RoomId       { get; set; }
    [JsonProperty("beatmap_id")]    public int                   BeatmapId    { get; set; }
    [JsonProperty("ruleset_id")]    public int                   RulesetId    { get; set; }
    [JsonProperty("allowed_mods")]  public List<RoomPlaylistItemMod> AllowedMods  { get; set; } = [];
    [JsonProperty("required_mods")] public List<RoomPlaylistItemMod> RequiredMods { get; set; } = [];
    [JsonProperty("expired")]       public bool                  Expired      { get; set; }
    [JsonProperty("owner_id")]      public int                   OwnerId      { get; set; }
    [JsonProperty("playlist_order")] public int?                 PlaylistOrder { get; set; }
    [JsonProperty("beatmap")]       public BeatmapCompact        Beatmap      { get; set; } = new();
    [JsonProperty("freestyle")]     public bool                  Freestyle    { get; set; }

    [JsonProperty("played_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? PlayedAt { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? CreatedAt { get; set; }
}

public class RoomPlaylistItemStats
{
    [JsonProperty("count_active")] public int       CountActive { get; set; }
    [JsonProperty("count_total")]  public int       CountTotal  { get; set; }
    [JsonProperty("ruleset_ids")]  public List<int> RulesetIds  { get; set; } = [];
}

public class RoomDifficultyRange
{
    [JsonProperty("min")] public double Min { get; set; }
    [JsonProperty("max")] public double Max { get; set; }
}

public class Room
{
    [JsonProperty("max_participants")] public int? MaxParticipants { get; set; }
    [JsonProperty("id")]                public int                   Id                { get; set; }
    [JsonProperty("name")]              public string                Name              { get; set; } = "";
    [JsonProperty("user_id")]           public int                   UserId            { get; set; }
    [JsonProperty("max_attempts")]      public int?                  MaxAttempts       { get; set; }
    [JsonProperty("participant_count")] public int                   ParticipantCount  { get; set; }
    [JsonProperty("channel_id")]        public int                   ChannelId         { get; set; }
    [JsonProperty("active")]            public bool                  Active            { get; set; }
    [JsonProperty("has_password")]      public bool                  HasPassword       { get; set; }
    [JsonProperty("queue_mode")]        public string                QueueMode         { get; set; } = "";
    [JsonProperty("auto_skip")]         public bool                  AutoSkip          { get; set; }
    [JsonProperty("host")]              public UserCompact           Host              { get; set; } = new();
    [JsonProperty("playlist")]          public List<RoomPlaylistItem> Playlist         { get; set; } = [];
    [JsonProperty("description")]       public string?               Description       { get; set; }
    [JsonProperty("status")]            public string                Status            { get; set; } = "";
    [JsonProperty("pinned")]            public bool                  Pinned            { get; set; }
    [JsonProperty("playlist_item_stats")] public RoomPlaylistItemStats? PlaylistItemStats { get; set; }
    [JsonProperty("current_playlist_item")] public RoomPlaylistItem? CurrentPlaylistItem { get; set; }
    [JsonProperty("difficulty_range")]  public RoomDifficultyRange?  DifficultyRange   { get; set; }
    [JsonProperty("recent_participants")] public List<UserCompact>   RecentParticipants { get; set; } = [];

    [JsonProperty("category")]
    [JsonConverter(typeof(RoomCategoryConverter))]
    public RoomCategory Category { get; set; }

    [JsonProperty("type")]
    [JsonConverter(typeof(RoomTypeConverter))]
    public RoomType Type { get; set; }

    [JsonProperty("starts_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset StartsAt { get; set; }

    [JsonProperty("ends_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? EndsAt { get; set; }
}

public class RoomLeaderboardScore
{
    [JsonProperty("accuracy")]    public double      Accuracy   { get; set; }
    [JsonProperty("attempts")]    public int         Attempts   { get; set; }
    [JsonProperty("completed")]   public int         Completed  { get; set; }
    [JsonProperty("pp")]          public double      Pp         { get; set; }
    [JsonProperty("room_id")]     public int         RoomId     { get; set; }
    [JsonProperty("total_score")] public int         TotalScore { get; set; }
    [JsonProperty("user_id")]     public int         UserId     { get; set; }
    [JsonProperty("user")]        public UserCompact User       { get; set; } = new();
}

public class RoomLeaderboardUserScore : RoomLeaderboardScore
{
    [JsonProperty("position")] public int Position { get; set; }
}

public class RoomLeaderboard
{
    [JsonProperty("leaderboard")] public List<RoomLeaderboardScore>  Leaderboard { get; set; } = [];
    [JsonProperty("user_score")]  public RoomLeaderboardUserScore?   UserScore   { get; set; }
}

// ---------------------------------------------------------------------------
// Multiplayer scores
// ---------------------------------------------------------------------------

public class MultiplayerScore
{
    [JsonProperty("id")]                  public long         Id                { get; set; }
    [JsonProperty("user_id")]             public int         UserId            { get; set; }
    [JsonProperty("room_id")]             public int         RoomId            { get; set; }
    [JsonProperty("playlist_item_id")]    public int         PlaylistItemId    { get; set; }
    [JsonProperty("beatmap_id")]          public int         BeatmapId         { get; set; }
    [JsonProperty("total_score")]         public long         TotalScore        { get; set; }
    [JsonProperty("max_combo")]           public int         MaxCombo          { get; set; }
    [JsonProperty("passed")]              public bool        Passed            { get; set; }
    [JsonProperty("position")]            public int?        Position          { get; set; }
    [JsonProperty("user")]                public User        User              { get; set; } = new();
    [JsonProperty("solo_score_id")]       public long         SoloScoreId       { get; set; }
    [JsonProperty("classic_total_score")] public long         ClassicTotalScore { get; set; }
    [JsonProperty("preserve")]            public bool        Preserve          { get; set; }
    [JsonProperty("processed")]           public bool        Processed         { get; set; }
    [JsonProperty("ranked")]              public bool        Ranked            { get; set; }
    [JsonProperty("total_score_without_mods")] public long   TotalScoreWithoutMods { get; set; }
    [JsonProperty("best_id")]             public long?        BestId            { get; set; }
    [JsonProperty("type")]                public string      Type              { get; set; } = "";
    [JsonProperty("accuracy")]            public double      Accuracy          { get; set; }
    [JsonProperty("build_id")]            public int?         BuildId           { get; set; }
    [JsonProperty("is_perfect_combo")]    public bool        IsPerfectCombo    { get; set; }
    [JsonProperty("replay")]              public bool        Replay            { get; set; }
    [JsonProperty("pp")]                  public double?      Pp                { get; set; }
    [JsonProperty("ruleset_id")]          public int         RulesetId         { get; set; }
    [JsonProperty("has_replay")]          public bool        HasReplay         { get; set; }
    [JsonProperty("legacy_perfect")]      public bool        LegacyPerfect     { get; set; }
    [JsonProperty("legacy_score_id")]     public long?        LegacyScoreId     { get; set; }
    [JsonProperty("legacy_total_score")]  public long         LegacyTotalScore  { get; set; }

    [JsonProperty("rank")]
    [JsonConverter(typeof(GradeConverter))]
    public Grade Rank { get; set; }

    [JsonProperty("mods")]       public List<NonLegacyMod> Mods              { get; set; } = [];
    [JsonProperty("statistics")] public Statistics?         Statistics        { get; set; }
    [JsonProperty("maximum_statistics")] public Statistics? MaximumStatistics  { get; set; }
    [JsonProperty("scores_around")] public MultiplayerScoresAround? ScoresAround { get; set; }

    [JsonProperty("ended_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset EndedAt { get; set; }

    [JsonProperty("started_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? StartedAt { get; set; }
}

public class MultiplayerScoresAround
{
    [JsonProperty("higher")] public List<MultiplayerScore> Higher { get; set; } = [];
    [JsonProperty("lower")]  public List<MultiplayerScore> Lower  { get; set; } = [];
}

public class MultiplayerScores
{
    [JsonProperty("cursor")]        public Cursor?                Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?                CursorString { get; set; }
    [JsonProperty("scores")]        public List<MultiplayerScore> Scores       { get; set; } = [];
    [JsonProperty("total")]         public int?                   Total        { get; set; }
    [JsonProperty("user_score")]    public MultiplayerScore?      UserScore    { get; set; }
}

// ---------------------------------------------------------------------------
// Beatmapset discussions
// ---------------------------------------------------------------------------

public class BeatmapsetDiscussionPost
{
    [JsonProperty("id")]                        public int    Id                     { get; set; }
    [JsonProperty("beatmapset_discussion_id")]  public int    BeatmapsetDiscussionId { get; set; }
    [JsonProperty("user_id")]                   public int    UserId                 { get; set; }
    [JsonProperty("last_editor_id")]            public int?   LastEditorId           { get; set; }
    [JsonProperty("deleted_by_id")]             public int?   DeletedById            { get; set; }
    [JsonProperty("system")]                    public bool   System                 { get; set; }
    [JsonProperty("message")]                   public string Message                { get; set; } = "";

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("updated_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonProperty("deleted_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? DeletedAt { get; set; }
}

public class BeatmapsetDiscussion
{
    [JsonProperty("id")]                 public int     Id               { get; set; }
    [JsonProperty("beatmapset_id")]      public int     BeatmapsetId     { get; set; }
    [JsonProperty("beatmap_id")]         public int?    BeatmapId        { get; set; }
    [JsonProperty("user_id")]            public int     UserId           { get; set; }
    [JsonProperty("deleted_by_id")]      public int?    DeletedById      { get; set; }
    [JsonProperty("parent_id")]          public int?    ParentId         { get; set; }
    [JsonProperty("timestamp")]          public int?    Timestamp        { get; set; }
    [JsonProperty("resolved")]           public bool    Resolved         { get; set; }
    [JsonProperty("can_be_resolved")]    public bool    CanBeResolved    { get; set; }
    [JsonProperty("can_grant_kudosu")]   public bool    CanGrantKudosu   { get; set; }
    [JsonProperty("kudosu_denied")]      public bool    KudosuDenied     { get; set; }
    [JsonProperty("starting_post")]      public BeatmapsetDiscussionPost? StartingPost { get; set; }
    [JsonProperty("posts")]              public List<BeatmapsetDiscussionPost>? Posts { get; set; }
    [JsonProperty("beatmap")]            public BeatmapCompact?    Beatmap    { get; set; }
    [JsonProperty("beatmapset")]         public BeatmapsetCompact? Beatmapset { get; set; }

    [JsonProperty("message_type")]
    [JsonConverter(typeof(MessageTypeConverter))]
    public MessageType MessageType { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("updated_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonProperty("deleted_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonProperty("last_post_at")]
    [JsonConverter(typeof(DateTimeOffsetConverter))]
    public DateTimeOffset? LastPostAt { get; set; }
}

public class ReviewsConfig
{
    [JsonProperty("max_blocks")] public int MaxBlocks { get; set; }
}

public class BeatmapsetDiscussions
{
    [JsonProperty("beatmaps")]             public List<Beatmap>                 Beatmaps            { get; set; } = [];
    [JsonProperty("cursor")]               public Cursor?                        Cursor              { get; set; }
    [JsonProperty("cursor_string")]        public string?                        CursorString        { get; set; }
    [JsonProperty("discussions")]          public List<BeatmapsetDiscussion>     Discussions         { get; set; } = [];
    [JsonProperty("included_discussions")] public List<BeatmapsetDiscussion>     IncludedDiscussions { get; set; } = [];
    [JsonProperty("reviews_config")]       public ReviewsConfig                  ReviewsConfig       { get; set; } = new();
    [JsonProperty("users")]                public List<UserCompact>              Users               { get; set; } = [];
    [JsonProperty("beatmapsets")]          public List<Beatmapset>              Beatmapsets         { get; set; } = [];
}

public class BeatmapsetDiscussionPosts
{
    [JsonProperty("beatmapsets")]   public List<BeatmapsetCompact>     Beatmapsets  { get; set; } = [];
    [JsonProperty("discussions")]   public List<BeatmapsetDiscussion>  Discussions  { get; set; } = [];
    [JsonProperty("cursor")]        public Cursor?                      Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?                      CursorString { get; set; }
    [JsonProperty("posts")]         public List<BeatmapsetDiscussionPost> Posts      { get; set; } = [];
    [JsonProperty("users")]         public List<UserCompact>            Users        { get; set; } = [];
}

public class BeatmapsetDiscussionVote
{
    [JsonProperty("id")]                        public int    Id                     { get; set; }
    [JsonProperty("score")]                     public int    Score                  { get; set; }
    [JsonProperty("user_id")]                   public int    UserId                 { get; set; }
    [JsonProperty("beatmapset_discussion_id")]  public int    BeatmapsetDiscussionId { get; set; }
    [JsonProperty("cursor_string")]             public string? CursorString          { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonProperty("updated_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset UpdatedAt { get; set; }
}

public class BeatmapsetDiscussionVotes
{
    [JsonProperty("cursor")]        public Cursor?                      Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?                      CursorString { get; set; }
    [JsonProperty("discussions")]   public List<BeatmapsetDiscussion>  Discussions  { get; set; } = [];
    [JsonProperty("votes")]         public List<BeatmapsetDiscussionVote> Votes      { get; set; } = [];
    [JsonProperty("users")]         public List<UserCompact>            Users        { get; set; } = [];
}

// ---------------------------------------------------------------------------
// Kudosu
// ---------------------------------------------------------------------------

public class KudosuGiver
{
    [JsonProperty("url")]      public string Url      { get; set; } = "";
    [JsonProperty("username")] public string Username { get; set; } = "";
}

public class KudosuPost
{
    [JsonProperty("url")]   public string? Url   { get; set; }
    [JsonProperty("title")] public string  Title { get; set; } = "";
}

public class KudosuVote
{
    [JsonProperty("user_id")] public int UserId { get; set; }
    [JsonProperty("score")]   public int Score  { get; set; }
}

public class KudosuHistory
{
    [JsonProperty("details")] public Newtonsoft.Json.Linq.JObject? Details { get; set; }
    [JsonProperty("id")]         public int         Id      { get; set; }
    [JsonProperty("amount")]     public int         Amount  { get; set; }
    [JsonProperty("model")]      public string      Model   { get; set; } = "";
    [JsonProperty("giver")]      public KudosuGiver? Giver  { get; set; }
    [JsonProperty("post")]       public KudosuPost  Post    { get; set; } = new();

    [JsonProperty("action")]
    [JsonConverter(typeof(KudosuActionConverter))]
    public KudosuAction Action { get; set; }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }
}

// ---------------------------------------------------------------------------
// Chat
// ---------------------------------------------------------------------------

public class ChatMessage
{
    [JsonProperty("channel_id")]  public int         ChannelId  { get; set; }
    [JsonProperty("content")]     public string      Content    { get; set; } = "";
    [JsonProperty("is_action")]   public bool        IsAction   { get; set; }
    [JsonProperty("message_id")]  public int         MessageId  { get; set; }
    [JsonProperty("sender")]      public UserCompact Sender     { get; set; } = new();
    [JsonProperty("sender_id")]   public int         SenderId   { get; set; }
    [JsonProperty("type")]        public string      Type       { get; set; } = "";

    [JsonProperty("timestamp")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset Timestamp { get; set; }
}

public class ChatChannel
{
    [JsonProperty("channel_id")]          public int              ChannelId          { get; set; }
    [JsonProperty("description")]         public string?          Description        { get; set; }
    [JsonProperty("icon")]               public string?           Icon               { get; set; }
    [JsonProperty("moderated")]          public bool?             Moderated          { get; set; }
    [JsonProperty("name")]               public string            Name               { get; set; } = "";
    [JsonProperty("uuid")]               public string?           Uuid               { get; set; }
    [JsonProperty("message_length_limit")] public int             MessageLengthLimit { get; set; }
    [JsonProperty("last_message_id")]    public int?              LastMessageId      { get; set; }
    [JsonProperty("last_read_id")]       public int?              LastReadId         { get; set; }
    [JsonProperty("recent_messages")]    public List<ChatMessage>? RecentMessages    { get; set; }
    [JsonProperty("users")]              public List<int>?         Users             { get; set; }

    [JsonProperty("type")]
    [JsonConverter(typeof(ChannelTypeConverter))]
    public ChannelType Type { get; set; }
}

public class CreatePMResponse
{
    [JsonProperty("message")]        public ChatMessage         Message       { get; set; } = new();
    [JsonProperty("new_channel_id")] public int                 NewChannelId  { get; set; }
    [JsonProperty("channel")]        public ChatChannel         Channel       { get; set; } = new();
    [JsonProperty("presence")]       public List<ChatChannel>?  Presence      { get; set; }
}

// ---------------------------------------------------------------------------
// BeatmapDifficultyAttributes
// ---------------------------------------------------------------------------

public class BeatmapDifficultyAttributes
{
    [JsonProperty("aim_difficult_slider_count")] public double? AimDifficultSliderCount { get; set; }
    [JsonProperty("aim_difficult_strain_count")] public double? AimDifficultStrainCount { get; set; }
    [JsonProperty("speed_difficult_strain_count")] public double? SpeedDifficultStrainCount { get; set; }
    [JsonProperty("mono_stamina_factor")] public double? MonoStaminaFactor { get; set; }
    [JsonProperty("max_combo")]                   public int    MaxCombo                  { get; set; }
    [JsonProperty("star_rating")]                 public double StarRating                { get; set; }
    [JsonProperty("aim_difficulty")]              public double? AimDifficulty            { get; set; }
    [JsonProperty("approach_rate")]               public double? ApproachRate             { get; set; }
    [JsonProperty("flashlight_difficulty")]       public double? FlashlightDifficulty     { get; set; }
    [JsonProperty("overall_difficulty")]          public double? OverallDifficulty        { get; set; }
    [JsonProperty("slider_factor")]               public double? SliderFactor             { get; set; }
    [JsonProperty("speed_difficulty")]            public double? SpeedDifficulty          { get; set; }
    [JsonProperty("speed_note_count")]            public double? SpeedNoteCount           { get; set; }
    [JsonProperty("stamina_difficulty")]          public double? StaminaDifficulty        { get; set; }
    [JsonProperty("rhythm_difficulty")]           public double? RhythmDifficulty         { get; set; }
    [JsonProperty("colour_difficulty")]           public double? ColourDifficulty         { get; set; }
    [JsonProperty("great_hit_window")]            public double? GreatHitWindow           { get; set; }
    [JsonProperty("score_multiplier")]            public double? ScoreMultiplier          { get; set; }
}

public class DifficultyAttributes
{
    [JsonProperty("attributes")] public BeatmapDifficultyAttributes Attributes { get; set; } = new();
}

// ---------------------------------------------------------------------------
// BeatmapPack
// ---------------------------------------------------------------------------

public class BeatmapPackUserCompletionData
{
    [JsonProperty("beatmapset_ids")] public List<int> BeatmapsetIds { get; set; } = [];
    [JsonProperty("completed")]      public bool      Completed     { get; set; }
}

public class BeatmapPack
{
    [JsonProperty("author")]            public string                        Author             { get; set; } = "";
    [JsonProperty("name")]              public string                        Name               { get; set; } = "";
    [JsonProperty("no_diff_reduction")] public bool                          NoDiffReduction    { get; set; }
    [JsonProperty("ruleset_id")]        public int?                          RulesetId          { get; set; }
    [JsonProperty("tag")]               public string                        Tag                { get; set; } = "";
    [JsonProperty("url")]               public string                        Url                { get; set; } = "";
    [JsonProperty("beatmapsets")]       public List<Beatmapset>?             Beatmapsets        { get; set; }
    [JsonProperty("user_completion_data")] public BeatmapPackUserCompletionData? UserCompletionData { get; set; }

    [JsonProperty("date")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset Date { get; set; }
}

public class BeatmapPacks
{
    [JsonProperty("cursor")]         public Cursor?          Cursor       { get; set; }
    [JsonProperty("cursor_string")]  public string?          CursorString { get; set; }
    [JsonProperty("beatmap_packs")]  public List<BeatmapPack> BeatmapPackList { get; set; } = [];
}

// ---------------------------------------------------------------------------
// Container / response models
// ---------------------------------------------------------------------------

public class Beatmaps
{
    [JsonProperty("beatmaps")] public List<Beatmap> BeatmapList { get; set; } = [];
}

public class BeatmapsetSearchResult
{
    [JsonProperty("search")] public Newtonsoft.Json.Linq.JObject? Search { get; set; }
    [JsonProperty("beatmapsets")]            public List<Beatmapset> Beatmapsets           { get; set; } = [];
    [JsonProperty("cursor")]                 public Cursor?          Cursor                { get; set; }
    [JsonProperty("cursor_string")]          public string?          CursorString          { get; set; }
    [JsonProperty("recommended_difficulty")] public double?          RecommendedDifficulty { get; set; }
    [JsonProperty("error")]                  public string?          Error                 { get; set; }
    [JsonProperty("total")]                  public int              Total                 { get; set; }
}

public class Scores
{
    [JsonProperty("cursor")]        public Cursor?      Cursor       { get; set; }
    [JsonProperty("cursor_string")] public string?      CursorString { get; set; }
    [JsonProperty("scores")]        public List<Score>  ScoreList    { get; set; } = [];
}

public class BeatmapPlaycount
{
    [JsonProperty("beatmap_id")] public int              BeatmapId  { get; set; }
    [JsonProperty("beatmap")]    public BeatmapCompact?  Beatmap    { get; set; }
    [JsonProperty("beatmapset")] public BeatmapsetCompact? Beatmapset { get; set; }
    [JsonProperty("count")]      public int              Count      { get; set; }
}

public class ModdingHistoryEventsBundle
{
    [JsonProperty("events")]         public List<BeatmapsetEvent> EventList     { get; set; } = [];
    [JsonProperty("reviewsConfig")]  public ReviewsConfig          ReviewsConfig { get; set; } = new();
    [JsonProperty("users")]          public List<UserCompact>      Users         { get; set; } = [];
}

public class BeatmapsetEvent
{
    [JsonProperty("id")]         public int                Id          { get; set; }
    [JsonProperty("user_id")]    public int?               UserId      { get; set; }
    [JsonProperty("beatmapset")] public BeatmapsetCompact? Beatmapset  { get; set; }
    [JsonProperty("discussion")] public BeatmapsetDiscussion? Discussion { get; set; }
    [JsonProperty("comment")]    public object?            Comment     { get; set; }

    [JsonIgnore] public BeatmapsetEventType Type { get; set; }
    [JsonIgnore] public string? RawType { get; set; }

    [JsonProperty("type")]
    public string ApiType
    {
        get => Type == BeatmapsetEventType.Unknown && RawType != null
            ? RawType : new BeatmapsetEventTypeConverter().Format(Type);
        set
        {
            RawType = value;
            Type = new BeatmapsetEventTypeConverter().TryParse(value, out var type)
                ? type : BeatmapsetEventType.Unknown;
        }
    }

    [JsonProperty("created_at")]
    [JsonConverter(typeof(DateTimeOffsetRequiredConverter))]
    public DateTimeOffset CreatedAt { get; set; }
}

// ---------------------------------------------------------------------------
// Tags
// ---------------------------------------------------------------------------

public class Tag
{
    [JsonProperty("id")]          public int    Id          { get; set; }
    [JsonProperty("name")]        public string Name        { get; set; } = "";
    [JsonProperty("description")] public string Description { get; set; } = "";
    [JsonProperty("ruleset_id")]  public int?   RulesetId   { get; set; }
}

public class Tags
{
    [JsonProperty("tags")] public List<Tag> TagList { get; set; } = [];
}

// ---------------------------------------------------------------------------
// BeatmapsPassed
// ---------------------------------------------------------------------------

public class BeatmapsPassed
{
    [JsonProperty("beatmaps_passed")] public List<BeatmapCompact> BeatmapsPassedList { get; set; } = [];
}
