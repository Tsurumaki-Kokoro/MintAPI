# Mint osu! 帮助海报预览

横版三栏指令指南，右侧插画按海报高度填充；HELP 字样和插画边缘融入正文背景，以整体白色渐变保证文字对比度。

- `minthelp.png`：2800px 宽的完整预览，高度由内容决定。
- `minthelp-chat.png`：720px 宽的聊天预览，详细参数需打开原图阅读。
- `minthelp.html`：本次渲染快照，使用仓库相对路径加载背景和字体。

运行时模板位于 `MintAPI/Rendering/HelpTheme/`，使用现有 PlaywrightRenderer、PlaywrightBrowserProvider 和 RenderAttribution。原始插画与将 LOG 替换为 HELP 的编辑版本保存在 `MintAPI/wwwroot/assets/help/`；编辑版本由内置图片编辑工具生成，人物局部细节可能与原图不同。

本轮仅完成模板与预览，尚未提供帮助图片接口或接入 AstrBot `/minthelp`。现有机器人仍返回文字帮助。管理员专用同步菜单/面板指令不属于这份普通用户海报。

重新生成：

```sh
MINTHELP_PREVIEW_DIR="$PWD/docs/designs/minthelp" dotnet test tests/MintAPI.Tests/MintAPI.Tests.csproj --filter FullyQualifiedName~HelpRenderingTests
```

测试生成的 HTML 会引用本机测试输出目录；分享前应将资源路径改为相对仓库路径。正文与背景需一起保留。
