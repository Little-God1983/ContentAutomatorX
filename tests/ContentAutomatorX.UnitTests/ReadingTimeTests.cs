using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class ReadingTimeTests
{
    [Theory]
    [InlineData(null, "1 min read")]
    [InlineData("", "1 min read")]
    [InlineData("   ", "1 min read")]
    [InlineData("one two three", "1 min read")]
    public void Short_or_absent_bodies_still_read_as_one_minute(string? body, string expected) =>
        Assert.Equal(expected, ReadingTime.Describe(body));

    [Theory]
    [InlineData(200, "1 min read")]
    [InlineData(300, "2 min read")]   // 1.5 rounds up
    [InlineData(1800, "9 min read")]
    public void Longer_bodies_scale_at_two_hundred_words_a_minute(int words, string expected) =>
        Assert.Equal(expected, ReadingTime.Describe(string.Join(" ", Enumerable.Repeat("word", words))));

    [Fact]
    public void Markdown_syntax_is_not_counted_as_words()
    {
        // Without stripping, the hashes, asterisks, brackets and URL inflate the count.
        var markdown = "## Heading\n\n**bold** _italic_ [link](https://example.com/a/very/long/path)";
        Assert.Equal("1 min read", ReadingTime.Describe(markdown));
        Assert.Equal(ReadingTime.Describe("Heading bold italic link"), ReadingTime.Describe(markdown));
    }
}
