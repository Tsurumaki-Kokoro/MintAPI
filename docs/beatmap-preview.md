# 谱面预览

新增接口沿用 `access_token` 请求头鉴权：

- `GET /beatmap/preview/image?beatmap_id=738063&format=gif`：GIF 默认每片段 6 秒，最多四个起点，30 FPS。
- `GET /beatmap/preview/image?beatmap_id=738063&format=png`：静态 PNG。
- `GET /beatmap/preview/video?beatmap_id=738063&start=preview&duration=30`：同步返回带音频 MP4，默认 30 秒，最多 60 秒，30 FPS；支持 Range。

共用参数：`mods=hd&mods=hr`（每项一个 token）、`convert=mania|ctb|taiko|standard`。只有 Standard 能转到其他模式；Mod 按模式和格式校验。自定义 DT/HT 可用 `mods=dt1.25`，键数 Mod 仅用于 `convert=mania`。

图片起点用重复参数：`time_points=5&time_points=10`，或 `time_points=preview`。GIF 的 `duration` 是每个片段的时长，范围 `(0,6]`；不指定起点时使用引擎的自动选段。Standard PNG 允许最多四个时间点，不接受 duration。Taiko/Catch/Mania PNG 默认全谱概览；区间预览必须同时提供单个 time_points 与 duration（最多 60 秒）。时间为游戏时间秒数，首个可玩物件为 0；允许有限负数，视频音轨开始之前静音。

参数错误为 400，引擎缺失/执行失败为 502，并发已满或超时为 503 + Retry-After。请求取消/超时会终止 CLI 进程树，进程退出后才释放并发额度。相同参数的并行请求等待同一次渲染；如果首个请求被取消，等待者可以重新生成。

## 安装与发布

固定引擎提交：`e3883affa62ae00224a2e16e28755ea712b943bc`（1.3.4）。在目标操作系统/架构上，安装 Python 3、Git、Rust 和 C/C++ 工具链，运行：

```bash
python3 scripts/install-preview-cli.py
dotnet publish HitCircleAPI -c Release
```

安装脚本编译固定源码，生成 CLI、许可证、第三方声明和二进制 SHA-256 清单，放在忽略的 `HitCircleAPI/tools/osu-preview/`。构建/发布会复制该目录。禁止在 CLI 旁放置 config.yml；当前接口只使用内置渲染配置与服务控制的缓存路径。发布时要求 CLI 和平台清单存在；指定 RuntimeIdentifier 时会检查 CLI 平台是否匹配。

按平台分别构建和发布：Windows x64、macOS x64/arm64、Linux x64 glibc。不要在 macOS 上携带 macOS CLI 发布为 linux-x64；.NET 的 RuntimeIdentifier 不会交叉编译这个外部程序，也不会交叉编译现有 rosu-pp 原生库。Windows 使用 .exe，Unix 安装脚本设置执行权限，部署时需要保留该权限。

`HitCircleAPI/Dockerfile` 在 Debian 构建环境中编译固定版本并放入发布目录。Linux x64 生产镜像应在 x64 环境构建，或显式指定 `--platform=linux/amd64`（构建机需支持仿真）。Linux ARM64、Windows ARM64、Alpine 不属于本轮验收范围。

运行时无需 Rust、FFmpeg 或 Node.js。保留随包许可证和第三方声明。启动会执行 --version 并记录检查结果；引擎缺失不阻止其他 API 启动，但预览接口返回 502。

## 配置与缓存

`BeatmapPreview`：ExecutablePath 默认相对 AppContext.BaseDirectory 的 `tools/osu-preview/osu-beatmap-preview-cli`；可配置绝对路径。总并发默认 1，图片超时 120 秒，视频超时 300 秒（包含等待、下载及编码）；视频最大时长 60 秒，GIF 最大时长 6 秒。

缓存根目录：`<CacheDir>/beatmap/preview/`。artifacts 按规范化参数、引擎二进制 hash 和服务配置隔离；downloads 为 CLI 的谱面/谱包下载缓存。图片复用现有 BeatmapFileService 的 .osu；视频由 CLI 下载完整谱包。缓存不自动过期；谱面更新后应清理，替换引擎后重启服务。

`POST /task/clear_cache` 同时清理预览产物和下载缓存，清理期间等待已有预览渲染完成。运维清理建议避开正在下载视频的客户端；Windows 文件占用可能使删除失败。

## 验证

```bash
python3 scripts/smoke-preview-cli.py HitCircleAPI/tools/osu-preview/osu-beatmap-preview-cli /tmp/preview-smoke
dotnet test HitCircleAPI-dotnet.sln
```

Windows 将 CLI 路径改为 .exe，输出目录改为本机路径。冒烟脚本生成本地四模式 .osu/.osz 和测试音频，离线导出 PNG/GIF/MP4，检查结果路径、文件签名和大小；不依赖 osu! 网络服务。它不替代真实谱面的视觉、音画同步和公网谱包下载验收。

## 本轮验收记录

macOS ARM64：四模式本地 PNG/GIF/MP4 共 12 项导出通过，真实 BID 738063 的在线谱包下载及短 MP4 导出通过。独立解码检查五个 MP4 均为 20 视频帧和 96000 音频采样（2 秒）；macOS AVFoundation 能读取轨道及视频帧，但音频 reader 未返回采样，目标播放器兼容性与音画同步仍需人工验收。

Linux x64 容器：Docker daemon 未运行，尚未完成构建和运行验收。Windows/macOS Intel 尚未验证。
