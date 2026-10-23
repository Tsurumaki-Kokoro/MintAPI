# Mint osu! 帮助海报预览

横版三栏指令指南，右侧插画按海报高度填充；HELP 字样和插画边缘融入正文背景，以整体白色渐变保证文字对比度。

- `minthelp.png`：2800px 宽的完整预览，高度由内容决定。
- `minthelp-chat.png`：720px 宽的聊天预览，详细参数需打开原图阅读。
- `minthelp.html`：本次渲染快照，使用仓库相对路径加载背景和字体。

运行时模板位于 `MintAPI/Rendering/HelpTheme/`，使用现有 PlaywrightRenderer、PlaywrightBrowserProvider 和 RenderAttribution。原始插画与将 LOG 替换为 HELP 的编辑版本保存在 `MintAPI/wwwroot/assets/help/`；编辑版本由内置图片编辑工具生成，人物局部细节可能与原图不同。

`GET /help/image` 返回 PNG 帮助海报，沿用 API 的 `access_token` 鉴权，无需绑定玩家或调用 osu!。渲染失败返回 `500 / RENDER_FAILED`，繁忙返回 `503 / SERVICE_BUSY` 与 `Retry-After`；错误均为 `application/problem+json`，包括仅接受 `image/png` 的请求。AstrBot `/minthelp` 优先显示海报，配置未完成或请求失败时回退到文字帮助。管理员专用同步菜单/面板指令作为额外文字，仅在 QQ 官方机器人管理员私聊中显示。

重新生成：

```sh
MINTHELP_PREVIEW_DIR="$PWD/docs/designs/minthelp" dotnet test tests/MintAPI.Tests/MintAPI.Tests.csproj --filter FullyQualifiedName~HelpRenderingTests
```

测试生成的 HTML 会引用本机测试输出目录；分享前应将资源路径改为相对仓库路径。正文与背景需一起保留。
