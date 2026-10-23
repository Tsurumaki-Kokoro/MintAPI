using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MintAPI.Controllers;
using MintAPI.Errors;
using MintAPI.Middleware;
using MintAPI.Rendering.HelpTheme;
using MintAPI.Services;
using MintAPI.Tests.TestDoubles;

namespace MintAPI.Tests.Integration;

public sealed class HelpControllerTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("busy")]
    public async Task Help_returns_png_or_problem_json_for_image_only_accept(string scenario)
    {
        var renderer = new Renderer(scenario);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>())
            .AddApplicationPart(typeof(HelpController).Assembly);
        builder.Services.AddSingleton<IRenderService>(renderer);
        builder.Services.AddSingleton<HelpTheme>();
        builder.Services.AddExceptionHandler<RetryableExceptionHandler>();
        builder.Services.AddProblemDetails();
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
            http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
            using var response = await http.GetAsync("/help/image");
            if (scenario == "success")
            {
                response.EnsureSuccessStatusCode();
                Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
                Assert.Equal(Renderer.Png, await response.Content.ReadAsByteArrayAsync());
                Assert.Contains("HELP", renderer.Html, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("/statme", renderer.Html);
                Assert.Contains("Powered By MintAPI", renderer.Html);
                Assert.Equal(HelpTheme.Width, renderer.Width);
            }
            else
            {
                await ApiErrorAssertions.AssertAsync(response, scenario == "busy" ? ErrorCatalog.ServiceBusy : ErrorCatalog.RenderFailed, "private-render-reason");
                if (scenario == "busy") Assert.NotNull(response.Headers.RetryAfter);
            }
        }
        finally { await app.StopAsync(); }
    }

    private sealed class Renderer(string scenario) : IRenderService
    {
        public static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10];
        public string Html = "";
        public int Width;
        public Task<byte[]> RenderHtmlAsync(string html, int width, int height, CancellationToken cancellationToken = default)
        {
            Html = html;
            Width = width;
            if (scenario == "failure") throw new InvalidOperationException("private-render-reason");
            if (scenario == "busy") throw new RenderBusyException();
            return Task.FromResult(Png);
        }
    }
}
