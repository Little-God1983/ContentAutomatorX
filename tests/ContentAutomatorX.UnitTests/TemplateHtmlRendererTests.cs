using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.UnitTests;

public class TemplateHtmlRendererTests
{
    private const string Template = """
        <!-- BLOCK: shell -->
        <html><head><title>{{issue_title}}</title></head>
        <body><span class="pre">{{preheader}}</span>{{sections}}
        <a href="{{unsubscribe_url}}">Unsubscribe</a></body></html>
        <!-- /BLOCK -->
        <!-- BLOCK: header -->
        <h1>{{title}}</h1>{{body_html}}
        <!-- /BLOCK -->
        <!-- BLOCK: topic -->
        <!-- IF: image --><img src="{{image_url}}" alt="{{title}}" /><!-- /IF -->
        <!-- IF: category --><span class="cat">{{category}} · {{reading_time}}</span><!-- /IF -->
        <h2>{{title}}</h2>{{body_html}}
        <!-- IF: link --><a class="more" href="{{link_url}}">{{link_text}}</a><!-- /IF -->
        <!-- /BLOCK -->
        <!-- BLOCK: video -->
        <!-- IF: thumbnail --><img class="thumb" src="{{thumbnail_url}}" /><!-- /IF -->
        <h2>{{title}}</h2><!-- IF: video --><a href="{{video_url}}">{{link_text}}</a><!-- /IF -->
        <!-- /BLOCK -->
        <!-- BLOCK: divider --><hr class="rule" /><!-- /BLOCK -->
        <!-- BLOCK: footer -->{{body_html}}<p>{{sender_identity}}</p><!-- /BLOCK -->
        """;

    private static Tenant MakeTenant() => new()
    {
        Name = "Into the Latent", Slug = "itl", SenderIdentity = "Christian Wenzl · Greven",
        BrandingJson = """{"accentColorHex":"#1AE6D5"}"""
    };

    private static IssueSection Section(string type, string? title = null, string? body = null,
        string? image = null, string? link = null, string? linkText = null, string? category = null,
        int position = 0) =>
        new()
        {
            PostId = Guid.NewGuid(), Position = position, Type = type, Title = title, BodyMd = body,
            ImageUrl = image, LinkUrl = link, LinkText = linkText, Category = category
        };

    private static string RenderOne(IssueSection section) => TemplateHtmlRenderer.Render(
        [section], MakeTenant(), "July issue", Template, new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Wraps_sections_in_the_shell_and_fills_globals()
    {
        var html = RenderOne(Section(SectionTypes.Divider));
        Assert.Contains("<title>July issue</title>", html);
        Assert.Contains("<hr class=\"rule\" />", html);
        Assert.Contains(SectionHtmlRenderer.UnsubscribeToken, html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public void Emits_one_block_per_section_in_position_order()
    {
        var html = TemplateHtmlRenderer.Render(
            [Section(SectionTypes.Topic, title: "Second", position: 1),
             Section(SectionTypes.Topic, title: "First", position: 0)],
            MakeTenant(), "t", Template, DateTimeOffset.UtcNow);
        Assert.True(html.IndexOf("First", StringComparison.Ordinal)
                  < html.IndexOf("Second", StringComparison.Ordinal));
    }

    [Fact]
    public void Present_fields_keep_their_regions()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "Flux LoRA", body: "Body text.",
            image: "https://img.example.com/a.png", link: "https://example.com/a", category: "Tutorial"));
        Assert.Contains("<img src=\"https://img.example.com/a.png\"", html);
        Assert.Contains("Tutorial · 1 min read", html);
        Assert.Contains("class=\"more\"", html);
        Assert.DoesNotContain("<!-- IF", html);
        Assert.DoesNotContain("<!-- /IF", html);
    }

