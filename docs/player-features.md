# 个人周报、同图对比与玩家动态

三个功能只在 MintAPI 提供能力，沿用 `access_token` API Key 请求头。本文不包含机器人命令或消息投递实现。

## 个人周报

```text
GET /user_info/weekly?platform=qq&platform_uid=123
GET /user_info/weekly?platform=qq&platform_uid=123&game_mode=0&week_start=2026-10-12
```

成功返回 `image/png`。`game_mode` 为 0–3，省略使用绑定模式；`week_start` 必须为已结束自然周的周一（2000 年起），默认上一完整自然周。使用 `History.TimeZone`，默认 Asia/Shanghai。

周报采用已确认的模板：玩家 banner 渐变头部、PP / 排名 / 活动概览、每日 PP 曲线、带谱面背景和整体小圆角的“本周新增 BP”列表。新增 BP 显示最多三条，注明总数，保留原 BP 位置。背景和头像由现有缓存服务获取；可选背景失败时回退纯色。

统计范围为周一至下周一，不含结束边界。资料增量使用这两个日期的快照，缺少任一边界则对应增量为“—”；不拿当前资料代替历史期末资料。活跃天数要求八个连续边界的 PlayCount 完整；缺口不补零，PP 曲线在缺失日期断开。计数或时长倒退不显示为正增长。

新增 BP 来自下周一的前 100 BP 快照，仅列出成绩发生时间属于本周、且期末仍在前 100 的成绩。不是本周所有曾进入榜单的成绩，也不等于 BP 位置净变化。没有期末 BP 快照时显示未知；有期末快照且没有本周成绩时显示零条。每日采集时间及补跑延迟会使观测时间偏离午夜，图片明确显示期末 BP 的实际观测时间（UTC）。重算后的 PP 不会回写旧快照。

`History.BestSnapshotEnabled` 默认为 true，历史调度在资料任务后、`InfoTime` 到时采集已绑定玩家四模式的前 100 BP，按玩家、模式、日期唯一保存。即使玩家不活跃也保存榜单，每个绑定玩家每天增加最多四次官方请求。只补跑当天，不伪造安装前或停机期间的快照。因此首次部署时，完整周报需要等积累到周边界；只存在部分资料时仍可生成部分周报，没有任何该周资料或期末 BP 则返回 `HISTORY_NOT_FOUND`。

## 同图成绩对比

```text
GET /score/compare?left_user=42&right_user=43&beatmap_id=657917
GET /score/compare?left_user=42&right_user=43&beatmap_id=657917&game_mode=0&mods=HD,HR&legacy_only=true
```

成功返回 `image/png`。双方可传 osu! ID 或用户名，`beatmap_id` 是具体难度 ID。`game_mode` 默认 0，支持 0–3；`mods` 精确匹配缩写组合、忽略 CL，NM 为无 Mod。`legacy_only=true` 仅 Stable，false 仅 Lazer，省略兼容两者。

从各自匹配成绩中，先取有 PP 且 PP 最大者，再按 API 总分、成绩时间倒序选择。有榜谱面使用官网各 Mods 当前最佳；无榜谱面使用本地已收录尝试。任一方没有匹配成绩返回 `SCORE_NOT_FOUND`；任一方无榜且本地完全未收录返回 `LOCAL_SCORE_NOT_COLLECTED`，不能解释为该玩家没玩过。

顶部使用共享谱面背景，双方身份区不使用各自 banner。四模式分别展示自己的判定字段。只有 Mods（含设置）一致、客户端一致，且 Lazer Build 已知并相同时，才显示优胜高亮和差值。不同条件仍可查看数值与条件，但不作胜负判断。最大连击和 Miss 不等于整体实力；Acc 差值用百分点。总分明确为上游 API `total_score`，不混用经典分。

## 新 BP / 排名突破纯文本提醒

### 1. 创建订阅

```http
POST /players/watch/subscriptions
Content-Type: application/json

{"user":"42","platform":"qq","groupId":"123","gameMode":0}
```

返回示例：

```json
{"id":"32位订阅ID","userId":42,"gameMode":0,"cursor":1,"expiresAt":"2026-10-26T00:00:00Z"}
```

