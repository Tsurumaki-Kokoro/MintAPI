using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MintAPI.Controllers;

namespace MintAPI.Tests.Integration;

public sealed class UserScoreHttpTests
{
    [Fact]
    public async Task Invalid_query_returns_original_error_instead_of_406()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ScoreController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(_ => new ScoreController(null!, null!, null!, null!, null!, NullLogger<ScoreController>.Instance));
        await using var app = builder.Build();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var response = await http.GetAsync("/score/user_score?platform=Test&platform_uid=123&beatmap_id=1949106&theme=invalid");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
            Assert.Contains("theme", await response.Content.ReadAsStringAsync());
        }
        finally { await app.StopAsync(); }
    }
}
