using HitCircleAPI.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace HitCircleAPI.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserModel> Users { get; set; }
    public DbSet<UserOsuInfoHistory> UserOsuInfoHistories { get; set; }

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
            entity.HasIndex(h => new { h.OsuUid, h.Date });
        });
    }
}
