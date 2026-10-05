# 多人比赛实时追踪

MintAPI 管理 osu! 查询、比赛状态、更新日志和图片；调用方（例如 NoneBot）管理群订阅、群权限、消息 ID、重试及消息编辑。API 不直接发送聊天消息。沿用 `access_token` 请求头鉴权。

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

图片按实际状态返回开局图、成绩图或中止图，含 `X-MatchLive-Revision`、`X-MatchLive-Mock`。沿用默认 user_score / user_info 的通栏封面、浅灰底色、字体层级与分隔线；宽度为 1500px，高度按图片和字体加载后的实际内容确定，使用红蓝队色和玩家表格。累计比分以已结束且有成绩的局为依据；不自动识别暖身局。没有成绩时不伪造分数。

## 查询、恢复与配额

同一 mp_id 共用状态与查询。首次查询补齐历史以计算累计比分（最多 20 次向前分页），之后只取增量。默认每 10 秒查询，最多同时查询两场，房间各自加锁；网络请求不占用全局注册锁。未结束的最后一个游戏事件使用 `after=eventId-1` 重读，其他情况下从已收到的最大事件 ID 继续。重复事件更新覆盖；内容没有变化时不生成新 revision。一次轮询最多补取 20 页，非前进分页/超限进入重试，不能假定已补齐。

默认同时追踪 4 场，一个 scope 最多 3 个订阅。实时查询单独限制 24 次/分钟，同时经过原有全局 osu! 限流（默认 50 次/分钟）。请求预算必须小于全局额度，给其他接口留余量。失败指数退避到最多 120 秒，不将失败当作房间结束。

Redis 保存比赛状态、订阅、游标和最近更新，重启恢复仍在租期的订阅。按 ASP.NET 环境隔离键空间。每场保留最近 1000 条更新，存储键 TTL 为 2 天；没有订阅的状态默认 60 分钟后清理。Redis 不可用时 API 返回错误，不宣称建立了可恢复订阅。

**本版本采用一个 MintAPI 实例负责实时追踪。** Redis 持久化用于进程重启恢复，尚无跨实例分布式调度和状态同步；不能同时启动多个同环境实例处理这些订阅。后续横向扩容需要比赛所有权租约及共享更新流。

## 开发 mock：109975520

源数据为本地已捕获的真实比赛 `109975520`，谱面 `4143665`（Sanctuary），游戏 `567283491`，6 人成绩。真实房间是历史比赛。Mock 保留房间名、谱面、玩家与分数，仅合成生命周期事件并重设时间；接口 `isMock=true`、响应头和图片均标明模拟。

启用：

```text
ASPNETCORE_ENVIRONMENT=Development
MatchLive__MockEnabled=true
MatchLive__MockAutoAdvanceSeconds=30
```

生产环境强制关闭 mock。先订阅 `109975520`，后台依次自动推进：等待 → 开局 → 结算 → 房间关闭，每阶段默认 30 秒。设置 `MockAutoAdvanceSeconds=0` 可改为手动推进：

```text
POST /multiplayer/live/mock/109975520/advance
POST /multiplayer/live/mock/109975520/reset
```

重置会生成新 revision 并清空本轮状态，原订阅继续有效。其他 mp_id 仍查询真实 osu! API。Mock 不消耗 osu! 查询预算；图片可能通过现有缓存加载封面和头像。

## 调用方流程

1. 群命令建立订阅，保存 subscriptionId/cursor；展示当前状态，必要时取 currentGame 开局图。
2. 每 10 秒获取更新。入退房等文字可合并发送；game-started 取开局图，game-finished/aborted 取结算图。支持编辑消息时按 gameId 更新对应消息，否则发送新结算消息。
3. 处理成功后保存 cursor。重连补取；410 则重建快照。处理 room-closed 后取消订阅。
4. 平台消息发送与 API 更新读取分别处理重试，避免读取成功但发送失败造成丢消息。

## 验证

`MatchLiveTests` 覆盖真实 mock 生命周期、同一事件更新、跨订阅共享查询、重启恢复、游标重放、错误退避/恢复和中止。`MatchLiveHttpTests` 通过实际 HTTP 推进模拟，验证开局与 6 人结算 PNG、游标与删除，并用 Chromium 检查图片宽度及内容边界。设置 `MULTIPLAYER_PREVIEW_DIR` 可导出图片。
