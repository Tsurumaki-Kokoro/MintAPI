using Newtonsoft.Json;
using Ossapi.Enums;

namespace Ossapi.Json;

/// <summary>Maps enum members to API string values.</summary>
public abstract class StringEnumConverterBase<T> : JsonConverter<T> where T : struct, Enum
{
    protected abstract IReadOnlyDictionary<string, T> ApiStringToEnum { get; }
    protected abstract IReadOnlyDictionary<T, string> EnumToApiString { get; }

    public override T ReadJson(JsonReader reader, Type objectType, T existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var s = reader.Value?.ToString();
        if (s is null) throw new JsonSerializationException($"Null value for {typeof(T).Name}");
        if (ApiStringToEnum.TryGetValue(s, out var value)) return value;
        throw new JsonSerializationException($"Unknown {typeof(T).Name} value: '{s}'");
    }

    public override void WriteJson(JsonWriter writer, T value, JsonSerializer serializer)
    {
        if (EnumToApiString.TryGetValue(value, out var s)) writer.WriteValue(s);
        else throw new JsonSerializationException($"Cannot serialize {typeof(T).Name}.{value}");
    }

    public bool TryParse(string s, out T value) => ApiStringToEnum.TryGetValue(s, out value);
}

// ---------------------------------------------------------------------------
// Concrete converters for enums where the API string != member name
// ---------------------------------------------------------------------------

public class ProfilePageConverter : StringEnumConverterBase<ProfilePage>
{
    private static readonly Dictionary<string, ProfilePage> Forward = new()
    {
        ["me"]              = ProfilePage.Me,
        ["recent_activity"] = ProfilePage.RecentActivity,
        ["beatmaps"]        = ProfilePage.Beatmaps,
        ["historical"]      = ProfilePage.Historical,
        ["kudosu"]          = ProfilePage.Kudosu,
        ["top_ranks"]       = ProfilePage.TopRanks,
        ["medals"]          = ProfilePage.Medals,
    };
    private static readonly Dictionary<ProfilePage, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, ProfilePage> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<ProfilePage, string> EnumToApiString  => Reverse;
}

public class UserAccountHistoryTypeConverter : StringEnumConverterBase<UserAccountHistoryType>
{
    private static readonly Dictionary<string, UserAccountHistoryType> Forward = new()
    {
        ["note"]            = UserAccountHistoryType.Note,
        ["restriction"]     = UserAccountHistoryType.Restriction,
        ["silence"]         = UserAccountHistoryType.Silence,
        ["tournament_ban"]  = UserAccountHistoryType.TournamentBan,
    };
    private static readonly Dictionary<UserAccountHistoryType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, UserAccountHistoryType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<UserAccountHistoryType, string> EnumToApiString  => Reverse;
}

public class MessageTypeConverter : StringEnumConverterBase<MessageType>
{
    private static readonly Dictionary<string, MessageType> Forward = new()
    {
        ["hype"]        = MessageType.Hype,
        ["mapper_note"] = MessageType.MapperNote,
        ["praise"]      = MessageType.Praise,
        ["problem"]     = MessageType.Problem,
        ["review"]      = MessageType.Review,
        ["suggestion"]  = MessageType.Suggestion,
    };
    private static readonly Dictionary<MessageType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, MessageType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<MessageType, string> EnumToApiString  => Reverse;
}

public class BeatmapsetEventTypeConverter : StringEnumConverterBase<BeatmapsetEventType>
{
    private static readonly Dictionary<string, BeatmapsetEventType> Forward = new()
    {
        ["approve"]                    = BeatmapsetEventType.Approve,
        ["beatmap_owner_change"]       = BeatmapsetEventType.BeatmapOwnerChange,
        ["discussion_delete"]          = BeatmapsetEventType.DiscussionDelete,
        ["discussion_lock"]            = BeatmapsetEventType.DiscussionLock,
        ["discussion_post_delete"]     = BeatmapsetEventType.DiscussionPostDelete,
        ["discussion_post_restore"]    = BeatmapsetEventType.DiscussionPostRestore,
        ["discussion_restore"]         = BeatmapsetEventType.DiscussionRestore,
        ["discussion_unlock"]          = BeatmapsetEventType.DiscussionUnlock,
        ["disqualify"]                 = BeatmapsetEventType.Disqualify,
        ["disqualify_legacy"]          = BeatmapsetEventType.DisqualifyLegacy,
        ["genre_edit"]                 = BeatmapsetEventType.GenreEdit,
        ["issue_reopen"]               = BeatmapsetEventType.IssueReopen,
        ["issue_resolve"]              = BeatmapsetEventType.IssueResolve,
        ["kudosu_allow"]               = BeatmapsetEventType.KudosuAllow,
        ["kudosu_deny"]                = BeatmapsetEventType.KudosuDeny,
        ["kudosu_gain"]                = BeatmapsetEventType.KudosuGain,
        ["kudosu_lost"]                = BeatmapsetEventType.KudosuLost,
        ["kudosu_recalculate"]         = BeatmapsetEventType.KudosuRecalculate,
        ["language_edit"]              = BeatmapsetEventType.LanguageEdit,
        ["love"]                       = BeatmapsetEventType.Love,
        ["nominate"]                   = BeatmapsetEventType.Nominate,
        ["nominate_modes"]             = BeatmapsetEventType.NominateModes,
        ["nomination_reset"]           = BeatmapsetEventType.NominationReset,
        ["nomination_reset_received"]  = BeatmapsetEventType.NominationResetReceived,
        ["qualify"]                    = BeatmapsetEventType.Qualify,
        ["rank"]                       = BeatmapsetEventType.Rank,
        ["remove_from_loved"]          = BeatmapsetEventType.RemoveFromLoved,
        ["nsfw_toggle"]                = BeatmapsetEventType.NsfwToggle,
    };
    private static readonly Dictionary<BeatmapsetEventType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, BeatmapsetEventType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<BeatmapsetEventType, string> EnumToApiString  => Reverse;
}

