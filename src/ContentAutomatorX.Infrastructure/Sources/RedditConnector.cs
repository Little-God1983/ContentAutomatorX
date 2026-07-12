using System.Text.Json;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class RedditConnector(HttpClient http) : ISourceConnector
{
    public string Type => SourceTypes.Reddit;

    private record RedditConfig(string Subreddit, string? Sort = null, string? Timeframe = null, int? Limit = null);

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<RedditConfig>(source.ConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid Reddit config");

        var sort = config.Sort ?? "hot";
        var url = $"https://www.reddit.com/r/{config.Subreddit}/{sort}.json?limit={config.Limit ?? 25}&t={config.Timeframe ?? "week"}&raw_json=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ContentAutomatorX/1.0 (content aggregation)");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
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
                MetadataJson: $"{{\"score\":{score}}}",
                PublishedAt: createdUtc));
        }
        return results;
    }
}
