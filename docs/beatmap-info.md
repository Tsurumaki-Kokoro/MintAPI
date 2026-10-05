# 谱面与谱面集信息图

原 `GET /beatmap/info` 已拆为两个接口，调用方需按查询对象迁移：

| 对象 | 接口 | 参数 |
| --- | --- | --- |
| 单张谱面 | `GET /beatmap/beatmap` | `beatmap_id` |
| 谱面集 | `GET /beatmap/beatmapset` | `beatmapset_id` |

对应 ID 必填且必须为正整数；缺失或无效返回 400。两个接口均支持 `theme=default`（默认）与 `theme=yaowan`，返回 `image/png`。其他主题返回 400。

```text
/beatmap/beatmap?beatmap_id=1949106&theme=default
/beatmap/beatmapset?beatmapset_id=933630&theme=default
```

单谱面沿用现有难度资料、各模式 PP 参考、局部难度曲线和 Mods 对比；谱面集沿用谱面集资料与各难度列表。文件读取和渲染失败返回 500。`/beatmap/cover` 的参数与行为保持不变。

时间难度图仅在 BPM 发生变化时显示与时间轴对齐的分段 BPM 带，不增加难度曲线。区间宽度表示持续时间，过窄的区间只保留分界刻度；图例旁显示 BPM 范围。谱面信息图不显示区间编号和详细变化列表。数据使用原速 BPM，忽略滑条速度变化；恒定 BPM 不显示标注，图片高度保持原样。

## 独立 BPM 明细图

```text
/beatmap/bpm?beatmap_id=4313549
/beatmap/bpm?beatmap_id=5148257
/beatmap/bpm?beatmap_id=5148257&include_details=true
```

返回 2000px 宽的完整 `image/png` 长图，不分页，包含完整 BPM 时间阶梯曲线、密集变速区间的局部放大图，以及所有区间明细。阶梯曲线按真实时间维持各区间 BPM，变化点垂直跳变，不做平滑插值；放大图保留独立时间刻度和 BPM 纵轴。明细使用较小字号，显示开始时间、结束时间、持续秒数、原 BPM、当前 BPM 和变化幅度。

`include_details` 默认 `false`，仅显示曲线和局部放大图；设为 `true` 时添加完整明细表格。图片按实际内容确定高度，统计与 BPM 数据保持完整。

时间显示到毫秒，按原速计算；滑条速度点和重复相同 BPM 点不作为变化。恒定 BPM 在此接口显示一个区间和一条水平曲线。

`beatmap_id` 必填且必须为正整数；缺少有效 ID 返回 400。无可用 BPM 数据返回 404，文件读取或渲染失败返回 500。响应头 `X-Bpm-Segment-Count` 表示完整区间数量，不再提供 `page` 参数或分页响应头。最后区间截至最后物件时间。

变速回归样本：`4313549`（Mei / LEGGENDARIA）与 `5148257`（Ren'ai = Seido x Ninshikiryoku / DIFFICULTY=CSxBPM），覆盖逐段加速、结尾减速与密集短区间变速。测试使用保存的官方 `.osu` 与 API 元数据，无需联网。
