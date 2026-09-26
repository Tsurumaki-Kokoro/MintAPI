namespace HitCircleAPI.Services;

/// <summary>
/// 调用方应当稍后重试：资源暂时不够用，不是请求本身有问题。
/// <para>
/// 全局处理器把它映射成 503 + Retry-After 而不是 500，调用方（bot）才有依据
/// 退避重试，而不是把"服务器忙"当成"这个功能坏了"报给用户。
/// </para>
/// </summary>
public abstract class RetryableException(string message) : Exception(message);
