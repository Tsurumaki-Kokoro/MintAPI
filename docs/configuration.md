# 运维配置

配置由 `appsettings.json`、环境配置文件、环境变量和命令行参数叠加。修改以下配置后重启服务生效，不提供运行时热更新。环境变量使用双下划线，例如 `Downloads__TimeoutSeconds=30`。

## 统一路径

| 键 | 默认值 | 说明 |
|---|---|---|
| `CacheDir` | `cache` | 资源下载与预览缓存目录 |
| `Storage:LogDirectory` | `logs` | 文件日志与 `/task/pack_logs` 共用 |
| `Storage:TokenCacheDirectory` | `null` | 未指定、为 null 或空字符串时沿用程序目录下 `token_cache`；可显式指定目录 |

相对路径以应用 **ContentRootPath** 为基准；绝对路径原样使用。随附资源按程序目录定位；浏览器目录通过 `PLAYWRIGHT_BROWSERS_PATH` 设置。

缓存、日志、令牌目录不允许相同或互相嵌套。清缓存删除谱面文件、预览下载和产物、头像、Banner、徽章，以及 `user` 缓存；保留用户上传到 `user_bg` 的背景。不会清除 Redis 搜索结果或 OAuth 令牌。

迁移存储位置时可使用绝对路径保留旧目录。更改令牌目录会重新获取 OAuth 令牌。

## 下载超时和下载源

| `Downloads` 下的键 | 默认值 | 用途 |
|---|---|---|
| `TimeoutSeconds` | 100 | 头像、徽章、谱面文件、背景下载的总超时；每个回退源独立计时 |
| `BannerTimeoutSeconds` | 8 | 用户 Banner |
| `ListCoverTimeoutSeconds` | 5 | 成绩列表、BP 分析列表封面 |
| `SearchCoverTimeoutSeconds` | 8 | 搜索图片封面 |
| `OsuTrackTimeoutSeconds` | 10 | osu!track 请求 |
| `MaxSearchCoverBytes` | 4194304 | 搜索封面下载上限，单位字节 |
| `OsuTrackBaseUrl` | `https://osutrack-api.ameo.dev/` | 要求末尾 `/`，接口路径由服务追加 |
| `BeatmapSources` | 官方 osu! → osu.direct | `.osu` 下载源数组，依次尝试；使用 `{beatmapId}` |
| `BackgroundSources` | osu.direct → Nerinyan → Sayobot | 背景下载源数组，依次尝试 |
| `CoverUrlTemplate` | 官方 `cover@2x.jpg` | 列表封面和谱面集背景；也是搜索封面缺失时的回退地址 |
| `SeasonalBackgroundsUrl` | 官方 seasonal-backgrounds | 背景全部失败后的季节背景列表 |
| `SearchCoverAllowedHosts` | `["assets.ppy.sh"]` | 搜索渲染只下载白名单中的 HTTPS 图片 |

完整默认 URL 见主配置文件。多层配置文件和环境变量仍遵循 .NET 的逐索引合并规则：只覆盖索引 0 不会移除较低配置层中的索引 1；缩短列表时应编辑主配置文件并删除多余项。模板支持 `{beatmapId}`、`{setId}`、`{backgroundName}`；背景文件名会作为 URL 路径部分编码。地址必须是 HTTPS，不得含用户凭据或 fragment；未知占位符会导致启动校验失败。下载超时范围为 1–86400 秒，搜索封面大小范围为 1–1073741824 字节。

搜索渲染优先使用 osu! API 返回的封面 URL；`CoverUrlTemplate` 仅在返回 URL 缺失时使用。如果更换封面域名，也要更新 `SearchCoverAllowedHosts`。用户 Banner 仅允许官方域名。

这些设置控制资源下载和 osu!track，不改变 MintOsuApi 的 osu! v2/OAuth 请求超时或协议版本。

## 缓存有效期

| `CachePolicy` 下的键 | 默认值 | 单位 |
|---|---|---|
| `AvatarHours`、`BannerHours` | 各 24 | 小时 |
| `SearchSeconds` | 120 | 秒 |
| `SearchCoverMinutes` | 30 | 分钟 |
| `OsuTrackMinutes` | 10 | 分钟 |
| `BeatmapFileHours`、`BackgroundHours`、`BadgeHours`、`ListCoverHours`、`PreviewArtifactHours` | 各 0 | 小时；0 表示保持永久缓存 |

