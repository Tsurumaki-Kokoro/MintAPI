namespace HitCircleAPI.Services;

/// <summary>可重试的资源不足错误，映射为 503。</summary>
public abstract class RetryableException(string message) : Exception(message);
