using System.Text;

namespace ContentAutomatorX.Application.Services;

/// <summary>
/// Canonicalizes URLs so the same page fetched with different tracking params,
/// casing, or trailing slashes dedups to one <c>NormalizedUrl</c>. Deliberately
/// conservative: path casing and non-tracking query values are preserved so
/// genuinely different pages are never merged.
/// </summary>
public static class UrlNormalizer
{
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
        { "fbclid", "gclid", "ref", "ref_src", "igshid", "mc_cid", "mc_eid" };

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.HostNameType is UriHostNameType.Unknown || uri.Host.Length == 0) return null;

        var query = uri.Query.TrimStart('?');
        var kept = query.Length == 0
            ? []
            : query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p =>
                {
                    var name = p.Split('=', 2)[0];
                    return !name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                        && !TrackingParams.Contains(name);
                })
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : uri.AbsolutePath;
        if (path.Length == 0) path = "/";

        var sb = new StringBuilder();
        sb.Append(uri.Scheme).Append("://").Append(uri.Host);
        if (!uri.IsDefaultPort) sb.Append(':').Append(uri.Port);
        sb.Append(path);
        if (kept.Count > 0) sb.Append('?').Append(string.Join('&', kept));
        return sb.ToString();
    }
}
