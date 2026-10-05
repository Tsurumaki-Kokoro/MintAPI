using Microsoft.Playwright;

namespace MintAPI.Tests.TestDoubles;

internal static class RenderingAttributionAssertions
{
    public static async Task CheckAsync(IPage page)
    {
        var stamp = page.Locator(".mint-attribution");
        Assert.Equal(1, await stamp.CountAsync());
        Assert.Matches(@"^Powered By MintAPI · 绘制日期 \d{4}-\d{2}-\d{2}$", await stamp.InnerTextAsync());
        Assert.True(await stamp.EvaluateAsync<bool>("e => { const r=e.getBoundingClientRect(), d=document.documentElement; return r.left>=0 && r.right>=d.clientWidth-120 && r.right<=d.clientWidth && r.bottom<=d.scrollHeight && d.scrollHeight-r.bottom<=48; }"));
        Assert.True(await page.Locator("footer").EvaluateAllAsync<bool>("els => els.every(e => e.getBoundingClientRect().bottom <= document.querySelector('.mint-attribution').getBoundingClientRect().top + 1)"));
    }
}
