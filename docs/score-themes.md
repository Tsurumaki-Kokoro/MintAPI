# 成绩模板与主题

`/score/recent_play`、`/score/best_play`、`/score/user_score` 默认使用 `theme=default`：白底、顶部封面横幅、图标与分组、突出主要成绩的字号层级及细分隔线，无渐变背景或数据卡片注释。原 Python 成绩模板通过 `theme=yaowan` 使用。

```text
/score/recent_play?platform=qq&platform_uid=123&recent_index=2&include_fails=true&theme=default
/score/best_play?platform=qq&platform_uid=123&best_index=3&theme=default
/score/best_play?platform=qq&platform_uid=123&best_index=1&best_end=10&theme=default
/score/recent_play?platform=qq&platform_uid=123&recent_index=1&recent_end=5&include_fails=true&theme=default
```

- `game_mode`：0–3，省略使用绑定模式。
- `recent_index`、`best_index`：1–100，从 1 开始。
- `best_end` / `recent_end`：可选区间终点（含），不得小于对应起点，最多 20 条，终点不超过 100；列表仅支持 default。省略终点时仍返回单条成绩图；指定与起点相同的终点时返回一行列表。实际可用成绩少于请求数量时返回已有成绩，并显示实际返回的序号范围。
- `legacy_only`：true 仅 Stable 成绩，false 包含 Lazer，省略沿用 osu! API 默认。
- 单条显示模式对应判定、实际 PP（优先 API 值）、FC/SS 估算、连击及谱面参数；列表显示序号、谱面背景、标题、作者、难度、Mods、等级、准确率和 PP；BP 另显示加权比例与加权 PP（API 提供时）。列表不再显示连击、Miss 和成绩时间，单条图仍保留这些信息。最近游玩列表明确标记失败成绩，并沿用 include_fails 和 legacy_only 过滤。单条时间统一使用 UTC。

主题迁移：用户信息旧 default → yaowan、apple → default；多人和 PP 分析 apple → default；谱面信息原 default → yaowan（谱面尚无新 default 模板）。apple 名称不再接受。静态图片资产目录保留原路径，以保持旧模板资源引用稳定。

FC/SS 与星级沿用现有本地 PP 计算器；本轮未改变该计算器对自定义 Lazer Mods 的支持范围。单条图使用实际谱面背景；列表使用背景缩略图与文字行，避免连续卡片。

std 模式的 default 单条成绩图额外显示本地 rosu-pp 计算的 AIM / SPEED / ACC PP 分项，图片基础尺寸为 1500×1240px，较长的玩家名、标题或难度名会增加头部高度，保留完整文字。分项不按比例分摊 API 总 PP，也不能直接相加得到总 PP；本地算法与 API 算法版本不一致时，分项与 API 总 PP可能存在差异。主要成绩区显示等级、PP、准确率、实际 / 理论最大连击及 Miss；下方分别展示判定、Score 与 PP 分项 / FC / SS。底部保留时间、谱面状态和 ID、时长、BPM、CS / HP / OD / AR、物件数量及 API 提供的排名；会员标识保留在玩家信息中。缺失判定或排名显示“—”，不补零。mania 另保留 MAX / 300 比值。yaowan 保持原排版。

default 单条图与列表的评级均使用文字，不使用评级美术资源。单条图玩家国家旁显示国旗，已知 Mods 以原有图标配缩写展示；未知 Mods 仍显示文字，无 Mods 为 NM。底部圆圈、滑条数量分别使用 `osu-web/public/images/layout/beatmapset-page/` 的对应图标。顶部仍展示真实谱面背景，原有准确率、连击、Miss、PP 分项等 Tabler 图标及全部成绩信息保留。所有图标素材以内嵌图片加载。

PP 分项的新版原生返回结构使用 `performance_calculate_v2`，旧 `performance_calculate` 保持原来的 24 字节布局，避免旧 C# 程序把 Aim PP 字节误读为最大连击。升级时应一起构建、部署 .NET 程序与原生库，并重启现有服务。3881559（Epitaph / Elegy）的回归样本最大连击为 2900，截图成绩应显示 1,623 / 2,900。

两种 default 列表复用单条图的视觉语言：白底、细线、语义色、Tabler 图标；每行分为背景与等级、曲目信息、PP 与准确率三组。PP 使用最大字号，准确率紧随其下；作者、难度和 BP 加权信息处于次要层级。无 Mods 显示 NM，缺失 PP 显示“—”。较长的标题、难度、作者会换行并增加行高，不使用截断或省略号。列表仅使用 API PP，不把基础星级冒充 Mods 星级。

列表复用 `assets/score/default/mods/` 的 Mods 图标和 `assets/flags/` 的国旗；背景上的评级使用文字。缺失头像使用 osu-web 的访客头像，缺失背景使用中性的谱面图标占位。素材来源与许可证见 `assets/osu-web/README.md`。

列表背景从 osu! 官方谱面集封面加载，按谱面集缓存为 `cache/beatmap/osu_file/{setId}/list-cover.jpg`。同一次渲染去重谱面集并最多同时加载 4 张，每张首次加载超时 5 秒；失败时使用占位，不下载 `.osu` 文件，不以随机季节背景替代实际谱面。背景和图标内嵌到 HTML 中，渲染无需再发出图片请求。

列表保持 osu! API 的原始排序（BP 顺序、最近时间顺序），不因缺失 PP 重排。宽度为 1500px，高度按实际条目和文字内容变化；大量条目的完整细节需打开原图阅读，日常群聊建议每张请求 3–5 条。
