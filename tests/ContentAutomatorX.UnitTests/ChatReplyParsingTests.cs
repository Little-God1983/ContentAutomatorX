using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.UnitTests;

public class ChatReplyParsingTests
{
    private static readonly Guid S1 = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Parses_a_reply_with_one_edit()
    {
        var ok = ChatReplyParser.TryParse(
            $$"""{"reply":"Done.","edits":[{"sectionId":"{{S1}}","title":"New","bodyMd":"Body"}]}""",
            out var reply);
        Assert.True(ok);
        Assert.Equal("Done.", reply!.Reply);
        var edit = Assert.Single(reply.Edits);
        Assert.Equal(S1, edit.SectionId);
        Assert.Equal("New", edit.Title);
        Assert.Equal("Body", edit.BodyMd);
        Assert.Equal(0, reply.DroppedEdits);
    }

    [Fact]
    public void Parses_a_fenced_reply()
    {
        var ok = ChatReplyParser.TryParse(
            $$"""
            ```json
            {"reply":"Done.","edits":[{"sectionId":"{{S1}}","bodyMd":"Body"}]}
            ```
            """, out var reply);
        Assert.True(ok);
        Assert.Single(reply!.Edits);
        Assert.Null(reply.Edits[0].Title);        // omitted field means "unchanged"
    }

    [Fact]
    public void Parses_a_reply_with_no_edits()
    {
        var ok = ChatReplyParser.TryParse("""{"reply":"That intro reads fine to me.","edits":[]}""", out var reply);
        Assert.True(ok);
        Assert.Empty(reply!.Edits);
    }

    [Fact]
    public void Accepts_a_missing_edits_array()
    {
        var ok = ChatReplyParser.TryParse("""{"reply":"Just answering."}""", out var reply);
        Assert.True(ok);
        Assert.Empty(reply!.Edits);
    }

    [Fact]
    public void Drops_structurally_invalid_edits_and_counts_them()
    {
        var ok = ChatReplyParser.TryParse(
            $$"""
            {"reply":"Two of these are junk.","edits":[
              {"sectionId":"{{S1}}","bodyMd":"Good"},
              {"sectionId":"00000000-0000-0000-0000-000000000000","bodyMd":"No id"},
              {"sectionId":"{{S1}}"}
            ]}
            """, out var reply);
        Assert.True(ok);
        Assert.Single(reply!.Edits);
        Assert.Equal(2, reply.DroppedEdits);      // empty guid, and neither field set
    }

    [Fact]
    public void Tolerates_a_placeholder_sectionId_echoed_from_the_prompt_template()
    {
        // The prompt hands the model a literal example containing "sectionId":"<id>". A model that
        // echoes the placeholder instead of a real id must not sink the good edit alongside it.
        var ok = ChatReplyParser.TryParse(
            $$"""
            {"reply":"One real edit, one echoed placeholder.","edits":[
              {"sectionId":"{{S1}}","bodyMd":"Good"},
              {"sectionId":"<id>","bodyMd":"Echoed placeholder"}
            ]}
            """, out var reply);
        Assert.True(ok);
        var edit = Assert.Single(reply!.Edits);
        Assert.Equal(S1, edit.SectionId);
        Assert.Equal("Good", edit.BodyMd);
        Assert.Equal(1, reply.DroppedEdits);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]                                       // an array is the topics shape, not this one
    [InlineData("""{"reply":"","edits":[]}""")]              // nothing said and nothing proposed
    [InlineData("""{"edits":[]}""")]
    public void Rejects_unusable_replies(string text)
    {
        Assert.False(ChatReplyParser.TryParse(text, out var reply));
        Assert.Null(reply);
    }

    [Fact]
    public void Parses_a_category_edit()
    {
        var id = Guid.NewGuid();
        var json = $$"""{"reply":"ok","edits":[{"sectionId":"{{id}}","category":"Tutorial"}]}""";

        Assert.True(ChatReplyParser.TryParse(json, out var reply));
        var edit = Assert.Single(reply!.Edits);
        Assert.Equal("Tutorial", edit.Category);
        Assert.Null(edit.Title);
        Assert.Null(edit.BodyMd);
    }

    [Fact]
    public void An_edit_carrying_only_a_category_is_kept_not_dropped()
    {
        var json = $$"""{"reply":"ok","edits":[{"sectionId":"{{Guid.NewGuid()}}","category":"News"}]}""";
        Assert.True(ChatReplyParser.TryParse(json, out var reply));
        Assert.Single(reply!.Edits);
        Assert.Equal(0, reply.DroppedEdits);
    }

    [Fact]
    public void An_edit_with_no_usable_field_at_all_is_still_dropped()
    {
        var json = $$"""{"reply":"ok","edits":[{"sectionId":"{{Guid.NewGuid()}}"}]}""";
        Assert.True(ChatReplyParser.TryParse(json, out var reply));
        Assert.Empty(reply!.Edits);
        Assert.Equal(1, reply.DroppedEdits);
    }
}
