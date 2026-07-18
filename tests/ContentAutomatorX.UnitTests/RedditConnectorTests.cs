using System.Net;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Sources;

namespace ContentAutomatorX.UnitTests;

public class RedditConnectorTests
{
    [Fact]
    public async Task Parses_posts_and_builds_correct_url()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-reddit.json", "application/json");
        var connector = new RedditConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Reddit, DisplayName = "sd",
            ConfigJson = """{"subreddit":"StableDiffusion","sort":"top","timeframe":"week"}"""
        };

        var items = await connector.FetchAsync(source);

        Assert.Equal(2, items.Count);
        Assert.Equal("abc123", items[0].ExternalId);
        Assert.Equal("New model released", items[0].Title);
        Assert.StartsWith("https://www.reddit.com/r/StableDiffusion/", items[0].Url);
        Assert.Contains("\"score\":456", items[0].MetadataJson);
        Assert.Contains("\"rank\":1", items[0].MetadataJson);
        Assert.Contains("\"rank\":2", items[1].MetadataJson);
        Assert.Equal("bob", items[0].Author);

        var requestUrl = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("/r/StableDiffusion/top.json", requestUrl);
        Assert.Contains("t=week", requestUrl);
        Assert.True(handler.Requests[0].Headers.UserAgent.Count > 0, "must send a User-Agent");
    }

    [Fact]
    public async Task Config_with_only_subreddit_defaults_to_hot_limit20_and_week()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-reddit.json", "application/json");
        var connector = new RedditConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Reddit, DisplayName = "x",
            ConfigJson = """{"subreddit":"x"}"""
        };

        await connector.FetchAsync(source);

        var requestUrl = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("/r/x/hot.json", requestUrl);
        Assert.Contains("limit=20", requestUrl);
        Assert.Contains("t=week", requestUrl);
    }

    [Fact]
    public async Task Json_403_falls_back_to_atom_feed()
    {
        var handler = new StubHttpHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith(".json")
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(File.ReadAllText("Fixtures/sample-reddit-atom.xml"),
                        System.Text.Encoding.UTF8, "application/atom+xml")
                });
        var connector = new RedditConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Reddit, DisplayName = "sd",
            ConfigJson = """{"subreddit":"StableDiffusion"}"""
        };

        var items = await connector.FetchAsync(source);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/r/StableDiffusion/hot/.rss", handler.Requests[1].RequestUri!.ToString());
        Assert.True(handler.Requests[1].Headers.UserAgent.Count > 0, "fallback must send a User-Agent");

        Assert.Equal(2, items.Count);
        Assert.Equal("abc123", items[0].ExternalId);           // "t3_" prefix stripped -> dedup matches .json ids
        Assert.Equal("New model released", items[0].Title);
        Assert.Equal("https://www.reddit.com/r/StableDiffusion/comments/abc123/new_model_released/", items[0].Url);
        Assert.Equal("bob", items[0].Author);
        Assert.Contains("Weights are out now.", items[0].Body); // HTML stripped to text
        Assert.DoesNotContain("<", items[0].Body);
        Assert.Contains("\"via\":\"rss\"", items[0].MetadataJson);
        Assert.Contains("\"rank\":1", items[0].MetadataJson);
        Assert.Contains("\"rank\":2", items[1].MetadataJson);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 15, 16, 29, TimeSpan.Zero), items[0].PublishedAt);
        Assert.Equal("def456", items[1].ExternalId);
    }

    [Fact]
    public async Task Atom_fallback_failure_still_throws()
    {
        var handler = new StubHttpHandler(req =>
            new HttpResponseMessage(req.RequestUri!.AbsolutePath.EndsWith(".json")
                ? HttpStatusCode.Forbidden
                : HttpStatusCode.TooManyRequests));
        var connector = new RedditConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Reddit, DisplayName = "sd",
            ConfigJson = """{"subreddit":"StableDiffusion"}"""
        };

        await Assert.ThrowsAsync<HttpRequestException>(() => connector.FetchAsync(source));
    }

    [Fact]
    public async Task Post_missing_permalink_has_null_url()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-reddit-no-permalink.json", "application/json");
        var connector = new RedditConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Reddit, DisplayName = "x",
            ConfigJson = """{"subreddit":"x"}"""
        };

        var items = await connector.FetchAsync(source);

        Assert.Single(items);
        Assert.Equal("noperma1", items[0].ExternalId);
        Assert.Null(items[0].Url);
    }
}
