# 谱面预览

新增接口沿用 `access_token` 请求头鉴权：

- `GET /beatmap/preview/image?beatmap_id=738063&format=gif`：GIF 默认每片段 6 秒，最多四个起点，30 FPS。
- `GET /beatmap/preview/image?beatmap_id=738063&format=png`：静态 PNG。
- `GET /beatmap/preview/video?beatmap_id=738063&start=preview&duration=30`：同步返回带音频 MP4，默认 30 秒，最多 60 秒，30 FPS；支持 Range。

共用参数：`mods=hd&mods=hr`（每项一个 token）、`convert=mania|ctb|taiko|standard`。只有 Standard 能转到其他模式；Mod 按模式和格式校验。自定义 DT/HT 可用 `mods=dt1.25`，键数 Mod 仅用于 `convert=mania`。

图片起点用重复参数：`time_points=5&time_points=10`，或 `time_points=preview`。GIF 的 `duration` 是每个片段的时长，默认 6 秒，范围 `(0,12]`；不指定起点时，四模式原生 GIF 默认使用预览时间加三个高难度片段，转谱保持引擎自动选段。Standard PNG 允许最多四个时间点，不接受 duration。Taiko/Catch/Mania PNG 默认全谱概览；区间预览必须同时提供单个 time_points 与 duration（最多 60 秒）。时间为游戏时间秒数，首个可玩物件为 0；允许有限负数，视频音轨开始之前静音。

参数错误为 400，引擎缺失/执行失败为 502，并发已满或超时为 503 + Retry-After。请求取消/超时会终止 CLI 进程树，进程退出后才释放并发额度。相同参数的并行请求等待同一次渲染；如果首个请求被取消，等待者可以重新生成。

## 安装与发布

固定引擎提交：`e3883affa62ae00224a2e16e28755ea712b943bc`（1.3.4）。在目标操作系统/架构上，安装 Python 3、Git、Rust 和 C/C++ 工具链，运行：

```bash
python3 scripts/install-preview-cli.py
dotnet publish MintAPI -c Release
```

安装脚本编译固定源码，生成 CLI、许可证、第三方声明和二进制 SHA-256 清单，放在忽略的 `MintAPI/tools/osu-preview/`。构建/发布会复制该目录。禁止在 CLI 旁放置 config.yml；当前接口只使用内置渲染配置与服务控制的缓存路径。发布时要求 CLI 和平台清单存在；指定 RuntimeIdentifier 时会检查 CLI 平台是否匹配。

按平台分别构建和发布：Windows x64、macOS x64/arm64、Linux x64 glibc。不要在 macOS 上携带 macOS CLI 发布为 linux-x64；.NET 的 RuntimeIdentifier 不会交叉编译这个外部程序，也不会交叉编译现有 rosu-pp 原生库。Windows 使用 .exe，Unix 安装脚本设置执行权限，部署时需要保留该权限。

`MintAPI/Dockerfile` 在 Debian 构建环境中编译固定版本并放入发布目录。Linux x64 生产镜像应在 x64 环境构建，或显式指定 `--platform=linux/amd64`（构建机需支持仿真）。Linux ARM64、Windows ARM64、Alpine 不属于本轮验收范围。

运行时无需 Rust、FFmpeg 或 Node.js。保留随包许可证和第三方声明。启动会执行 --version 并记录检查结果；引擎缺失不阻止其他 API 启动，但预览接口返回 502。

## 配置与缓存

`BeatmapPreview`：ExecutablePath 默认相对 AppContext.BaseDirectory 的 `tools/osu-preview/osu-beatmap-preview-cli`；可配置绝对路径。总并发默认 1，图片超时 120 秒，视频超时 300 秒（包含等待、下载及编码）；视频最大时长 60 秒，GIF 最大时长 6 秒。

缓存根目录：`<CacheDir>/beatmap/preview/`。artifacts 按规范化参数、引擎二进制 hash 和服务配置隔离；downloads 为 CLI 的谱面/谱包下载缓存。图片复用现有 BeatmapFileService 的 .osu；视频由 CLI 下载完整谱包。缓存不自动过期；谱面更新后应清理，替换引擎后重启服务。

`POST /task/clear_cache` 同时清理预览产物和下载缓存，清理期间等待已有预览渲染完成。运维清理建议避开正在下载视频的客户端；Windows 文件占用可能使删除失败。

## 验证

```bash
python3 scripts/smoke-preview-cli.py MintAPI/tools/osu-preview/osu-beatmap-preview-cli /tmp/preview-smoke
dotnet test MintAPI.sln
```

Windows 将 CLI 路径改为 .exe，输出目录改为本机路径。冒烟脚本生成本地四模式 .osu/.osz 和测试音频，离线导出 PNG/GIF/MP4，检查结果路径、文件签名和大小；不依赖 osu! 网络服务。它不替代真实谱面的视觉、音画同步和公网谱包下载验收。

## 本轮验收记录

