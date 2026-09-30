# 玩家与成绩历史

`POST /task/update_user_info` 已移除。API 进程内部每天执行资料快照和成绩采集，不再需要外部定时请求。

## 调度配置

在 appsettings 或环境变量中配置 `History`，环境变量例如 `History__TimeZone`。

```json
{
  "History": {
    "Enabled": true,
    "TimeZone": "Asia/Shanghai",
    "InfoTime": "00:00",
    "ScoreTime": "02:00",
    "CatchUpOnStartup": true,
    "ScoreCollectionEnabled": true,
    "RecentLimit": 200,
    "OsuTrackEnabled": true,
    "OsuTrackDefaultDays": 365
  }
}
```

时间采用 `HH:mm`；时区采用系统支持的时区 ID，推荐 IANA ID。成绩采集时间不得早于资料快照时间，可以相同。配置在进程启动时验证，修改后需要重启。

调度每分钟检查一次，以配置时区的日期记录任务完成状态。默认启动时补跑当天已到执行时间的任务，不补造停机期间的历史快照。关闭 `CatchUpOnStartup` 后，当天启动前已错过的任务不会补跑。跨夏令时的重复时间通过每日完成记录防止重复执行；跳过的时间在下一次检查时执行。

MySQL 连接锁防止共享同一数据库的多个实例同时执行历史任务。所有实例应使用相同调度配置。完成记录中的 `FailedTargets` 与日志记录单个玩家/模式失败；失败不会阻止其他玩家或成绩采集，失败目标在下一天再次尝试。整体调度异常会在下一分钟重试。关闭进程会取消正在执行的任务。

资料更新按 osu! ID 去重，保存四个模式每日快照。同一玩家、模式、日期唯一，重复执行保留已保存快照。首次只有一条快照时不判断活跃；之后对比当天与此前最近一次快照的 PlayCount。只采集有增长的模式。成绩请求包含失败与 Lazer/Stable，每次最多 100 条分页拉取，总上限 `RecentLimit`（1–1000），按成绩 ID 去重。

只保存 Graveyard、WIP、Pending 等无官网排行榜谱面的成绩。Ranked、Approved、Qualified、Loved 使用官网成绩查询。历史不自动清理。最近成绩接口只能提供有限记录，每日采集不能保证涵盖所有历史尝试。

## 历史查询

以下接口沿用 `access_token` API Key 请求头及平台绑定身份。

### PP / 全球排名趋势

```text
GET /user_info/history?platform=qq&platform_uid=123&game_mode=0&days=30&format=json
GET /user_info/history?platform=qq&platform_uid=123&days=30&format=png
```

`game_mode` 默认为绑定模式；`days` 为 0–3650，0 返回全部本地历史，并以 `OsuTrackDefaultDays` 指定外部查询范围。JSON 提供玩家、模式、来源及按日期排列的数据点，每点包含 Date、Pp、GlobalRank、Source（HTTP JSON 为 camelCase）。排名缺失/为零或 PP 缺失的数据点不用于趋势。没有数据返回 404。

osu!track 查询只读，每次最多等待 10 秒，成功/404 缓存 10 分钟。相同日期本地优先，外部异常写日志并使用本地数据。不存在的数据不会补零或写回历史表。

### 指定谱面的成绩列表

```text
GET /score/history?platform=qq&platform_uid=123&beatmap_id=456&format=json
GET /score/history?platform=qq&platform_uid=123&beatmap_id=456&mods=HD,HR&page=1&page_size=20&format=png
```

支持 `game_mode`、`legacy_only`、`mods`、`page`、`page_size`。Mod 组合精确匹配，忽略 CL，NM 表示无 Mod。默认兼容 Lazer/Stable，`legacy_only=true` 按 LegacyScoreId 筛选 Stable 成绩。页码从 1 开始，每页 1–50 条，默认 20。结果按成绩时间倒序。JSON 包含来源、说明、总数、分页及完整成绩；PNG 提供分页列表，时间显示 UTC。

有榜谱面返回官网当前保留的各 Mod 最佳成绩，不能视为所有历史尝试。无榜谱面返回本地已采集记录，空列表或超出页码返回 404。现有 `GET /score/user_score` 同样使用该来源规则，无榜时渲染本地最近一条成绩。

## 数据库升级

应用启动时自动应用 `AddScheduledHistory` 迁移，新增 `score_history` 和 `history_task_run`，并为资料历史建立玩家/模式/日期唯一索引。旧历史表存在重复快照时，同组仅保留 ID 最大的一条。升级前按现有运维流程备份数据库。

既有资料卡的日期对比采用配置时区，并按实际查询到的 osu! 玩家寻找历史数据。
