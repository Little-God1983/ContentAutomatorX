using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class EmailHtmlRendererTests
{
    [Fact]
    public void Renders_headings_paragraphs_and_links_with_inline_styles()
    {
        var html = EmailHtmlRenderer.Render(
            "# Top stories\n\nBig [thing](https://ex.com) happened.\n\n- one\n- two", "AI Weekly #1");

        Assert.Contains("<html", html);
        Assert.Contains("AI Weekly #1", html);
        Assert.Contains("Top stories", html);
        Assert.Contains("href=\"https://ex.com\"", html);
        Assert.Contains("<li", html);
        Assert.Contains("style=", html);           // inline styles present
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Escapes_raw_html_in_the_markdown()
    {
        var html = EmailHtmlRenderer.Render("hello <script>alert(1)</script> world", "t");
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
