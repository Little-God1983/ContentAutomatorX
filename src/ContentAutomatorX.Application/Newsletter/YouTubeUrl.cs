using System.Diagnostics.CodeAnalysis;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Pure URL work. The HEAD probe that decides which thumbnail actually exists lives
/// behind IYouTubeThumbnailResolver, because Application does not do HTTP.</summary>
public static class YouTubeUrl
{
    public static bool TryGetVideoId(string? url, [NotNullWhen(true)] out string? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var candidate = host.ToLowerInvariant() switch
        {
            "youtu.be" => segments.FirstOrDefault(),
            "youtube.com" or "m.youtube.com" => segments switch
            {
                ["watch"] => QueryValue(uri.Query, "v"),
                ["shorts", var s, ..] => s,
                ["embed", var e, ..] => e,
                _ => null
            },
            _ => null
        };

        if (string.IsNullOrWhiteSpace(candidate)) return false;
        id = candidate;
        return true;
    }

    public static string MaxResThumbnail(string videoId) =>
        $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg";

    /// <summary>Always exists, for every video, at 480x360.</summary>
    public static string FallbackThumbnail(string videoId) =>
        $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            if (split.Length == 2 && split[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(split[1]);
        }
        return null;
    }
}
