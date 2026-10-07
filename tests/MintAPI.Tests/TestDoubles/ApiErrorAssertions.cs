using System.Net.Http.Json;
using MintAPI.Errors;

namespace MintAPI.Tests.TestDoubles;

/// <summary>新接口 HTTP 测试复用的错误契约断言。</summary>
public static class ApiErrorAssertions
{
    public static async Task<ApiError> AssertAsync(HttpResponseMessage response, ErrorDefinition expected, params string[] privateValues)
    {
        Assert.Equal(expected.Status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.NotNull(error);
        Assert.Equal(expected.Status, error.Status);
        Assert.Equal(expected.Code, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(string.IsNullOrWhiteSpace(error.TraceId));
        Assert.DoesNotContain("diagnostic", body, StringComparison.OrdinalIgnoreCase);
        foreach (var value in privateValues) Assert.DoesNotContain(value, body);
        return error;
    }
}
