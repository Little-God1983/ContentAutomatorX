using System.Net;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Infrastructure.Newsletter;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContentAutomatorX.UnitTests;

public class YouTubeThumbnailResolverTests
{
    private static YouTubeThumbnailResolver Resolver(StubHttpHandler handler) =>
        new(new HttpClient(handler), NullLogger<YouTubeThumbnailResolver>.Instance);

    [Fact]
    public async Task Successful_probe_returns_the_high_res_thumbnail()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var url = await Resolver(handler).ResolveAsync("abc123");

        Assert.Equal(YouTubeUrl.MaxResThumbnail("abc123"), url);
    }

    [Fact]
    public async Task Non_success_status_falls_back_to_the_always_available_thumbnail()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var url = await Resolver(handler).ResolveAsync("abc123");

        Assert.Equal(YouTubeUrl.FallbackThumbnail("abc123"), url);
    }

    [Fact]
    public async Task Http_request_exception_falls_back_to_the_always_available_thumbnail()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("offline"));

        var url = await Resolver(handler).ResolveAsync("abc123");

        Assert.Equal(YouTubeUrl.FallbackThumbnail("abc123"), url);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_yielding_the_fallback()
    {
        // A caller-initiated cancellation must surface as OperationCanceledException, not be
        // swallowed like an HttpClient timeout would be.
        var cts = new CancellationTokenSource();
        var handler = new StubHttpHandler(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException(null, null, cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Resolver(handler).ResolveAsync("abc123", cts.Token));
    }
}
