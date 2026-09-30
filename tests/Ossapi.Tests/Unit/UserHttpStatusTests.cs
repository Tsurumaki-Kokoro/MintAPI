using System.Net;

namespace Ossapi.Tests.Unit;

public sealed class UserHttpStatusTests
{
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{\"error\":null}") });
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task GetUser_preserves_http_status(HttpStatusCode status)
    {
        using var http = new HttpClient(new Handler(status)) { BaseAddress = new Uri("https://example.test/") };
        using var client = new OssapiClient(http);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetUserAsync("missing"));
        Assert.Equal(status, error.StatusCode);
    }
}
