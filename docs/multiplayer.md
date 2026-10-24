# 多人比赛历史与评分

供 API 接入方使用，通过 `access_token` 请求头鉴权。两个接口均返回 PNG，支持 `theme=default`。

```text
GET /multiplayer/history?mp_id=12345&theme=default&page=1
GET /multiplayer/rating?mp_id=12345&theme=default&algorithm=osuplus&page=1
```

`mp_id` 为正整数。响应头 `X-Page`、`X-Page-Count` 和图片页脚提供分页信息。

## 历史

仅展示已结束且有成绩的对局，包含零分与失败成绩。默认 `page=1`，每页约 32 个玩家成绩行，单局保持完整。`page=0` 返回包含全部对局的长图，此时 `X-Page=0`、`X-Page-Count=1`。

图片包含房间、比赛时间（UTC+8）、团队比分、谱面及玩家成绩。胜负按该局计分方式判断，分数列始终显示原始分数。缺失用户或谱面时显示 ID。

`team_type` 可筛选 `head-to-head`、`team-vs`、`tag-team-vs` 或 `tag-coop`。合作局不统计胜负；团队平局不增加胜场；个人并列第一显示并列结果。

## 评分

默认 `algorithm=osuplus`，另支持 `bathbot`、`flashlight`。三种算法采用不同的相对表现与参赛次数权重，分值不可跨算法直接比较。

- 每页最多 24 位玩家，按评分降序，同分按用户 ID 排序。
- 只纳入非零成绩；某局零分不影响该玩家其他成绩参与评分。
- 仅支持 Score / Score V2 的个人或团队对抗；准确率、连击计分和接力、合作模式返回 400。
- 混合团队模式需用 `team_type` 筛选；换队玩家按每局所属队伍统计。
- 团队平局计 D；个人并列最高分均计第一，第一率以非零上场次数为分母。

评分不自动识别暖身局或赛事规则。`rating` 的 `page` 必须从 1 开始。

## 错误与数据类型

参数、评分模式或页码无效返回 400；房间不存在返回 404；上游失败或历史分页不完整返回 502；图片生成失败返回 500；服务繁忙返回 503 并附 `Retry-After`。完整格式见 [API 错误响应](api-errors.md)。

事件 ID 与 `before` / `after` 游标为 64 位整数，接入方不要使用 32 位类型保存。直接使用 MintOsuApi 时，`MatchGame.Scores` 为 `List<LegacyScore>`，分数字段为 `LegacyScore.Score`。

实时订阅和单局图片见 [实时比赛追踪](matchlive.md)。
