using Newtonsoft.Json;
using Ossapi.Auth;
using Ossapi.Enums;
using Ossapi.Json;
using Ossapi.Models;
using Ossapi.Mods;

namespace Ossapi;

/// <summary>Asynchronous osu! API v2 client.</summary>
public class OssapiClient : IDisposable
{
    private const string BaseUrl      = "https://osu.ppy.sh/api/v2";
    private const string TokenUrl     = "https://osu.ppy.sh/oauth/token";
    private const int    ApiVersion   = 20241024;
    private const string UserAgent    = "Ossapi.NET/1.0";

    private readonly HttpClient _http;
    private readonly JsonSerializerSettings _jsonSettings;
    private bool _disposed;

    /// <summary>Creates a client-credentials client for public data.</summary>
    /// <param name="clientId">Your osu! OAuth application client ID.</param>
    /// <param name="clientSecret">Your osu! OAuth application client secret.</param>
    /// <param name="tokenDirectory">Directory to store token cache files. Defaults to AppData.</param>
    public OssapiClient(int clientId, string clientSecret, string? tokenDirectory = null)
    {
        var tokenStore = new TokenStore(tokenDirectory);
        var authHandler = new OssapiAuthHandler(clientId, clientSecret, TokenUrl, tokenStore)
        {
            InnerHandler = new HttpClientHandler()
        };

        _http = new HttpClient(authHandler)
        {
            BaseAddress = new Uri(BaseUrl + "/"),
        };
        _http.DefaultRequestHeaders.Add("x-api-version", ApiVersion.ToString());
        _http.DefaultRequestHeaders.Add("User-Agent", UserAgent);

        _jsonSettings = BuildJsonSettings();
    }

    /// <summary>Creates a client with a configured HTTP client.</summary>
    public OssapiClient(HttpClient httpClient)
    {
        _http = httpClient;
        if (!_http.DefaultRequestHeaders.Contains("x-api-version"))
            _http.DefaultRequestHeaders.Add("x-api-version", ApiVersion.ToString());
        _jsonSettings = BuildJsonSettings();
    }

