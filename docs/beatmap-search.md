# 谱面搜索 JSON 接口

`GET /beatmap/search`，使用与现有接口相同的 API Key 验证方式。

示例：

```http
GET /beatmap/search?query=Freedom%20Dive&mode=osu&status=ranked&sort=relevance_desc
```

| 参数 | 默认值 | 说明 |
| --- | --- | --- |
| `query` | 必填 | 1～500 字符；关键词或官方搜索表达式，例如 `star>=5 star<=6 bpm>=180` |
| `mode` | `any` | `any`、`osu`、`taiko`、`catch`、`mania`，也接受 -1～3 |
| `status` | `any` | `any`、`leaderboard`、`ranked`、`qualified`、`loved`、`pending`、`wip`、`graveyard` |
| `sort` | 官方默认 | 字段加 `_asc` 或 `_desc`；字段为 `title`、`artist`、`difficulty`、`ranked`、`rating`、`plays`、`favourites`、`updated`、`relevance`、`nominations`、`creator` |
| `cursor_string` | 无 | 上次响应的游标；翻页时原样传回并保持其他参数一致 |

返回保留 osu! 客户端的 JSON 字段名称：

- `total`：官方匹配的谱面集总数。
- `beatmapsets`：本页谱面集，其中 `beatmaps` 包含具体难度。
- `cursor_string` / `cursor`：官方返回的分页信息；可空字段为 null 时省略。
- `search`、`recommended_difficulty`：官方返回的附加搜索信息（如有）。

搜索单位是谱面集，不能把 `total` 当作具体难度数量。模式和搜索表达式透传官方接口；本接口不额外删减谱面集内的难度、不重排官方结果，也不提供自定义 page_size。无结果返回 200、`total: 0` 和空数组。

相同关键词、模式、状态、排序和游标缓存 2 分钟。请求使用现有 osu! 限流服务；无效参数返回 400，上游 HTTP 请求失败返回 502，配额不足由现有中间件返回可重试错误。失败结果不缓存。

未增加机器人对接。

## 搜索结果图片

`GET /beatmap/search/image` 返回 `image/png`，接受与 JSON 搜索相同的参数，另加 `page`（默认 1）。

```http
GET /beatmap/search/image?query=Freedom%20Dive&mode=osu&status=ranked&page=1
```

图片宽 900px，每页最多 5 个谱面集，行高 96px。行背景使用官方背景封面，叠加透明渐变；展示歌名、艺术家、谱师和状态。每个难度以模式图标展示，颜色采用 osu-web 官方难度色谱；按模式和星级排序，指定模式优先，其他模式降低透明度。最多显示 18 个难度图标，超出显示 `+N`。首版不解析 `query` 内的星级条件做图标高亮。

导航响应头：

- `X-Page`：当前批次内的图片页码。
- `X-Page-Count`：当前批次的图片总页数。
- `X-Total`：官方匹配的谱面集总数。
- `X-Next-Cursor`：下一官方批次的游标（如有）。

先用相同搜索条件和游标逐页读取本批结果；到达 `X-Page-Count` 后，使用 `X-Next-Cursor` 并把 `page` 重置为 1。图片底部标记“本批”，不会把它误当成全局页码。无结果返回空状态图片；超出本批页码返回 400。搜索数据与 JSON 接口共享缓存，背景封面缓存 30 分钟，封面加载失败时仍展示文字和图标。

官方图标沿用项目已有 osu-web 资源。色谱参考：https://github.com/ppy/osu-web/blob/master/resources/js/utils/beatmap-helper.ts 。
