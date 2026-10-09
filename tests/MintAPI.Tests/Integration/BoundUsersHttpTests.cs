using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MintAPI.Controllers;
using MintAPI.Data;
using MintAPI.Errors;
using MintAPI.Models.Entities;
using MintAPI.Tests.TestDoubles;

namespace MintAPI.Tests.Integration;

public sealed class BoundUsersHttpTests
{
    [Fact]
    public async Task Binding_discovery_filters_candidates_and_preserves_http_errors()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new UserModel { Platform = "qq", PlatformUid = "1", OsuUid = "123" });
        db.Users.Add(new UserModel { Platform = "discord", PlatformUid = "2", OsuUid = "456" });
        await db.SaveChangesAsync();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddControllers(options => options.Filters.Add<ApiErrorFilter>()).AddApplicationPart(typeof(UserRankingController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(_ => new UserRankingController(db, new RecordingOsuApiService(), null!));
        await using var app = builder.Build();
        app.MapControllers();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()) };
        using var success = await http.PostAsJsonAsync("/users/bindings", new UserRankingRequest("qq", ["1", "2", "1"]));
        success.EnsureSuccessStatusCode();
        var data = await success.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(new[] { "1" }, data.GetProperty("platform_uids").EnumerateArray().Select(x => x.GetString()));
        using var empty = await http.PostAsJsonAsync("/users/bindings", new UserRankingRequest("qq", ["missing"]));
        empty.EnsureSuccessStatusCode();
        Assert.Empty((await empty.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("platform_uids").EnumerateArray());
        http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
        using var invalid = await http.PostAsJsonAsync("/users/bindings", new UserRankingRequest("qq", []));
        await ApiErrorAssertions.AssertAsync(invalid, ErrorCatalog.InvalidArgument);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE user");
        using var failed = await http.PostAsJsonAsync("/users/bindings", new UserRankingRequest("qq", ["1"]));
        await ApiErrorAssertions.AssertAsync(failed, ErrorCatalog.InternalError, "SQLite", "no such table");
        await app.StopAsync();
    }
}
