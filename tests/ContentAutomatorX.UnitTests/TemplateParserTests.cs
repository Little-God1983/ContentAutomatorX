using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class TemplateParserTests
{
    [Fact]
    public void Extracts_named_blocks_and_ignores_text_between_them()
    {
        var parsed = TemplateParser.Parse("""
            <!-- a long explanatory header comment that is not a block -->
            <!-- BLOCK: shell -->SHELL {{sections}}<!-- /BLOCK -->
            loose text nobody asked about
            <!-- BLOCK: topic -->TOPIC<!-- /BLOCK -->
            """);

        Assert.Empty(parsed.Issues);
        Assert.Equal(2, parsed.Blocks.Count);
        Assert.Equal("SHELL {{sections}}", parsed.Blocks["shell"].Content);
        Assert.Equal("TOPIC", parsed.Blocks["topic"].Content);
    }

    [Fact]
    public void Reports_the_line_a_block_starts_on()
    {
        var parsed = TemplateParser.Parse("one\ntwo\n<!-- BLOCK: topic -->x<!-- /BLOCK -->");
        Assert.Equal(3, parsed.Blocks["topic"].Line);
    }

    [Fact]
    public void ContentLine_accounts_for_the_newline_stripped_when_the_marker_sits_on_its_own_line()
    {
        // Marker is on line 3; its own line break is discarded by Content.Trim('\r','\n'), so the
        // trimmed content actually begins on line 4. Line must stay pointing at the marker (3);
        // ContentLine must point at where the trimmed text really starts (4).
        var parsed = TemplateParser.Parse("one\ntwo\n<!-- BLOCK: topic -->\nx\n<!-- /BLOCK -->");
        var block = parsed.Blocks["topic"];
        Assert.Equal(3, block.Line);
        Assert.Equal(4, block.ContentLine);
    }

    [Fact]
    public void ContentLine_equals_Line_when_marker_and_content_share_a_line()
    {
        // No newline is stripped in this authoring style, so ContentLine and Line must agree —
        // otherwise the fix for the own-line case would just move the off-by-one here instead.
        var parsed = TemplateParser.Parse("one\ntwo\n<!-- BLOCK: topic -->x<!-- /BLOCK -->");
        var block = parsed.Blocks["topic"];
        Assert.Equal(3, block.Line);
        Assert.Equal(3, block.ContentLine);
    }

    [Fact]
    public void Tolerates_whitespace_variations_in_the_markers()
    {
        var parsed = TemplateParser.Parse("<!--BLOCK:topic-->x<!--/BLOCK-->");
        Assert.Empty(parsed.Issues);
        Assert.True(parsed.Blocks.ContainsKey("topic"));
    }

    [Fact]
    public void Unknown_block_name_is_an_error_and_the_block_is_dropped()
    {
        var parsed = TemplateParser.Parse("<!-- BLOCK: banana -->x<!-- /BLOCK -->");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("banana"));
        Assert.Empty(parsed.Blocks);
    }

    [Fact]
    public void Duplicate_block_name_is_an_error_and_the_first_wins()
    {
        var parsed = TemplateParser.Parse(
            "<!-- BLOCK: topic -->first<!-- /BLOCK --><!-- BLOCK: topic -->second<!-- /BLOCK -->");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("more than once"));
        Assert.Equal("first", parsed.Blocks["topic"].Content);
    }

    [Fact]
    public void Unclosed_block_is_an_error()
    {
        var parsed = TemplateParser.Parse("<!-- BLOCK: topic -->x");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("never closed"));
        Assert.Empty(parsed.Blocks);
    }

    [Fact]
    public void Closing_marker_with_no_open_block_is_an_error()
    {
        var parsed = TemplateParser.Parse("x<!-- /BLOCK -->");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("no block is open"));
    }

    [Fact]
    public void Nested_block_open_is_an_error()
    {
        var parsed = TemplateParser.Parse("<!-- BLOCK: topic --><!-- BLOCK: video -->x<!-- /BLOCK --><!-- /BLOCK -->");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("already open"));
    }

    [Theory]
    [InlineData("Header", "header")]
    [InlineData("Topic", "topic")]
    [InlineData("Video", "video")]
    [InlineData("Sponsor", "sponsor")]
    [InlineData("Button", "button")]
    [InlineData("Divider", "divider")]
    [InlineData("Footer", "footer")]
    [InlineData("LegacyBody", null)]
    public void Maps_section_types_to_block_names(string sectionType, string? expected) =>
        Assert.Equal(expected, TemplateBlocks.ForSectionType(sectionType));
}
