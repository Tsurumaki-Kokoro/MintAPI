# 绑定用户排名

两个接口均接受同一平台的已绑定用户列表，最多 100 个 ID，重复 ID 去重。

- `POST /users/ranking?game_mode=0`：指定模式的所有用户。
- `POST /users/ranking/top5`：该列表在四个模式各自的前五名。

模式：0 osu!、1 taiko、2 catch、3 mania。请求体：

```json
{"platform":"qq","platformUids":["123","456"]}
```

两个接口默认返回 `image/png`。单模式为完整排行榜，四模式为 2×2 前五合图。添加 `format=json` 可返回原始数据。

单模式 JSON 响应示例：

```json
{"gameMode":0,"users":[{"platformUid":"123","osuUid":"987","username":"example","rank":100,"pp":12000.5,"acc":98.6}]}
```

四模式接口在 `format=json` 时返回四个上述结构组成的数组，按模式 0–3 排列，每个 `users` 最多五人。
图片显示头像和国旗；头像下载失败时显示占位标识，未知旗帜显示国家代码。JSON 同时包含 `avatarUrl`、`countryCode`。

rank 为全球排名，数字越小排名越高，无排名放最后。acc 为百分数。缺失统计返回 null；不足五人返回全部。多个平台账号绑定同一 osu! 用户时，排行榜保留各平台账号。

非法列表或模式返回 400；存在未绑定 ID 时返回 `404 / USER_NOT_BOUND`，不返回部分榜单。可先用 `POST /users/bindings` 筛选候选 ID，响应 `platform_uids` 仅包含已绑定账号。请求使用 `access_token` 鉴权。
