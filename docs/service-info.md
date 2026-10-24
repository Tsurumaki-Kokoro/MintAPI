# 服务信息、健康检查与帮助海报

本文供插件和前端接入方使用。业务接口通过 `access_token` 请求头鉴权，健康检查无需鉴权。

## 服务信息

```http
GET /api/info
access_token: YOUR_API_KEY
```

响应为 JSON：

| 字段 | 用途 |
| --- | --- |
| `name` | 产品名，`MintAPI` |
| `version` | 应用版本 |
| `apiVersion` | 接口契约版本 |
| `commit` / `builtAt` | 源码提交与构建时间；开发构建可能为 `unknown` |
| `features` | 当前启用的功能标识 |
| `gameModes` | 支持的模式 |
| `themes` | 可用主题；具体接口支持范围见对应文档 |
| `limits.bestPlayList` | BP 列表单次最大条数 |
| `components` | Playwright、Chromium、预览工具版本；不可用组件可能为 null |

功能标识包括 `help.image`、`player.weekly`、`score.compare`、`player.watch.group`、`score.history`、`beatmap.preview`。关闭玩家追踪后不返回 `player.watch.group`，预览工具不可用时不返回 `beatmap.preview`。

调用方按 `features` 判断是否展示或调用对应功能；不要只根据应用版本号推断能力。响应不提供密钥、内部地址或文件路径。

## 健康检查

| 接口 | 成功 | 失败 |
| --- | --- | --- |
| `GET /health/live` | 200，`{"status":"alive"}` | 无法连接表示进程未响应 |
| `GET /health/ready` | 200，`{"status":"ready"}` | 503，`SERVICE_NOT_READY`，附 `Retry-After: 10` |

就绪检查覆盖数据库、缓存、浏览器与预览工具。存活不等于就绪；启动期间连接拒绝或 503 均应继续等待。检查不访问 osu!。

## 帮助海报

```http
GET /help/image
access_token: YOUR_API_KEY
Accept: image/png
```

成功返回横版 PNG 指令指南，无需绑定玩家账号。渲染失败返回 `500 / RENDER_FAILED`，繁忙返回 `503 / SERVICE_BUSY` 并附 `Retry-After`。错误为 `application/problem+json`，即使请求仅接受图片。

AstrBot `/minthelp` 优先使用此海报，请求失败时回退文字帮助；管理员菜单与面板同步提示仅在 QQ 官方机器人管理员私聊显示。完整错误格式见 [API 错误响应](api-errors.md)。
