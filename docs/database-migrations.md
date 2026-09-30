# 数据库迁移

服务启动时执行 EF Core 迁移。已创建的空数据库会应用 `InitialCreate`，之后新增的迁移也会依次应用。不要再用 `EnsureCreated` 建库；迁移工具版本由仓库中的 `.config/dotnet-tools.json` 固定：

```sh
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --project HitCircleAPI/HitCircleAPI.csproj --startup-project HitCircleAPI/HitCircleAPI.csproj --output-dir Data/Migrations
dotnet tool run dotnet-ef migrations script --project HitCircleAPI/HitCircleAPI.csproj --startup-project HitCircleAPI/HitCircleAPI.csproj
```

`AppDbContextFactory` 让迁移工具无需启动 API、Redis 或连接实际数据库即可生成迁移。提交迁移文件和模型快照，部署前审查生成的 SQL，并备份数据库。

## 接管旧版 `EnsureCreated` 数据库

旧版数据库已有 `user` 和 `user_osu_info_history` 表，但没有 `__EFMigrationsHistory` 记录。若直接运行初始迁移，建表会与现有表冲突。服务会识别这种状态并停止启动，避免自动覆盖旧表。只需对每个旧数据库执行一次以下步骤：

1. 停止写入服务并备份数据库。
2. 对实际数据库执行 `SHOW CREATE TABLE user;` 和 `SHOW CREATE TABLE user_osu_info_history;`，与 [`InitialCreate`](../HitCircleAPI/Data/Migrations/20260930023304_InitialCreate.cs) 核对表名、列名、类型、可空性、主键与两个索引。若不一致，先处理差异；不要登记基线。
3. 确认 `SELECT * FROM __EFMigrationsHistory;` 尚无记录；如果该表不存在，这是预期情况。随后在**已核对的目标数据库**执行：

```sql
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260930023304_InitialCreate', '9.0.4');
```

4. 查询 `SELECT * FROM __EFMigrationsHistory;`，确认只有该迁移记录，再启动新版服务。此操作仅登记已存在的初始 schema，不修改业务表及其数据。

如果数据库已由 EF 迁移管理，直接正常部署，勿重复执行基线 SQL。
