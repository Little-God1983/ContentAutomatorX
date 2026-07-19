using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.UnitTests;

public class SectionHtmlRendererTests
{
    [Fact]
    public void EmailFonts_resolves_known_key_case_insensitively_and_falls_back()
    {
        Assert.Contains("Georgia", EmailFonts.Stack("GEORGIA"));
        Assert.Equal(EmailFonts.Stack(EmailFonts.DefaultKey), EmailFonts.Stack(null));
        Assert.Equal(EmailFonts.Stack(EmailFonts.DefaultKey), EmailFonts.Stack("comic-sans"));
    }

    [Fact]
    public void RenderFragment_returns_inline_styled_html_without_document_wrapper()
    {
        var html = EmailHtmlRenderer.RenderFragment("# Hi\n\nsee [x](https://ex.com)");
        Assert.DoesNotContain("<html", html);
        Assert.Contains("<h1 style=", html);
        Assert.Contains("<a style=\"color:#1e88e5;\" href=\"https://ex.com\">", html);
    }

    [Fact]
    public void RenderFragment_recolors_links_with_the_given_accent()
    {
        var html = EmailHtmlRenderer.RenderFragment("see [x](https://ex.com)", "#7C3AED");
        Assert.Contains("color:#7C3AED;", html);
        Assert.DoesNotContain("#1e88e5", html);
    }
}
