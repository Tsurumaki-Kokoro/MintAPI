# API 错误响应

控制器错误统一返回 `application/problem+json`，成功响应保留原有图片或 JSON。
即使请求声明 `Accept: image/png`，错误也返回 JSON，保留真实状态码。

```json
{
  "status": 404,
  "code": "SCORE_NOT_FOUND",
  "message": "该模式下未查询到你在此谱面的成绩",
  "traceId": "00-..."
}
```

机器人按 `code` 判断业务结果，不要解析 `message` 或将所有非 200 都视为查无记录。

| 状态 | code | 含义 |
| --- | --- | --- |
| 400 | INVALID_ARGUMENT | 请求参数无效 |
| 404 | USER_NOT_BOUND | 平台账号未绑定 |
| 404 | OSU_USER_NOT_FOUND | osu! 用户不存在 |
| 404 | BEATMAP_NOT_FOUND | 谱面不存在 |
| 404 | SCORE_NOT_FOUND | 指定模式或筛选条件下没有成绩 |
| 404 | LOCAL_SCORE_NOT_COLLECTED | 无榜谱面本地尚未收录，不能据此断言没玩过 |
| 404 | RECENT_PLAY_NOT_FOUND | 没有符合条件的最近游玩 |
| 404 | BEST_PLAY_NOT_FOUND | 没有该模式的 BP |
| 404 | HISTORY_NOT_FOUND | 没有玩家历史数据 |
| 404 | SCORE_PAGE_NOT_FOUND | 页码超出已有成绩范围 |
| 404 | RECORD_NOT_FOUND | 其他资源或记录未找到 |
| 401 | UNAUTHORIZED | 身份凭据无效 |
| 403 | FORBIDDEN | 没有访问权限 |
| 410 | RESOURCE_EXPIRED | 资源已过期 |
| 409 | CONFLICT | 当前状态与请求冲突 |
| 502 | OSU_API_UNAVAILABLE | 上游服务查询失败，请稍后重试 |
| 503 | SERVICE_BUSY | 资源繁忙，可按 Retry-After 重试 |
| 500 | RENDER_FAILED | 图片生成失败 |
| 500 | INTERNAL_ERROR | 其他服务内部错误 |

日志中的 API error / API exception 记录包含状态、code、traceId、请求方法、路径和白名单定位参数。
业务查无记录使用 Information，服务故障使用 Error；异常日志保留堆栈。
文件日志的 traceId 同时关联控制器已有的异常日志与最终错误响应。
不记录请求令牌、完整请求头或完整查询字符串；服务故障响应不暴露异常详情。

机器人应优先处理已列出的 code，并为未知 code 保留通用提示。
鉴权中间件的 403、静态资源以及未匹配路由不属于控制器错误契约，仍保留原有行为。

## 新接口实现与验证

错误定义集中在 `MintAPI/Errors/ErrorCatalog.cs`。控制器只选择定义：

```csharp
if (binding is null)
    return ApiErrors.Result(ErrorCatalog.UserNotBound);

// 内部原因只写日志，不进入 JSON。
return ApiErrors.Result(ErrorCatalog.InternalError,
    diagnostic: "Score has no beatmap info");
```

全局过滤器补齐 traceId、记录业务/故障日志并固定 JSON 类型。捕获异常时需记录原始异常对象；不捕获时由全局异常处理记录。`ApiLookupException` 接受目录定义，用来标识明确的资源查询 404。
错误提示可调整，错误码语义保持稳定；新代码不依赖旧文字分类器。

新接口的 HTTP 测试复用 `ApiErrorAssertions.AssertAsync(response, ErrorCatalog.ScoreNotFound)`，共用断言检查状态码、JSON 类型、code、message、traceId 和内部诊断隔离。
`ApiErrorContractTests` 覆盖公共传输和日志机制；`ScoreErrorContractTests` 示例覆盖真实成绩接口的未绑定、官方空成绩、本地未收录、谱面缺失及上游故障。两层均应保留，公共测试不能代替接口业务分类测试。

预览生成服务使用 `502 / PREVIEW_UNAVAILABLE`，以区别 osu! 上游查询失败。

`POST /users/bindings` 用于筛选调用方提供的候选账号，每批最多 100 人：无绑定为成功的空列表；无效候选列表使用 `INVALID_ARGUMENT`，数据库失败由全局错误处理返回 `INTERNAL_ERROR`。不把数据库故障当作未绑定。
