namespace ContentAutomatorX.Domain.Abstractions;

/// <summary>Decides which of a video's thumbnails actually exists. maxresdefault.jpg is published
/// only for videos uploaded above 720p, so it cannot be used blindly — a dead image in a sent
/// newsletter is worse than a low-resolution one.</summary>
public interface IYouTubeThumbnailResolver
{
    /// <summary>Returns an absolute thumbnail URL. Never throws: a failed probe falls back.</summary>
    Task<string> ResolveAsync(string videoId, CancellationToken ct = default);
}
