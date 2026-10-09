# 运维配置

配置由 `appsettings.json`、环境配置文件、环境变量和命令行参数叠加。修改以下配置后重启服务生效，不提供运行时热更新。环境变量使用双下划线，例如 `Downloads__TimeoutSeconds=30`。

## 统一路径

| 键 | 默认值 | 说明 |
|---|---|---|
| `CacheDir` | `cache` | 保留已有配置键，下载服务、预览和清缓存接口共用 |
| `Storage:LogDirectory` | `logs` | 文件日志与 `/task/pack_logs` 共用 |
| `Storage:TokenCacheDirectory` | `null` | 未指定或为 null 时沿用程序目录下 `token_cache`；可显式指定目录 |

相对路径以应用 **ContentRootPath** 为基准；绝对路径原样使用。程序随附的模板、字体、图标和预览 CLI 仍按程序目录定位。浏览器路径没有新增配置。

缓存、日志、令牌目录不允许相同或互相嵌套。原来日志打包和部分清缓存操作固定使用程序目录，现在会使用实际存储目录。清缓存删除谱面文件、预览下载和产物、头像、Banner、徽章，以及原有规则覆盖的旧版 `user` 缓存；保留用户上传到 `user_bg` 的背景。不会清除 Redis 搜索结果或 OAuth 令牌。

若以前工作目录与 ContentRootPath 不同，先确认旧缓存和日志所在位置，可用绝对路径继续使用旧目录。令牌默认目录不变；改变令牌目录会重新取得 OAuth 令牌。

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

完整默认 URL 见主配置文件。源数组替换 Options 类的默认数组，而非在默认源后追加。多层配置文件和环境变量仍遵循 .NET 的逐索引合并规则：只覆盖索引 0 不会移除较低配置层中的索引 1；缩短列表时应编辑主配置文件并删除多余项。模板支持 `{beatmapId}`、`{setId}`、`{backgroundName}`；背景文件名会作为 URL 路径部分编码。地址必须是 HTTPS，不得含用户凭据或 fragment；未知占位符会导致启动校验失败。下载超时范围为 1–86400 秒，搜索封面大小范围为 1–1073741824 字节。

搜索渲染优先使用 osu! API 返回的封面 URL；`CoverUrlTemplate` 仅在返回 URL 缺失时使用。如果更换封面域名，也要更新 `SearchCoverAllowedHosts`。用户 Banner 原有的官方域名限制继续保留。

这些设置控制资源下载和 osu!track，不改变 MintOsuApi 的 osu! v2/OAuth 请求超时或协议版本。

## 缓存有效期

| `CachePolicy` 下的键 | 默认值 | 单位 |
|---|---|---|
| `AvatarHours`、`BannerHours` | 各 24 | 小时 |
| `SearchSeconds` | 120 | 秒 |
| `SearchCoverMinutes` | 30 | 分钟 |
| `OsuTrackMinutes` | 10 | 分钟 |
| `BeatmapFileHours`、`BackgroundHours`、`BadgeHours`、`ListCoverHours`、`PreviewArtifactHours` | 各 0 | 小时；0 表示保持永久缓存 |

只有上述五种原本永久缓存支持 0。其他期限必须为正数；所有期限上限为约 10 年。磁盘缓存到期后在下一次请求时刷新，不运行定时删除任务，不限制磁盘总容量。头像和 Banner 保留下载失败时读取旧缓存的行为。用户上传的背景不使用 TTL。

资源下载源或超时变化不会主动清除旧缓存；需要立即更新资源时使用清缓存接口。新的缓存下载采用临时文件后替换，避免刷新过程中读到不完整文件。

## 并发和比赛状态

| 键 | 默认值 | 说明 |
|---|---|---|
| `Concurrency:PpCalculation` | 4 | BP 修正、BP 分析的并行计算数量 |
| `Concurrency:CoverDownload` | 4 | 成绩列表、BP 分析、搜索图片的并行封面下载数量 |
| `MatchLive:PollConcurrency` | 2 | 单轮比赛轮询的并行数量 |
| `MatchLive:StateTtlHours` | 48 | Redis 比赛状态 TTL，每次保存刷新 |

新并发项范围 1–64。计算和封面限制作用于单次处理任务，不是进程级全局资源池；现有 `Rendering:MaxConcurrency` 和 `BeatmapPreview:MaxConcurrency` 仍负责对应渲染服务的进程级并发。已有的 osu! API 限流配置保持生效。

`StateTtlHours` 与 `RetentionMinutes` 分别控制 Redis 恢复数据和无订阅房间的内存保留，不应混用；TTL 范围 1–87600 小时，应覆盖希望在服务停机后恢复的时间窗口。

## 日志策略

日志级别从 `Logging:LogLevel` 明确映射到 Serilog，支持 Trace、Debug、Information、Warning、Error、Critical、None；`Default` 是全局阈值，其他键为来源前缀覆盖。现有环境配置中的 Debug 设置现在会实际生效。

| `LogFiles` 下的键 | 默认值 | 说明 |
|---|---|---|
| `Enabled` | true | 文件日志开关 |
| `ConsoleEnabled` | true | 控制台日志开关 |
| `FileName` | `.log` | 只允许文件名；目录由 `Storage:LogDirectory` 决定 |
| `RollingInterval` | `Day` | Infinite、Year、Month、Day、Hour、Minute |
| `RetainedFileCountLimit` | 31 | 保留文件数量，必须为正数 |
| `FileSizeLimitBytes` | 1073741824 | 单文件大小限制，必须为正数 |
| `RollOnFileSizeLimit` | false | 达到大小限制后是否立即滚动；false 沿用原默认行为 |
| `OutputTemplate` | 原有模板 | 默认保留时间、级别、TraceId、消息和异常 |

默认文件保留数量和大小采用原 Serilog 文件 sink 的默认值。若需达到文件大小后继续写新文件，设置 `RollOnFileSizeLimit=true`。不要同时通过 `Serilog:WriteTo` 重复注册相同的 Console/File sink。

日志导出使用唯一临时 ZIP 文件，避免同时请求共用 `logs.zip`。停止文件日志不会删除既有日志，导出接口仍可导出目录中的历史文件。
