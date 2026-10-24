# 多人比赛实时追踪

供机器人和前端开发者接入。通过 `access_token` 请求头鉴权；调用方负责群权限与消息投递。

## 接口

```http
POST /multiplayer/live/subscriptions
Content-Type: application/json

{"matchId":109975520,"scope":"osubot:qq:123456"}
```

同一 scope 与比赛重复订阅会续期并返回同一个订阅 ID。响应包含 `subscriptionId`、`expiresAt`、`snapshot` 和 `cursor`。首次订阅只返回当前快照，不补发旧事件。`scope` 是调用方自定义命名空间，不是用户身份凭证；当前接口沿用服务共享 API Key 的信任边界。

```text
GET    /multiplayer/live/subscriptions/{id}/updates?after={cursor}
DELETE /multiplayer/live/subscriptions/{id}
GET    /multiplayer/live/{mp_id}/games/{game_id}/image
```

增量响应返回 `snapshot`、`updates` 和新 `cursor`。快照状态为 `waiting`、`playing` 或 `closed`；上游失败以 `error` 与 `lastSuccess` 表示，保留最近有效状态。更新类型包括：

- `game-started`、`game-finished`、`game-aborted`。
- `player-joined`、`player-left`、`player-kicked`、`host-changed`。
- `room-created`、`room-closed`、`room-event`。
- `polling-error`、`recovered`；开发重放另有 `mock-reset`。

每条更新含 MintAPI `revision`、osu! `eventId`、`gameId`、文字、时间及对应事件。游戏事件包含成绩与谱面信息，快照的 users 提供玩家 ID 与名字。`snapshot.currentGame` 只在游戏进行中提供；中途订阅正在进行的比赛时，可直接用其 `id` 取开局图。

**调用方应在成功处理更新后保存 cursor。** 使用相同 cursor 重试会返回相同更新，符合至少一次读取语义；发送侧按 revision 去重。无变化返回空数组。游标或订阅过期返回 410，重新订阅并读取快照。请求更新会续租，默认 30 分钟。取消最后一个订阅后停止查询，短期保留比赛数据。房间关闭后停止查询，订阅仍可读取最后更新。

图片按对局状态返回开局图、成绩图或中止图，含 `X-MatchLive-Revision`、`X-MatchLive-Mock`。累计比分只统计已结束且有成绩的局，不自动识别暖身局。

## 查询、恢复与配额

同一比赛的多个订阅共享查询。默认每 10 秒轮询，最多并行查询两场；上游数据未取全或请求失败时稍后重试，不将失败视为房间结束。

默认同时追踪 4 场，一个 scope 最多 3 个订阅。实时查询单独限制 24 次/分钟，同时经过原有全局 osu! 限流（默认 50 次/分钟）。请求预算必须小于全局额度，给其他接口留余量。失败指数退避到最多 120 秒，不将失败当作房间结束。

Redis 保存比赛状态、订阅、游标和最近更新，重启恢复仍在租期的订阅。按 ASP.NET 环境隔离键空间。每场保留最近 1000 条更新，存储键 TTL 为 2 天；没有订阅的状态默认 60 分钟后清理。Redis 不可用时 API 返回错误，不宣称建立了可恢复订阅。

**实时追踪只支持单个 MintAPI 实例。** 不要让多个同环境实例同时处理这些订阅；Redis 用于重启恢复。

## 调用方流程

1. 群命令建立订阅，保存 subscriptionId/cursor；展示当前状态，必要时取 currentGame 开局图。
2. 每 10 秒获取更新。入退房等文字可合并发送；game-started 取开局图，game-finished/aborted 取结算图。支持编辑消息时按 gameId 更新对应消息，否则发送新结算消息。
3. 处理成功后保存 cursor。重连补取；410 则重建快照。处理 room-closed 后取消订阅。
4. 平台消息发送与 API 更新读取分别处理重试，避免读取成功但发送失败造成丢消息。

## AstrBot 成绩订阅

AstrBot 插件使用 `/mpwatch` 管理订阅，默认每轮结束后等待 30 秒，可在插件配置中调整。首次订阅以已有单局为基线，不补发旧成绩。

`snapshot.games` 是按开局时间与 ID 排序的完整已结束单局摘要：`[{ "gameId": 123, "ready": true }]`。`ready` 仅表示有成绩；没有成绩的中止局为 false，不编造分数。首次订阅把现有名单作为基线，订阅或游标过期后可以对照原先保存的单局名单补发遗漏，而不是将全部历史重新推送。无已结束单局时为空数组。

发送端按单局 ID 保存投递进度，只有消息成功发送后才确认。进程若在发送成功、保存进度前退出，恢复后可能重复投递。开发环境模拟接口见 [开发指南](development.md#实时比赛模拟)。