macOS ARM64：四模式本地 PNG/GIF/MP4 共 12 项导出通过，真实 BID 738063 的在线谱包下载及短 MP4 导出通过。独立解码检查五个 MP4 均为 20 视频帧和 96000 音频采样（2 秒）；macOS AVFoundation 能读取轨道及视频帧，但音频 reader 未返回采样，目标播放器兼容性与音画同步仍需人工验收。

Linux x64 容器：Docker daemon 未运行，尚未完成构建和运行验收。Windows/macOS Intel 尚未验证。

## 四模式高难度片段选择

`GET /beatmap/preview/image?beatmap_id=3881559&format=gif&selection=hardest&duration=6`

`selection=auto` 为默认策略：四模式原生 GIF 使用预览时间加三个高难度片段；PNG、视频及转谱保持引擎原有自动选段。`selection=hardest` 当前仅支持 四模式原生 GIF（convert 可省略或为 standard）。显式 `time_points` 优先，此时不执行高难度选段。转谱和视频不支持本轮自动选难段。

算法 `native-strains-v3` 使用 rosu-pp 4 的分段 strain：Standard 为 sqrt(aim²+speed²)，Catch 为 movement，Mania 为 strains；Taiko 使用色彩/耐力的 1.5 范数组合，再与节奏/读谱作 2 范数组合，权重分别为 0.375/0.445/0.75/0.1（参照上游，未加入全谱 pattern/length bonus），窗口评分为峰值和时长加权平均值各占 50%。它是片段排序指标，不能当成星数。候选窗口从分段附近提前约一秒生成，按评分排序，最多选四段，selection=hardest 的片段间至少间隔一秒播放时间，默认组合策略使用五秒间隔，然后按时间顺序显示。短谱可能少于四段；纯 hardest 没有可计算 strain 时返回 400，默认组合仍保留预览段。面板容量严格等于选段数，避免 CLI 额外填入自动时间点。

EZ/HR/HD、DA、DT/HT 按请求应用；TC 仅影响预览外观，不单独改变本轮 strain 评分。DT/HT 将 6 秒播放时长映射为对应谱面时长；输出起点统一换算成“绝对谱面时间减首物件时间”，不会额外除以倍速。原生层使用首个难度物件的倍速调整时间定位第一段 strain：Standard/Mania 为第二物件，Taiko 为第三物件，Catch 为展开后第二个水果或水滴，避免把数组下标误当成从零开始的时间。

每次自动选难段请求的产物目录保存 selection.json，包括完整 strain 时间线、倍速、所选区间及分数。缓存身份包括规范化请求和选段算法版本。升级 rosu-pp 或变更选段规则后应更新算法版本并清理旧缓存。

2026-10-02：使用仓库真实谱面 3881559（Epitaph / Elegy）验证 Normal 与 DT1.25；生成默认 GIF、高难度 GIF 及 DT 高难度 GIF。本轮为选段算法试验，后续需要更多不同类型谱面的人工评估。

### 默认预览时间与难段组合

四模式原生 GIF 无手动 time_points 时，默认固定保留谱面 [General] 的 PreviewTime，再选三个高难度片段。先排名三个独立难段，与预览段重叠或距离不足的难段会被替换为下一个满足间距条件的候选。所选四段之间至少间隔 5 秒播放时间（不是单纯起点差 5 秒），DT/HT 下按倍速换算谱面时间。PreviewTime 缺失或为负时使用首物件时间；位于尾部无法容纳完整窗口时向前移。

结果按时间顺序显示；selection.json 的 IsPreview 标记保留的预览段。短谱可能无法提供四个足够分散的区间，此时只返回实际选到的片段。显式 selection=hardest 仍为四个难段，间隔沿用 1 秒；显式 time_points 仍优先覆盖自动选段。PNG、视频及转谱本轮不改变默认行为。


### 四模式原生谱面验证

2026-10-02：Taiko 1028484、Catch 2118524、Mania 1638954 使用 rosu-pp 4.0.1 自带 resources 谱面（测试副本存放于 tests/MintAPI.Tests/Fixtures）。三个模式各生成 Normal、DT1.25、HT 共九个实际 GIF，均为 180 帧、6 秒；检查正常速度输出的预览标记、四个区间及布局。

Catch 使用 rosu-map 的曲线和 SliderEventsIter 展开水果/水滴时间，忽略不参与 strain 的香蕉与小水滴，滑条终点计入可用范围。Mania 长按终点计入范围，IN/HO 按预览引擎的物件变换计算曲线；CS、DS、SW 等外观/布局变化不单独改变选段评分。原生 Mania 键数使用谱面本身的 CircleSize，转谱与转谱键数策略留待后续验证。

DT1.5 下，较短的 Mania 测试谱面因预览位置及五秒间距只能提供三个区间；面板容量与实际选段数一致，不补入引擎默认片段。当前难度分数是局部排序指标，Taiko 属于上游权重的局部近似；更多不同谱面类型的人工评估仍有必要。跨平台发布验收状态沿用前文，本轮本地渲染在 macOS ARM64 完成。
