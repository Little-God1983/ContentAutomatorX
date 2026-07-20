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

    private static Tenant TestTenant(string branding = "{}") => new()
    {
        Name = "Acme", Slug = "acme", BrandingJson = branding,
        SenderIdentity = "Acme Media, Musterstr. 1, Berlin, DE"
    };

    private static List<IssueSection> AllSectionTypes() =>
    [
        new() { Position = 0, Type = SectionTypes.Header, BodyMd = "Hi friends!" },
        new() { Position = 1, Type = SectionTypes.Topic, Title = "Big <News>", BodyMd = "It happened.",
                ImageUrl = "https://ex.com/pic.png", LinkUrl = "https://ex.com/story" },
        new() { Position = 2, Type = SectionTypes.Sponsor, Title = "Acme Tools", BodyMd = "Ship faster.",
                ImageUrl = "https://ex.com/logo.png", LinkUrl = "https://acme.dev", LinkText = "Try Acme" },
        new() { Position = 3, Type = SectionTypes.Button, LinkUrl = "https://ex.com/cta", LinkText = "Visit" },
        new() { Position = 4, Type = SectionTypes.Divider },
        new() { Position = 5, Type = SectionTypes.Footer, BodyMd = "Bye! — Chris" },
    ];

    [Fact]
    public void Render_produces_one_email_document_with_every_section_type()
    {
        var html = SectionHtmlRenderer.Render(AllSectionTypes(), TestTenant(), "AI Weekly #1");

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("AI Weekly #1", html);                      // title h1
        Assert.Contains("Hi friends!", html);                       // header
        Assert.Contains("Big &lt;News&gt;", html);                  // topic title, encoded
        Assert.Contains("src=\"https://ex.com/pic.png\"", html);    // topic image
        Assert.Contains("Read more", html);                         // topic link
        Assert.Contains("SPONSORED", html);                         // sponsor label
        Assert.Contains("Try Acme", html);                          // sponsor CTA
        Assert.Contains("https://ex.com/cta", html);                // button
        Assert.Contains("Bye! — Chris", html);                      // footer
        Assert.Contains(SectionHtmlRenderer.UnsubscribeToken, html);
        Assert.Contains("Acme Media, Musterstr. 1, Berlin, DE", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_applies_branding_and_ignores_invalid_accent()
    {
        var branded = SectionHtmlRenderer.Render(AllSectionTypes(),
            TestTenant("""{"accentColorHex":"#7C3AED","logoUrl":"https://ex.com/l.png","fontKey":"georgia"}"""),
            "t");
        Assert.Contains("color:#7C3AED;", branded);
        Assert.Contains("src=\"https://ex.com/l.png\"", branded);
        Assert.Contains("Georgia", branded);

        var evil = SectionHtmlRenderer.Render(AllSectionTypes(),
            TestTenant("""{"accentColorHex":"red;} body{display:none"}"""), "t");
        Assert.Contains(EmailHtmlRenderer.DefaultAccent, evil);
        Assert.DoesNotContain("display:none", evil);
    }

    [Fact]
    public void Render_drops_non_http_image_and_link_urls()
    {
        var sections = new List<IssueSection>
        {
            new() { Position = 0, Type = SectionTypes.Topic, Title = "T",
                    BodyMd = "b", ImageUrl = "javascript:alert(1)", LinkUrl = "file://c/x" },
        };
        var html = SectionHtmlRenderer.Render(sections, TestTenant(), "t");
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file://", html);
    }

    [Fact]
    public void Render_orders_by_position_not_list_order()
    {
        var sections = new List<IssueSection>
        {
            new() { Position = 1, Type = SectionTypes.Footer, BodyMd = "LAST" },
            new() { Position = 0, Type = SectionTypes.Header, BodyMd = "FIRST" },
        };
        var html = SectionHtmlRenderer.Render(sections, TestTenant(), "t");
        Assert.True(html.IndexOf("FIRST", StringComparison.Ordinal) < html.IndexOf("LAST", StringComparison.Ordinal));
    }

    [Fact]
    public void ToMarkdown_exports_all_section_types_without_the_compliance_footer()
    {
        var md = SectionHtmlRenderer.ToMarkdown(AllSectionTypes());
        Assert.Contains("Hi friends!", md);
        Assert.Contains("## Big <News>", md);
        Assert.Contains("[Read more](https://ex.com/story)", md);
        Assert.Contains("**Sponsored: Acme Tools**", md);
        Assert.Contains("[Try Acme](https://acme.dev)", md);
        Assert.Contains("[Visit](https://ex.com/cta)", md);
        Assert.Contains("---", md);
        Assert.Contains("Bye! — Chris", md);
        Assert.DoesNotContain("Unsubscribe", md);
    }
}
