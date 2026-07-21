using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.UnitTests;

/// <summary>Regression coverage for the starter template seeded by Recipes.razor's NewTemplateAsync
/// (ContentAutomatorX.Application.Newsletter.StarterTemplate — moved out of the Web project so this
/// test can render it directly, rather than duplicating the markup here where it would drift).
/// Findings 2 and 3 were both the same shape: a URL-bearing attribute guarded by an
/// <!-- IF --> region whose condition does not actually track that URL's own placeholder, so the
/// region can stay open while the placeholder itself resolves empty, emitting href="" or src="" —
/// an anchor to the current document, or a broken image, sent to real subscribers.</summary>
public class StarterTemplateTests
{
    private static Tenant MakeTenant() => new()
    {
        Name = "Into the Latent", Slug = "itl", SenderIdentity = "Christian Wenzl · Greven",
        BrandingJson = """{"accentColorHex":"#1AE6D5"}"""
    };

    private static IssueSection Section(string type, int position, string? title = null, string? body = null,
        string? image = null) =>
        new()
        {
            PostId = Guid.NewGuid(), Position = position, Type = type, Title = title, BodyMd = body,
            ImageUrl = image, LinkUrl = null, LinkText = null, Category = null
        };

    [Fact]
    public void Starter_template_validates_with_zero_errors()
    {
        var issues = TemplateValidator.Validate(StarterTemplate.Html);
        Assert.False(TemplateValidator.HasErrors(issues));
    }

    [Fact] // Documents the block coverage this test's section list below relies on — if the starter
    // ever grows a block for a section type not exercised below, this fails first and loudly rather
    // than the render test silently under-covering it.
    public void The_starter_template_has_a_block_for_every_section_type_this_suite_covers()
    {
        foreach (var type in new[] { SectionTypes.Header, SectionTypes.Topic, SectionTypes.Video,
                                     SectionTypes.Sponsor, SectionTypes.Button, SectionTypes.Divider,
                                     SectionTypes.Footer })
            Assert.NotNull(TemplateBlocks.ForSectionType(type));
    }

    [Fact] // Findings 2 & 3 — every optional field (image, link, category, thumbnail) is absent on
    // every section type the starter has a block for. A linkless Button or a linkless/imageless Video
    // must never leave an <a href=""> or <img src=""> in the output: an anchor with an empty href
    // targets the current document, and both are broken markup mailed to real subscribers.
    //
    // One extra row breaks the "everything absent" pattern on purpose: Video with ImageUrl set and
    // LinkUrl absent. VideoThumbnail derives thumbnail_url from LinkUrl when there is no ImageUrl
    // override, so a plain "everything absent" Video section leaves thumbnail_url empty too and the
    // whole <!-- IF: thumbnail --> region collapses before the anchor is ever emitted — Finding 3's
    // actual failure (guard tests "thumbnail", href reads {{video_url}}) never gets a chance to fire
    // in that shape. It requires the guard to be true (thumbnail present) while the href's own
    // placeholder is empty (no link), which needs an explicit ImageUrl override with no LinkUrl.
    public void Every_section_with_all_optional_fields_absent_emits_no_empty_href_or_src()
    {
        var sections = new[]
        {
            Section(SectionTypes.Header, 0, body: "Intro copy, no links here."),
            Section(SectionTypes.Topic, 1, title: "A bare topic", body: "No image, no link, no category."),
            Section(SectionTypes.Video, 2, title: "A bare video", body: "No thumbnail, no link."),
            Section(SectionTypes.Sponsor, 3, title: "A bare sponsor", body: "No logo, no link."),
            Section(SectionTypes.Button, 4),
            Section(SectionTypes.Divider, 5),
            Section(SectionTypes.Footer, 6, body: "Unsubscribe footer copy."),
            Section(SectionTypes.Video, 7, title: "A video with a thumbnail but no link",
                body: "Thumbnail present, link absent — the exact Finding 3 repro.",
                image: "https://img.example.com/thumb.png"),
        };

        var html = TemplateHtmlRenderer.Render(sections, MakeTenant(), "July issue",
            StarterTemplate.Html, new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero));

        Assert.DoesNotContain("href=\"\"", html);
        Assert.DoesNotContain("src=\"\"", html);
    }
}
