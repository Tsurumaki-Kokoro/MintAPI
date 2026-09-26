namespace Ossapi.Enums;

// ========================
// Core API enums (Phase 1)
// ========================

public enum GameMode
{
    Osu,
    Taiko,
    Catch,
    Mania,
}

public enum RankStatus
{
    Graveyard = -2,
    Wip       = -1,
    Pending   = 0,
    Ranked    = 1,
    Approved  = 2,
    Qualified = 3,
    Loved     = 4,
}

public enum Grade
{
    SSH,
    SS,
    SH,
    S,
    A,
    B,
    C,
    D,
    F,
}

// ========================
// String-value enums
// ========================

public enum ProfilePage
{
    Me,
    RecentActivity,
    Beatmaps,
    Historical,
    Kudosu,
    TopRanks,
    Medals,
}

public enum UserAccountHistoryType
{
    Note,
    Restriction,
    Silence,
    TournamentBan,
}

public enum MessageType
{
    Hype,
    MapperNote,
    Praise,
    Problem,
    Review,
    Suggestion,
}

public enum BeatmapsetEventType
{
    Approve,
    BeatmapOwnerChange,
    DiscussionDelete,
    DiscussionLock,
    DiscussionPostDelete,
    DiscussionPostRestore,
    DiscussionRestore,
    DiscussionUnlock,
    Disqualify,
    DisqualifyLegacy,
    GenreEdit,
    IssueReopen,
    IssueResolve,
    KudosuAllow,
    KudosuDeny,
    KudosuGain,
    KudosuLost,
    KudosuRecalculate,
    LanguageEdit,
    Love,
    Nominate,
    NominateModes,
    NominationReset,
    NominationResetReceived,
    Qualify,
    Rank,
    RemoveFromLoved,
    NsfwToggle,
}

public enum BeatmapsetDownload
{
    All,
    NoVideo,
    Direct,
}

public enum UserListFilters
{
    All,
    Online,
    Offline,
}

public enum UserListSorts
{
    LastVisit,
    Rank,
    Username,
}

public enum UserListViews
{
    Card,
    List,
    Brick,
}

/// <summary>Values differ from member names: Give="vote.give", Reset="vote.reset", Revoke="vote.revoke"</summary>
public enum KudosuAction
{
    Give,
    Reset,
    Revoke,
}

public enum EventType
{
    Achievement,
    BeatmapPlaycount,
    BeatmapsetApprove,
    BeatmapsetDelete,
    BeatmapsetRevive,
    BeatmapsetUpdate,
    BeatmapsetUpload,
    Rank,
    RankLost,
    UserSupportFirst,
    UserSupportAgain,
    UserSupportGift,
    UsernameChange,
}

public enum BeatmapsetApproval
{
    Ranked,
    Approved,
    Qualified,
    Loved,
}

public enum ForumTopicType
{
    Normal,
    Sticky,
    Announcement,
}

public enum ChangelogMessageFormat
{
    Html,
    Markdown,
}

public enum UserRelationType
{
    Friend,
    Block,
}

public enum RoomType
{
    Playlists,
    HeadToHead,
    TeamVersus,
    Matchmaking,
}

public enum RoomCategory
{
    Normal,
    Spotlight,
    FeaturedArtist,
    DailyChallenge,
}

public enum MatchEventType
{
    PlayerLeft,
    PlayerJoined,
    PlayerKicked,
    MatchCreated,
    MatchDisbanded,
    HostChanged,
    Other,
}

public enum ScoringType
{
    Score,
    Accuracy,
    Combo,
    ScoreV2,
}

public enum TeamType
{
    HeadToHead,
    TagCoop,
    TeamVs,
    TagTeamVs,
}

public enum Variant
{
    Key4,
    Key7,
}

// ========================
// Parameter enums
// ========================

public enum ScoreType
{
    Best,
    Firsts,
    Recent,
}

public enum RankingFilter
{
    All,
    Friends,
}

public enum RankingType
{
    Charts,
    Country,
    Performance,
    Score,
}

public enum UserLookupKey
{
    Id,
    Username,
}

public enum UserBeatmapType
{
    Favourite,
    Graveyard,
    Loved,
    MostPlayed,
    Ranked,
    Pending,
    Guest,
    Nominated,
}

public enum BeatmapDiscussionPostSort
{
    New,
    Old,
}

public enum BeatmapsetStatus
{
    All,
    Ranked,
    Qualified,
    Disqualified,
    NeverQualified,
}

public enum ChannelType
{
    Public,
    Private,
    Multiplayer,
    Spectator,
    Temporary,
    Pm,
    Group,
    Announce,
}

public enum CommentableType
{
    NewsPost,
    Changelog,
    Beatmapset,
}

public enum CommentSort
{
    New,
    Old,
    Top,
}

public enum ForumTopicSort
{
    New,
    Old,
}

public enum SearchMode
{
    All,
    Users,
    Wiki,
}

public enum MultiplayerScoresSort
{
    New,
    Old,
}

public enum BeatmapsetDiscussionVoteValue
{
    Upvote   = 1,
    Downvote = -1,
}

public enum BeatmapsetDiscussionVoteSort
{
    New,
    Old,
}

public enum BeatmapsetSearchCategory
{
    Any,
    HasLeaderboard,
    Ranked,
    Qualified,
    Loved,
    Favourites,
    Pending,
    Wip,
    Graveyard,
    MyMaps,
}

public enum BeatmapsetSearchMode
{
    Any  = -1,
    Osu  = 0,
    Taiko = 1,
    Catch = 2,
    Mania = 3,
}

public enum BeatmapsetSearchExplicitContent
{
    Hide,
    Show,
}

public enum BeatmapsetSearchGenre
{
    Any          = 0,
    Unspecified  = 1,
    VideoGame    = 2,
    Anime        = 3,
    Rock         = 4,
    Pop          = 5,
    Other        = 6,
    Novelty      = 7,
    HipHop       = 9,
    Electronic   = 10,
    Metal        = 11,
    Classical    = 12,
    Folk         = 13,
    Jazz         = 14,
}

public enum BeatmapsetSearchLanguage
{
    Any          = 0,
    Unspecified  = 1,
    English      = 2,
    Japanese     = 3,
    Chinese      = 4,
    Instrumental = 5,
    Korean       = 6,
    French       = 7,
    German       = 8,
    Swedish      = 9,
    Spanish      = 10,
    Italian      = 11,
    Russian      = 12,
    Polish       = 13,
    OtherLang    = 14,
}

public enum BeatmapsetSearchSort
{
    TitleDescending,
    TitleAscending,
    ArtistDescending,
    ArtistAscending,
    DifficultyDescending,
    DifficultyAscending,
    RankedDescending,
    RankedAscending,
    RatingDescending,
    RatingAscending,
    PlaysDescending,
    PlaysAscending,
    FavoritesDescending,
    FavoritesAscending,
    UpdatedDescending,
    UpdatedAscending,
    RelevanceDescending,
    RelevanceAscending,
    NominationsDescending,
    NominationsAscending,
    CreatorDescending,
    CreatorAscending,
}

public enum NewsPostKey
{
    Slug,
    Id,
}

public enum RoomSearchMode
{
    Active,
    All,
    Ended,
    Participated,
    Owned,
}

public enum EventsSort
{
    New,
    Old,
}

public enum BeatmapPackType
{
    Standard,
    Featured,
    Tournament,
    Loved,
    Chart,
    Theme,
    Artist,
}
