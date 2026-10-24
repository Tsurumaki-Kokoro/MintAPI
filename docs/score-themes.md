# 成绩图片与 BP 分析

供 API 接入方使用。所有请求通过 `access_token` 请求头鉴权，并使用平台绑定账号。

## 单条成绩与列表

```text
GET /score/recent_play?platform=qq&platform_uid=123&include_fails=true&theme=default
GET /score/best_play?platform=qq&platform_uid=123&best_index=3&theme=default
GET /score/best_plays?platform=qq&platform_uid=123&best_index=1&best_end=10
GET /score/recent_play?platform=qq&platform_uid=123&recent_index=1&recent_end=5&include_fails=true
```

| 参数 | 说明 |
| --- | --- |
| `game_mode` | 0 osu!、1 taiko、2 catch、3 mania；省略使用绑定模式 |
| `theme` | 单条成绩支持 `default`（默认）和 `yaowan`；列表仅支持 `default` |
| `recent_index` / `best_index` | 从 1 开始，范围 1–100 |
| `recent_end` / `best_end` | 区间终点，包含终点，每次最多 20 条，不超过 100 |
| `include_fails` | 最近成绩是否包含失败 |
| `legacy_only` | true 仅 Stable，false 包含 Lazer，省略使用 osu! API 默认 |

`best_play` 返回单条 BP，`best_plays` 返回列表，默认第 1–10 条。最近成绩省略 `recent_end` 时返回单条图；指定终点时返回列表，包括起止相同的情况。可用成绩不足时返回已有部分，并标明范围。

单条图片显示判定、PP、FC/SS 估算、准确率、连击、Miss、时间和谱面参数，时间使用 UTC。列表显示谱面、Mods、评级、PP 和准确率；BP 另显示 API 提供的加权信息，最近成绩标明失败。列表保持官方顺序，聊天场景可每次请求 3–5 条。

Standard 单条图片包含本地 AIM / SPEED / ACC PP 分项。分项不等于 API 总 PP 的拆分，也不能直接相加得到总 PP。无 Mods 显示 NM，缺失数据为“—”。

## BP 补 FC 分析

```text
GET /score/fix?platform=qq&platform_uid=123&game_mode=0&legacy_only=true
```

分析前 100 BP，支持四模式及 `legacy_only`，返回 default 主题 PNG。

- 仅分析通过且有谱面、PP 的成绩；跳过 SS、完整 FC 和 Miss 超过物件数 1% 的成绩。
- 保留准确率和 Mods，估算补满连击、清零 Miss 后的 FC PP。
- 总 PP 提升按修正后的 BP 排名及权重估算，保留原总 PP 中的额外部分。
- 最多展示收益最高的 12 条；展示数量不影响总提升估算。

结果受本地计算器版本及已获取 BP 范围影响，不保证与官方重算一致。部分计算失败时保留原 PP，并显示失败数量；全部计算失败返回 500，无可分析成绩返回 404。

## 近 N 日新增 BP

```text
GET /score/new_best_plays?platform=qq&platform_uid=123
GET /score/new_best_plays?platform=qq&platform_uid=123&days=7&game_mode=0&legacy_only=true&mods=HD,HR&first=1&last=10
```

返回 default 主题 PNG 列表，显示原 BP 位置、成绩信息和游玩时间（UTC）。

| 参数 | 说明 |
| --- | --- |
| `days` | 1–365，默认 1；从请求时刻向前滚动，不按自然日截断 |
| `game_mode` / `legacy_only` | 与成绩接口相同 |
| `mods` | 逗号分隔缩写，忽略大小写，包含匹配；`NM` 单独使用，忽略 CL |
| `first` / `last` | 筛选后列表中的位置，范围 1–200，默认 1–20，每次最多 20 条 |

筛选范围为当前前 200 BP，结果保持 BP 顺序。这里的“新增”仅指窗口内完成且目前仍在榜单的成绩，不能还原已被替换的 BP。无匹配或页码超出范围返回 404。

## 升级兼容

旧主题名 `apple` 已停用，调用方改用 `default`；旧版布局通过 `yaowan` 使用。升级应用时应同时更新随包原生 PP 库。完整错误码见 [API 错误响应](api-errors.md)。
