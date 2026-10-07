namespace MintAPI.Errors;

/// <summary>稳定的错误契约；修改提示文字不能改变 Code 的含义。</summary>
public sealed record ErrorDefinition(int Status, string Code, string Message);

public static class ErrorCatalog
{
    public static readonly ErrorDefinition InvalidArgument = new(400, "INVALID_ARGUMENT", "请求参数有误");
    public static readonly ErrorDefinition Unauthorized = new(401, "UNAUTHORIZED", "请提供有效的身份凭据");
    public static readonly ErrorDefinition Forbidden = new(403, "FORBIDDEN", "没有访问该资源的权限");
    public static readonly ErrorDefinition UserNotBound = new(404, "USER_NOT_BOUND", "请先绑定 osu! 账号");
    public static readonly ErrorDefinition OsuUserNotFound = new(404, "OSU_USER_NOT_FOUND", "未找到该 osu! 用户");
    public static readonly ErrorDefinition BeatmapNotFound = new(404, "BEATMAP_NOT_FOUND", "未找到该谱面或谱面集，请检查 ID");
    public static readonly ErrorDefinition ScoreNotFound = new(404, "SCORE_NOT_FOUND", "未查询到符合模式及筛选条件的成绩");
    public static readonly ErrorDefinition LocalScoreNotCollected = new(404, "LOCAL_SCORE_NOT_COLLECTED", "本地暂未收录该无榜谱面的成绩，未收录不代表没有游玩过");
    public static readonly ErrorDefinition RecentPlayNotFound = new(404, "RECENT_PLAY_NOT_FOUND", "未查询到符合条件的最近游玩记录");
    public static readonly ErrorDefinition BestPlayNotFound = new(404, "BEST_PLAY_NOT_FOUND", "未查询到该模式下的 BP 成绩");
    public static readonly ErrorDefinition HistoryNotFound = new(404, "HISTORY_NOT_FOUND", "暂未查询到该玩家的历史数据");
    public static readonly ErrorDefinition ScorePageNotFound = new(404, "SCORE_PAGE_NOT_FOUND", "该页没有成绩，请检查页码");
    public static readonly ErrorDefinition RecordNotFound = new(404, "RECORD_NOT_FOUND", "未查询到相关记录");
    public static readonly ErrorDefinition Conflict = new(409, "CONFLICT", "当前状态与请求冲突");
    public static readonly ErrorDefinition ResourceExpired = new(410, "RESOURCE_EXPIRED", "该资源已过期，请重新创建");
    public static readonly ErrorDefinition OsuApiUnavailable = new(502, "OSU_API_UNAVAILABLE", "osu! 服务暂时不可用，请稍后重试");
    public static readonly ErrorDefinition PreviewUnavailable = new(502, "PREVIEW_UNAVAILABLE", "预览生成服务暂时不可用，请稍后重试");
    public static readonly ErrorDefinition ServiceBusy = new(503, "SERVICE_BUSY", "服务繁忙，请稍后重试");
    public static readonly ErrorDefinition RenderFailed = new(500, "RENDER_FAILED", "图片生成失败，请稍后重试");
    public static readonly ErrorDefinition InternalError = new(500, "INTERNAL_ERROR", "服务发生异常，请联系维护者");
}
