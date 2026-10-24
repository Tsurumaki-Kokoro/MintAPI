# 谱面与谱面集信息图

按查询对象选择接口，使用 `access_token` 请求头鉴权：

| 对象 | 接口 | 参数 |
| --- | --- | --- |
| 单张谱面 | `GET /beatmap/beatmap` | `beatmap_id` |
| 谱面集 | `GET /beatmap/beatmapset` | `beatmapset_id` |

对应 ID 必填且必须为正整数；缺失或无效返回 400。两个接口均支持 `theme=default`（默认）与 `theme=yaowan`，返回 `image/png`。其他主题返回 400。

```text
/beatmap/beatmap?beatmap_id=1949106&theme=default
/beatmap/beatmapset?beatmapset_id=933630&theme=default
```

单谱面图片包含难度资料、各模式 PP 参考、局部难度曲线和 Mods 对比；谱面集图片包含基本资料与各难度列表。`GET /beatmap/cover` 可单独获取封面。错误格式见 [API 错误响应](api-errors.md)。

谱面信息图在 BPM 变化时显示分段 BPM 带，区间宽度表示持续时间。BPM 按原速统计，不包含滑条速度变化。详细区间请使用下方 BPM 接口。

## 独立 BPM 明细图

```text
/beatmap/bpm?beatmap_id=4313549
/beatmap/bpm?beatmap_id=5148257
/beatmap/bpm?beatmap_id=5148257&include_details=true
```

返回 2000px 宽的 `image/png` 长图，包含 BPM 阶梯曲线和密集变速区间的局部放大图，不分页。明细表显示开始时间、结束时间、持续秒数、原 BPM、当前 BPM 和变化幅度。

`include_details` 默认 `false`，仅显示曲线和局部放大图；设为 `true` 时添加完整明细表格。图片按实际内容确定高度，统计与 BPM 数据保持完整。

时间显示到毫秒，按原速计算；滑条速度点和重复相同 BPM 点不作为变化。恒定 BPM 在此接口显示一个区间和一条水平曲线。

`beatmap_id` 必填且必须为正整数；缺少有效 ID 返回 400。无可用 BPM 数据返回 404，文件读取或渲染失败返回 500。响应头 `X-Bpm-Segment-Count` 表示完整区间数量，不支持 `page` 参数。最后区间截至最后物件时间。
