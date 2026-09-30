using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ossapi.Enums;
using Ossapi.Models;

namespace Ossapi.Json;

/// <summary>Deserializes events by their <c>type</c> discriminator.</summary>
public class EventConverter : JsonConverter
{
    private static readonly Dictionary<EventType, Type> TypeMap = new()
    {
        [EventType.Achievement]        = typeof(AchievementEvent),
        [EventType.BeatmapPlaycount]   = typeof(BeatmapPlaycountEvent),
        [EventType.BeatmapsetApprove]  = typeof(BeatmapsetApproveEvent),
        [EventType.BeatmapsetDelete]   = typeof(BeatmapsetDeleteEvent),
        [EventType.BeatmapsetRevive]   = typeof(BeatmapsetReviveEvent),
        [EventType.BeatmapsetUpdate]   = typeof(BeatmapsetUpdateEvent),
        [EventType.BeatmapsetUpload]   = typeof(BeatmapsetUploadEvent),
        [EventType.Rank]               = typeof(RankEvent),
        [EventType.RankLost]           = typeof(RankLostEvent),
        [EventType.UserSupportFirst]   = typeof(UserSupportFirstEvent),
        [EventType.UserSupportAgain]   = typeof(UserSupportAgainEvent),
        [EventType.UserSupportGift]    = typeof(UserSupportGiftEvent),
        [EventType.UsernameChange]     = typeof(UsernameChangeEvent),
    };

    // Only intercept the exact base type — subclasses deserialize normally.
    public override bool CanConvert(Type objectType) => objectType == typeof(Event);

    public override object? ReadJson(JsonReader reader, Type objectType,
        object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var obj = JObject.Load(reader);
        var typeStr = obj["type"]?.Value<string>() ?? "";

        var eventTypeConverter = new EventTypeConverter();
        if (!eventTypeConverter.TryParse(typeStr, out var eventType) ||
            !TypeMap.TryGetValue(eventType, out var targetType))
            targetType = typeof(Event);

        // targetType is always a concrete subclass here, so CanConvert returns false → no recursion.
        return obj.ToObject(targetType, serializer);
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}

/// <summary>Deserializes beatmapset event comments by event type.</summary>
public class BeatmapsetEventConverter : JsonConverter
{
    private static readonly Dictionary<BeatmapsetEventType, Type> CommentTypeMap = new()
    {
        [BeatmapsetEventType.BeatmapOwnerChange]      = typeof(BeatmapsetEventCommentOwnerChange),
        [BeatmapsetEventType.DiscussionDelete]         = typeof(BeatmapsetEventCommentNoPost),
        [BeatmapsetEventType.DiscussionPostDelete]     = typeof(BeatmapsetEventComment),
        [BeatmapsetEventType.DiscussionPostRestore]    = typeof(BeatmapsetEventComment),
        [BeatmapsetEventType.DiscussionRestore]        = typeof(BeatmapsetEventCommentNoPost),
        [BeatmapsetEventType.Disqualify]               = typeof(BeatmapsetEventCommentWithNominators),
        [BeatmapsetEventType.GenreEdit]                = typeof(BeatmapsetEventCommentChange),
        [BeatmapsetEventType.IssueReopen]              = typeof(BeatmapsetEventComment),
        [BeatmapsetEventType.IssueResolve]             = typeof(BeatmapsetEventComment),
        [BeatmapsetEventType.KudosuAllow]              = typeof(BeatmapsetEventCommentNoPost),
        [BeatmapsetEventType.KudosuDeny]               = typeof(BeatmapsetEventCommentNoPost),
        [BeatmapsetEventType.KudosuGain]               = typeof(BeatmapsetEventCommentKudosuChange),
        [BeatmapsetEventType.KudosuLost]               = typeof(BeatmapsetEventCommentKudosuChange),
        [BeatmapsetEventType.KudosuRecalculate]        = typeof(BeatmapsetEventCommentKudosuRecalculate),
        [BeatmapsetEventType.LanguageEdit]             = typeof(BeatmapsetEventCommentChange),
        [BeatmapsetEventType.Love]                     = typeof(object),
        [BeatmapsetEventType.Nominate]                 = typeof(BeatmapsetEventCommentNominate),
        [BeatmapsetEventType.NominationReset]          = typeof(BeatmapsetEventCommentWithNominators),
        [BeatmapsetEventType.NominationResetReceived]  = typeof(BeatmapsetEventCommentWithSourceUser),
        [BeatmapsetEventType.Qualify]                  = typeof(object),
        [BeatmapsetEventType.Rank]                     = typeof(object),
        [BeatmapsetEventType.RemoveFromLoved]          = typeof(BeatmapsetEventCommentLovedRemoval),
        [BeatmapsetEventType.NsfwToggle]               = typeof(BeatmapsetEventCommentChange),
    };

    public override bool CanConvert(Type objectType) => objectType == typeof(BeatmapsetEvent);

    public override object? ReadJson(JsonReader reader, Type objectType,
        object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
            return null;

        var obj = JObject.Load(reader);
        var typeStr = obj["type"]?.Value<string>() ?? "";

        var bsEventTypeConverter = new BeatmapsetEventTypeConverter();
        bsEventTypeConverter.TryParse(typeStr, out var bsEventType);
        CommentTypeMap.TryGetValue(bsEventType, out var commentType);

        var result = new BeatmapsetEvent
        {
            Id         = obj["id"]?.Value<int>()    ?? 0,
            UserId     = obj["user_id"]?.Value<int?>(),
            Type       = bsEventType,
            CreatedAt  = obj["created_at"] is JToken ca
                         ? ca.ToObject<DateTimeOffset>(serializer)
                         : default,
            Beatmapset = obj["beatmapset"]?.ToObject<BeatmapsetCompact>(serializer),
            Discussion = obj["discussion"]?.ToObject<BeatmapsetDiscussion>(serializer),
        };

        var commentToken = obj["comment"];
        if (commentToken == null || commentToken.Type == JTokenType.Null)
        {
            result.Comment = null;
        }
        else if (commentToken.Type == JTokenType.String)
        {
            // Disqualify and NominationReset can have a plain string comment
            result.Comment = commentToken.Value<string>();
        }
        else if (commentType != null && commentType != typeof(object))
        {
            result.Comment = commentToken.ToObject(commentType, serializer);
        }
        else
        {
            result.Comment = commentToken.ToObject<object>(serializer);
        }

        return result;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override bool CanWrite => false;
}
