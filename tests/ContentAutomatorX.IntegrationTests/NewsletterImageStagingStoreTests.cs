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

    private static byte[] PngBytes() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

    private static NewsletterImageStagingStore WithResponse(byte[] body, string contentType, out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "cx-stage-" + Guid.NewGuid().ToString("N"));
        var handler = new StubHandler(_ =>
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return msg;
        });
        return new NewsletterImageStagingStore(dir, new HttpClient(handler));
    }

    [Fact]
    public async Task SaveFromUrl_stages_a_valid_png()
    {
        var store = WithResponse(PngBytes(), "image/png", out var dir);
        var key = await store.SaveFromUrlAsync("https://host/img.png");
        Assert.EndsWith(".png", key);
        Assert.True(File.Exists(Path.Combine(dir, key)));
    }

    [Fact]
    public async Task SaveFromUrl_rejects_html_masquerading_as_png()
    {
        var store = WithResponse(System.Text.Encoding.UTF8.GetBytes("<html>nope</html>"), "image/png", out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveFromUrlAsync("https://host/x"));
    }

    [Fact]
    public async Task SaveFromUrl_rejects_disallowed_content_even_with_image_header()
    {
        // Body is not a real image (no magic bytes) though header claims image/png.
        var store = WithResponse(new byte[] { 0, 1, 2, 3, 4, 5 }, "image/png", out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveFromUrlAsync("https://host/x"));
    }

    [Fact]
    public async Task SaveFromUrl_rejects_relative_url()
    {
        var store = Make(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveFromUrlAsync("/not/absolute"));
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
