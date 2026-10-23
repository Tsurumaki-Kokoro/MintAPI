using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MintAPI.Errors;
using MintAPI.Services.PlayerFeatures;
using MintAPI.Tests.TestDoubles;

namespace MintAPI.Tests.Integration;

public sealed class PlayerWatchGroupTests
{
    [Fact]
    public async Task Structured_and_legacy_subscriptions_share_group_and_read_only_queries_keep_expiry()
    {
        await using var f = new PlayerFeatureTests.Fixture(); await f.InitAsync();
        var legacy = await f.Watches.SubscribeAsync("42", 0, "qq:group:123", default);
        await f.Watches.SubscribeAsync("42", 1, "qq:group:123", default);
        await f.Watches.SubscribeAsync("43", 0, "qq:group:123", default);
        await f.Watches.SubscribeAsync("42", 0, "qq:group:456", default);
        await f.Watches.SubscribeAsync("42", 0, "discord:group:123", default);
        await f.Watches.SubscribeAsync("42", 0, "private:qq:42", default);
        await using var app = await PlayerFeatureTests.AppAsync(f, new PlayerFeatureTests.CaptureRenderer());
        using var http = PlayerFeatureTests.Http(app);
        using var created = await http.PostAsJsonAsync("/players/watch/subscriptions", new { user = "42", platform = "QQ", groupId = "123", gameMode = 0 });
        created.EnsureSuccessStatusCode();
        Assert.Equal(legacy.Id, (await created.Content.ReadFromJsonAsync<SubscriptionResult>())!.Id);
        var expiration = (await f.Db.PlayerSubscriptions.SingleAsync(x => x.Id == legacy.Id)).ExpiresAt;
        var calls = f.Api.CallCount;
        var groupList = await http.GetFromJsonAsync<PlayerGroupPage>("/players/watch/groups?platform=qq");
        Assert.Equal(2, groupList!.Total);
        var first = Assert.Single(groupList.Items, x => x.GroupId == "123");
        Assert.Equal(3, first.SubscriptionCount); Assert.Equal(2, first.UserCount);
        var page = await http.GetFromJsonAsync<PlayerSubscriptionPage>("/players/watch/subscriptions?platform=qq&group_id=123&page_size=2");
        Assert.Equal(3, page!.Total); Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, x => { Assert.Equal("qq", x.Platform); Assert.Equal("123", x.GroupId); Assert.NotNull(x.Username); });
        var last = await http.GetFromJsonAsync<PlayerSubscriptionPage>("/players/watch/subscriptions?platform=qq&group_id=123&page_size=2&page=2");
        Assert.Single(last!.Items); Assert.DoesNotContain(last.Items[0].Id, page.Items.Select(x => x.Id));
        var filtered = await http.GetFromJsonAsync<PlayerSubscriptionPage>("/players/watch/subscriptions?platform=qq&group_id=123&user_id=42&game_mode=1");
        Assert.Equal(1, filtered!.Total);
        var detail = await http.GetFromJsonAsync<PlayerSubscriptionInfo>($"/players/watch/subscriptions/{legacy.Id}?platform=qq&group_id=123");
        Assert.Equal(legacy.Cursor, detail!.StartCursor); Assert.Equal("123", detail.GroupId);
        Assert.Equal(calls, f.Api.CallCount);
        Assert.Equal(expiration, (await f.Db.PlayerSubscriptions.SingleAsync(x => x.Id == legacy.Id)).ExpiresAt);
    }

    [Fact]
    public async Task Group_cancellation_supports_player_mode_whole_group_and_keeps_other_groups_active()
    {
        await using var f = new PlayerFeatureTests.Fixture(); await f.InitAsync();
        var first = await f.Watches.SubscribeAsync("42", 0, "qq:group:123", default);
        await f.Watches.SubscribeAsync("42", 1, "qq:group:123", default);
        await f.Watches.SubscribeAsync("43", 0, "qq:group:123", default);
        var other = await f.Watches.SubscribeAsync("42", 0, "qq:group:456", default);
        var discord = await f.Watches.SubscribeAsync("42", 0, "discord:group:123", default);
        await using var app = await PlayerFeatureTests.AppAsync(f, new PlayerFeatureTests.CaptureRenderer());
        using var http = PlayerFeatureTests.Http(app);
        using (var wrong = await http.DeleteAsync($"/players/watch/subscriptions/{other.Id}?platform=qq&group_id=123"))
            await ApiErrorAssertions.AssertAsync(wrong, ErrorCatalog.RecordNotFound);
        using (var wrong = await http.GetAsync($"/players/watch/subscriptions/{other.Id}?platform=qq&group_id=123"))
            await ApiErrorAssertions.AssertAsync(wrong, ErrorCatalog.RecordNotFound);
        using (var removed = await http.DeleteAsync("/players/watch/subscriptions?platform=qq&group_id=123&user_id=42&game_mode=0"))
            Assert.Equal(1, (await removed.Content.ReadFromJsonAsync<PlayerSubscriptionsRemoved>())!.Removed);
        using (var missing = await http.GetAsync($"/players/watch/subscriptions/{first.Id}"))
            await ApiErrorAssertions.AssertAsync(missing, ErrorCatalog.ResourceExpired);
        using (var removed = await http.DeleteAsync("/players/watch/subscriptions?platform=qq&group_id=123"))
            Assert.Equal(2, (await removed.Content.ReadFromJsonAsync<PlayerSubscriptionsRemoved>())!.Removed);
        using (var removed = await http.DeleteAsync("/players/watch/subscriptions?platform=qq&group_id=123"))
            Assert.Equal(0, (await removed.Content.ReadFromJsonAsync<PlayerSubscriptionsRemoved>())!.Removed);
        Assert.Equal(2, await f.Db.PlayerSubscriptions.CountAsync());
        Assert.NotNull(await f.Watches.GetAsync(other.Id, null, default));
        Assert.Empty((await f.Watches.UpdatesAsync(other.Id, other.Cursor, default)).Updates);
        using (var removed = await http.DeleteAsync($"/players/watch/subscriptions/{discord.Id}?platform=discord&group_id=123"))
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    [Theory]
    [InlineData("/players/watch/subscriptions?platform=qq")]
    [InlineData("/players/watch/subscriptions?group_id=123")]
    [InlineData("/players/watch/subscriptions?platform=&group_id=123")]
    [InlineData("/players/watch/subscriptions?platform=qq&group_id=123&scope=other")]
    [InlineData("/players/watch/subscriptions?page_size=101")]
    [InlineData("/players/watch/subscriptions?game_mode=4")]
    [InlineData("/players/watch/groups?platform=")]
    public async Task Invalid_group_queries_return_problem_json_for_png_accept(string path)
    {
        await using var f = new PlayerFeatureTests.Fixture(); await f.InitAsync();
        await using var app = await PlayerFeatureTests.AppAsync(f, new PlayerFeatureTests.CaptureRenderer());
        using var http = PlayerFeatureTests.Http(app); http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
        using var response = await http.GetAsync(path);
        await ApiErrorAssertions.AssertAsync(response, ErrorCatalog.InvalidArgument);
    }

    [Fact]
    public async Task Empty_groups_expiry_and_partial_create_or_cancel_requests_are_explicit()
    {
        await using var f = new PlayerFeatureTests.Fixture(); await f.InitAsync();
        var expired = await f.Watches.SubscribeAsync("42", 0, "qq:group:123", default);
        (await f.Db.PlayerSubscriptions.SingleAsync()).ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await f.Db.SaveChangesAsync();
        await using var app = await PlayerFeatureTests.AppAsync(f, new PlayerFeatureTests.CaptureRenderer());
        using var http = PlayerFeatureTests.Http(app);
        Assert.Empty((await http.GetFromJsonAsync<PlayerGroupPage>("/players/watch/groups"))!.Items);
        var visible = await http.GetFromJsonAsync<PlayerSubscriptionPage>("/players/watch/subscriptions?include_expired=true");
        Assert.True(Assert.Single(visible!.Items).IsExpired);
        Assert.Single((await http.GetFromJsonAsync<PlayerGroupPage>("/players/watch/groups?include_expired=true"))!.Items);
        Assert.Empty((await http.GetFromJsonAsync<PlayerSubscriptionPage>("/players/watch/subscriptions?platform=qq&group_id=missing"))!.Items);
        foreach (var payload in new object[] { new { user = "42", platform = "qq" }, new { user = "42", platform = "qq", groupId = "123", scope = "other" }, new { user = "42" } })
        {
            using var response = await http.PostAsJsonAsync("/players/watch/subscriptions", payload);
            await ApiErrorAssertions.AssertAsync(response, ErrorCatalog.InvalidArgument);
        }
        using (var invalid = await http.DeleteAsync("/players/watch/subscriptions"))
            await ApiErrorAssertions.AssertAsync(invalid, ErrorCatalog.InvalidArgument);
        Assert.Equal(1, await f.Db.PlayerSubscriptions.CountAsync());
    }

    [Fact]
    public async Task Query_dependency_failure_is_not_misreported_as_empty_list()
    {
        await using var f = new PlayerFeatureTests.Fixture(); await f.InitAsync();
        await using var app = await PlayerFeatureTests.AppAsync(f, new PlayerFeatureTests.CaptureRenderer());
        await f.Db.Database.ExecuteSqlRawAsync("DROP TABLE player_subscription");
        using var http = PlayerFeatureTests.Http(app); http.DefaultRequestHeaders.Accept.ParseAdd("image/png");
        using var response = await http.GetAsync("/players/watch/subscriptions?platform=qq&group_id=123");
        await ApiErrorAssertions.AssertAsync(response, ErrorCatalog.InternalError, "no such table", "SELECT");
    }
}
