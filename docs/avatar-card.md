# 方形玩家名片

`GET /user_info/avatar_card` 返回固定 512×512、不透明的 PNG 名片：淡紫色背景（`#EDE7F6`），居中 400×400 圆角头像，下方一行国旗和 osu! 用户名。用户名默认 48px，国旗 64×44；按最长 15 个英文及 `[]-_` 字符设计，较宽的名字自动缩小字号；不显示数字 UID、排名或 PP。

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

头像复用项目缓存；国旗读取现有 `wwwroot/assets/flags` 资源。缺少国旗时显示国家代码；头像不可用或无法解码时返回错误。没有尺寸、主题或 JSON 输出参数。

| HTTP 状态 | 含义 |
|---|---|
| 200 | `image/png`，512×512 图片 |
| 400 | 缺少有效玩家或完整平台身份 |
| 404 | 平台绑定或 osu! 玩家不存在 |
| 500 | 外部调用、头像获取或渲染失败 |
| 503 | osu! 配额或渲染资源暂时不足，遵循现有重试响应 |

无需新增配置或数据库迁移。
