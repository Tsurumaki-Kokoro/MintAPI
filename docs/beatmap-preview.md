# 谱面图片与视频预览

供 API 接入方使用。所有请求通过 `access_token` 请求头鉴权。

| 接口 | 输出 | 默认行为 |
| --- | --- | --- |
| `GET /beatmap/preview/image?beatmap_id=738063&format=gif` | GIF | 每片段 6 秒，最多四个片段，30 FPS |
| `GET /beatmap/preview/image?beatmap_id=738063&format=png` | PNG | 静态预览 |
| `GET /beatmap/preview/video?beatmap_id=738063&start=preview&duration=30` | 带音频 MP4 | 30 秒，最多 60 秒，30 FPS；支持 Range |

## 参数

- `mods`：重复传入，例如 `mods=hd&mods=hr`；自定义倍速例如 `mods=dt1.25`。
- `convert`：`mania`、`ctb`、`taiko`、`standard`。仅 Standard 谱面可转换到其他模式，键数 Mod 仅用于 `convert=mania`。
- `time_points`：图片起点，重复传入，例如 `time_points=5&time_points=10`，也可使用 `preview`。
- `duration`：GIF 每片段时长，默认 6 秒，范围 `(0,12]`；视频时长默认 30 秒，最大 60 秒。

时间单位为秒，首个可玩物件为 0，允许有限负数；视频音轨开始之前静音。

Standard PNG 最多接受四个时间点，不接受 `duration`。Taiko、Catch、Mania PNG 默认显示全谱概览；区间预览须同时提供一个 `time_points` 和 `duration`，最长 60 秒。Mods 按模式和输出格式校验。

## GIF 自动选段

```text
GET /beatmap/preview/image?beatmap_id=3881559&format=gif&selection=hardest&duration=6
```

| `selection` | 行为 |
| --- | --- |
| `auto`（默认） | 四模式原生 GIF 保留谱面预览时间，再选三个高难度片段 |
| `hardest` | 四模式原生 GIF 最多选四个高难度片段 |

显式 `time_points` 优先于自动选段。自动组合片段之间至少间隔 5 秒播放时间，`hardest` 片段之间至少间隔 1 秒；短谱可能少于四段。预览时间缺失时使用首物件位置，靠近结尾时向前调整。

自动选段支持 DT/HT 自定义倍速。难段分数是局部难度排序指标，不是星级。PNG、视频和转谱不支持 `hardest`，使用各自默认选段方式或手动指定起点。

## 错误与重试

| 状态 | 场景 |
| --- | --- |
| 400 | 参数、模式转换或格式组合不支持；无法选出要求的难段 |
| 502 | 预览引擎缺失或执行失败，`PREVIEW_UNAVAILABLE` |
| 503 | 并发已满或超时，按 `Retry-After` 重试 |

错误格式见 [API 错误响应](api-errors.md)。请求取消或超时会停止生成任务。

## 部署与缓存

预编译包和 Docker 镜像已包含预览引擎，无需另行安装 Rust、FFmpeg 或 Node.js。原生与 Compose 部署步骤见 [部署文档](deployment.md)。从源码构建预览引擎见 [开发指南](development.md#谱面预览引擎)。

`BeatmapPreview` 配置默认并发 1、图片超时 120 秒、视频超时 300 秒；超时包含等待、下载和编码。`ExecutablePath` 默认为程序目录下的 `tools/osu-preview/osu-beatmap-preview-cli`，Windows 自动添加 `.exe`。

预览缓存位于 `<CacheDir>/beatmap/preview/`，默认不自动过期。谱面更新后可调用 `POST /task/clear_cache`；该操作也会清除其他磁盘缓存。替换预览引擎后重启服务。引擎不可用时预览接口返回 502，`/health/ready` 返回 503。
