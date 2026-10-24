<div align="center">

# MintAPI

面向 osu! 的数据查询、分析与图片渲染 API

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET-Core-512BD4?style=for-the-badge)
[![License](https://img.shields.io/badge/LICENSE-AGPL--3.0-blue?style=for-the-badge)](LICENSE)
![Playwright](https://img.shields.io/badge/Playwright-2EAD33?style=for-the-badge&logo=playwright&logoColor=white)

[功能](#功能) · [效果预览](#效果预览) · [部署](#部署) · [API 与文档](#api-与文档) · [开发](#开发)

</div>

## 简介

MintAPI 由 [**HitCircleAPI**](https://github.com/Tsurumaki-Kokoro/HitCircle-API) 迁移改造而来，在原有 osu! 数据查询与图片渲染能力的基础上，持续完善接口、渲染和服务架构。可作为机器人或其他应用的后端，提供玩家、成绩、谱面与多人比赛相关的数据和可视化输出。

## 功能

| 类别 | 能力 |
| --- | --- |
| 玩家 | 信息图、头像卡、平台账号绑定、自定义背景、排行榜 |
| 成绩 | Recent、Best Play、BP 列表、新增 BP、指定谱面成绩、PP 分析与修复预测 |
| 历史 | 玩家数据变化、已采集成绩记录与趋势图 |
| 谱面 | 谱面与谱面集信息图、搜索、BPM 明细、难度曲线与 Mods 对比 |
| 预览 | 四模式谱面 PNG / GIF 预览、带音频 MP4、原生 GIF 高难度片段选择 |
| 比赛 | 多人房历史、评分、实时订阅与单局结果图 |

按接口提供 JSON、PNG、GIF 或 MP4；图片接口支持的主题见对应文档。

## 效果预览

### 玩家信息

![玩家信息](docs/images/user-info.png)

### 最佳成绩

![最佳成绩](docs/images/best-play.png)

### 谱面信息

![谱面信息](docs/images/beatmap.png)

## 部署

使用 Release 中的预编译包，无需安装编译环境：

- **Debian / Ubuntu x64**：解压 Linux 包，运行 `sudo bash install.sh`。可分别选择代装 MariaDB、Redis，或填写已有服务地址。
- **Windows x64**：解压 Windows 包，以管理员身份运行 `install.ps1`。可代装 MySQL，缓存填写已有 Redis 服务地址。
- **Docker Compose**：解压 Compose 包，运行 `configure.sh` 或 `configure.ps1`，填写镜像版本和 osu! 凭证。

安装、配置、升级和服务管理见 [部署文档](docs/deployment.md)。从源码运行见 [开发指南](docs/development.md)。

业务接口通过 `access_token` 请求头传入 API Key。例如：

```sh
curl --fail 'http://localhost:8080/beatmap/beatmap?beatmap_id=1949106' \
  -H 'access_token: YOUR_API_KEY' \
  -H 'Accept: image/png' \
  -o beatmap.png
```

## API 与文档

开发环境提供 [Scalar](http://localhost:5251/scalar/v1) 和 [OpenAPI JSON](http://localhost:5251/openapi/v1.json)，用于查看完整参数和响应类型。

| 主题 | 文档 |
| --- | --- |
| 玩家 | [周报、对比与动态订阅](docs/player-features.md) · [头像卡](docs/avatar-card.md) · [排行榜](docs/user-ranking.md) |
| 成绩与历史 | [成绩主题](docs/score-themes.md) · [历史记录](docs/history.md) |
| 谱面 | [信息图](docs/beatmap-info.md) · [搜索](docs/beatmap-search.md) · [预览与视频](docs/beatmap-preview.md) |
| 多人比赛 | [历史与评分](docs/multiplayer.md) · [实时订阅](docs/matchlive.md) |
| 运维与接入 | [服务信息与帮助](docs/service-info.md) · [配置](docs/configuration.md) · [数据库迁移](docs/database-migrations.md) · [错误码](docs/api-errors.md) |
| API 客户端 | [MintOsuApi 开发](docs/mint-osuapi-development.md) |

控制器业务错误使用 `application/problem+json`，包含 `status`、`code`、`message` 和 `traceId`；图片接口的错误也按此格式返回。接入方应根据状态码及 `code` 判断结果。

## 开发

- [开发指南](docs/development.md)：环境准备、源码运行、测试与渲染预览。
- [MintOsuApi 联调](docs/mint-osuapi-development.md)：相邻客户端仓库与 Docker 构建。
- [发布维护](docs/releasing.md)：版本标签、Actions、Release 和 Docker Hub。

欢迎通过 [Issues](https://github.com/Tsurumaki-Kokoro/MintAPI/issues) 反馈问题或提交 Pull Request。接口变更请同步文档与测试，图片变更请附渲染结果。

## 致谢与许可证

- [HitCircleAPI](https://github.com/Tsurumaki-Kokoro/HitCircle-API)：项目迁移改造的起点。
- [osu!](https://osu.ppy.sh/) 与 [MintOsuApi](https://github.com/Tsurumaki-Kokoro/mint-osuapi)：游戏数据与 API 客户端。
- [rosu-pp](https://github.com/MaxOhn/rosu-pp)、[Playwright](https://github.com/microsoft/playwright) 与 [Scriban](https://github.com/scriban/scriban)：性能计算、浏览器渲染与模板支持。

本项目采用 [AGPL-3.0 许可证](LICENSE)。依赖、字体及图片等资源遵循各自的许可证与使用条款。
