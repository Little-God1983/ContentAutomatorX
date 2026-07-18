using System.Text.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class WebsiteConnector(HttpClient http) : ISourceConnector
{
    public string Type => SourceTypes.Website;

    private const int BodyMaxChars = 8000;
    private const int MinLinkTextLength = 20; // auto mode: skip nav/chrome links

    private record WebsiteConfig(string Url, string? Mode, string? ItemSelector, int MaxItems = 10);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly HtmlParser Parser = new();

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<WebsiteConfig>(source.ConfigJson, JsonOpts)
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid Website config");
        var baseUri = new Uri(config.Url);

        var listingHtml = await http.GetStringAsync(config.Url, ct);
        using var doc = await Parser.ParseDocumentAsync(listingHtml, ct);

        var anchors = (config.Mode == "selector" && !string.IsNullOrWhiteSpace(config.ItemSelector)
                ? doc.QuerySelectorAll(config.ItemSelector).OfType<IHtmlAnchorElement>()
                    .Concat(doc.QuerySelectorAll(config.ItemSelector)
                        .SelectMany(e => e.QuerySelectorAll("a").OfType<IHtmlAnchorElement>()))
                : doc.QuerySelectorAll("article a[href], main a[href]").OfType<IHtmlAnchorElement>()
                    .Where(a => (a.TextContent?.Trim().Length ?? 0) >= MinLinkTextLength))
            .Where(a => !string.IsNullOrWhiteSpace(a.GetAttribute("href")))
            .ToList();

        var items = new List<FetchedItem>();
        var seen = new HashSet<string>();
        foreach (var a in anchors)
        {
            if (items.Count >= config.MaxItems) break;
            if (!Uri.TryCreate(baseUri, a.GetAttribute("href"), out var abs)) continue;
            var url = abs.GetLeftPart(UriPartial.Path); // canonical: strip query/fragment
            if (!seen.Add(url)) continue;

            var title = a.TextContent.Trim();
            if (title.Length == 0) continue;
            items.Add(new FetchedItem(
                ExternalId: url, Title: title, Url: url, Author: null,
                Body: await FetchBodyAsync(url, ct),
                MetadataJson: "{}", PublishedAt: null));
        }
        return items;
    }

    private async Task<string> FetchBodyAsync(string url, CancellationToken ct)
    {
        try
        {
            var html = await http.GetStringAsync(url, ct);
            using var doc = await Parser.ParseDocumentAsync(html, ct);
            var text = (doc.QuerySelector("main") ?? doc.QuerySelector("article") ?? doc.Body)?
                .TextContent ?? "";
            text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= BodyMaxChars ? text : text[..BodyMaxChars];
        }
        catch
        {
            return ""; // item still lands with title+url; per-source failure isolation stays in the pipeline
        }
    }
}
