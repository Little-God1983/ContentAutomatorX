using System.Net;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using AngleSharp.Html.Parser;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class RedditConnector(HttpClient http) : ISourceConnector
{
    public string Type => SourceTypes.Reddit;

    private static readonly HtmlParser HtmlParser = new();

    private record RedditConfig(string Subreddit, string? Sort = null, string? Timeframe = null, int? Limit = null);

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<RedditConfig>(source.ConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid Reddit config");

        var sort = config.Sort ?? "hot";
        var limit = config.Limit ?? 25;
        var timeframe = config.Timeframe ?? "week";

        var url = $"https://www.reddit.com/r/{config.Subreddit}/{sort}.json?limit={limit}&t={timeframe}&raw_json=1";
        using var request = BuildRequest(url);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            // Reddit blocks the public .json endpoints for non-browser clients,
            // but keeps the Atom feed open (no score/selftext-as-markdown there).
            return await FetchViaAtomAsync(config.Subreddit, sort, limit, timeframe, ct);
        }
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

        return feed.Items.Select(item =>
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
                MetadataJson: """{"via":"rss"}""",
                PublishedAt: item.PublishDate == default ? null : item.PublishDate);
        }).ToList();
    }

    private static HttpRequestMessage BuildRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ContentAutomatorX/1.0 (content aggregation)");
        return request;
    }
}
