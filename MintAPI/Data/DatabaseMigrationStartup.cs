using Microsoft.EntityFrameworkCore;

namespace MintAPI.Data;

public static class DatabaseMigrationStartup
{
    public static async Task MigrateAsync(AppDbContext db)
    {
        var applied = await db.Database.GetAppliedMigrationsAsync();
        if (!applied.Any() && await LegacyTablesExistAsync(db))
        {
            throw new InvalidOperationException(
                "检测到由 EnsureCreated 创建、尚未登记 EF 迁移的旧数据库。" +
                "请先按 docs/database-migrations.md 核对 schema 并登记 InitialCreate，再启动服务。");
        }

        await db.Database.MigrateAsync();
    }

    private static async Task<bool> LegacyTablesExistAsync(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = DATABASE()
                  AND table_name IN ('user', 'user_osu_info_history')
                """;
            return Convert.ToInt32(await command.ExecuteScalarAsync()) > 0;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
