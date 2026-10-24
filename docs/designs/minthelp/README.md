# 指令帮助海报

供模板开发者参考。海报为横向三栏，右侧插画铺满高度；正文与插画交界处使用浅色遮罩。

| 文件 | 用途 |
| --- | --- |
| `minthelp.png` | 完整预览，宽 2800px |
| `minthelp-chat.png` | 宽 720px 的聊天预览 |
| `minthelp.html` | 静态渲染快照，引用仓库字体与背景 |

动态模板位于 `MintAPI/Rendering/HelpTheme/`，背景素材位于 `MintAPI/wwwroot/assets/help/`。修改模板后生成预览：

```sh
MINTHELP_PREVIEW_DIR="$PWD/artifacts/minthelp" dotnet test tests/MintAPI.Tests/MintAPI.Tests.csproj --filter FullyQualifiedName~HelpRenderingTests
```

输出 HTML 引用本机测试资源路径，分享前改为相对路径。接口接入说明见 [服务信息与帮助接口](../../service-info.md)。