public class KudosuActionConverter : StringEnumConverterBase<KudosuAction>
{
    private static readonly Dictionary<string, KudosuAction> Forward = new()
    {
        ["vote.give"]   = KudosuAction.Give,
        ["vote.reset"]  = KudosuAction.Reset,
        ["vote.revoke"] = KudosuAction.Revoke,
    };
    private static readonly Dictionary<KudosuAction, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, KudosuAction> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<KudosuAction, string> EnumToApiString  => Reverse;
}

public class EventTypeConverter : StringEnumConverterBase<EventType>
{
    private static readonly Dictionary<string, EventType> Forward = new()
    {
        ["achievement"]       = EventType.Achievement,
        ["beatmapPlaycount"]  = EventType.BeatmapPlaycount,
        ["beatmapsetApprove"] = EventType.BeatmapsetApprove,
        ["beatmapsetDelete"]  = EventType.BeatmapsetDelete,
        ["beatmapsetRevive"]  = EventType.BeatmapsetRevive,
        ["beatmapsetUpdate"]  = EventType.BeatmapsetUpdate,
        ["beatmapsetUpload"]  = EventType.BeatmapsetUpload,
        ["rank"]              = EventType.Rank,
        ["rankLost"]          = EventType.RankLost,
        ["userSupportFirst"]  = EventType.UserSupportFirst,
        ["userSupportAgain"]  = EventType.UserSupportAgain,
        ["userSupportGift"]   = EventType.UserSupportGift,
        ["usernameChange"]    = EventType.UsernameChange,
    };
    private static readonly Dictionary<EventType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, EventType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<EventType, string> EnumToApiString  => Reverse;
}

public class BeatmapsetApprovalConverter : StringEnumConverterBase<BeatmapsetApproval>
{
    private static readonly Dictionary<string, BeatmapsetApproval> Forward = new()
    {
        ["ranked"]    = BeatmapsetApproval.Ranked,
        ["approved"]  = BeatmapsetApproval.Approved,
        ["qualified"] = BeatmapsetApproval.Qualified,
        ["loved"]     = BeatmapsetApproval.Loved,
    };
    private static readonly Dictionary<BeatmapsetApproval, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, BeatmapsetApproval> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<BeatmapsetApproval, string> EnumToApiString  => Reverse;
}

public class ForumTopicTypeConverter : StringEnumConverterBase<ForumTopicType>
{
    private static readonly Dictionary<string, ForumTopicType> Forward = new()
    {
        ["normal"]       = ForumTopicType.Normal,
        ["sticky"]       = ForumTopicType.Sticky,
        ["announcement"] = ForumTopicType.Announcement,
    };
    private static readonly Dictionary<ForumTopicType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, ForumTopicType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<ForumTopicType, string> EnumToApiString  => Reverse;
}

public class RoomTypeConverter : StringEnumConverterBase<RoomType>
{
    private static readonly Dictionary<string, RoomType> Forward = new()
    {
        ["playlists"]    = RoomType.Playlists,
        ["head_to_head"] = RoomType.HeadToHead,
        ["team_versus"]  = RoomType.TeamVersus,
        ["matchmaking"]  = RoomType.Matchmaking,
    };
    private static readonly Dictionary<RoomType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, RoomType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<RoomType, string> EnumToApiString  => Reverse;
}

