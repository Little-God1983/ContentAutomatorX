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
        Assert.Equal("bob", items[0].Author);

        var requestUrl = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("/r/StableDiffusion/top.json", requestUrl);
        Assert.Contains("t=week", requestUrl);
        Assert.True(handler.Requests[0].Headers.UserAgent.Count > 0, "must send a User-Agent");
    }
}
