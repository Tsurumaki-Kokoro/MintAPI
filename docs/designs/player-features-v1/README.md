# 玩家功能设计样例

供模板开发者参考，使用虚构玩家与示例成绩。运行时接口及统计口径见 [玩家功能](../../player-features.md)。

| 文件 | 内容 |
| --- | --- |
| `weekly-report.html` / `.png` | Standard 个人周报示例 |
| `score-comparison.html` / `.png` | Standard 同图成绩对比示例 |
| `breakthrough-alert.txt` | 新 BP 与排名突破提醒文案 |

HTML 通过相对路径引用仓库字体；PNG 用于查看版式。正式模板位于 `MintAPI/Rendering/PlayerFeatures/`，不会读取这些样例中的数值。

修改动态模板后，可运行以下渲染测试并检查输出：

```sh
PLAYER_FEATURE_PREVIEW_DIR="$PWD/artifacts/player-features" dotnet test tests/MintAPI.Tests/MintAPI.Tests.csproj --filter FullyQualifiedName~PlayerFeatureRenderingTests
```