public class RoomCategoryConverter : StringEnumConverterBase<RoomCategory>
{
    private static readonly Dictionary<string, RoomCategory> Forward = new()
    {
        ["normal"]          = RoomCategory.Normal,
        ["spotlight"]       = RoomCategory.Spotlight,
        ["featured_artist"] = RoomCategory.FeaturedArtist,
        ["daily_challenge"] = RoomCategory.DailyChallenge,
    };
    private static readonly Dictionary<RoomCategory, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, RoomCategory> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<RoomCategory, string> EnumToApiString  => Reverse;
}

public class MatchEventTypeConverter : StringEnumConverterBase<MatchEventType>
{
    private static readonly Dictionary<string, MatchEventType> Forward = new()
    {
        ["player-left"]      = MatchEventType.PlayerLeft,
        ["player-joined"]    = MatchEventType.PlayerJoined,
        ["player-kicked"]    = MatchEventType.PlayerKicked,
        ["match-created"]    = MatchEventType.MatchCreated,
        ["match-disbanded"]  = MatchEventType.MatchDisbanded,
        ["host-changed"]     = MatchEventType.HostChanged,
        ["other"]            = MatchEventType.Other,
    };
    private static readonly Dictionary<MatchEventType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, MatchEventType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<MatchEventType, string> EnumToApiString  => Reverse;
}

public class ScoringTypeConverter : StringEnumConverterBase<ScoringType>
{
    private static readonly Dictionary<string, ScoringType> Forward = new()
    {
        ["score"]    = ScoringType.Score,
        ["accuracy"] = ScoringType.Accuracy,
        ["combo"]    = ScoringType.Combo,
        ["scorev2"]  = ScoringType.ScoreV2,
    };
    private static readonly Dictionary<ScoringType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, ScoringType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<ScoringType, string> EnumToApiString  => Reverse;
}

public class TeamTypeConverter : StringEnumConverterBase<TeamType>
{
    private static readonly Dictionary<string, TeamType> Forward = new()
    {
        ["head-to-head"]  = TeamType.HeadToHead,
        ["tag-coop"]      = TeamType.TagCoop,
        ["team-vs"]       = TeamType.TeamVs,
        ["tag-team-vs"]   = TeamType.TagTeamVs,
    };
    private static readonly Dictionary<TeamType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, TeamType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<TeamType, string> EnumToApiString  => Reverse;
}

public class VariantConverter : StringEnumConverterBase<Variant>
{
    private static readonly Dictionary<string, Variant> Forward = new()
    {
        ["4k"] = Variant.Key4,
        ["7k"] = Variant.Key7,
    };
    private static readonly Dictionary<Variant, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, Variant> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<Variant, string> EnumToApiString  => Reverse;
}

public class ChannelTypeConverter : StringEnumConverterBase<ChannelType>
{
    private static readonly Dictionary<string, ChannelType> Forward = new()
    {
        ["PUBLIC"]      = ChannelType.Public,
        ["PRIVATE"]     = ChannelType.Private,
        ["MULTIPLAYER"] = ChannelType.Multiplayer,
        ["SPECTATOR"]   = ChannelType.Spectator,
        ["TEMPORARY"]   = ChannelType.Temporary,
        ["PM"]          = ChannelType.Pm,
        ["GROUP"]       = ChannelType.Group,
        ["ANNOUNCE"]    = ChannelType.Announce,
    };
    private static readonly Dictionary<ChannelType, string> Reverse =
        Forward.ToDictionary(kv => kv.Value, kv => kv.Key);
    protected override IReadOnlyDictionary<string, ChannelType> ApiStringToEnum => Forward;
    protected override IReadOnlyDictionary<ChannelType, string> EnumToApiString  => Reverse;
}

/// <summary>Converts matching enum names to lowercase strings.</summary>
public class SimpleStringEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private readonly Dictionary<string, T> _forward;
    private readonly Dictionary<T, string> _reverse;

    public SimpleStringEnumConverter(IReadOnlyDictionary<string, T> mapping)
    {
        _forward = new Dictionary<string, T>(mapping, StringComparer.OrdinalIgnoreCase);
        _reverse = mapping.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    public override T ReadJson(JsonReader reader, Type objectType, T existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var s = reader.Value?.ToString();
        if (s is not null && _forward.TryGetValue(s, out var v)) return v;
        throw new JsonSerializationException($"Unknown {typeof(T).Name}: '{s}'");
    }

    public override void WriteJson(JsonWriter writer, T value, JsonSerializer serializer)
    {
        if (_reverse.TryGetValue(value, out var s)) writer.WriteValue(s);
        else throw new JsonSerializationException($"Cannot serialize {typeof(T).Name}.{value}");
    }
}
