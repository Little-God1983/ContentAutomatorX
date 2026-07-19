using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.UnitTests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Garbage_input_returns_null(string? input) =>
        Assert.Null(UrlNormalizer.Normalize(input));

    [Fact]
    public void Lowercases_scheme_and_host_but_not_path() =>
        Assert.Equal("https://example.com/Some/Path",
            UrlNormalizer.Normalize("HTTPS://EXAMPLE.COM/Some/Path"));

    [Fact]
    public void Drops_fragment() =>
        Assert.Equal("https://example.com/post",
            UrlNormalizer.Normalize("https://example.com/post#section-2"));

    [Theory]
    [InlineData("https://example.com/p?utm_source=rss&utm_medium=feed")]
    [InlineData("https://example.com/p?fbclid=abc123")]
    [InlineData("https://example.com/p?gclid=xyz&IGSHID=99")]
    [InlineData("https://example.com/p?ref=hn&mc_cid=1&mc_eid=2&ref_src=tw")]
    public void Strips_tracking_params(string input) =>
        Assert.Equal("https://example.com/p", UrlNormalizer.Normalize(input));

    [Fact]
    public void Keeps_and_sorts_real_query_params() =>
        Assert.Equal("https://example.com/search?a=2&q=hello",
            UrlNormalizer.Normalize("https://example.com/search?q=hello&utm_campaign=x&a=2"));

    [Fact]
    public void Trims_trailing_slash_but_keeps_root() =>
        Assert.Equal("https://example.com/blog/post",
            UrlNormalizer.Normalize("https://example.com/blog/post/"));

    [Fact]
    public void Root_url_normalizes_to_single_slash()
    {
        Assert.Equal("https://example.com/", UrlNormalizer.Normalize("https://example.com"));
        Assert.Equal("https://example.com/", UrlNormalizer.Normalize("https://example.com/"));
    }

    [Fact]
    public void Preserves_non_default_port_drops_default_port()
    {
        Assert.Equal("https://example.com:8443/x", UrlNormalizer.Normalize("https://example.com:8443/x"));
        Assert.Equal("https://example.com/x", UrlNormalizer.Normalize("https://example.com:443/x"));
    }

    [Fact]
    public void Equivalent_messy_urls_converge()
    {
        var a = UrlNormalizer.Normalize("HTTPS://Example.com/post/?utm_source=a#top");
        var b = UrlNormalizer.Normalize("https://example.com/post?utm_medium=b");
        Assert.NotNull(a);
        Assert.Equal(a, b);
    }
}
