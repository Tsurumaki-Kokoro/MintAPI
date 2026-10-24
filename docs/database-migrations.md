# 数据库初始化与迁移

供部署维护者和数据库开发者使用。

服务启动时执行 EF Core 迁移。默认数据库名称为 `mintapi`。已创建的空数据库会应用 `InitialCreate`，之后新增的迁移也会依次应用。不要再用 `EnsureCreated` 建库；迁移工具版本由仓库中的 `.config/dotnet-tools.json` 固定：

```sh
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --project MintAPI/MintAPI.csproj --startup-project MintAPI/MintAPI.csproj --output-dir Data/Migrations
dotnet tool run dotnet-ef migrations script --project MintAPI/MintAPI.csproj --startup-project MintAPI/MintAPI.csproj
```

`AppDbContextFactory` 让迁移工具无需启动 API、Redis 或连接实际数据库即可生成迁移。提交迁移文件和模型快照，部署前审查生成的 SQL，并备份数据库。

## MintAPI 初始化

默认数据库名为 `mintapi`。对全新的数据库，先创建空库，然后让服务启动时应用现有迁移；无需删除或重新生成迁移文件。

```sql
CREATE DATABASE `mintapi` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

将本地 `MintAPI/appsettings.Development.json` 或部署环境的 `ConnectionStrings__DefaultConnection` 指向该数据库。也可通过环境变量给 EF 工具提供实际连接，再执行：

```sh
dotnet tool run dotnet-ef database update --project MintAPI/MintAPI.csproj --startup-project MintAPI/MintAPI.csproj
```

设计时工厂默认使用占位凭据，仅供生成迁移；执行数据库操作时需要设置实际连接。重置已有数据库前应停止写入并备份，再删除、重建目标库和应用迁移。服务启动不会删除已有数据库。

初始化后包含 `user`、`user_osu_info_history`、`score_history`、`history_task_run`、`best_play_snapshot`、`player_watch`、`player_subscription`、`player_watch_event` 八张业务表，以及 `__EFMigrationsHistory` 迁移记录表。

## 接管旧版 `EnsureCreated` 数据库

旧版数据库已有 `user` 和 `user_osu_info_history` 表，但没有 `__EFMigrationsHistory` 记录。若直接运行初始迁移，建表会与现有表冲突。服务会识别这种状态并停止启动，避免自动覆盖旧表。只需对每个旧数据库执行一次以下步骤：

1. 停止写入服务并备份数据库。
2. 对实际数据库执行 `SHOW CREATE TABLE user;` 和 `SHOW CREATE TABLE user_osu_info_history;`，与 [`InitialCreate`](../MintAPI/Data/Migrations/20260930023304_InitialCreate.cs) 核对表名、列名、类型、可空性、主键与两个索引。若不一致，先处理差异；不要登记基线。
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

`AddPlayerFeatures` 新增每日 BP 快照、玩家动态观测、订阅和纯文本事件四张表。旧表不回填或改写历史；新周报的 BP 数据从部署后的定时采集开始积累。迁移不会发送历史 BP 提醒。

## 历史与订阅结构升级

`RefineHistoryAndSubscriptions` 保留已有迁移，按顺序升级八张业务表，不要求重建数据库：

- `user.osu_uid`、`user_osu_info_history.osu_uid` 改为 `int`，与其他业务表和 osu! 客户端的用户 ID 类型一致；平台账号和群号仍为字符串。现有 HTTP 字段名及 ID 的 JSON 类型保持不变。
- `total_hits` 扩为可空 `bigint`。日统计新增可空的 `ranked_score`、`total_score`、`observed_at`；成绩历史新增可空的 `CollectedAt`。新增时间记录实际观测或首次收录时刻，按 UTC 存储；日统计的 `date` 仍按 `History.TimeZone` 归档。旧记录的新增字段保持 `NULL`，不根据游玩日期或迁移时间回填。
- 订阅新增可空的 `Platform`、`GroupId`，从符合现有群 Scope 规则的数据提取。原始 `Scope`、订阅 ID、游标和到期时间保留；自定义 Scope 不强制转成群订阅。平台统一为小写，群号与自定义 Scope 按大小写区分。应用写入时同步生成群字段，直接写数据库也必须保持它们与 Scope 一致。
- 新索引用于群查询及唯一性、Scope 查询、到期筛选和事件保留期清理。列表、群统计、分页和到期筛选由数据库执行；查询不会续期，也不会请求 osu!。

升级前停止旧版本的写入并备份。迁移在修改永久表之前检查两类冲突：

1. **`Invalid or noncanonical osu_uid`**：两个旧表存在空值文本、非数字、前导零、非正数或超过 `2147483647` 的 ID。先查明对应玩家并修正；不要直接把异常值转成零。
2. **`Duplicate normalized group subscriptions`**：同一玩家、模式、平台和群有多个 Scope 别名。检查 `player_subscription` 的 `Id`、`Scope`、`StartCursor` 和 `ExpiresAt`，明确保留哪条订阅后再处理冲突。迁移不会自动删除或合并订阅。

异常会以临时校验表的重复键错误显示上述原因；修正数据后可重新启动服务或执行 `database update`。迁移需要临时表权限，以及支持 `REGEXP_REPLACE` 的 MySQL 8 / MariaDB 服务。

回退到旧结构会删除新增列；应优先从备份恢复。若 `total_hits` 已超过旧 `int` 范围，或 Scope 在旧排序规则下发生唯一键冲突，回退迁移会在改表前拒绝执行，避免截断数据或部分回退。
