using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Sources;

namespace ContentAutomatorX.UnitTests;

public class RssConnectorTests
{
    [Fact]
    public async Task Parses_rss_items_with_external_ids()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-rss.xml", "application/rss+xml");
        var connector = new RssConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Rss, DisplayName = "blog",
            ConfigJson = """{"feedUrl":"https://example.com/feed"}"""
        };

        var items = await connector.FetchAsync(source);

        Assert.Equal(2, items.Count);
        Assert.Equal("post-1", items[0].ExternalId);
        Assert.Equal("First Post", items[0].Title);
        Assert.Equal("https://example.com/1", items[0].Url);
        Assert.Contains("first post", items[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(items[0].PublishedAt);
    }
}
