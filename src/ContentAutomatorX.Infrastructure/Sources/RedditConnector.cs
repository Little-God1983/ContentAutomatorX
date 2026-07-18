using System.Net;
using System.Net.Http.Headers;
using System.ServiceModel.Syndication;
using System.Text;
using System.Text.Json;
using System.Xml;
using AngleSharp.Html.Parser;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class RedditConnector(HttpClient http, ICredentialStore? credentials = null) : ISourceConnector
{
    public string Type => SourceTypes.Reddit;

    /// <summary>Global credential blob: {"clientId":"...","clientSecret":"..."} for a Reddit "script" app.</summary>
    public const string CredentialName = "reddit-api";

    private const string UserAgent = "windows:ContentAutomatorX:v1.0 (content aggregation)";

    private static readonly HtmlParser HtmlParser = new();
    private static readonly SemaphoreSlim TokenLock = new(1, 1);
    private static string? _cachedToken;
    private static string? _cachedTokenClientId;
    private static DateTimeOffset _tokenExpires;

    private record RedditConfig(string Subreddit, string? Sort = null, string? Timeframe = null, int? Limit = null);
    private record ApiCredentials(string ClientId, string ClientSecret);

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<RedditConfig>(source.ConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid Reddit config");

        var sort = (config.Sort ?? "hot").ToLowerInvariant();
        var limit = Math.Clamp(config.Limit ?? 20, 1, 20);
        var timeframe = (config.Timeframe ?? "week").ToLowerInvariant();

        var creds = await LoadCredentialsAsync(ct);
        if (creds is not null)
            return await FetchViaOAuthAsync(creds, config.Subreddit, sort, limit, timeframe, ct);

        var url = $"https://www.reddit.com/r/{config.Subreddit}/{sort}.json?limit={limit}&t={timeframe}&raw_json=1";
        using var request = BuildRequest(url);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            // Reddit blocks the public .json endpoints for non-browser clients,
            // but keeps the Atom feed open (no scores there — configure the
            // Reddit API on the Sources page to get full data).
            return await FetchViaAtomAsync(config.Subreddit, sort, limit, timeframe, ct);
        }
        response.EnsureSuccessStatusCode();
        return ParseListing(await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>Forces a fresh token so the UI can verify pasted credentials. Throws on rejection.</summary>
    public async Task TestCredentialsAsync(CancellationToken ct = default)
    {
        var creds = await LoadCredentialsAsync(ct)
            ?? throw new InvalidOperationException("No Reddit API credentials stored.");
        InvalidateToken();
        await GetTokenAsync(creds, ct);
    }

    private async Task<ApiCredentials?> LoadCredentialsAsync(CancellationToken ct)
    {
        if (credentials is null) return null;
        var secret = await credentials.GetAsync(CredentialName, ct);
        if (string.IsNullOrWhiteSpace(secret)) return null;
        try
        {
            var parsed = JsonSerializer.Deserialize<ApiCredentials>(secret,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return string.IsNullOrWhiteSpace(parsed?.ClientId) || string.IsNullOrWhiteSpace(parsed.ClientSecret)
                ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<FetchedItem>> FetchViaOAuthAsync(
        ApiCredentials creds, string subreddit, string sort, int limit, string timeframe, CancellationToken ct)
    {
        var url = $"https://oauth.reddit.com/r/{subreddit}/{sort}?limit={limit}&t={timeframe}&raw_json=1";
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(creds, ct);
            using var request = BuildRequest(url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                InvalidateToken();   // token expired/revoked — re-authenticate once
                continue;
            }
            response.EnsureSuccessStatusCode();
            return ParseListing(await response.Content.ReadAsStringAsync(ct));
        }
    }

    private async Task<string> GetTokenAsync(ApiCredentials creds, CancellationToken ct)
    {
        if (CachedTokenFor(creds) is { } cached) return cached;
        await TokenLock.WaitAsync(ct);
        try
        {
            if (CachedTokenFor(creds) is { } cachedAgain) return cachedAgain;

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://www.reddit.com/api/v1/access_token");
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{creds.ClientId}:{creds.ClientSecret}")));
            request.Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { ["grant_type"] = "client_credentials" });

            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new InvalidOperationException(
                    "Reddit API credentials rejected — check the client id and secret on the Sources page.");
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException(
                    "Reddit did not issue a token — check the client id and secret on the Sources page.");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

            _cachedToken = token;
            _cachedTokenClientId = creds.ClientId;
            _tokenExpires = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return token;
        }
        finally { TokenLock.Release(); }
    }

    private static string? CachedTokenFor(ApiCredentials creds) =>
        _cachedToken is not null && _cachedTokenClientId == creds.ClientId
            && DateTimeOffset.UtcNow < _tokenExpires - TimeSpan.FromMinutes(5)
        ? _cachedToken : null;

    private static void InvalidateToken()
    {
        _cachedToken = null;
        _cachedTokenClientId = null;
    }

    private static List<FetchedItem> ParseListing(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var results = new List<FetchedItem>();
        foreach (var child in doc.RootElement.GetProperty("data").GetProperty("children").EnumerateArray())
        {
            var d = child.GetProperty("data");
            var score = d.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
            var createdUtc = d.TryGetProperty("created_utc", out var c)
                ? DateTimeOffset.FromUnixTimeSeconds((long)c.GetDouble())
                : (DateTimeOffset?)null;
            results.Add(new FetchedItem(
                ExternalId: d.GetProperty("id").GetString()!,
                Title: d.GetProperty("title").GetString() ?? "(untitled)",
                Url: d.TryGetProperty("permalink", out var p) && p.GetString() is { Length: > 0 } permalink
                    ? "https://www.reddit.com" + permalink
                    : null,
                Author: d.TryGetProperty("author", out var a) ? a.GetString() : null,
                Body: d.TryGetProperty("selftext", out var t) ? (t.GetString() ?? "") : "",
                // rank = listing position, so the chosen sort (hot etc.) survives into the Inbox
                MetadataJson: $"{{\"score\":{score},\"rank\":{results.Count + 1}}}",
                PublishedAt: createdUtc));
        }
        return results;
    }

    private async Task<IReadOnlyList<FetchedItem>> FetchViaAtomAsync(
        string subreddit, string sort, int limit, string timeframe, CancellationToken ct)
    {
        var url = $"https://www.reddit.com/r/{subreddit}/{sort}/.rss?limit={limit}&t={timeframe}";
        using var request = BuildRequest(url);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        return feed.Items.Select((item, index) =>
        {
            var id = item.Id ?? "";
            var author = item.Authors.FirstOrDefault()?.Name;
            var html = (item.Content as TextSyndicationContent)?.Text ?? item.Summary?.Text ?? "";
            return new FetchedItem(
                // feed ids are "t3_<id>"; strip so dedup matches items ingested via .json
                ExternalId: id.StartsWith("t3_") ? id[3..] : id,
                Title: item.Title?.Text ?? "(untitled)",
                Url: item.Links.FirstOrDefault()?.Uri.ToString(),
                Author: author?.StartsWith("/u/") == true ? author[3..] : author,
                Body: html.Length == 0 ? "" : HtmlParser.ParseDocument(html).Body?.TextContent.Trim() ?? "",
                // the Atom feed carries no score — rules with MinScore treat these as 0
                MetadataJson: $"{{\"via\":\"rss\",\"rank\":{index + 1}}}",
                PublishedAt: item.PublishDate == default ? null : item.PublishDate);
        }).ToList();
    }

    private static HttpRequestMessage BuildRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Reddit's recommended "<platform>:<app>:<version>" UA is not a valid
        // .NET product token, so bypass header validation
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return request;
    }
}
