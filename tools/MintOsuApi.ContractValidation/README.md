# MintOsuApi 响应契约检查

供客户端开发者使用，检查真实 osu! 响应与客户端模型是否匹配。工具独立于 MintAPI 服务运行，不需要 MySQL 或 Redis；在线采集需要 `MintAPI/appsettings.Development.json` 中的 osu! 凭证。

## 采集与回放

在仓库根目录执行：

```sh
dotnet run --project tools/MintOsuApi.ContractValidation -- "$PWD" tools/MintOsuApi.ContractValidation/cases.json MintAPI/cache/mintosuapi-validation/run

dotnet run --project tools/MintOsuApi.ContractValidation -- "$PWD" tools/MintOsuApi.ContractValidation/cases.json MintAPI/cache/mintosuapi-validation/run --replay
```

首次采集访问官方接口；回放使用同一目录，不需要网络或凭证。请求间隔至少 1.1 秒，遇到 429 会停止并记录 Retry-After，不自动重试。工具仅允许公开读取和只读难度计算，不执行聊天、论坛写入或需要用户授权的读取。

采集目录包含原始响应、请求信息、时间、HTTP 版本及校验值，不记录认证头或密钥。响应和令牌缓存保存在忽略目录中，不应提交到仓库。

## 用例清单

每个用例包含唯一的 `name`、客户端 `method`、使用 C# 参数名的 `arguments`，可选 `resultPath` 指定响应数组。枚举支持 C# 名称；模式使用 `osu`、`taiko`、`fruits`、`mania`，Mods 可用传统缩写。省略参数使用方法默认值。

可复制 `cases.json` 并删减为需要检查的用例。公开 ID 可能失效，失败时应先核对资源是否仍存在。

## 结果与语义检查

回放比较原始 JSON 与类型转换结果，差异或失败时退出码非零。空数组没有差异不表示已覆盖数组元素的字段。该工具只检查已采集响应，不替代完整 OpenAPI 校验或空值、错误、授权场景测试。

```sh
python3 tools/MintOsuApi.ContractValidation/check_semantics.py MintAPI/cache/mintosuapi-validation/run
```

语义检查依赖清单中的用例名、玩家 ID 与分页关系；更换样本时应同步修改预期。可保留的匿名样本放入 `../mint-osuapi/tests/MintOsuApi.Tests/Fixtures/`。
