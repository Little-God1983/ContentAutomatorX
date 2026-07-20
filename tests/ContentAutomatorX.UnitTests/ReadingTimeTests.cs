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

    [Fact]
    public void Intra_word_hyphens_and_underscores_do_not_split_the_word()
    {
        Assert.Equal(1, ReadingTime.CountWords("state-of-the-art"));
        Assert.Equal(1, ReadingTime.CountWords("well_known_function"));
    }

    [Fact]
    public void Markdown_positioned_hyphens_and_underscores_still_do_not_inflate_the_count()
    {
        // The leading "-" of a list bullet, a "---" horizontal rule, and the "_..._" wrapping an
        // italicized word all carry markdown meaning and must not be counted as/split into words.
        var markdown = "_italic_ word\n\n---\n\n- list item";
        Assert.Equal(4, ReadingTime.CountWords(markdown)); // italic, word, list, item
    }

    [Fact]
    public void Fenced_code_blocks_are_stripped_before_counting()
    {
        var markdown = "intro text\n\n```\nvar x = 1;\nconsole.log(x);\n```\n\noutro text";
        Assert.Equal(4, ReadingTime.CountWords(markdown)); // intro, text, outro, text — code excluded
    }
}
