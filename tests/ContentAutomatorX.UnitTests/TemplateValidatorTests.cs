using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class TemplateValidatorTests
{
    // A minimal template that must validate clean. Every negative test below is this, broken
    // in exactly one way — so a failure names the rule it broke, not a pile of unrelated noise.
    private const string Valid = """
        <!-- BLOCK: shell -->
        <html><body>{{sections}}<a href="{{unsubscribe_url}}">Unsubscribe</a></body></html>
        <!-- /BLOCK -->
        <!-- BLOCK: topic -->
        <!-- IF: image --><img src="{{image_url}}" /><!-- /IF -->
        <h2>{{title}}</h2>{{body_html}}
        <!-- /BLOCK -->
        """;

    [Fact]
    public void A_valid_template_produces_no_errors()
    {
        var issues = TemplateValidator.Validate(Valid);
        Assert.False(TemplateValidator.HasErrors(issues));
    }

    [Theory]
    // E1 empty
    [InlineData("", "is empty")]
    // E3 no shell
    [InlineData("<!-- BLOCK: topic -->x<!-- /BLOCK -->", "must contain a BLOCK: shell")]
    // E6 unknown block
    [InlineData("<!-- BLOCK: shell -->{{sections}}{{unsubscribe_url}}<!-- /BLOCK -->"
              + "<!-- BLOCK: banana -->x<!-- /BLOCK -->", "Unknown block name")]
    // E8 unclosed block
    [InlineData("<!-- BLOCK: shell -->{{sections}}{{unsubscribe_url}}", "never closed")]
    public void Structural_problems_are_errors(string html, string expectedFragment)
    {
        var issues = TemplateValidator.Validate(html);
        Assert.True(TemplateValidator.HasErrors(issues));
        Assert.Contains(issues, i => i.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // E2
    public void Oversized_template_is_an_error()
    {
        var html = Valid + new string('x', TemplateValidator.MaxBytes);
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("too large"));
    }

    [Fact] // E4
    public void Shell_without_the_sections_slot_is_an_error()
    {
        var html = "<!-- BLOCK: shell --><html>{{unsubscribe_url}}</html><!-- /BLOCK -->";
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("{{sections}}"));
    }

    [Fact] // E5 — the one that matters most
    public void Template_without_an_unsubscribe_link_is_an_error()
    {
        var html = "<!-- BLOCK: shell --><html>{{sections}}</html><!-- /BLOCK -->";
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("unsubscribe"));
    }

    [Fact] // Finding 4 — internal whitespace must be tolerated, same as every other placeholder
    public void Unsubscribe_placeholder_with_internal_whitespace_is_accepted()
    {
        var html = Valid.Replace("{{unsubscribe_url}}", "{{ unsubscribe_url }}");
        var issues = TemplateValidator.Validate(html);
        Assert.DoesNotContain(issues, i => i.Message.Contains("unsubscribe"));
    }

    [Fact] // Finding D — the captured placeholder name is compared with ordinal equality on purpose:
    // TemplateHtmlRenderer's value dictionary lookup is also Ordinal (StringComparer.Ordinal), so
    // {{UNSUBSCRIBE_URL}} would resolve to "" at render time and silently ship with no link. If this
    // comparison is ever loosened to OrdinalIgnoreCase, an uppercase token would validate here while
    // still rendering empty — an email with no unsubscribe link and no error anywhere. This test must
    // keep failing if that happens.
    public void Uppercase_unsubscribe_placeholder_is_rejected_not_silently_accepted()
    {
        var html = Valid.Replace("{{unsubscribe_url}}", "{{UNSUBSCRIBE_URL}}");
        var issues = TemplateValidator.Validate(html);
        Assert.Contains(issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("unsubscribe"));
    }

    [Fact] // Finding 4 — a token that appears only outside every block must still be rejected
    public void Unsubscribe_placeholder_outside_every_block_is_still_an_error()
    {
        // Remove the only in-block occurrence, then add one outside any BLOCK/ /BLOCK pair —
        // the parser does not capture text outside blocks, so this must still be rejected.
        var html = Valid.Replace("<a href=\"{{unsubscribe_url}}\">Unsubscribe</a>", "")
            + "\n{{unsubscribe_url}}";
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("unsubscribe"));
    }

    [Fact] // Finding 4 — a template with a genuine parse error (unclosed block) and no unsubscribe
    // token anywhere must still be rejected for the missing link, not just for the parse error.
    public void Unsubscribe_check_still_rejects_a_template_with_parse_errors()
    {
        const string html = "<!-- BLOCK: shell -->{{sections}}<html>"; // never closed, no unsubscribe token
        var issues = TemplateValidator.Validate(html);
        Assert.Contains(issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("never closed"));
        Assert.Contains(issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("unsubscribe"));
    }

    [Fact] // Finding A (Important) — a token inside an IF region that can collapse must not satisfy
    // the rule: an issue whose footer BodyMd is empty (Tenant.DefaultFooterMd defaults to "") would
    // render with no unsubscribe link at all, even though this template "validates clean" today.
    public void Unsubscribe_token_only_inside_a_collapsing_IF_region_in_footer_is_rejected()
    {
        const string html = """
            <!-- BLOCK: shell -->
            <html><body>{{sections}}</body></html>
            <!-- /BLOCK -->
            <!-- BLOCK: footer -->
            <!-- IF: body -->{{body_html}}<a href="{{unsubscribe_url}}">Unsubscribe</a><!-- /IF -->
            <p>{{sender_identity}}</p>
            <!-- /BLOCK -->
            """;
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("unsubscribe"));
    }

    [Fact] // Finding A (Important) — a token that only lives in a block that may render zero times
    // (sponsor/video/button — none of which is guaranteed for every issue) must not satisfy the
    // rule: an issue without that section type renders with no unsubscribe link anywhere.
    public void Unsubscribe_token_only_in_a_block_that_may_render_zero_times_is_rejected()
    {
        const string html = """
            <!-- BLOCK: shell -->
            <html><body>{{sections}}</body></html>
            <!-- /BLOCK -->
            <!-- BLOCK: sponsor -->
            <p>{{title}}</p><a href="{{unsubscribe_url}}">Unsubscribe</a>
            <!-- /BLOCK -->
            """;
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("unsubscribe"));
    }

    [Fact] // Finding A — the footer is one of the two blocks guaranteed to render for every issue,
    // so a token placed there outside any IF region must still be accepted.
    public void Unsubscribe_token_in_footer_outside_any_IF_is_accepted()
    {
        const string html = """
            <!-- BLOCK: shell -->
            <html><body>{{sections}}</body></html>
            <!-- /BLOCK -->
            <!-- BLOCK: footer -->
            {{body_html}}<a href="{{unsubscribe_url}}">Unsubscribe</a>
            <!-- /BLOCK -->
            """;
        Assert.DoesNotContain(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("unsubscribe"));
    }

    [Fact] // E10
    public void Unknown_placeholder_is_an_error_naming_the_block()
    {
        var html = Valid.Replace("{{title}}", "{{titel}}");
        var issue = Assert.Single(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("titel"));
        Assert.Contains("topic", issue.Message);
    }

    [Fact] // E10 — capitalisation must not hide a placeholder from validation
    public void Capitalised_placeholder_is_still_reported_as_unknown()
    {
        // {{Title}} is not in the (lowercase-only) vocabulary. Without RegexOptions.IgnoreCase on
        // PlaceholderRegex this simply never matches — it is invisible to the validator, is not
        // counted, is not flagged, and would ship as literal "{{Title}}" text in every email.
        var html = Valid.Replace("{{title}}", "{{Title}}");
        var issue = Assert.Single(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("Unknown placeholder"));
        Assert.Contains("Title", issue.Message);
        Assert.Contains("topic", issue.Message);
    }

    [Fact] // Finding 1 regression — line number must account for the marker's own stripped newline
    public void Unknown_placeholder_line_number_is_exact_when_the_marker_is_on_its_own_line()
    {
        // {{titel}} sits on document line 5. The marker for BLOCK: topic is on line 4; its own
        // line break is stripped by Content.Trim before the validator ever sees the content, so
        // a line computed from the marker's line undercounts by one.
        var html = string.Join('\n',
            "<!-- BLOCK: shell -->",
            "<html><body>{{sections}}<a href=\"{{unsubscribe_url}}\"></a></body></html>",
            "<!-- /BLOCK -->",
            "<!-- BLOCK: topic -->",
            "<h2>{{titel}}</h2>",
            "<!-- /BLOCK -->");

        var issue = Assert.Single(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("titel"));
        Assert.Equal(5, issue.Line);
    }

    [Fact] // Finding 1 regression — same-line authoring must not be pushed off by the fix either
    public void Unknown_placeholder_line_number_is_exact_when_marker_and_content_share_a_line()
    {
        // {{titel}} sits on document line 4, the same line as the opening marker — no newline is
        // stripped here, so a correct fix must not add an extra line the way a blanket +1 would.
        var html = string.Join('\n',
            "<!-- BLOCK: shell -->",
            "<html><body>{{sections}}<a href=\"{{unsubscribe_url}}\"></a></body></html>",
            "<!-- /BLOCK -->",
            "<!-- BLOCK: topic -->{{titel}}<h2></h2>",
            "<!-- /BLOCK -->");

        var issue = Assert.Single(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("titel"));
        Assert.Equal(4, issue.Line);
    }

    [Fact] // E10 — a placeholder legal elsewhere is still illegal here
    public void Placeholder_from_another_block_is_an_error()
    {
        var html = Valid.Replace("{{title}}", "{{thumbnail_url}}");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("thumbnail_url"));
    }

    [Fact] // E11
    public void Sections_slot_outside_the_shell_is_an_error()
    {
        var html = Valid.Replace("<h2>{{title}}</h2>", "<h2>{{sections}}</h2>");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("only in the shell"));
    }

    [Fact] // E12
    public void Unclosed_if_region_is_an_error()
    {
        var html = Valid.Replace("<!-- /IF -->", "");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("never closed"));
    }

    [Fact] // E12 — stray close
    public void Closing_if_with_nothing_open_is_an_error()
    {
        var html = Valid.Replace("<h2>{{title}}</h2>", "<!-- /IF --><h2>{{title}}</h2>");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("no IF is open"));
    }

    [Fact] // E13
    public void Nested_if_region_is_an_error()
    {
        var html = Valid.Replace("<img src=\"{{image_url}}\" />",
            "<!-- IF: link --><img src=\"{{image_url}}\" /><!-- /IF -->");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("cannot nest"));
    }

    [Fact] // E14
    public void Unknown_if_condition_is_an_error()
    {
        var html = Valid.Replace("<!-- IF: image -->", "<!-- IF: banana -->");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("banana"));
    }

    [Fact] // W1
    public void Missing_optional_blocks_are_warnings_not_errors()
    {
        var issues = TemplateValidator.Validate(Valid);
        Assert.False(TemplateValidator.HasErrors(issues));
        // Valid defines shell and topic; the other six optional blocks warn.
        Assert.Equal(6, issues.Count(i => i.Level == TemplateIssueLevel.Warning
            && i.Message.Contains("built-in design")));
    }

    [Fact] // W2
    public void Block_with_no_placeholders_at_all_is_a_warning()
    {
        var html = Valid + "\n<!-- BLOCK: sponsor --><td>nothing dynamic</td><!-- /BLOCK -->";
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Warning && i.Message.Contains("no placeholders"));
    }

    [Fact]
    public void Divider_needs_no_placeholders_and_does_not_warn()
    {
        var html = Valid + "\n<!-- BLOCK: divider --><hr /><!-- /BLOCK -->";
        Assert.DoesNotContain(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Warning && i.Message.Contains("no placeholders"));
    }

    [Fact]
    public void The_reference_template_is_not_yet_annotated_and_that_is_expected()
    {
        // docs/user-braindumps/preview.html carries BLOCK comments but no placeholders yet —
        // Task 10 converts it. This test documents the current state so the conversion has a
        // before-and-after, and fails loudly if someone converts it without updating this.
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "docs", "user-braindumps", "preview.html");
        if (!File.Exists(path)) return; // not shipped with the test output on all machines
        var issues = TemplateValidator.Validate(File.ReadAllText(path));
        Assert.True(TemplateValidator.HasErrors(issues));
    }
}