    public static JsonSerializerSettings BuildJsonSettings() => new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling     = NullValueHandling.Ignore,
        Converters =
        {
            // Polymorphic converters (must come before their base-type converters)
            new EventConverter(),
            new BeatmapsetEventConverter(),
            // Core converters
            new GameModeConverter(),
            new RankStatusConverter(),
            new GradeConverter(),
            new ModConverter(),
            new DateTimeOffsetConverter(),
            new DateTimeOffsetRequiredConverter(),
            // String enum converters
            new ProfilePageConverter(),
            new UserAccountHistoryTypeConverter(),
            new MessageTypeConverter(),
            new BeatmapsetEventTypeConverter(),
            new KudosuActionConverter(),
            new EventTypeConverter(),
            new BeatmapsetApprovalConverter(),
            new ForumTopicTypeConverter(),
            new RoomTypeConverter(),
            new RoomCategoryConverter(),
            new MatchEventTypeConverter(),
            new ScoringTypeConverter(),
            new TeamTypeConverter(),
            new VariantConverter(),
            new ChannelTypeConverter(),
        },
    };

    // -------------------------------------------------------------------------
    // Internal HTTP helpers
    // -------------------------------------------------------------------------

    private async Task<T> GetAsync<T>(string url, Dictionary<string, object?>? queryParams = null,
        CancellationToken ct = default)
    {
        var fullUrl = BuildUrl(url, queryParams);
        using var response = await _http.GetAsync(fullUrl, ct);
        return await DeserializeAsync<T>(response, ct);
    }

    private async Task<T> PostAsync<T>(string url, object? body = null, CancellationToken ct = default)
    {
        HttpContent? content = null;
        if (body is not null)
        {
            var json = JsonConvert.SerializeObject(body, _jsonSettings);
            content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }
        using var requestContent = content;
        using var response = await _http.PostAsync(url, requestContent, ct);
        return await DeserializeAsync<T>(response, ct);
    }

    private async Task<T> PutAsync<T>(string url, object? body = null, CancellationToken ct = default)
    {
        HttpContent? content = null;
        if (body is not null)
        {
            var json = JsonConvert.SerializeObject(body, _jsonSettings);
            content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }
        using var requestContent = content;
        using var response = await _http.PutAsync(url, requestContent, ct);
        return await DeserializeAsync<T>(response, ct);
    }

    private async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        CheckApiError(body, response.RequestMessage?.RequestUri?.ToString() ?? "");
        var result = JsonConvert.DeserializeObject<T>(body, _jsonSettings);
        return result ?? throw new InvalidOperationException("API returned null or empty response.");
    }

    private static void CheckApiError(string body, string url)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        try
        {
            var obj = JsonConvert.DeserializeObject<Dictionary<string, object?>>(body);
            if (obj is not null && obj.Count == 1 && obj.ContainsKey("error"))
                throw new OssapiException($"API error: {obj["error"]} (url: {url})");
            if (obj is not null && obj.TryGetValue("authentication", out var auth) && auth?.ToString() == "basic")
                throw new OssapiException($"Permission denied for {url}. Authorization Code grant may be required.");
        }
        catch (JsonException) { /* not an error object */ }
    }

    private static string BuildUrl(string path, Dictionary<string, object?>? queryParams)
    {
        if (queryParams is null || queryParams.Count == 0) return path;

        var parts = new List<string>();
        foreach (var (key, value) in queryParams)
        {
            if (value is null) continue;
            FormatParam(parts, key, value);
        }

        if (parts.Count == 0) return path;
        return path + "?" + string.Join("&", parts);
    }

    private static void FormatParam(List<string> parts, string key, object value)
    {
        // Cursor expands as cursor[field]=value pairs
        if (value is Cursor cursor)
        {
            foreach (var (k, v) in cursor)
                if (v is not null)
                    parts.Add($"{Uri.EscapeDataString($"cursor[{k}]")}={Uri.EscapeDataString(FormatValue(v))}");
            return;
        }

        if (value is System.Collections.IEnumerable list and not string)
        {
            foreach (var item in list)
                if (item is not null)
                    parts.Add($"{Uri.EscapeDataString(key + "[]")}={Uri.EscapeDataString(FormatValue(item))}");
            return;
        }

        if (value is Mod mod)
        {
            var mods = mod == Mod.NM ? [Mod.NM] : mod.Decompose(clean: true);
            foreach (var m in mods)
                parts.Add($"{Uri.EscapeDataString(key + "[]")}={Uri.EscapeDataString(m.ToShortName())}");
            return;
        }

        parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(FormatValue(value))}");
    }

    private static string FormatValue(object value) => value switch
    {
        bool b            => b ? "true" : "false",
        GameMode gm       => gm switch
        {
            GameMode.Osu   => "osu",
            GameMode.Taiko => "taiko",
            GameMode.Catch => "fruits",
            GameMode.Mania => "mania",
            _              => gm.ToString(),
        },
        ScoreType v                      => v switch
        {
            ScoreType.Best    => "best",
            ScoreType.Firsts  => "firsts",
            ScoreType.Recent  => "recent",
            _                 => v.ToString(),
        },
        BeatmapScoreRankingType v => v.ToString().ToLowerInvariant(),
        RankingType v                    => v switch
        {
            RankingType.Charts      => "charts",
            RankingType.Country     => "country",
            RankingType.Performance => "performance",
            RankingType.Score       => "score",
            _                       => v.ToString(),
        },
        RankingFilter v                  => v switch
        {
            RankingFilter.All     => "all",
            RankingFilter.Friends => "friends",
            _                     => v.ToString(),
        },
        UserBeatmapType v                => v switch
        {
            UserBeatmapType.Favourite  => "favourite",
            UserBeatmapType.Graveyard  => "graveyard",
            UserBeatmapType.Loved      => "loved",
            UserBeatmapType.MostPlayed => "most_played",
            UserBeatmapType.Ranked     => "ranked",
            UserBeatmapType.Pending    => "pending",
            UserBeatmapType.Guest      => "guest",
            UserBeatmapType.Nominated  => "nominated",
            _                          => v.ToString(),
        },
        UserLookupKey v                  => v switch
        {
            UserLookupKey.Id       => "id",
            UserLookupKey.Username => "username",
            _                      => v.ToString(),
        },
        BeatmapDiscussionPostSort v      => v switch
        {
            BeatmapDiscussionPostSort.New => "id_desc",
            BeatmapDiscussionPostSort.Old => "id_asc",
            _                             => v.ToString(),
        },
        BeatmapsetStatus v               => v switch
        {
            BeatmapsetStatus.All            => "all",
            BeatmapsetStatus.Ranked         => "ranked",
            BeatmapsetStatus.Qualified      => "qualified",
            BeatmapsetStatus.Disqualified   => "disqualified",
            BeatmapsetStatus.NeverQualified => "never_qualified",
            _                               => v.ToString(),
        },
        CommentableType v                => v switch
        {
            CommentableType.NewsPost   => "news_post",
            CommentableType.Changelog  => "build",
            CommentableType.Beatmapset => "beatmapset",
            _                          => v.ToString(),
        },
        CommentSort v                    => v switch
        {
            CommentSort.New => "new",
            CommentSort.Old => "old",
            CommentSort.Top => "top",
            _               => v.ToString(),
        },
        ForumTopicSort v                 => v switch
        {
            ForumTopicSort.New => "id_desc",
            ForumTopicSort.Old => "id_asc",
            _                  => v.ToString(),
        },
        SearchMode v                     => v switch
        {
            SearchMode.All   => "all",
            SearchMode.Users => "user",
            SearchMode.Wiki  => "wiki_page",
            _                => v.ToString(),
        },
        MultiplayerScoresSort v          => v switch
        {
            MultiplayerScoresSort.New => "score_desc",
            MultiplayerScoresSort.Old => "score_asc",
            _                         => v.ToString(),
        },
        BeatmapsetDiscussionVoteSort v   => v switch
        {
            BeatmapsetDiscussionVoteSort.New => "id_desc",
            BeatmapsetDiscussionVoteSort.Old => "id_asc",
            _                                => v.ToString(),
        },
        BeatmapsetSearchCategory v       => v switch
        {
            BeatmapsetSearchCategory.Any           => "any",
            BeatmapsetSearchCategory.HasLeaderboard => "leaderboard",
            BeatmapsetSearchCategory.Ranked        => "ranked",
            BeatmapsetSearchCategory.Qualified     => "qualified",
            BeatmapsetSearchCategory.Loved         => "loved",
            BeatmapsetSearchCategory.Favourites    => "favourites",
            BeatmapsetSearchCategory.Pending       => "pending",
            BeatmapsetSearchCategory.Wip           => "wip",
            BeatmapsetSearchCategory.Graveyard     => "graveyard",
            BeatmapsetSearchCategory.MyMaps        => "mine",
            _                                      => v.ToString(),
        },
        BeatmapsetSearchSort v           => v switch
        {
            BeatmapsetSearchSort.TitleDescending       => "title_desc",
            BeatmapsetSearchSort.TitleAscending        => "title_asc",
            BeatmapsetSearchSort.ArtistDescending      => "artist_desc",
            BeatmapsetSearchSort.ArtistAscending       => "artist_asc",
            BeatmapsetSearchSort.DifficultyDescending  => "difficulty_desc",
            BeatmapsetSearchSort.DifficultyAscending   => "difficulty_asc",
            BeatmapsetSearchSort.RankedDescending      => "ranked_desc",
            BeatmapsetSearchSort.RankedAscending       => "ranked_asc",
            BeatmapsetSearchSort.RatingDescending      => "rating_desc",
            BeatmapsetSearchSort.RatingAscending       => "rating_asc",
            BeatmapsetSearchSort.PlaysDescending       => "plays_desc",
            BeatmapsetSearchSort.PlaysAscending        => "plays_asc",
            BeatmapsetSearchSort.FavoritesDescending   => "favourites_desc",
            BeatmapsetSearchSort.FavoritesAscending    => "favourites_asc",
            BeatmapsetSearchSort.UpdatedDescending     => "updated_desc",
            BeatmapsetSearchSort.UpdatedAscending      => "updated_asc",
            BeatmapsetSearchSort.RelevanceDescending   => "relevance_desc",
            BeatmapsetSearchSort.RelevanceAscending    => "relevance_asc",
            BeatmapsetSearchSort.NominationsDescending => "nominations_desc",
            BeatmapsetSearchSort.NominationsAscending  => "nominations_asc",
            BeatmapsetSearchSort.CreatorDescending     => "creator_desc",
            BeatmapsetSearchSort.CreatorAscending      => "creator_asc",
            _                                          => v.ToString(),
        },
        EventsSort v                     => v switch
        {
            EventsSort.New => "id_desc",
            EventsSort.Old => "id_asc",
            _              => v.ToString(),
        },
        NewsPostKey v                    => v switch
        {
            NewsPostKey.Slug => "slug",
            NewsPostKey.Id   => "id",
            _                => v.ToString(),
        },
        RoomSearchMode v                 => v switch
        {
            RoomSearchMode.Active       => "active",
            RoomSearchMode.All          => "all",
            RoomSearchMode.Ended        => "ended",
            RoomSearchMode.Participated => "participated",
            RoomSearchMode.Owned        => "owned",
            _                           => v.ToString(),
        },
        BeatmapPackType v                => v switch
        {
            BeatmapPackType.Standard   => "standard",
            BeatmapPackType.Featured   => "featured",
            BeatmapPackType.Tournament => "tournament",
            BeatmapPackType.Loved      => "loved",
            BeatmapPackType.Chart      => "chart",
            BeatmapPackType.Theme      => "theme",
            BeatmapPackType.Artist     => "artist",
            _                          => v.ToString(),
        },
        ChangelogMessageFormat v         => v switch
        {
            ChangelogMessageFormat.Html     => "html",
            ChangelogMessageFormat.Markdown => "markdown",
            _                               => v.ToString(),
        },
        BeatmapsetEventType v => new BeatmapsetEventTypeConverter().Format(v),
        MessageType v => new MessageTypeConverter().Format(v),
        BeatmapsetDiscussionVoteValue v  => ((int)v).ToString(),
        Enum e                           => Convert.ToInt64(e).ToString(),
        DateTimeOffset dt                => dt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _                                => value.ToString() ?? "",
    };

    // -------------------------------------------------------------------------
    // Beatmap Packs endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get a paginated list of beatmap packs.</summary>
    public Task<BeatmapPacks> GetBeatmapPacksAsync(
        BeatmapPackType? type = null,
        string? cursorString  = null,
        bool? legacyOnly      = null,
        CancellationToken ct  = default)
        => GetAsync<BeatmapPacks>("beatmaps/packs", new()
        {
            ["type"]          = type,
            ["cursor_string"] = cursorString,
            ["legacy_only"]   = legacyOnly.HasValue ? (int?)(legacyOnly.Value ? 1 : 0) : null,
        }, ct);

    /// <summary>Get a single beatmap pack by its tag.</summary>
    public Task<BeatmapPack> GetBeatmapPackAsync(string pack, CancellationToken ct = default)
        => GetAsync<BeatmapPack>($"beatmaps/packs/{Uri.EscapeDataString(pack)}", ct: ct);

    // -------------------------------------------------------------------------
    // Beatmaps endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get a user's best score on a beatmap.</summary>
    public Task<BeatmapUserScore> GetBeatmapUserScoreAsync(
        int beatmapId,
        int userId,
        GameMode? mode       = null,
        Mod? mods            = null,
        bool? legacyOnly     = null,
        CancellationToken ct = default)
        => GetAsync<BeatmapUserScore>($"beatmaps/{beatmapId}/scores/users/{userId}", new()
        {
            ["mode"]        = mode,
            ["mods"]        = mods,
            ["legacy_only"] = legacyOnly.HasValue ? (int?)(legacyOnly.Value ? 1 : 0) : null,
        }, ct);

    /// <summary>Get all of a user's scores on a beatmap.</summary>
    public async Task<List<Score>> GetBeatmapUserScoresAsync(
        int beatmapId,
        int userId,
        GameMode? mode       = null,
        bool? legacyOnly     = null,
        CancellationToken ct = default)
    {
        var result = await GetAsync<BeatmapUserScores>($"beatmaps/{beatmapId}/scores/users/{userId}/all", new()
        {
            ["mode"]        = mode,
            ["legacy_only"] = legacyOnly.HasValue ? (int?)(legacyOnly.Value ? 1 : 0) : null,
        }, ct);
        return result.Scores;
    }

    /// <summary>Get the top scores of a beatmap.</summary>
    public Task<BeatmapScores> GetBeatmapScoresAsync(
        int beatmapId,
        GameMode? mode       = null,
        Mod? mods            = null,
        BeatmapScoreRankingType? type = null,
        int? limit           = null,
        bool? legacyOnly     = null,
        CancellationToken ct = default)
        => GetAsync<BeatmapScores>($"beatmaps/{beatmapId}/scores", new()
        {
            ["mode"]        = mode,
            ["mods"]        = mods,
            ["type"]        = type,
            ["limit"]       = limit,
            ["legacy_only"] = legacyOnly.HasValue ? (int?)(legacyOnly.Value ? 1 : 0) : null,
        }, ct);

    /// <summary>Look up a beatmap by id, MD5 checksum, or filename.</summary>
    public Task<Beatmap> GetBeatmapAsync(
        int? beatmapId       = null,
        string? checksum     = null,
        string? filename     = null,
        CancellationToken ct = default)
    {
        if (beatmapId == null && checksum == null && filename == null)
            throw new ArgumentException("At least one of beatmapId, checksum, or filename must be provided.");
        return GetAsync<Beatmap>("beatmaps/lookup", new()
        {
            ["id"]       = beatmapId,
            ["checksum"] = checksum,
            ["filename"] = filename,
        }, ct);
    }

    /// <summary>Batch-get beatmaps by id.</summary>
    public async Task<List<Beatmap>> GetBeatmapsAsync(
        IEnumerable<int> beatmapIds,
        CancellationToken ct = default)
    {
        var result = await GetAsync<Beatmaps>("beatmaps", new()
        {
            ["ids"] = beatmapIds.ToList(),
        }, ct);
        return result.BeatmapList;
    }

    /// <summary>Get difficulty attributes for a beatmap (used for pp calculation).</summary>
    public Task<DifficultyAttributes> GetBeatmapAttributesAsync(
        int beatmapId,
        Mod? mods            = null,
        GameMode? ruleset    = null,
        int? rulesetId       = null,
        CancellationToken ct = default)
        => PostAsync<DifficultyAttributes>($"beatmaps/{beatmapId}/attributes", new
        {
            mods        = mods?.Value,
            ruleset     = ruleset.HasValue ? FormatValue(ruleset.Value) : null,
            ruleset_id  = rulesetId,
        }, ct);

    // -------------------------------------------------------------------------
    // Beatmapsets endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get posts of a beatmapset discussion.</summary>
    public Task<BeatmapsetDiscussionPosts> GetBeatmapsetDiscussionPostsAsync(
        int? beatmapsetDiscussionId           = null,
        int? limit                            = null,
        int? page                             = null,
        BeatmapDiscussionPostSort? sort       = null,
        int? userId                           = null,
        bool? withDeleted                     = null,
        CancellationToken ct                  = default)
        => GetAsync<BeatmapsetDiscussionPosts>("beatmapsets/discussions/posts", new()
        {
            ["beatmapset_discussion_id"] = beatmapsetDiscussionId,
            ["limit"]                    = limit,
            ["page"]                     = page,
            ["sort"]                     = sort,
            ["user"]                     = userId,
            ["with_deleted"]             = withDeleted,
        }, ct);

    /// <summary>Get votes on beatmapset discussions.</summary>
    public Task<BeatmapsetDiscussionVotes> GetBeatmapsetDiscussionVotesAsync(
        int? beatmapsetDiscussionId                = null,
        int? limit                                 = null,
        int? page                                  = null,
        int? receiverId                            = null,
        BeatmapsetDiscussionVoteValue? vote        = null,
        BeatmapsetDiscussionVoteSort? sort         = null,
        int? userId                                = null,
        bool? withDeleted                          = null,
        CancellationToken ct                       = default)
        => GetAsync<BeatmapsetDiscussionVotes>("beatmapsets/discussions/votes", new()
        {
            ["beatmapset_discussion_id"] = beatmapsetDiscussionId,
            ["limit"]                    = limit,
            ["page"]                     = page,
            ["receiver"]                 = receiverId,
            ["score"]                    = vote,
            ["sort"]                     = sort,
            ["user"]                     = userId,
            ["with_deleted"]             = withDeleted,
        }, ct);

    /// <summary>Get beatmapset discussions.</summary>
    public Task<BeatmapsetDiscussions> GetBeatmapsetDiscussionsAsync(
        int? beatmapsetId                          = null,
        int? beatmapId                             = null,
        BeatmapsetStatus? beatmapsetStatus         = null,
        int? limit                                 = null,
        IEnumerable<MessageType>? messageTypes     = null,
        bool? onlyUnresolved                       = null,
        int? page                                  = null,
        BeatmapDiscussionPostSort? sort            = null,
        int? userId                                = null,
        bool? withDeleted                          = null,
        CancellationToken ct                       = default)
        => GetAsync<BeatmapsetDiscussions>("beatmapsets/discussions", new()
        {
            ["beatmapset_id"]      = beatmapsetId,
            ["beatmap_id"]         = beatmapId,
            ["beatmapset_status"]  = beatmapsetStatus,
            ["limit"]              = limit,
            ["message_types"]      = messageTypes?.ToList(),
            ["only_unresolved"]    = onlyUnresolved,
            ["page"]               = page,
            ["sort"]               = sort,
            ["user"]               = userId,
            ["with_deleted"]       = withDeleted,
        }, ct);

    /// <summary>Search beatmapsets (equivalent to the website search page).</summary>
    public Task<BeatmapsetSearchResult> SearchBeatmapsetsAsync(
        string? query                                         = null,
        BeatmapsetSearchMode mode                             = BeatmapsetSearchMode.Any,
        BeatmapsetSearchCategory category                     = BeatmapsetSearchCategory.HasLeaderboard,
        BeatmapsetSearchExplicitContent explicitContent       = BeatmapsetSearchExplicitContent.Hide,
        BeatmapsetSearchGenre genre                           = BeatmapsetSearchGenre.Any,
        BeatmapsetSearchLanguage language                     = BeatmapsetSearchLanguage.Any,
        bool forceVideo                                       = false,
        bool forceStoryboard                                  = false,
        bool forceRecommendedDifficulty                       = false,
        bool includeConverts                                  = false,
        bool forceFollowedMappers                             = false,
        bool forceSpotlights                                  = false,
        bool forceFeaturedArtists                             = false,
        Cursor? cursor                                        = null,
        BeatmapsetSearchSort? sort                            = null,
        CancellationToken ct                                  = default)
    {
        var extras = new List<string>();
        if (forceVideo) extras.Add("video");
        if (forceStoryboard) extras.Add("storyboard");

        var generals = new List<string>();
        if (forceRecommendedDifficulty) generals.Add("recommended");
        if (includeConverts) generals.Add("converts");
        if (forceFollowedMappers) generals.Add("follows");
        if (forceSpotlights) generals.Add("spotlights");
        if (forceFeaturedArtists) generals.Add("featured_artists");

        var nsfw = explicitContent == BeatmapsetSearchExplicitContent.Show ? "true" : "false";

        var @params = new Dictionary<string, object?>
        {
            ["q"]    = query,
            ["s"]    = category,
            ["m"]    = mode == BeatmapsetSearchMode.Any ? null : (object?)((int)mode),
            ["nsfw"] = nsfw,
            ["e"]    = extras.Count > 0 ? string.Join(".", extras) : null,
            ["c"]    = generals.Count > 0 ? string.Join(".", generals) : null,
            ["sort"]   = sort,
            ["cursor"] = cursor,
        };
        if (genre != BeatmapsetSearchGenre.Any) @params["g"] = (int)genre;
        if (language != BeatmapsetSearchLanguage.Any) @params["l"] = (int)language;

        return GetAsync<BeatmapsetSearchResult>("beatmapsets/search/", @params, ct);
    }

    /// <summary>Get a beatmapset by beatmapset id or by a beatmap id it contains.</summary>
    public Task<Beatmapset> GetBeatmapsetAsync(
        int? beatmapsetId    = null,
        int? beatmapId       = null,
        CancellationToken ct = default)
    {
        if ((beatmapId == null) == (beatmapsetId == null))
            throw new ArgumentException("Exactly one of beatmapsetId or beatmapId must be provided.");
        if (beatmapId.HasValue)
            return GetAsync<Beatmapset>("beatmapsets/lookup", new() { ["beatmap_id"] = beatmapId }, ct);
        return GetAsync<Beatmapset>($"beatmapsets/{beatmapsetId}", ct: ct);
    }

    /// <summary>Get beatmapset events (modding history).</summary>
    public Task<ModdingHistoryEventsBundle> GetBeatmapsetEventsAsync(
        int? limit                               = null,
        int? page                                = null,
        int? userId                              = null,
        IEnumerable<BeatmapsetEventType>? types  = null,
        DateTimeOffset? minDate                  = null,
        DateTimeOffset? maxDate                  = null,
        int? beatmapsetId                        = null,
        CancellationToken ct                     = default)
        => GetAsync<ModdingHistoryEventsBundle>("beatmapsets/events", new()
        {
            ["limit"]         = limit,
            ["page"]          = page,
            ["user"]          = userId,
            ["types"]         = types?.ToList(),
            ["min_date"]      = minDate,
            ["max_date"]      = maxDate,
            ["beatmapset_id"] = beatmapsetId,
        }, ct);

    // -------------------------------------------------------------------------
    // Changelog endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get details of a specific changelog build.</summary>
    public Task<Build> GetChangelogBuildAsync(string stream, string build, CancellationToken ct = default)
        => GetAsync<Build>($"changelog/{Uri.EscapeDataString(stream)}/{Uri.EscapeDataString(build)}", ct: ct);

    /// <summary>Get list of changelogs.</summary>
    public Task<ChangelogListing> GetChangelogListingAsync(
        string? from                                       = null,
        string? to                                         = null,
        int? maxId                                         = null,
        string? stream                                     = null,
        IEnumerable<ChangelogMessageFormat>? messageFormats = null,
        CancellationToken ct                               = default)
        => GetAsync<ChangelogListing>("changelog", new()
        {
            ["from"]            = from,
            ["to"]              = to,
            ["max_id"]          = maxId,
            ["stream"]          = stream,
            ["message_formats"] = (messageFormats ?? [ChangelogMessageFormat.Html, ChangelogMessageFormat.Markdown]).ToList(),
        }, ct);

    /// <summary>Look up a changelog build by version, stream name, or id.</summary>
    public Task<Build> GetChangelogBuildLookupAsync(
        string changelog,
        string? key                                        = null,
        IEnumerable<ChangelogMessageFormat>? messageFormats = null,
        CancellationToken ct                               = default)
        => GetAsync<Build>($"changelog/{Uri.EscapeDataString(changelog)}", new()
        {
            ["key"]             = key,
            ["message_formats"] = (messageFormats ?? [ChangelogMessageFormat.Html, ChangelogMessageFormat.Markdown]).ToList(),
        }, ct);

    // -------------------------------------------------------------------------
    // Chat endpoints
    // -------------------------------------------------------------------------

    /// <summary>Send a private message to a user.</summary>
    public Task<CreatePMResponse> SendPmAsync(
        int userId,
        string message,
        bool isAction        = false,
        CancellationToken ct = default)
        => PostAsync<CreatePMResponse>("chat/new", new
        {
            target_id = userId,
            message,
            is_action = isAction,
        }, ct);

    /// <summary>Send an announcement to a group of users (requires announce usergroup).</summary>
    public Task<ChatChannel> SendAnnouncementAsync(
        string channelName,
        string channelDescription,
        string message,
        IEnumerable<int> targetIds,
        CancellationToken ct = default)
        => PostAsync<ChatChannel>("chat/channels", new Dictionary<string, object?>
        {
            ["channel.name"]        = channelName,
            ["channel.description"] = channelDescription,
            ["message"]             = message,
            ["target_ids"]          = targetIds.ToList(),
            ["type"]                = "ANNOUNCE",
        }, ct);

    // -------------------------------------------------------------------------
    // Comments endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get recent comments and their replies.</summary>
    public Task<CommentBundle> GetCommentsAsync(
        CommentableType? commentableType = null,
        int? commentableId               = null,
        Cursor? cursor                   = null,
        int? parentId                    = null,
        CommentSort? sort                = null,
        CancellationToken ct             = default)
        => GetAsync<CommentBundle>("comments", new()
        {
            ["commentable_type"] = commentableType,
            ["commentable_id"]   = commentableId,
            ["cursor"]           = cursor,
            ["parent_id"]        = parentId,
            ["sort"]             = sort,
        }, ct);

    /// <summary>Get a single comment and its replies.</summary>
    public Task<CommentBundle> GetCommentAsync(int commentId, CancellationToken ct = default)
        => GetAsync<CommentBundle>($"comments/{commentId}", ct: ct);

    // -------------------------------------------------------------------------
    // Events endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get most recent events across all users.</summary>
    public Task<Events> GetEventsAsync(
        EventsSort? sort         = null,
        string? cursorString     = null,
        CancellationToken ct     = default)
        => GetAsync<Events>("events", new()
        {
            ["sort"]          = sort,
            ["cursor_string"] = cursorString,
        }, ct);

    // -------------------------------------------------------------------------
    // Forums endpoints
    // -------------------------------------------------------------------------

    /// <summary>Create a new forum topic.</summary>
    public Task<CreateForumTopicResponse> ForumCreateTopicAsync(
        int forumId,
        string title,
        string body,
        ForumPollRequest? poll = null,
        CancellationToken ct   = default)
    {
        var data = new Dictionary<string, object?>
        {
            ["body"]     = body,
            ["forum_id"] = forumId,
            ["title"]    = title,
        };
        if (poll is not null)
        {
            data["with_poll"]                          = true;
            data["forum_topic_poll[hide_results]"]     = poll.HideResults;
            data["forum_topic_poll[length_days]"]      = poll.LengthDays;
            data["forum_topic_poll[max_options]"]      = poll.MaxOptions;
            data["forum_topic_poll[options]"]          = string.Join("\r\n", poll.Options);
            data["forum_topic_poll[title]"]            = poll.Title;
            data["forum_topic_poll[vote_change]"]      = poll.VoteChange;
        }
        return PostAsync<CreateForumTopicResponse>("forums/topics", data, ct);
    }

    /// <summary>Reply to a forum topic.</summary>
    public Task<ForumPost> ForumReplyAsync(int topicId, string body, CancellationToken ct = default)
        => PostAsync<ForumPost>($"forums/topics/{topicId}/reply", new { body }, ct);

    /// <summary>Edit a forum topic title.</summary>
    public Task<ForumTopic> ForumEditTopicAsync(int topicId, string title, CancellationToken ct = default)
        => PutAsync<ForumTopic>($"forums/topics/{topicId}", new Dictionary<string, object?>
        {
            ["forum_topic[topic_title]"] = title,
        }, ct);

    /// <summary>Edit a forum post body.</summary>
    public Task<ForumPost> ForumEditPostAsync(int postId, string body, CancellationToken ct = default)
        => PutAsync<ForumPost>($"forums/posts/{postId}", new { body }, ct);

    /// <summary>Get a forum topic and its posts.</summary>
    public Task<ForumTopicAndPosts> GetForumTopicAsync(
        int topicId,
        string? cursorString = null,
        ForumTopicSort? sort = null,
        int? limit           = null,
        int? start           = null,
        int? end             = null,
        CancellationToken ct = default)
        => GetAsync<ForumTopicAndPosts>($"forums/topics/{topicId}", new()
        {
            ["cursor_string"] = cursorString,
            ["sort"]          = sort,
            ["limit"]         = limit,
            ["start"]         = start,
            ["end"]           = end,
        }, ct);

    /// <summary>Get top-level forums and their subforums.</summary>
    public async Task<List<Forum>> GetForumListingAsync(CancellationToken ct = default)
    {
        var result = await GetAsync<Forums>("forums", ct: ct);
        return result.ForumList;
    }

    /// <summary>Get a forum and its pinned/recent topics.</summary>
    public Task<ForumTopics> GetForumTopicsAsync(int forumId, CancellationToken ct = default)
        => GetAsync<ForumTopics>($"forums/{forumId}", ct: ct);

    // -------------------------------------------------------------------------
    // Friends endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get the friends of the authenticated user (requires Authorization Code grant).</summary>
    public async Task<List<UserRelation>> GetFriendsAsync(CancellationToken ct = default)
        => await GetAsync<List<UserRelation>>("friends", ct: ct);

    // -------------------------------------------------------------------------
    // Home / Search endpoints
    // -------------------------------------------------------------------------

    /// <summary>Search for users and wiki pages.</summary>
    public Task<Search> SearchAsync(
        string query,
        SearchMode? mode     = null,
        int? page            = null,
        CancellationToken ct = default)
        => GetAsync<Search>("search", new()
        {
            ["mode"]  = mode,
            ["query"] = query,
            ["page"]  = page,
        }, ct);

    // -------------------------------------------------------------------------
    // Matches endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get current active matches.</summary>
    public Task<Matches> GetMatchesAsync(CancellationToken ct = default)
        => GetAsync<Matches>("matches", ct: ct);

    /// <summary>Get a specific match by id.</summary>
    public Task<MatchResponse> GetMatchAsync(
        int matchId,
        long? afterId        = null,
        long? beforeId       = null,
        int? limit           = null,
        CancellationToken ct = default)
        => GetAsync<MatchResponse>($"matches/{matchId}", new()
        {
            ["after"]  = afterId,
            ["before"] = beforeId,
            ["limit"]  = limit,
        }, ct);

    // -------------------------------------------------------------------------
    // Me endpoint
    // -------------------------------------------------------------------------

    /// <summary>Get data about the authenticated user (requires Authorization Code grant).</summary>
    public Task<User> GetMeAsync(GameMode? mode = null, CancellationToken ct = default)
    {
        var modeSegment = mode.HasValue ? $"/{FormatValue(mode.Value)}" : "";
        return GetAsync<User>($"me{modeSegment}", ct: ct);
    }

    // -------------------------------------------------------------------------
    // News endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get news posts.</summary>
    public Task<NewsListing> GetNewsListingAsync(
        int? limit           = null,
        int? year            = null,
        string? cursorString = null,
        CancellationToken ct = default)
        => GetAsync<NewsListing>("news", new()
        {
            ["limit"]         = limit,
            ["year"]          = year,
            ["cursor_string"] = cursorString,
        }, ct);

    /// <summary>Get a news post by id or slug.</summary>
    public Task<NewsPost> GetNewsPostAsync(
        string news,
        NewsPostKey? key     = NewsPostKey.Slug,
        CancellationToken ct = default)
    {
        // API: key should be unset to query by slug
        var keyParam = key == NewsPostKey.Slug ? null : key;
        return GetAsync<NewsPost>($"news/{Uri.EscapeDataString(news)}", new()
        {
            ["key"] = keyParam,
        }, ct);
    }

    // -------------------------------------------------------------------------
    // Rankings endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get current rankings for a game mode.</summary>
    public Task<Rankings> GetRankingAsync(
        GameMode mode,
        RankingType type,
        string? country       = null,
        Cursor? cursor        = null,
        RankingFilter filter  = RankingFilter.All,
        int? spotlight        = null,
        string? variant       = null,
        CancellationToken ct  = default)
        => GetAsync<Rankings>($"rankings/{FormatValue(mode)}/{FormatValue(type)}", new()
        {
            ["country"]    = country,
            ["cursor"]     = cursor,
            ["filter"]     = filter,
            ["spotlight"]  = spotlight,
            ["variant"]    = variant,
        }, ct);

    // -------------------------------------------------------------------------
    // Rooms endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get scores on a playlist item in a multiplayer room.</summary>
    public Task<MultiplayerScores> GetMultiplayerScoresAsync(
        int roomId,
        int playlistId,
        int? limit                    = null,
        MultiplayerScoresSort? sort   = null,
        string? cursorString          = null,
        CancellationToken ct          = default)
        => GetAsync<MultiplayerScores>($"rooms/{roomId}/playlist/{playlistId}/scores", new()
        {
            ["limit"]         = limit,
            ["sort"]          = sort,
            ["cursor_string"] = cursorString,
        }, ct);

    /// <summary>Get a multiplayer room.</summary>
    public Task<Room> GetRoomAsync(int roomId, CancellationToken ct = default)
        => GetAsync<Room>($"rooms/{roomId}", ct: ct);

    /// <summary>Get the leaderboard of a multiplayer room.</summary>
    public Task<RoomLeaderboard> GetRoomLeaderboardAsync(
        int roomId,
        int? limit           = null,
        int? page            = null,
        CancellationToken ct = default)
        => GetAsync<RoomLeaderboard>($"rooms/{roomId}/leaderboard", new()
        {
            ["limit"] = limit,
            ["page"]  = page,
        }, ct);

    /// <summary>Get the list of current multiplayer rooms.</summary>
    public async Task<List<Room>> GetRoomsAsync(
        int? limit                = null,
        RoomSearchMode? mode      = null,
        int? seasonId             = null,
        string? sort              = null,
        string? typeGroup         = null,
        CancellationToken ct      = default)
        => await GetAsync<List<Room>>("rooms", new()
        {
            ["limit"]      = limit,
            ["mode"]       = mode,
            ["season_id"]  = seasonId,
            ["sort"]       = sort,
            ["type_group"] = typeGroup,
        }, ct);

    // -------------------------------------------------------------------------
    // Scores endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get a score by its global id (new id format).</summary>
    public Task<Score> GetScoreAsync(long scoreId, CancellationToken ct = default)
        => GetAsync<Score>($"scores/{scoreId}", ct: ct);

    /// <summary>Get most recent 1000 passed scores across all users.</summary>
    public Task<Scores> GetScoresAsync(
        GameMode? mode       = null,
        string? cursorString = null,
        CancellationToken ct = default)
        => GetAsync<Scores>("scores", new()
        {
            ["ruleset"]       = mode,
            ["cursor_string"] = cursorString,
        }, ct);

    /// <summary>Get a score by its mode-specific id (old id format).</summary>
    public Task<Score> GetScoreModeAsync(GameMode mode, long scoreId, CancellationToken ct = default)
        => GetAsync<Score>($"scores/{FormatValue(mode)}/{scoreId}", ct: ct);

    // -------------------------------------------------------------------------
    // Seasonal Backgrounds endpoint
    // -------------------------------------------------------------------------

    /// <summary>Get current seasonal backgrounds.</summary>
    public Task<SeasonalBackgrounds> GetSeasonalBackgroundsAsync(CancellationToken ct = default)
        => GetAsync<SeasonalBackgrounds>("seasonal-backgrounds", ct: ct);

    // -------------------------------------------------------------------------
    // Spotlights endpoint
    // -------------------------------------------------------------------------

    /// <summary>Get active spotlights.</summary>
    public async Task<List<Spotlight>> GetSpotlightsAsync(CancellationToken ct = default)
    {
        var result = await GetAsync<Spotlights>("spotlights", ct: ct);
        return result.SpotlightList;
    }

    // -------------------------------------------------------------------------
    // Tags endpoint
    // -------------------------------------------------------------------------

    /// <summary>Get beatmap tags.</summary>
    public async Task<List<Tag>> GetTagsAsync(CancellationToken ct = default)
    {
        var result = await GetAsync<Tags>("tags", ct: ct);
        return result.TagList;
    }

    // -------------------------------------------------------------------------
    // Users endpoints
    // -------------------------------------------------------------------------

    /// <summary>Get a user by ID or username.</summary>
    /// <param name="user">User ID (int) or username (string).</param>
    /// <param name="mode">Game mode to get stats for. Uses user's default if null.</param>
    /// <param name="key">"id" or "username" to force lookup type. Auto-detected if null.</param>
    public Task<User> GetUserAsync(int user, GameMode? mode = null, string? key = null,
        CancellationToken ct = default)
        => GetUserAsync(user.ToString(), mode, key, ct);

    /// <summary>Get a user by ID or username.</summary>
    public async Task<User> GetUserAsync(string user, GameMode? mode = null, string? key = null,
        CancellationToken ct = default)
    {
        var modeSegment = mode.HasValue ? $"/{FormatValue(mode.Value)}" : "";
        var url = $"users/{Uri.EscapeDataString(user)}{modeSegment}";
        var query = key is not null
            ? new Dictionary<string, object?> { ["key"] = key }
            : null;
        return await GetAsync<User>(url, query, ct);
    }

    /// <summary>Get kudosu history of a user.</summary>
    public async Task<List<KudosuHistory>> GetUserKudosuAsync(
        int userId,
        int? limit           = null,
        int? offset          = null,
        CancellationToken ct = default)
        => await GetAsync<List<KudosuHistory>>($"users/{userId}/kudosu", new()
        {
            ["limit"]  = limit,
            ["offset"] = offset,
        }, ct);

    /// <summary>Get scores of a user.</summary>
    public async Task<List<Score>> GetUserScoresAsync(
        int userId,
        ScoreType type,
        bool? includeFails   = null,
        GameMode? mode       = null,
        int? limit           = null,
        int? offset          = null,
        bool? legacyOnly     = null,
        CancellationToken ct = default)
        => await GetAsync<List<Score>>($"users/{userId}/scores/{FormatValue(type)}", new()
        {
            ["include_fails"] = includeFails.HasValue ? (int?)(includeFails.Value ? 1 : 0) : null,
            ["mode"]          = mode,
            ["limit"]         = limit,
            ["offset"]        = offset,
            ["legacy_only"]   = legacyOnly.HasValue ? (int?)(legacyOnly.Value ? 1 : 0) : null,
        }, ct);

    /// <summary>Get beatmaps associated with a user.</summary>
    public async Task<List<Beatmapset>> GetUserBeatmapsAsync(
        int userId,
        UserBeatmapType type,
        int? limit           = null,
        int? offset          = null,
        CancellationToken ct = default)
        => await GetAsync<List<Beatmapset>>($"users/{userId}/beatmapsets/{FormatValue(type)}", new()
        {
            ["limit"]  = limit,
            ["offset"] = offset,
        }, ct);

    /// <summary>Get most-played beatmaps for a user.</summary>
    public async Task<List<BeatmapPlaycount>> GetUserMostPlayedAsync(
        int userId,
        int? limit           = null,
        int? offset          = null,
        CancellationToken ct = default)
        => await GetAsync<List<BeatmapPlaycount>>($"users/{userId}/beatmapsets/most_played", new()
        {
            ["limit"]  = limit,
            ["offset"] = offset,
        }, ct);

    /// <summary>Get recent activity events for a user.</summary>
    public async Task<List<Event>> GetUserRecentActivityAsync(
        int userId,
        int? limit           = null,
        int? offset          = null,
        CancellationToken ct = default)
        => await GetAsync<List<Event>>($"users/{userId}/recent_activity/", new()
        {
            ["limit"]  = limit,
            ["offset"] = offset,
        }, ct);

    /// <summary>Search for beatmaps a user has passed from a given list of beatmapsets.</summary>
    public async Task<List<BeatmapCompact>> SearchBeatmapsPassedAsync(
        int userId,
        IEnumerable<int> beatmapsetIds,
        bool excludeConverts  = false,
        bool? isLegacy        = null,
        bool noDiffReduction  = true,
        int? rulesetId        = null,
        CancellationToken ct  = default)
    {
        var result = await GetAsync<BeatmapsPassed>($"users/{userId}/beatmaps-passed", new()
        {
            ["beatmapset_ids"]   = beatmapsetIds.ToList(),
            ["exclude_converts"] = excludeConverts ? 1 : 0,
            ["is_legacy"]        = isLegacy.HasValue ? (int?)(isLegacy.Value ? 1 : 0) : null,
            ["no_diff_reduction"] = noDiffReduction ? 1 : 0,
            ["ruleset_id"]       = rulesetId,
        }, ct);
        return result.BeatmapsPassedList;
    }

    /// <summary>Batch-look up users by id or username.</summary>
    public async Task<List<UserCompact>> GetUsersLookupAsync(
        IEnumerable<object> users,
        bool? excludeBots    = null,
        int? rulesetId       = null,
        CancellationToken ct = default)
    {
        var result = await GetAsync<Users>("users/lookup", new()
        {
            ["ids"]          = users.ToList(),
            ["exclude_bots"] = excludeBots.HasValue ? (int?)(excludeBots.Value ? 1 : 0) : null,
            ["ruleset_id"]   = rulesetId,
        }, ct);
        return result.UserList;
    }

    /// <summary>Batch-get users by id.</summary>
    public Task<List<UserCompact>> GetUsersAsync(IEnumerable<int> userIds, CancellationToken ct = default)
        => GetUsersAsync(userIds, includeVariantStatistics: false, ct);

    /// <summary>Batch-get users, optionally including ruleset variant statistics.</summary>
    public async Task<List<UserCompact>> GetUsersAsync(
        IEnumerable<int> userIds,
        bool includeVariantStatistics,
        CancellationToken ct = default)
    {
        var result = await GetAsync<Users>("users", new()
        {
            ["ids"] = userIds.ToList(),
            ["include_variant_statistics"] = includeVariantStatistics ? 1 : 0,
        }, ct);
        return result.UserList;
    }

    // -------------------------------------------------------------------------
    // Wiki endpoint
    // -------------------------------------------------------------------------

    /// <summary>Get a wiki page.</summary>
    public Task<WikiPage> GetWikiPageAsync(string locale, string path, CancellationToken ct = default)
        => GetAsync<WikiPage>($"wiki/{Uri.EscapeDataString(locale)}/{string.Join("/", path.Split('/').Select(Uri.EscapeDataString))}", ct: ct);

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}

public class OssapiException : Exception
{
    public OssapiException(string message) : base(message) { }
}

/// <summary>Poll configuration for <see cref="OssapiClient.ForumCreateTopicAsync"/>.</summary>
public record ForumPollRequest(
    IEnumerable<string> Options,
    string Title,
    bool HideResults  = false,
    int LengthDays    = 0,
    int MaxOptions    = 1,
    bool VoteChange   = false);
