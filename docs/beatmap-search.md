# 谱面搜索 JSON 接口

`GET /beatmap/search`，使用 `access_token` 请求头鉴权。

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

相同搜索条件和游标默认缓存 2 分钟。无效参数返回 400，上游失败返回 502，服务繁忙返回 503 并附 `Retry-After`。详见 [API 错误响应](api-errors.md)。


## 搜索结果图片

`GET /beatmap/search/image` 返回 `image/png`，接受与 JSON 搜索相同的参数，另加 `page`（默认 1）。

```http
GET /beatmap/search/image?query=Freedom%20Dive&mode=osu&status=ranked&page=1
```

图片宽 900px，每页最多 5 个谱面集，显示标题、艺术家、谱师、状态及难度图标。难度按模式和星级排序，指定模式优先；最多显示 18 个难度图标，其余显示为 `+N`。图标颜色表示难度，不额外高亮搜索表达式中的星级区间。

导航响应头：

- `X-Page`：当前批次内的图片页码。
- `X-Page-Count`：当前批次的图片总页数。
- `X-Total`：官方匹配的谱面集总数。
- `X-Next-Cursor`：下一官方批次的游标（如有）。

先用相同搜索条件和游标逐页读取本批结果；到达 `X-Page-Count` 后，使用 `X-Next-Cursor` 并把 `page` 重置为 1。页码只针对本批结果。无结果返回空状态图片；超出本批页码返回 400。搜索数据与 JSON 接口共享缓存，背景封面缓存 30 分钟，封面加载失败时仍展示文字和图标。