`user` 是 osu! ID 或用户名，`gameMode` 默认 0，范围 0–3。群订阅直接传 `platform`（1–32 位 ASCII 字母、数字、下划线或短横线）和 `groupId`（1–80 字符，不含空白或控制字符），两者必须同时提供；平台忽略大小写，群 ID 按文本区分。相同平台、群、玩家和模式续期并返回原订阅 ID，不同群和平台独立管理，同一玩家/模式仍共享后台查询。

兼容旧的 `scope`（1–128 字符）输入。例如 `qq:group:123` 会被识别为 qq 群 123，无需数据库迁移。新格式内部沿用这个稳定 scope 表达，不依赖调用方自行拼接。其他任意 scope 保留普通订阅，通过 scope 或 ID 查询，不会被猜测为某个群。若同时传群身份和 scope，scope 必须与规范群 scope 一致，否则 400。

首次只建立基线，不推送之前的 BP。首次返回的 cursor 是本次订阅起点，续订不应覆盖调用方已经成功消费的 cursor。

### 2. 增量读取

```text
GET /players/watch/subscriptions/{id}/updates?after=1
GET /players/watch/subscriptions/{id}/updates?after=1&format=text
```

默认 JSON 提供 `cursor`、`checkedAt`、`expiresAt`、`hasMore` 和 `updates`；每条 update 包含 `cursor`、`observedAt`、`text`。`text` 即可直接发送的纯文本，提醒不生成图片。

`format=text` 直接返回 `text/plain; charset=utf-8`，多条事件用空行分隔，无变化返回空文本。响应头 `X-PlayerWatch-Cursor` 和 `X-PlayerWatch-Has-More` 用于续取。读取成功续期；只应在成功投递后保存 cursor。相同 cursor 的重读会重放同一批事件，用于投递失败重试；发送端可用订阅 ID + 事件 cursor 去重。

每页最多 100 个观测事件。`hasMore=true` 时继续从返回 cursor 读取；同一次观测的多个新 BP 合并一条文本，最多展开十条并显示其余数量。JSON 的 `checkedAt` 可以判断上游采集是否滞后。

识别范围：

- 新成绩：相对上一观测新增的成绩 ID、成绩发生于上一观测之后、进入当前前 100 BP。旧成绩因 PP 重算或退榜后重新进入，不报为新 BP。两次轮询间进入又退出前 100 的成绩无法保证捕获。
- 排名里程碑：从更大排名跨入前 100000、50000、25000、10000、5000、1000、500、100、50、10、1；一次跨越多个门槛显示最优门槛。未知排名不用于推断突破，不声称历史首次。
- PP 里程碑：总 PP 跨过新的整千门槛。

总 PP 和排名变化是相邻观测差值，不全部归因于某条成绩。排名下降显示向下方向，未知数据为“—”。服务端仅提供事件，不向 QQ、Discord 等平台发消息。

### 3. 按群分组与查询订阅

```text
GET /players/watch/groups
GET /players/watch/groups?platform=qq
GET /players/watch/subscriptions?platform=qq&group_id=123
GET /players/watch/subscriptions?platform=qq&group_id=123&user_id=42&game_mode=0
GET /players/watch/subscriptions?scope=qq%3Agroup%3A123
GET /players/watch/subscriptions/{id}?platform=qq&group_id=123
```

`groups` 返回按 `platform + groupId` 聚合的群列表，每组含 `subscriptionCount` 和去重后的 `userCount`；同玩家的不同模式分别计订阅数，但只计一个玩家。可按平台筛选，不包含任意非群 scope。

订阅列表可按完整群身份、osu! 数字 `user_id`、`game_mode` 或精确 `scope` 筛选；不允许同时传群筛选和 scope。省略所有条件可分页查看全部订阅。群筛选必须同时给出 `platform` 和 `group_id`，单独一个参数、空值或非法值返回 400，避免错误扩大查询或取消范围。

两种列表均支持 `page`（默认 1）、`page_size`（默认 50，1–100），返回 `page`、`pageSize`、`total`、`items`。无匹配或页码超过结果范围时，成功返回空列表。默认只列有效订阅；`include_expired=true` 包含数据库中尚未清理的过期记录，不会恢复已删除记录。

订阅条目包括 `id`、`userId`、最近已知 `username`、`gameMode`、`scope`、`platform`、`groupId`、`startCursor`、`expiresAt`、`isExpired`、`checkedAt`。查询不调用 osu!，不续期，不推进消费游标。`startCursor` 是订阅初始游标，不是机器人已经投递到的游标；客户端仍须保存投递进度。普通非群 scope 的 `platform` / `groupId` 为 null。

