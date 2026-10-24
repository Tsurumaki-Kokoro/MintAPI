using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintAPI.Data;
using MintAPI.Models.Entities;
using MintAPI.Services;
using MintAPI.Services.PlayerFeatures;
using MintAPI.Tests.TestDoubles;
using MySqlConnector;

namespace MintAPI.Tests.Integration;

public sealed class SchemaMigrationTests
{
    private const string Previous = "20261019032257_AddPlayerFeatures";

    public sealed class MySqlFactAttribute : FactAttribute
    {
        public MySqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MINTAPI_TEST_MYSQL")))
                Skip = "Set MINTAPI_TEST_MYSQL to a disposable MySQL 8 server; tests create and drop isolated databases.";
        }
    }

    private sealed class Database : IAsyncDisposable
    {
        private readonly MySqlConnection _server;
        private readonly string _name = "minttest_" + Guid.NewGuid().ToString("N");
        public AppDbContext Db { get; private set; } = null!;

        public Database() => _server = new(new MySqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("MINTAPI_TEST_MYSQL")!) { Database = "" }.ConnectionString);

        public async Task InitAsync()
        {
            await _server.OpenAsync();
            await new MySqlCommand($"CREATE DATABASE `{_name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", _server).ExecuteNonQueryAsync();
            var connection = new MySqlConnectionStringBuilder(_server.ConnectionString) { Database = _name };
            Db = new(new DbContextOptionsBuilder<AppDbContext>()
                .UseMySql(connection.ConnectionString, ServerVersion.AutoDetect(connection.ConnectionString)).Options);
            await Db.GetService<IMigrator>().MigrateAsync(Previous);
        }

        public async ValueTask DisposeAsync()
        {
            if (Db != null) await Db.DisposeAsync();
            if (_server.State == System.Data.ConnectionState.Open)
                await new MySqlCommand($"DROP DATABASE IF EXISTS `{_name}`", _server).ExecuteNonQueryAsync();
            await _server.DisposeAsync();
        }
    }

    [MySqlFact]
    public async Task Upgrade_preserves_old_data_and_backfills_only_group_identity()
    {
        await using var database = new Database(); await database.InitAsync();
        var db = database.Db;
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO user (osu_uid, game_mode, platform, platform_uid) VALUES ('42', 0, 'qq', '1');
            INSERT INTO user_osu_info_history (osu_uid, game_mode, total_hits, date) VALUES ('42', 0, 123, '2026-10-01');
            INSERT INTO score_history (ScoreId, UserId, BeatmapId, GameMode, EndedAt, Payload)
                VALUES (1, 42, 123, 0, '2026-10-01 01:00:00', '{{}}');
            INSERT INTO player_watch (UserId, GameMode, Revision, FloorCursor, CheckedAt, Payload)
                VALUES (42, 0, 3, 0, '2026-10-01', '{{}}');
            INSERT INTO player_subscription (Id, UserId, GameMode, Scope, StartCursor, ExpiresAt) VALUES
                ('group', 42, 0, ' QQ:group:123 ', 3, '2099-01-01'),
                ('other', 43, 0, 'discord:group:123', 0, '2099-01-01'),
                ('custom', 42, 0, 'private:qq:42', 3, '2099-01-01'),
                ('invalid', 44, 0, 'qq:group:has space', 0, '2099-01-01'),
                ('case', 45, 0, 'qq:GROUP:123', 0, '2099-01-01'),
                ('unicode', 46, 0, 'qq:group:群😀', 0, '2099-01-01');
            """);
        await db.Database.MigrateAsync();
        Assert.Equal(42, (await db.Users.SingleAsync()).OsuUserId);
        var history = await db.UserOsuInfoHistories.SingleAsync();
        Assert.Equal(42, history.OsuUserId); Assert.Equal(123, history.TotalHits);
        Assert.Null(history.ObservedAt); Assert.Null(history.RankedScore); Assert.Null(history.TotalScore);
        Assert.Null((await db.ScoreHistories.SingleAsync()).CollectedAt);
        var group = await db.PlayerSubscriptions.SingleAsync(x => x.Id == "group");
        Assert.Equal("qq", group.Platform); Assert.Equal("123", group.GroupId);
        Assert.Equal(" QQ:group:123 ", group.Scope); Assert.Equal(3, group.StartCursor);
        foreach (var id in new[] { "custom", "invalid", "case" })
        {
            var custom = await db.PlayerSubscriptions.SingleAsync(x => x.Id == id);
            Assert.Null(custom.Platform); Assert.Null(custom.GroupId);
        }
        Assert.Equal("群😀", (await db.PlayerSubscriptions.SingleAsync(x => x.Id == "unicode")).GroupId);
        history.TotalHits = 3_000_000_000;
        history.RankedScore = 4_000_000_000; history.TotalScore = 5_000_000_000;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(3_000_000_000, (await db.UserOsuInfoHistories.SingleAsync()).TotalHits);
        var error = await Assert.ThrowsAsync<MySqlException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Contains("total_hits exceeds int range", error.Message);
        Assert.Equal(6, await db.PlayerSubscriptions.CountAsync());
    }

    [MySqlFact]
    public async Task Downgrade_checks_scope_collisions_before_dropping_columns_and_round_trips_safe_data()
    {
        await using var database = new Database(); await database.InitAsync();
        var db = database.Db; await db.Database.MigrateAsync();
        db.PlayerSubscriptions.AddRange(
            new PlayerSubscription { UserId = 42, Scope = "custom:ABC", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) },
            new PlayerSubscription { UserId = 42, Scope = "custom:abc", ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<MySqlException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
        Assert.Equal(1062, error.Number);
        Assert.Equal(2, await db.PlayerSubscriptions.CountAsync());
        db.PlayerSubscriptions.Remove(await db.PlayerSubscriptions.SingleAsync(x => x.Scope == "custom:abc"));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.GetService<IMigrator>().MigrateAsync(Previous);
        await db.Database.MigrateAsync();
        Assert.Equal("custom:ABC", (await db.PlayerSubscriptions.SingleAsync()).Scope);
    }

    [MySqlFact]
    public async Task Invalid_ID_stops_before_permanent_schema_changes_even_without_strict_mode()
    {
        await using var database = new Database(); await database.InitAsync();
        var db = database.Db;
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = ''; INSERT INTO user (osu_uid, game_mode, platform, platform_uid) VALUES ('not-a-number', 0, 'qq', '1')");
        var error = await Assert.ThrowsAsync<MySqlException>(() => db.Database.MigrateAsync());
        Assert.Contains("Invalid or noncanonical osu_uid", error.Message);
        Assert.DoesNotContain((await db.Database.GetAppliedMigrationsAsync()), x => x.EndsWith("RefineHistoryAndSubscriptions"));
        await db.Database.ExecuteSqlRawAsync("UPDATE user SET osu_uid = '42'");
        await db.Database.MigrateAsync();
        Assert.Equal(42, (await db.Users.SingleAsync()).OsuUserId);
    }

    [MySqlFact]
    public async Task Duplicate_group_aliases_stop_before_changes_and_can_be_retried_after_resolution()
    {
        await using var database = new Database(); await database.InitAsync();
        var db = database.Db;
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO player_subscription (Id, UserId, GameMode, Scope, StartCursor, ExpiresAt) VALUES
                ('one', 42, 0, 'qq:group:123', 0, '2099-01-01'),
                ('two', 42, 0, ' qq:group:123', 1, '2099-01-01');
            """);
        var error = await Assert.ThrowsAsync<MySqlException>(() => db.Database.MigrateAsync());
        Assert.Contains("Duplicate normalized group subscriptions", error.Message);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM player_subscription WHERE Id = 'two'");
        await db.Database.MigrateAsync();
        Assert.Equal("123", (await db.PlayerSubscriptions.SingleAsync()).GroupId);
    }

    [MySqlFact]
    public async Task Group_backfill_matches_scope_parser_for_unicode_whitespace_and_length_limits()
    {
        await using var database = new Database(); await database.InitAsync();
        var db = database.Db;
        var scopes = new[]
        {
            "qq:group:123", " QQ:group: 123 ", "qq:GROUP:123", "qq:group:has space", "custom",
            "qq:group:", "qq:group:群😀", "qq:group:a:group:b", "qq:group:Abc", "qq:group:abc",
            "\u00a0QQ\u3000:group:\u2003123\u0085", "qq:group:a\u0085b", "qq:group:a\u0001b",
            "qq:group:a\u200bb", "qq:group:a\u2028b", "qq:group:a\u007fb", "qq:group:a\u009fb",
            "KK:group:123", "é:group:123", "qq:group:" + new string('界', 80),
            "qq:group:" + new string('界', 81), "qq:group:" + string.Concat(Enumerable.Repeat("😀", 40)),
            "qq:group:" + string.Concat(Enumerable.Repeat("😀", 41)), "qq:group:a\nb"
        };
        for (var i = 0; i < scopes.Length; i++)
        {
            var id = i.ToString(); var uid = i + 100; var scope = scopes[i];
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO player_subscription (Id, UserId, GameMode, Scope, StartCursor, ExpiresAt) VALUES ({id}, {uid}, 0, {scope}, 0, '2099-01-01')");
        }
        await db.Database.MigrateAsync();
        var rows = await db.PlayerSubscriptions.ToDictionaryAsync(x => x.Id);
        for (var i = 0; i < scopes.Length; i++)
        {
            var expected = PlayerWatchGroup.Parse(scopes[i]); var row = rows[i.ToString()];
            Assert.True(expected?.Platform == row.Platform && expected?.GroupId == row.GroupId,
                $"Scope parser mismatch at case {i}: {scopes[i]}");
        }
    }

    [MySqlFact]
    public async Task MySql_group_queries_page_filter_and_preserve_custom_scopes()
    {
        await using var database = new Database(); await database.InitAsync();
        var db = database.Db; await db.Database.MigrateAsync();
        var api = new RecordingOsuApiService
        {
            UserHandler = (id, _) => PlayerFeatureTests.Fixture.User(int.Parse(id)),
            ScoresHandler = (_, _, _, _, _, _, _) => [PlayerFeatureTests.Fixture.Score()]
        };
        var historyOptions = Options.Create(new HistoryOptions());
        var history = new HistoryService(db, api, null!, null!, historyOptions, NullLogger<HistoryService>.Instance);
        var reports = new PlayerReportService(db, api, history, historyOptions);
        var watches = new PlayerWatchService(db, api, reports, Options.Create(new PlayerWatchOptions()), historyOptions);
        var first = await watches.SubscribeAsync("42", 0, "QQ:group:123", default);
        await watches.SubscribeAsync("43", 0, "qq:group:123", default);
        await watches.SubscribeAsync("42", 1, "qq:group:123", default);
        await watches.SubscribeAsync("42", 0, "qq:group:Abc", default);
        await watches.SubscribeAsync("42", 0, "qq:group:abc", default);
        await watches.SubscribeAsync("42", 0, "custom:ABC", default);
        await watches.SubscribeAsync("42", 0, "custom:abc", default);
        Assert.Equal(first.Id, (await watches.SubscribeAsync("42", 0, "qq:group:123", default)).Id);
        var expired = await db.PlayerSubscriptions.SingleAsync(x => x.Id == first.Id);
        expired.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1); await db.SaveChangesAsync();
        Assert.NotEqual(first.Id, (await watches.SubscribeAsync("42", 0, "qq:group:123", default)).Id);
        var group = new PlayerWatchGroup("qq", "123");
        var page = await watches.ListAsync(group, null, null, null, false, 1, 2, default);
        Assert.Equal(3, page.Total); Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, (await watches.GroupsAsync("qq", false, 1, 50, default)).Total);
        Assert.Single((await watches.ListAsync(null, "custom:ABC", null, null, false, 1, 50, default)).Items);
        Assert.Equal(3, await watches.CancelGroupAsync(group, null, null, default));
        Assert.Equal(4, await db.PlayerSubscriptions.CountAsync());
        // The group key is unique independently of the raw legacy scope spelling.
        var sub = await db.PlayerSubscriptions.SingleAsync(x => x.GroupId == "Abc");
        db.PlayerSubscriptions.Add(new PlayerSubscription { UserId = sub.UserId, GameMode = sub.GameMode,
            Platform = sub.Platform, GroupId = sub.GroupId, Scope = "alias", ExpiresAt = sub.ExpiresAt });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
