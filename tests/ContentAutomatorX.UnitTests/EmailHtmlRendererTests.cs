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
        Assert.Contains("<a style=\"color:#1e88e5;\" href=\"https://ex.com\">", html);
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

    [Fact]
    public void Encodes_title_with_special_characters()
    {
        var html = EmailHtmlRenderer.Render("Test content", "AI & <Weekly> #1");
        Assert.Contains("AI &amp; &lt;Weekly&gt; #1", html);
        Assert.DoesNotContain("<Weekly>", html);
    }

    [Fact]
    public void Styles_anchors_that_carry_a_title_attribute()
    {
        var html = EmailHtmlRenderer.Render("see [docs](https://ex.com/d \"Docs\")", "Test");
        Assert.Contains("<a style=\"color:#1e88e5;\" href=\"https://ex.com/d\" title=\"Docs\">", html);
    }

    [Fact]
    public void Strips_javascript_scheme_hrefs_but_keeps_http_and_mailto()
    {
        var html = EmailHtmlRenderer.Render(
            "[bad](javascript:alert(1)) [ok](https://ex.com) [mail](mailto:a@ex.com)", "Test");

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<a style=\"color:#1e88e5;\" href=\"#\">", html);
        Assert.Contains("<a style=\"color:#1e88e5;\" href=\"https://ex.com\">", html);
        Assert.Contains("<a style=\"color:#1e88e5;\" href=\"mailto:a@ex.com\">", html);
    }

    [Fact]
    public void Styles_ordered_lists_like_unordered_ones()
    {
        var html = EmailHtmlRenderer.Render("1. first\n2. second", "t");

        Assert.Contains("<ol style=\"margin:0 0 14px;padding-left:24px;\">", html);
        Assert.Contains("<li style=\"margin:0 0 6px;\">", html);
    }

    [Fact]
    public void Styles_tables_with_borders_and_padding()
    {
        var html = EmailHtmlRenderer.Render("| A | B |\n| --- | --- |\n| 1 | 2 |", "t");

        Assert.Contains("<table style=\"border-collapse:collapse;width:100%;margin:0 0 14px;\">", html);
        Assert.Contains("<th style=\"border:1px solid #dddddd;padding:6px 10px;text-align:left;background:#f7f7f7;\">", html);
        Assert.Contains("<td style=\"border:1px solid #dddddd;padding:6px 10px;\">", html);
    }

    [Fact]
    public void Styles_horizontal_rules()
    {
        var html = EmailHtmlRenderer.Render("above\n\n---\n\nbelow", "t");

        Assert.Contains("<hr style=\"border:none;border-top:1px solid #dddddd;margin:20px 0;\" />", html);
    }

    [Fact]
    public void Null_and_empty_markdown_render_the_shell_without_crashing()
    {
        foreach (var md in new[] { null, "", "   " })
        {
            var html = EmailHtmlRenderer.Render(md!, "Empty");

            Assert.Contains("<html", html);
            Assert.Contains("Empty", html);
        }
    }
}
