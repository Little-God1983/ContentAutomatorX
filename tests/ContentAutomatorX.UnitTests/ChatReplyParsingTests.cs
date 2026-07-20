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
}
