using System.Net;
using ContentAutomatorX.Web.Services;
using Xunit;

namespace ContentAutomatorX.IntegrationTests;

public class NewsletterImageStagingStoreTests
{
    // Minimal stand-in HTTP handler (IntegrationTests has no shared stub).
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private static NewsletterImageStagingStore Make(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "cx-stage-" + Guid.NewGuid().ToString("N"));
        return new NewsletterImageStagingStore(dir, new HttpClient(new StubHandler(_ => new(HttpStatusCode.OK))));
    }

    [Fact]
    public void UrlFor_formats_or_nulls()
    {
        Assert.Null(NewsletterImageStagingStore.UrlFor(null));
        Assert.Null(NewsletterImageStagingStore.UrlFor(""));
        Assert.Equal("/newsletter-images/x.png", NewsletterImageStagingStore.UrlFor("x.png"));
    }

    [Fact]
    public async Task SaveStream_writes_a_uniquely_named_file()
    {
        var store = Make(out var dir);
        await using var src = new MemoryStream(new byte[] { 1, 2, 3 });
        var key = await store.SaveStreamAsync(src, ".png", CancellationToken.None);
        Assert.EndsWith(".png", key);
        Assert.True(File.Exists(Path.Combine(dir, key)));
    }

    [Fact]
    public void Delete_ignores_traversal_and_missing()
    {
        var store = Make(out _);
        store.Delete("../secret");   // must not throw, must not escape
        store.Delete("nope.png");    // missing, must not throw
        store.Delete(null);
    }

    [Fact]
    public async Task Delete_removes_a_staged_file()
    {
        var store = Make(out var dir);
        await using var src = new MemoryStream(new byte[] { 1, 2, 3 });
        var key = await store.SaveStreamAsync(src, ".png", CancellationToken.None);
        store.Delete(key);
        Assert.False(File.Exists(Path.Combine(dir, key)));
    }
}
