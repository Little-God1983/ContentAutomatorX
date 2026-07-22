using System.Text.RegularExpressions;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Derived, never stored. A stored field is one the model can fill wrongly, one the chat
/// contract has to carry, and one that goes stale the moment a paragraph is edited.</summary>
public static partial class ReadingTime
{
    private const int WordsPerMinute = 200;

    public static string Describe(string? markdown)
    {
        var words = CountWords(markdown);
        var minutes = Math.Max(1, (int)Math.Round(words / (double)WordsPerMinute, MidpointRounding.AwayFromZero));
        return $"{minutes} min read";
    }

    public static int CountWords(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return 0;
        // Fenced code blocks first: a code sample is not prose.
        var text = FencedCodeRegex().Replace(markdown, " ");
        // Link targets next, so a long URL does not count as a word; then the rest of the syntax.
        text = LinkRegex().Replace(text, "$1");
        text = EmphasisRegex().Replace(text, " ");
        text = SyntaxRegex().Replace(text, " ");
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    [GeneratedRegex(@"```[\s\S]*?```")]
    private static partial Regex FencedCodeRegex();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    // *, _, ~ and - carry markdown meaning only at a word boundary: "**bold**", "_italic_",
    // "~~strike~~", a "- " list bullet, or a "---" horizontal rule are all flanked by a non-word
    // character (start/end of text, whitespace, punctuation) on at least one side. An intra-word
    // hyphen or underscore ("state-of-the-art", "well_known_function") has word characters on both
    // immediate sides and must be left alone, or ordinary hyphenated/underscored prose gets split
    // into extra words. This pattern removes a run only when it is NOT fully internal to a word.
    [GeneratedRegex(@"(?<!\w)[*_~-]+|[*_~-]+(?!\w)")]
    private static partial Regex EmphasisRegex();

    [GeneratedRegex(@"[#>`\[\]()|]+")]
    private static partial Regex SyntaxRegex();
}
