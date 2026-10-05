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
