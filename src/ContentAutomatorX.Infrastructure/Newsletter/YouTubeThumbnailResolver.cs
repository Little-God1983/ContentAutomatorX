using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace ContentAutomatorX.Infrastructure.Newsletter;

public class YouTubeThumbnailResolver(HttpClient http, ILogger<YouTubeThumbnailResolver> log)
    : IYouTubeThumbnailResolver
{
    public async Task<string> ResolveAsync(string videoId, CancellationToken ct = default)
    {
        var maxRes = YouTubeUrl.MaxResThumbnail(videoId);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, maxRes);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return maxRes;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller's own token fired (not the HttpClient timeout, which also throws
            // TaskCanceledException but leaves ct un-cancelled). Propagate so the caller knows its
            // own cancellation fired, instead of pretending the probe merely failed.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline, or slow (HttpClient timeout). Falling back beats failing the user's save.
            log.LogDebug(ex, "maxresdefault probe failed for {VideoId}; using hqdefault", videoId);
        }
        return YouTubeUrl.FallbackThumbnail(videoId);
    }
}
