using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class RssConnector(HttpClient http) : ISourceConnector
{
    public string Type => SourceTypes.Rss;

    private static readonly HtmlParser HtmlParser = new();

    private record RssConfig(string FeedUrl, bool SplitLinkedStories = false);

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<RssConfig>(source.ConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid RSS config");

        await using var stream = await http.GetStreamAsync(config.FeedUrl, ct);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        return config.SplitLinkedStories
            ? ExtractLinkedStories(feed, config.FeedUrl)
            : feed.Items.Select(item => new FetchedItem(
                ExternalId: item.Id ?? item.Links.FirstOrDefault()?.Uri.ToString() ?? item.Title.Text,
                Title: item.Title?.Text ?? "(untitled)",
                Url: item.Links.FirstOrDefault()?.Uri.ToString(),
                Author: item.Authors.FirstOrDefault()?.Name ?? item.Authors.FirstOrDefault()?.Email,
                Body: (item.Summary?.Text ?? (item.Content as TextSyndicationContent)?.Text ?? "").Trim(),
                MetadataJson: "{}",
                PublishedAt: item.PublishDate == default ? null : item.PublishDate)).ToList();
    }

    /// <summary>
    /// Digest/aggregator mode: every external link inside a post's body becomes its own
    /// item — title from the surrounding paragraph's bold heading (or the link text),
    /// body from the paragraph, url = the linked original source.
    /// </summary>
    private static List<FetchedItem> ExtractLinkedStories(SyndicationFeed feed, string feedUrl)
    {
        var feedHost = Uri.TryCreate(feedUrl, UriKind.Absolute, out var fu) ? fu.Host : "";
        var results = new List<FetchedItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rank = 0;

        foreach (var item in feed.Items)
        {
            var html = FullContentHtml(item);
            if (string.IsNullOrWhiteSpace(html)) continue;
            var postUrl = item.Links.FirstOrDefault()?.Uri.ToString();

            var doc = HtmlParser.ParseDocument(html);
            foreach (var noscript in doc.QuerySelectorAll("noscript").ToList()) noscript.Remove();

            foreach (var anchor in doc.QuerySelectorAll("a[href]"))
            {
                var href = anchor.GetAttribute("href")!;
                if (!Uri.TryCreate(href, UriKind.Absolute, out var uri)) continue;
                if (uri.Scheme is not ("http" or "https")) continue;
                if (uri.Host.Equals(feedHost, StringComparison.OrdinalIgnoreCase)) continue; // post-internal link
                if (!seen.Add(href)) continue;

                var paragraph = anchor.Closest("p") ?? anchor.ParentElement;
                var heading = paragraph?.QuerySelector("strong,b,h1,h2,h3")?.TextContent.Trim();
                var anchorText = anchor.TextContent.Trim();
                var paragraphText = paragraph?.TextContent.Trim() ?? "";

                var title = !string.IsNullOrWhiteSpace(heading) ? heading
                    : anchorText.Length > 3 && !IsGenericLinkText(anchorText) ? anchorText
                    : Truncate(paragraphText, 90);
                if (string.IsNullOrWhiteSpace(title)) title = href;

                rank++;
                results.Add(new FetchedItem(
                    ExternalId: href,
                    Title: title,
                    Url: href,
                    Author: item.Authors.FirstOrDefault()?.Name,
                    Body: Truncate(paragraphText, 4000),
                    MetadataJson: JsonSerializer.Serialize(new { via = "rss-links", post = postUrl, rank }),
                    PublishedAt: item.PublishDate == default ? null : item.PublishDate));
            }
        }
        return results;
    }

    private static bool IsGenericLinkText(string text) =>
        text.Length > 60 ||
        text.Equals("read more", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("here", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("link", StringComparison.OrdinalIgnoreCase) ||
        text.Equals("source", StringComparison.OrdinalIgnoreCase);

    /// <summary>Prefers the full article HTML (RSS content:encoded) over the short description.</summary>
    private static string? FullContentHtml(SyndicationItem item)
    {
        foreach (var ext in item.ElementExtensions)
            if (ext.OuterName == "encoded" && ext.OuterNamespace == "http://purl.org/rss/1.0/modules/content/")
                return ext.GetReader().ReadElementContentAsString();
        return (item.Content as TextSyndicationContent)?.Text ?? item.Summary?.Text;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