只有上述五种磁盘缓存支持 0。其他期限必须为正数；所有期限上限为约 10 年。磁盘缓存到期后在下一次请求时刷新，不运行定时删除任务，不限制磁盘总容量。头像和 Banner 保留下载失败时读取旧缓存的行为。用户上传的背景不使用 TTL。

资源下载源或超时变化不会主动清除旧缓存；需要立即更新资源时使用清缓存接口。

## 并发和比赛状态

| 键 | 默认值 | 说明 |
|---|---|---|
| `Concurrency:PpCalculation` | 4 | BP 修正、BP 分析的并行计算数量 |
| `Concurrency:CoverDownload` | 4 | 成绩列表、BP 分析、搜索图片的并行封面下载数量 |
| `MatchLive:PollConcurrency` | 2 | 单轮比赛轮询的并行数量 |
| `MatchLive:StateTtlHours` | 48 | Redis 比赛状态 TTL，每次保存刷新 |

并发项范围 1–64。计算和封面限制作用于单次处理任务，不是进程级全局资源池；`Rendering:MaxConcurrency` 和 `BeatmapPreview:MaxConcurrency` 控制对应服务的进程级并发，osu! API 限流另行生效。

`StateTtlHours` 与 `RetentionMinutes` 分别控制 Redis 恢复数据和无订阅房间的内存保留，不应混用；TTL 范围 1–87600 小时，应覆盖希望在服务停机后恢复的时间窗口。

## 日志策略

`Logging:LogLevel` 支持 Trace、Debug、Information、Warning、Error、Critical、None；`Default` 为全局级别，其他键按日志来源覆盖。

| `LogFiles` 下的键 | 默认值 | 说明 |
|---|---|---|
| `Enabled` | true | 文件日志开关 |
| `ConsoleEnabled` | true | 控制台日志开关 |
| `FileName` | `.log` | 只允许文件名；目录由 `Storage:LogDirectory` 决定 |
| `RollingInterval` | `Day` | Infinite、Year、Month、Day、Hour、Minute |
| `RetainedFileCountLimit` | 31 | 保留文件数量，必须为正数 |
| `FileSizeLimitBytes` | 1073741824 | 单文件大小限制，必须为正数 |
| `RollOnFileSizeLimit` | false | 达到大小限制后是否立即滚动；false 时达到上限后停止写入当前文件 |
| `OutputTemplate` | 见 `appsettings.json` | 日志行格式，包含时间、级别、TraceId、消息和异常 |

若需达到文件大小后继续写新文件，设置 `RollOnFileSizeLimit=true`。不要同时通过 `Serilog:WriteTo` 重复注册相同的 Console/File sink。

停止文件日志不会删除既有日志，导出接口仍可导出目录中的历史文件。

## 玩家周报与动态订阅

`History.BestSnapshotEnabled` 控制每日四模式 BP 快照，默认启用。`PlayerWatch.Enabled` 默认 true，`PollSeconds` 默认 300（60–86400），`SubscriptionDays` 默认 7（1–90），`MaxTrackedUsers` 默认 100（1–1000）。环境变量采用 `PlayerWatch__PollSeconds` 等命名。修改后重启生效；只有活跃订阅才轮询，共用 osu! 全局限流额度。接口与统计口径见 [玩家功能](player-features.md)。

## 预编译包与服务配置

安装器把配置保存在独立数据目录，通过 `MINTAPI_CONFIG_FILE` 指向该 JSON 文件；该文件覆盖内置 appsettings，环境变量与命令行仍优先。原生向导生成随机 API Key 并分别询问数据库、缓存来源；密码隐藏输入，配置与备份受文件权限限制。应用版本更新不覆盖该文件。包内存在 `browsers/` 时默认设置 Playwright 浏览器目录，显式 `PLAYWRIGHT_BROWSERS_PATH` 仍优先。详见 [部署文档](deployment.md)。
