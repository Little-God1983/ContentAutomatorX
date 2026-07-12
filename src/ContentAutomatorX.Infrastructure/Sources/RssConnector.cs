using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class RssConnector(HttpClient http) : ISourceConnector
{
    public string Type => SourceTypes.Rss;

    private record RssConfig(string FeedUrl);

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<RssConfig>(source.ConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid RSS config");

        await using var stream = await http.GetStreamAsync(config.FeedUrl, ct);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        return feed.Items.Select(item => new FetchedItem(
            ExternalId: item.Id ?? item.Links.FirstOrDefault()?.Uri.ToString() ?? item.Title.Text,
            Title: item.Title?.Text ?? "(untitled)",
            Url: item.Links.FirstOrDefault()?.Uri.ToString(),
            Author: item.Authors.FirstOrDefault()?.Name ?? item.Authors.FirstOrDefault()?.Email,
            Body: (item.Summary?.Text ?? (item.Content as TextSyndicationContent)?.Text ?? "").Trim(),
            MetadataJson: "{}",
            PublishedAt: item.PublishDate == default ? null : item.PublishDate)).ToList();
    }
}
