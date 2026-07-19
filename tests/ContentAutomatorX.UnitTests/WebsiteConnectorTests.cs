using System.Net;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Sources;

namespace ContentAutomatorX.UnitTests;

public class WebsiteConnectorTests
{
    private static StubHttpHandler SiteHandler() => new(req =>
    {
        var path = req.RequestUri!.AbsolutePath;
        var file = path == "/blog" ? "Fixtures/sample-site-listing.html" : "Fixtures/sample-site-article.html";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(File.ReadAllText(file), System.Text.Encoding.UTF8, "text/html")
        };
    });

    private static Source Site(string config) => new()
    {
        Type = SourceTypes.Website, DisplayName = "blog", ConfigJson = config
    };

    [Fact]
    public async Task Auto_mode_extracts_article_links_with_absolute_urls_and_bodies()
    {
        var connector = new WebsiteConnector(new HttpClient(SiteHandler()));
        var items = await connector.FetchAsync(Site("""{"url":"https://blog.example.com/blog","mode":"auto"}"""));

        Assert.Contains(items, i => i.ExternalId == "https://blog.example.com/posts/alpha");
        Assert.Contains(items, i => i.ExternalId == "https://blog.example.com/posts/beta");
        var alpha = items.Single(i => i.ExternalId.EndsWith("/posts/alpha"));
        Assert.Equal("Alpha release notes for the new engine", alpha.Title);
        Assert.Contains("quick brown fox", alpha.Body);
        Assert.DoesNotContain(items, i => i.ExternalId.EndsWith("/nav")); // short link text filtered
    }

    [Fact]
    public async Task Auto_mode_skips_self_link_anchors_and_canonicalizes_query_and_fragment()
    {
        var connector = new WebsiteConnector(new HttpClient(SiteHandler()));
        var items = await connector.FetchAsync(Site("""{"url":"https://blog.example.com/blog","mode":"auto"}"""));

        // "#content" and other fragment-only/self-links resolve back to the listing page itself
        // and must not become spurious recurring items.
        Assert.DoesNotContain(items, i => i.ExternalId == "https://blog.example.com/blog");
        // Query string + fragment must be stripped so the same article isn't re-ingested under variants.
        Assert.Contains(items, i => i.ExternalId == "https://blog.example.com/posts/delta");
    }

    [Fact]
    public async Task Selector_mode_uses_the_configured_css_selector()
    {
        var connector = new WebsiteConnector(new HttpClient(SiteHandler()));
        var items = await connector.FetchAsync(Site(
            """{"url":"https://blog.example.com/blog","mode":"selector","itemSelector":".card a"}"""));

        var item = Assert.Single(items);
        Assert.Equal("https://blog.example.com/posts/gamma", item.ExternalId);
        Assert.Equal("Gamma model comparison megathread", item.Title);
    }

    [Fact]
    public async Task Body_fetch_failure_still_yields_the_item_with_empty_body()
    {
        var handler = new StubHttpHandler(req =>
            req.RequestUri!.AbsolutePath == "/blog"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(File.ReadAllText("Fixtures/sample-site-listing.html"),
                        System.Text.Encoding.UTF8, "text/html")
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var connector = new WebsiteConnector(new HttpClient(handler));

        var items = await connector.FetchAsync(Site("""{"url":"https://blog.example.com/blog","mode":"auto"}"""));

        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Equal("", i.Body));
    }

    [Fact]
    public async Task MaxItems_caps_the_result()
    {
        var connector = new WebsiteConnector(new HttpClient(SiteHandler()));
        var items = await connector.FetchAsync(Site(
            """{"url":"https://blog.example.com/blog","mode":"auto","maxItems":1}"""));
        Assert.Single(items);
    }

    [Fact]
    public async Task Cancellation_during_body_fetch_propagates_instead_of_yielding_empty_body()
    {
        var cts = new CancellationTokenSource();
        var requestCount = 0;
        var handler = new StubHttpHandler(req =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(File.ReadAllText("Fixtures/sample-site-listing.html"),
                        System.Text.Encoding.UTF8, "text/html")
                };
            }

            cts.Cancel();
            throw new TaskCanceledException(null, null, cts.Token);
        });
        var connector = new WebsiteConnector(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connector.FetchAsync(Site("""{"url":"https://blog.example.com/blog","mode":"auto"}"""), cts.Token));
    }

    [Fact]
    public async Task Title_internal_whitespace_is_collapsed_to_single_spaces()
    {
        // listing anchor text spans lines and runs of spaces: "Big\n        AI   News"
        const string anchorText = "Big\n        AI   News";
        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var html = path == "/blog"
                ? "<!DOCTYPE html><html><body><article><a href=\"/story\">" + anchorText + "</a></article></body></html>"
                : "<!DOCTYPE html><html><body><main>story body</main></body></html>";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
            };
        });
        var connector = new WebsiteConnector(new HttpClient(handler));

        var items = await connector.FetchAsync(Site("""{"url":"https://blog.example.com/blog","mode":"auto"}"""));

        var item = Assert.Single(items);
        Assert.Equal("Big AI News", item.Title);
    }

    [Fact]
    public async Task Selector_matching_both_container_and_anchor_yields_one_item_per_url()
    {
        // ".card, .card a" matches the anchor via both the container-descendant branch and
        // the direct-anchor branch; dedup via the `seen` hash-set must yield a single item.
        var handler = new StubHttpHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var html = path == "/blog"
                ? "<!DOCTYPE html><html><body><div class=\"card\"><a href=\"/one\">Story one headline</a></div></body></html>"
                : "<!DOCTYPE html><html><body><main>ignored</main></body></html>";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html")
            };
        });
        var connector = new WebsiteConnector(new HttpClient(handler));

        var items = await connector.FetchAsync(Site(
            """{"url":"https://blog.example.com/blog","mode":"selector","itemSelector":".card, .card a"}"""));

        var item = Assert.Single(items);
        Assert.Equal("https://blog.example.com/one", item.ExternalId);
    }
}
