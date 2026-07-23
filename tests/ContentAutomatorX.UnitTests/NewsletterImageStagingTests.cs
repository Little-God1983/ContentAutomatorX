using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Xunit;

namespace ContentAutomatorX.UnitTests;

public class NewsletterImageStagingTests
{
    private static IssueSection Sec(string? key = null, string? url = null) =>
        new() { Type = SectionTypes.Topic, ImageKey = key, ImageUrl = url };

    [Fact]
    public void PreviewSrc_prefers_staged_key() =>
        Assert.Equal("/newsletter-images/a.png",
            NewsletterImageStaging.PreviewSrc(Sec(key: "a.png", url: "https://x/y.png")));

    [Fact]
    public void PreviewSrc_falls_back_to_hotlink() =>
        Assert.Equal("https://x/y.png", NewsletterImageStaging.PreviewSrc(Sec(url: "https://x/y.png")));

    [Fact]
    public void PreviewSrc_null_when_nothing() =>
        Assert.Null(NewsletterImageStaging.PreviewSrc(Sec()));

    [Fact]
    public void PushSrc_omits_staged_key() =>
        Assert.Null(NewsletterImageStaging.PushSrc(Sec(key: "a.png")));

    [Fact]
    public void PushSrc_keeps_hotlink() =>
        Assert.Equal("https://x/y.png", NewsletterImageStaging.PushSrc(Sec(url: "https://x/y.png")));
}
