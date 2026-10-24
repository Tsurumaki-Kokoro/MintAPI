# API 错误响应

控制器错误统一返回 `application/problem+json`，成功响应为接口约定的图片、视频、文本或 JSON。
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
| 502 | PREVIEW_UNAVAILABLE | 预览引擎不可用或执行失败 |
| 503 | SERVICE_BUSY | 资源繁忙，可按 Retry-After 重试 |
| 503 | SERVICE_NOT_READY | 依赖未就绪，可按 Retry-After 重试 |
| 500 | RENDER_FAILED | 图片生成失败 |
| 500 | INTERNAL_ERROR | 其他服务内部错误 |

排查问题时提供 `traceId`，可在服务日志中查找对应请求。不要解析 `message` 或将所有非 200 视为查无记录；未知 code 使用通用错误提示。

鉴权中间件的 403、静态资源及未匹配路由不属于控制器错误契约。

## 新接口实现与验证

错误定义集中在 `MintAPI/Errors/ErrorCatalog.cs`。控制器只选择定义：

```csharp
if (binding is null)
    return ApiErrors.Result(ErrorCatalog.UserNotBound);

// 内部原因只写日志，不进入 JSON。
return ApiErrors.Result(ErrorCatalog.InternalError,
    diagnostic: "Score has no beatmap info");
```

新增业务结果时，在错误目录添加定义，并为端点更新 XML 注释和功能文档。保持已有 code 的含义及 HTTP 状态稳定。

捕获异常时记录原始异常对象，不把路径、凭证或上游响应传给客户端；未捕获异常由全局处理器记录。

新增端点使用真实 HTTP 测试，复用 `ApiErrorAssertions.AssertAsync`，覆盖业务错误、依赖失败与 `Accept: image/png`。公共错误测试见 `ApiErrorContractTests`，成绩业务示例见 `ScoreErrorContractTests`。

## 订阅与批量查询

- `POST /users/bindings`：每批最多 100 人，未绑定返回成功空列表；参数无效为 `INVALID_ARGUMENT`。
- 玩家订阅：无变化返回成功空结果，取消、过期或游标失效为 `RESOURCE_EXPIRED`。
- 群管理：身份不完整或 scope 冲突为 `INVALID_ARGUMENT`，归属不匹配为 `RECORD_NOT_FOUND`；零匹配查询或取消仍成功。

接口参数与状态说明见 [玩家功能](player-features.md)。
