using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

/// <summary>Proves docs/user-braindumps/preview-template.html — the block conversion of the user's
/// own hand-written design — actually renders a sendable email over SampleIssue.Sections, which
/// exercises every block and both sides of every IF region (see SampleIssue's own doc comment).
/// Companion to TemplateValidatorTests' The_reference_template_validates_with_no_errors /
/// The_reference_template_defines_every_block, which only prove the template parses and validates —
/// neither of those renders it, so neither would have caught a guard mismatched to the wrong
/// placeholder (Findings 2 & 3 on this branch: an href/src guarded by the wrong IF condition renders
/// empty rather than failing validation).</summary>
public class ReferenceTemplateTests
{
    private static string Path() => System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "docs", "user-braindumps", "preview-template.html");

    private static string Html() => File.ReadAllText(Path());

    [Fact]
    public void Reference_template_renders_the_sample_issue_with_no_leftover_placeholders_or_empty_urls()
    {
        Assert.True(File.Exists(Path()), $"Reference template not found at {System.IO.Path.GetFullPath(Path())}");

        var html = TemplateHtmlRenderer.Render(SampleIssue.Sections, SampleIssue.Tenant,
            "Signals from the latent space", Html(), new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains(SectionHtmlRenderer.UnsubscribeToken, html);
        Assert.DoesNotContain("{{", html);
        // The one hard requirement the task brief calls out by name: a URL-bearing attribute guarded
        // by the wrong IF condition (or not guarded at all) renders href="" / src="" — broken markup
        // mailed to real subscribers. SampleIssue's position-3 topic has nothing optional set, which
        // is exactly the shape that catches a mismatched guard.
        Assert.DoesNotContain("href=\"\"", html);
        Assert.DoesNotContain("src=\"\"", html);
    }
}
