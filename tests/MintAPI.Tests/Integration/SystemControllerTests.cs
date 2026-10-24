using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MintAPI.Controllers;
using MintAPI.Errors;
using MintAPI.Middleware;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;

namespace MintAPI.Tests.Integration;

public sealed class SystemControllerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Info_requires_key_and_health_reports_readiness_without_disclosing_dependencies(bool ready)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ApiKey"] = "private-test-key", ["PlayerWatch:Enabled"] = "false", ["ConnectionStrings:Redis"] = "private-cache-address" });
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>()).AddApplicationPart(typeof(SystemController).Assembly);
        builder.Services.AddSingleton(new RuntimeComponentState { ChromiumVersion = "156.test", PreviewVersion = "preview.test" });
        builder.Services.AddSingleton<ServiceInformation>();
        var probeResolutions = 0;
        builder.Services.AddScoped<IReadinessProbe>(_ => { probeResolutions++; return new Probe(ready); });
        await using var app = builder.Build();
        app.UseMiddleware<ApiKeyMiddleware>();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
            using var forbidden = await http.GetAsync("/api/info");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            using var live = await http.GetAsync("/health/live");
            live.EnsureSuccessStatusCode();
            Assert.Equal(0, probeResolutions);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
            request.Headers.Accept.ParseAdd("image/png");
            using var response = await http.SendAsync(request);
            if (ready) response.EnsureSuccessStatusCode();
            else
            {
                await ApiErrorAssertions.AssertAsync(response, ErrorCatalog.ServiceNotReady, "private-cache-address", "private-test-key");
                Assert.Equal(TimeSpan.FromSeconds(10), response.Headers.RetryAfter?.Delta);
            }
            http.DefaultRequestHeaders.Add("access_token", "private-test-key");
            var info = await http.GetFromJsonAsync<JsonElement>("/api/info");
            Assert.Equal("MintAPI", info.GetProperty("name").GetString());
            Assert.Equal("1.0", info.GetProperty("apiVersion").GetString());
            var features = info.GetProperty("features").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Contains("help.image", features);
            Assert.Contains("beatmap.preview", features);
            Assert.DoesNotContain("player.watch.group", features);
            Assert.DoesNotContain("private-", info.ToString());
            Assert.Equal(1, probeResolutions);
        }
        finally { await app.StopAsync(); }
    }

    private sealed class Probe(bool ready) : IReadinessProbe
    {
        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(ready);
    }
}