单条查询只返回有效订阅，取消/过期返回 410。可同时传平台和群号校验归属，属于其他群时返回 `404 / RECORD_NOT_FOUND`。

### 4. 取消订阅

```text
# 按 ID 取消一条，兼容原接口
DELETE /players/watch/subscriptions/{id}

# 校验群归属后取消一条
DELETE /players/watch/subscriptions/{id}?platform=qq&group_id=123

# 取消该群指定玩家的指定模式
DELETE /players/watch/subscriptions?platform=qq&group_id=123&user_id=42&game_mode=0

# 取消该群指定玩家的所有模式
DELETE /players/watch/subscriptions?platform=qq&group_id=123&user_id=42

# 取消该群全部订阅
DELETE /players/watch/subscriptions?platform=qq&group_id=123
```

按 ID 取消成功 204，取消/过期返回 410；指定了群但归属不符返回 404。按群取消返回 `200` 和 `{"removed":实际删除数}`，零匹配也成功，可重复调用。按群取消强制完整群身份，不支持省略群身份删除全库，也不会影响其他平台或其他群。

按群取消包括尚未清理的过期记录，`user_id` / `game_mode` 进一步收窄范围。同一玩家在其他群仍有有效订阅时继续轮询；最后一个有效订阅消失后停止查询。其他群的订阅 ID、基线和增量读取不受影响。重新订阅不活跃玩家会重新建立基线。

### 配置与持久化

```json
{
  "History": { "BestSnapshotEnabled": true },
  "PlayerWatch": {
    "Enabled": true,
    "PollSeconds": 300,
    "SubscriptionDays": 7,
    "MaxTrackedUsers": 100
  }
}
```

后台每 30 秒检查一次，到达 `PollSeconds`（60–86400 秒）后才查询玩家；默认约五分钟更新，遇到全局官方限流时可能更慢。每次轮询最多两个官方请求，共用既有额度。`SubscriptionDays`（1–90 天）也决定事件重放保留期。默认最多追踪 100 个玩家/模式组合，总活跃 scope 上限为其十倍。多实例通过观测版本的数据库并发令牌保证不会重复提交同一批事件。

订阅、基线、事件存入数据库，重启保留。上游失败不覆盖原基线；请求取消正常传播，资源压力稍后重试。禁用 PlayerWatch 后不进行后台查询，创建/续订返回带 Retry-After 的 503，既有留存事件仍可读取。

启动自动应用 `AddPlayerFeatures` 迁移，新增 `best_play_snapshot`、`player_watch`、`player_subscription`、`player_watch_event`。资料和 BP 快照不自动清理；活跃玩家的过期事件在轮询时清理，已停止追踪的玩家事件保留到下一次启用轮询。订阅过期后可重新创建。

## 错误契约

所有错误均为 `application/problem+json`，包含 `status`、`code`、`message`、`traceId`，包括 `Accept: image/png` 和纯文本接口。无变化是成功空结果。

| code | 使用场景 |
| --- | --- |
| INVALID_ARGUMENT | 日期、模式、谱面 ID、筛选条件或未来游标无效 |
| USER_NOT_BOUND | 周报平台账号未绑定 |
| OSU_USER_NOT_FOUND / BEATMAP_NOT_FOUND | 官方资源明确不存在 |
| HISTORY_NOT_FOUND | 该周没有本地历史资料和期末 BP 快照 |
| SCORE_NOT_FOUND | 一方没有满足条件的成绩 |
| LOCAL_SCORE_NOT_COLLECTED | 一方无榜谱面没有本地收录 |
| RECORD_NOT_FOUND | 单条查询/取消时订阅不属于指定群 |
| RESOURCE_EXPIRED | 订阅已取消/过期、游标早于订阅起点或事件保留窗口 |
| CONFLICT | 活跃追踪或 scope 数量超过限制 |
| OSU_API_UNAVAILABLE | 上游请求失败 |
| RENDER_FAILED | PNG 渲染失败 |
| SERVICE_BUSY | 限流、观测并发写入冲突或订阅服务关闭，按 Retry-After 重试 |

这些接口沿用既有错误定义，没有新增或改变错误码语义。
