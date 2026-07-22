using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.UnitTests;

public class TopicParsingTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Parses_a_plain_json_array()
    {
        var ok = IssueComposerService.TryParseTopics(
            $$"""[{"itemId":"{{Id}}","title":"T","blurb":"B"}]""", out var topics);
        Assert.True(ok);
        Assert.Equal(new TopicBlurb(Id, "T", "B", null), Assert.Single(topics!));
    }

    [Fact]
    public void Parses_a_fenced_json_array()
    {
        var ok = IssueComposerService.TryParseTopics(
            $$"""
            ```json
            [{"itemId":"{{Id}}","title":"T","blurb":"B"}]
            ```
            """, out var topics);
        Assert.True(ok);
        Assert.Single(topics!);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]                                                    // empty array is useless
    [InlineData("""[{"itemId":"11111111-2222-3333-4444-555555555555","title":"T","blurb":""}]""")]  // blank blurb
    [InlineData("""[{"itemId":"00000000-0000-0000-0000-000000000000","title":"T","blurb":"B"}]""")] // empty guid
    public void Rejects_unusable_replies(string reply)
    {
        Assert.False(IssueComposerService.TryParseTopics(reply, out var topics));
        Assert.Null(topics);
    }
}