    [Fact]
    public void Absent_fields_drop_their_whole_region()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "No extras", body: "Body."));
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("class=\"cat\"", html);
        Assert.DoesNotContain("class=\"more\"", html);
        Assert.Contains("<h2>No extras</h2>", html);
    }

    [Fact]
    public void Link_text_falls_back_per_section_type()
    {
        Assert.Contains("Read more", RenderOne(Section(SectionTypes.Topic,
            title: "t", link: "https://example.com")));
        Assert.Contains("Watch on YouTube", RenderOne(Section(SectionTypes.Video,
            title: "v", link: "https://youtu.be/dQw4w9WgXcQ")));
    }

    [Fact]
    public void Body_markdown_becomes_html()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "t", body: "A **bold** word."));
        Assert.Contains("<strong>bold</strong>", html);
    }

    [Fact] // The trust boundary. Section content is never trusted.
    public void Script_in_a_section_title_is_escaped_not_emitted()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "<script>alert(1)</script>", body: "x"));
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Raw_html_in_a_section_body_is_escaped_not_emitted()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "t", body: "<script>alert(1)</script>"));
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Javascript_url_resolves_empty_and_collapses_its_region()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "t", body: "b",
            link: "javascript:alert(1)", image: "javascript:alert(2)"));
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("class=\"more\"", html);   // region collapsed, not left with href="#"
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Video_thumbnail_is_derived_from_the_url_when_no_override_is_set()
    {
        var html = RenderOne(Section(SectionTypes.Video, title: "v",
            link: "https://youtu.be/dQw4w9WgXcQ"));
        Assert.Contains("https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg", html);
    }

    [Fact]
    public void Video_thumbnail_override_wins_over_the_derived_one()
    {
        var html = RenderOne(Section(SectionTypes.Video, title: "v",
            link: "https://youtu.be/dQw4w9WgXcQ", image: "https://img.example.com/custom.png"));
        Assert.Contains("https://img.example.com/custom.png", html);
        Assert.DoesNotContain("img.youtube.com", html);
    }

    [Fact]
    public void A_section_whose_block_is_missing_falls_back_to_the_built_in_design()
    {
        // Template above defines no sponsor block.
        var html = RenderOne(Section(SectionTypes.Sponsor, title: "Acme", body: "Pitch."));
        Assert.Contains("SPONSORED", html);          // built-in sponsor markup
        Assert.Contains("Acme", html);
        Assert.Contains("<html>", html);             // still inside the template shell
    }

    [Fact]
    public void Preheader_comes_from_the_header_body_with_markdown_stripped()
    {
        var html = TemplateHtmlRenderer.Render(
            [Section(SectionTypes.Header, body: "**Flux** LoRA training that holds a face.")],
            MakeTenant(), "t", Template, DateTimeOffset.UtcNow);
        Assert.Contains("Flux LoRA training that holds a face.", html);
        Assert.DoesNotContain("**Flux**", html);
    }

    [Fact]
    public void Issue_date_is_formatted_as_month_and_year()
    {
        const string dated = "<!-- BLOCK: shell -->{{issue_date}}{{sections}}{{unsubscribe_url}}<!-- /BLOCK -->";
        var html = TemplateHtmlRenderer.Render([], MakeTenant(), "t", dated,
            new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains("July 2026", html);
    }

    [Fact] // Finding 2 — an unclosed IF marker must drop to the end of the block, not leak verbatim
    public void Unclosed_if_region_drops_the_marker_and_everything_after_it_in_the_block()
    {
        const string unclosed = """
            <!-- BLOCK: shell -->{{sections}}{{unsubscribe_url}}<!-- /BLOCK -->
            <!-- BLOCK: topic -->
            <!-- IF: image --><img src="{{image_url}}" />
            <h2>{{title}}</h2>
            <!-- /BLOCK -->
            """;
        var html = TemplateHtmlRenderer.Render(
            [Section(SectionTypes.Topic, title: "No image")], MakeTenant(), "t", unclosed, DateTimeOffset.UtcNow);
        Assert.DoesNotContain("<!-- IF", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("No image", html); // the unclosed region swallows the rest of the block, {{title}} included
    }

    [Fact] // Finding 3 — the preheader must come from the header at Position 0, regardless of list order
    public void Preheader_uses_position_order_not_list_order()
    {
        var html = TemplateHtmlRenderer.Render(
            [Section(SectionTypes.Header, body: "Wrong header.", position: 1),
             Section(SectionTypes.Header, body: "Right header.", position: 0)],
            MakeTenant(), "t", Template, DateTimeOffset.UtcNow);
        Assert.Contains("Right header.", html.Split("</span>")[0]);
        Assert.DoesNotContain("Wrong header.", html.Split("</span>")[0]);
    }

    [Fact]
    public void A_template_with_no_shell_renders_nothing_rather_than_throwing()
    {
        var html = TemplateHtmlRenderer.Render([Section(SectionTypes.Divider)], MakeTenant(), "t",
            "<!-- BLOCK: topic -->x<!-- /BLOCK -->", DateTimeOffset.UtcNow);
        Assert.Equal("", html);
    }

    [Fact] // Item 2 — an empty template must not throw either; it has no shell, same as above.
    public void An_empty_template_renders_nothing_rather_than_throwing()
    {
        var html = TemplateHtmlRenderer.Render([Section(SectionTypes.Divider)], MakeTenant(), "t",
            "", DateTimeOffset.UtcNow);
        Assert.Equal("", html);
    }

    // Item 2 — the render-time backstop. TemplateValidator gates saving, but two independent
    // adversarial reviews each defeated its unsubscribe rule a different way, so the guarantee that a
    // sent newsletter carries an unsubscribe link must not rest solely on the validator being perfect.

    [Fact] // The backstop must fire ONLY when the token is genuinely absent — a correct template
    // (this fixture's shell already carries {{unsubscribe_url}}) must render byte-identically to
    // what RenderBlock alone would produce, with nothing appended.
    public void Backstop_does_not_alter_output_when_the_template_already_guarantees_the_token()
    {
        const string html = "<!-- BLOCK: shell -->\n<html><body>{{sections}}{{unsubscribe_url}}</body></html>\n<!-- /BLOCK -->";
        var rendered = TemplateHtmlRenderer.Render([], MakeTenant(), "t", html, DateTimeOffset.UtcNow);
        Assert.Equal($"<html><body>{SectionHtmlRenderer.UnsubscribeToken}</body></html>", rendered);
    }

    [Fact] // The backstop's other half: when the shell truly has no unsubscribe token anywhere, one
    // must be appended so the token is always present in what leaves the renderer.
    public void Backstop_appends_an_unsubscribe_paragraph_when_the_token_is_genuinely_absent()
    {
        const string html = "<!-- BLOCK: shell --><html><body>{{sections}}</body></html><!-- /BLOCK -->";
        var rendered = TemplateHtmlRenderer.Render([], MakeTenant(), "t", html, DateTimeOffset.UtcNow);
        Assert.Contains(SectionHtmlRenderer.UnsubscribeToken, rendered);
        Assert.Contains("Unsubscribe</a>", rendered);
        // Inserted before the shell's own closing markup, not tacked on after it blindly.
        Assert.True(rendered.IndexOf(SectionHtmlRenderer.UnsubscribeToken, StringComparison.Ordinal)
                  < rendered.IndexOf("</body>", StringComparison.Ordinal));
    }

    [Fact] // The second confirmed gap: a template whose only unsubscribe token lives in BLOCK: footer
    // renders with no token at all when the issue's section list has no footer section. Unreachable
    // through the UI today (IssueComposerService seeds and protects header/footer), but
    // TemplateHtmlRenderer.Render is a public method with no such guard, so a section list arriving
    // by another path must still come out with the token present.
    public void Section_list_with_no_footer_section_still_has_the_token_via_the_backstop()
    {
        const string footerOnlyToken = """
            <!-- BLOCK: shell -->
            <html><body>{{sections}}</body></html>
            <!-- /BLOCK -->
            <!-- BLOCK: footer -->
            {{body_html}}<a href="{{unsubscribe_url}}">Unsubscribe</a>
            <!-- /BLOCK -->
            """;
        var rendered = TemplateHtmlRenderer.Render(
            [Section(SectionTypes.Topic, title: "t", body: "b")], // no footer section at all
            MakeTenant(), "t", footerOnlyToken, DateTimeOffset.UtcNow);
        Assert.Contains(SectionHtmlRenderer.UnsubscribeToken, rendered);
    }

    [Fact] // Finding E — the video fixture must not reintroduce the empty-href-anchor defect just
    // fixed on the built-in (SectionHtmlRenderer) path: a video with no usable link must emit no
    // anchor at all, not <a href="">...</a>.
    public void Video_section_with_no_usable_link_emits_no_anchor()
    {
        var html = RenderOne(Section(SectionTypes.Video, title: "No link", body: "b"));
        Assert.DoesNotContain("href=\"\"", html);
        Assert.DoesNotContain("</h2><a ", html); // the video block's own anchor, not the shell's unsubscribe link
    }

    [Fact] // Finding E — the flip side: a good link must still produce the anchor.
    public void Video_section_with_a_good_link_still_emits_the_anchor()
    {
        var html = RenderOne(Section(SectionTypes.Video, title: "v",
            link: "https://youtu.be/dQw4w9WgXcQ", linkText: "Watch"));
        Assert.Contains("<a href=\"https://youtu.be/dQw4w9WgXcQ\">Watch</a>", html);
    }

    [Fact]
    public void The_sample_issue_exercises_every_block()
    {
        var types = SampleIssue.Sections.Select(s => s.Type).ToHashSet();
        foreach (var type in new[] { SectionTypes.Header, SectionTypes.Topic, SectionTypes.Video,
                                     SectionTypes.Sponsor, SectionTypes.Button, SectionTypes.Divider,
                                     SectionTypes.Footer })
            Assert.Contains(type, types);

        // And at least one topic with nothing optional set, so IF-collapse is visible in the preview.
        Assert.Contains(SampleIssue.Sections, s => s.Type == SectionTypes.Topic
            && s.ImageUrl is null && s.LinkUrl is null && s.Category is null);
    }
}
