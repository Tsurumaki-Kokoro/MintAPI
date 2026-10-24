# 方形玩家名片

`GET /user_info/avatar_card` 返回 512×512 的 PNG 名片，包含头像、国旗和 osu! 用户名。背景为淡紫色，长用户名自动缩小字号；不显示 UID、排名、PP、水印或绘制日期。

所有请求沿用 `access_token` API Key 请求头。

直接指定 osu! UID 或用户名，不要求平台绑定：

```text
GET /user_info/avatar_card?user=2
GET /user_info/avatar_card?user=peppy
```

通过绑定身份查询：

```text
GET /user_info/avatar_card?platform=qq&platform_uid=123456
```

非空 `user` 优先于平台参数；`user` 会去除首尾空白。未提供有效 `user` 时，必须提供完整的 `platform` 和 `platform_uid`。用户名包含空格等特殊字符时按标准 URL 查询参数编码。

缺少国旗时显示国家代码；头像不可用或无法解码时返回错误。不支持尺寸、主题或 JSON 输出参数。

| HTTP 状态 | 含义 |
|---|---|
| 200 | `image/png`，512×512 图片 |
| 400 | 缺少有效玩家或完整平台身份 |
| 404 | 平台绑定或 osu! 玩家不存在 |
| 500 | 玩家查询、头像获取或图片生成失败 |
| 503 | osu! 配额或渲染资源暂时不足，按 Retry-After 重试 |

错误码及响应格式见 [API 错误响应](api-errors.md)。
