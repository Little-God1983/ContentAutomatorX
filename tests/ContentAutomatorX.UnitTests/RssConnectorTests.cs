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

        Assert.Equal(3, items.Count);
        Assert.Equal("post-1", items[0].ExternalId);
        Assert.Equal("First Post", items[0].Title);
        Assert.Equal("https://example.com/1", items[0].Url);
        Assert.Contains("first post", items[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(items[0].PublishedAt);
    }

    [Fact]
    public async Task Split_mode_turns_every_external_link_into_an_item()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-rss-digest.xml", "application/rss+xml");
        var connector = new RssConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Rss, DisplayName = "digest",
            ConfigJson = """{"feedUrl":"https://aisearch.example.com/feed","splitLinkedStories":true}"""
        };

        var items = await connector.FetchAsync(source);

        // 2 stories + 1 sponsor; same-host subscribe link, noscript link,
        // and the text-less image/CDN ad anchor are all skipped
        Assert.Equal(3, items.Count);
        Assert.DoesNotContain(items, i => i.Url!.Contains("substackcdn"));

        Assert.Equal("LongCat 2.0 released.", items[0].Title);            // paragraph's bold heading
        Assert.Equal("https://longcat.example/blog/2.0/", items[0].Url);  // the original source
        Assert.Equal("https://longcat.example/blog/2.0/", items[0].ExternalId);
        Assert.Contains("1M-token context", items[0].Body);
        Assert.Contains("\"via\":\"rss-links\"", items[0].MetadataJson);
        Assert.Contains("digest-1", items[0].MetadataJson);               // provenance: the digest post
        Assert.Contains("\"rank\":1", items[0].MetadataJson);
        Assert.NotNull(items[0].PublishedAt);

        Assert.Equal("Anthropic launched Sonnet 5.", items[1].Title);
        Assert.Equal("https://anthropic.example/news/sonnet-5", items[1].Url);

        // sponsor link has no bold heading -> anchor text becomes the title (easy to keyword-exclude)
        Assert.Equal("Try SponsorTool for free today!", items[2].Title);
    }

    [Fact]
    public async Task Limit_caps_returned_items()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-rss-digest.xml", "application/rss+xml");
        var connector = new RssConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Rss, DisplayName = "digest",
            ConfigJson = """{"feedUrl":"https://aisearch.example.com/feed","splitLinkedStories":true,"limit":2}"""
        };

        var items = await connector.FetchAsync(source);

        Assert.Equal(2, items.Count);
        Assert.Equal("LongCat 2.0 released.", items[0].Title);   // newest-first feed order kept
    }

    [Fact]
    public async Task Split_mode_off_keeps_one_item_per_post()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-rss-digest.xml", "application/rss+xml");
        var connector = new RssConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Rss, DisplayName = "digest",
            ConfigJson = """{"feedUrl":"https://aisearch.example.com/feed"}"""
        };

        var items = await connector.FetchAsync(source);

        Assert.Single(items);
        Assert.Equal("HUGE AI NEWS: LongCat 2.0, Sonnet 5", items[0].Title);
        Assert.Equal("https://aisearch.example.com/p/digest-1", items[0].Url);
    }

    [Fact]
    public async Task Item_without_guid_or_author_falls_back_external_id_to_link_and_leaves_author_null()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-rss.xml", "application/rss+xml");
        var connector = new RssConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Rss, DisplayName = "blog",
            ConfigJson = """{"feedUrl":"https://example.com/feed"}"""
        };

        var items = await connector.FetchAsync(source);

        var third = items[2];
        Assert.Equal("Third Post", third.Title);
        Assert.Equal("https://example.com/3", third.ExternalId);
        Assert.Equal("https://example.com/3", third.Url);
        Assert.Null(third.Author);
    }
}
