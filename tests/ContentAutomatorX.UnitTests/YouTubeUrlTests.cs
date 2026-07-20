using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class YouTubeUrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&t=42s", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc123", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ#t=10", "dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void Extracts_the_video_id_from_every_url_shape(string url, string expected)
    {
        Assert.True(YouTubeUrl.TryGetVideoId(url, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://vimeo.com/12345")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/watch?list=PL123")]
    [InlineData("not a url at all")]
    public void Returns_false_for_anything_it_cannot_read(string? url)
    {
        Assert.False(YouTubeUrl.TryGetVideoId(url, out var id));
        Assert.Null(id);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=%22%3E%3Cimg%20onerror%3Dalert(1)%3E")]
    [InlineData("https://www.youtube.com/shorts/%22%3E%3Cimg%20onerror%3Dalert(1)%3E")]
    [InlineData("https://www.youtube.com/embed/%22%3E%3Cimg%20onerror%3Dalert(1)%3E")]
    [InlineData("https://youtu.be/%22%3E%3Cimg%20onerror%3Dalert(1)%3E")]
    public void Rejects_video_ids_outside_the_safe_character_set(string url)
    {
        // A crafted v= or path-segment value must never reach MaxResThumbnail unvalidated; it is
        // destined for an <img src> and href in a sent email.
        Assert.False(YouTubeUrl.TryGetVideoId(url, out var id));
        Assert.Null(id);
    }

    [Fact]
    public void Builds_both_thumbnail_urls()
    {
        Assert.Equal("https://img.youtube.com/vi/abc/maxresdefault.jpg", YouTubeUrl.MaxResThumbnail("abc"));
        Assert.Equal("https://img.youtube.com/vi/abc/hqdefault.jpg", YouTubeUrl.FallbackThumbnail("abc"));
    }
}
