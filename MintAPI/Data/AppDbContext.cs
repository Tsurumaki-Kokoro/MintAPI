using Microsoft.EntityFrameworkCore;
using MintAPI.Models.Entities;

namespace MintAPI.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserModel> Users { get; set; }
    public DbSet<UserOsuInfoHistory> UserOsuInfoHistories { get; set; }

    public DbSet<ScoreHistory> ScoreHistories { get; set; }
    public DbSet<HistoryTaskRun> HistoryTaskRuns { get; set; }

    public DbSet<BestPlaySnapshot> BestPlaySnapshots { get; set; }
    public DbSet<PlayerWatch> PlayerWatches { get; set; }
    public DbSet<PlayerSubscription> PlayerSubscriptions { get; set; }
    public DbSet<PlayerWatchEvent> PlayerWatchEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var identifierCollation = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite" ? "BINARY" : "utf8mb4_bin";

        modelBuilder.Entity<BestPlaySnapshot>(e =>
        {
            e.ToTable("best_play_snapshot");
            e.HasKey(x => new { x.UserId, x.GameMode, x.Date });
            e.Property(x => x.Payload).HasColumnType("longtext");
        });
        modelBuilder.Entity<PlayerWatch>(e =>
        {
            e.ToTable("player_watch");
            e.HasKey(x => new { x.UserId, x.GameMode });
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.Property(x => x.Payload).HasColumnType("longtext");
        });
        modelBuilder.Entity<PlayerSubscription>(e =>
        {
            e.ToTable("player_subscription");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.Scope).HasMaxLength(128);
            e.Property(x => x.Platform).HasMaxLength(32).UseCollation(identifierCollation);
            e.Property(x => x.GroupId).HasMaxLength(80).UseCollation(identifierCollation);
            e.Property(x => x.Scope).UseCollation(identifierCollation);
            e.Property(x => x.ExpiresAt).HasConversion(x => x.UtcDateTime,
                x => new DateTimeOffset(DateTime.SpecifyKind(x, DateTimeKind.Utc))).IsConcurrencyToken();
            e.HasIndex(x => new { x.Platform, x.GroupId, x.UserId, x.GameMode }).IsUnique();
            e.HasIndex(x => new { x.Scope, x.UserId, x.GameMode });
            e.HasIndex(x => x.ExpiresAt);
            e.HasIndex(x => new { x.UserId, x.GameMode, x.Scope }).IsUnique();
        });
        modelBuilder.Entity<PlayerWatchEvent>(e =>
        {
            e.ToTable("player_watch_event");
            e.HasKey(x => new { x.UserId, x.GameMode, x.Cursor });
            e.Property(x => x.Text).HasColumnType("longtext");
            e.Property(x => x.ObservedAt).HasConversion(x => x.UtcDateTime,
                x => new DateTimeOffset(DateTime.SpecifyKind(x, DateTimeKind.Utc)));
            e.HasIndex(x => new { x.UserId, x.GameMode, x.ObservedAt });
        });

        // UserModel：(platform, platform_uid) 联合唯一索引
        modelBuilder.Entity<UserModel>(entity =>
        {
            entity.HasIndex(u => new { u.Platform, u.PlatformUid })
                  .IsUnique();
        });

        // UserOsuInfoHistory：(osu_uid, game_mode, date) 联合唯一索引
        modelBuilder.Entity<UserOsuInfoHistory>(entity =>
        {
            entity.HasIndex(h => new { h.OsuUserId, h.GameMode, h.Date }).IsUnique();
        });
        modelBuilder.Entity<ScoreHistory>(entity =>
        {
            entity.ToTable("score_history");
            entity.HasKey(s => s.ScoreId);
            entity.Property(s => s.ScoreId).ValueGeneratedNever();
            entity.Property(s => s.Payload).HasColumnType("longtext");
            entity.HasIndex(s => new { s.UserId, s.BeatmapId, s.GameMode, s.EndedAt });
        });
        modelBuilder.Entity<HistoryTaskRun>(entity =>
        {
            entity.ToTable("history_task_run");
            entity.Property(r => r.Job).HasMaxLength(32);
            entity.HasKey(r => new { r.Job, r.Date });
        });
    }
}
