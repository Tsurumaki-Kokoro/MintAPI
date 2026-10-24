# 开发指南

本文供 MintAPI 开发者使用。安装预编译服务请查看 [部署文档](deployment.md)。

## 环境与仓库

准备 .NET 10 SDK、Rust、Python 3、Git 与 C/C++ 工具链。两个仓库放在同一父目录：

```sh
git clone https://github.com/Tsurumaki-Kokoro/MintAPI.git
git clone https://github.com/Tsurumaki-Kokoro/mint-osuapi.git
cd MintAPI
```

准备可连接的 MySQL / MariaDB、Redis 和 osu! OAuth 应用凭证：

```sh
cp MintAPI/appsettings.Development.json.example MintAPI/appsettings.Development.json
```

填写 `ApiKey`、`OsuApi` 和 `ConnectionStrings`，按 [数据库迁移](database-migrations.md) 创建空库。不要提交本地凭证。

## 构建与运行

```sh
dotnet build MintAPI.sln
pwsh MintAPI/bin/Debug/net10.0/playwright.ps1 install --only-shell chromium
dotnet run --project MintAPI --launch-profile http
```

浏览器安装入口需 PowerShell；Linux 可加 `--with-deps` 安装系统运行库，需要 sudo 权限。预览引擎按下一节安装。

开发服务地址为 `http://localhost:5251`，交互文档为 [Scalar](http://localhost:5251/scalar/v1)，接口描述为 [OpenAPI JSON](http://localhost:5251/openapi/v1.json)。

## 谱面预览引擎

在目标操作系统与架构上运行：

```sh
python3 scripts/install-preview-cli.py
```

固定源码版本、许可证和平台清单会安装到忽略的 `MintAPI/tools/osu-preview/`。Windows 使用 `.exe`，Unix 发布时保留执行权限。不要在 CLI 旁添加 `config.yml`。

预览引擎支持源码构建的 Windows x64、Linux x64 glibc、macOS x64/arm64；Release 原生包只提供 Linux x64 和 Windows x64。指定 .NET RuntimeIdentifier 不会交叉编译预览引擎或 PP 原生库，须在目标平台构建。Linux 发布基线见 [发布维护](releasing.md)。

## 测试与预览

```sh
dotnet test MintAPI.sln
python3 -m unittest discover -s tests/deployment -v
python3 scripts/smoke-preview-cli.py MintAPI/tools/osu-preview/osu-beatmap-preview-cli artifacts/preview-smoke
```

Windows 的最后一条命令为 CLI 路径加 `.exe`。渲染测试需要 Chromium；预览冒烟脚本使用本地四模式谱面生成 PNG/GIF/MP4，无需 osu! 网络访问。

可通过环境变量导出功能样例，例如：

```sh
MULTIPLAYER_PREVIEW_DIR="$PWD/artifacts/multiplayer" dotnet test tests/MintAPI.Tests/MintAPI.Tests.csproj --filter FullyQualifiedName~MultiplayerRenderingTests
```

模板修改后检查真实 PNG，确认长文本、缺失值和不同模式均可阅读。客户端真实响应检查见 [契约检查工具](../tools/MintOsuApi.ContractValidation/README.md)。

## 代码位置

| 目录 | 修改内容 |
| --- | --- |
| `MintAPI/Controllers/` | HTTP 接口与参数文档 |
| `MintAPI/Services/` | 查询、计算和渲染服务 |
| `MintAPI/Data/` | 数据模型和迁移 |
| `MintAPI/Rendering/` | 图片模板 |
| `MintAPI/wwwroot/` | 字体、图标和图片 |
| `native/rosu-pp-ffi/` | PP 原生库与 C# 绑定 |
| `tests/MintAPI.Tests/` | API 与渲染测试 |
| `../mint-osuapi/` | osu! API 客户端及其测试 |

.NET 目标为 net10.0，数据库依赖为 EF Core 9 + Pomelo 9。升级数据库提供程序需同步验证迁移和查询。修改接口时同步 XML 注释、功能文档及 [错误响应](api-errors.md) 测试。

## 实时比赛模拟

开发环境可用固定房间 `109975520` 调试订阅和消息生命周期：

```text
ASPNETCORE_ENVIRONMENT=Development
MatchLive__MockEnabled=true
MatchLive__MockAutoAdvanceSeconds=30
```

订阅该房间后依次进入等待、开局、结算、关闭。响应及图片标明模拟，其他房间继续查询官方 API；生产环境禁用此功能。

将 `MockAutoAdvanceSeconds` 设为 0 后，可通过带 API Key 的请求手动控制：

```text
POST /multiplayer/live/mock/109975520/advance
POST /multiplayer/live/mock/109975520/reset
```

重置后原订阅仍有效。接口协议见 [实时比赛追踪](matchlive.md)。
