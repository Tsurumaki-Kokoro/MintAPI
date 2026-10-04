using Microsoft.EntityFrameworkCore;
using MintAPI.Models.Entities;

namespace MintAPI.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserModel> Users { get; set; }
    public DbSet<UserOsuInfoHistory> UserOsuInfoHistories { get; set; }

    public DbSet<ScoreHistory> ScoreHistories { get; set; }
    public DbSet<HistoryTaskRun> HistoryTaskRuns { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // UserModel：(platform, platform_uid) 联合唯一索引
        modelBuilder.Entity<UserModel>(entity =>
        {
            entity.HasIndex(u => new { u.Platform, u.PlatformUid })
                  .IsUnique();
        });

        // UserOsuInfoHistory：(osu_uid, date) 复合索引
        modelBuilder.Entity<UserOsuInfoHistory>(entity =>
        {
            entity.HasIndex(h => new { h.OsuUid, h.GameMode, h.Date }).IsUnique();
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
