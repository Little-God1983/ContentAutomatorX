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
        // Link targets first, so a long URL does not count as a word; then the rest of the syntax.
        var text = LinkRegex().Replace(markdown, "$1");
        text = SyntaxRegex().Replace(text, " ");
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"[#*_>`~\[\]()|-]+")]
    private static partial Regex SyntaxRegex();
}
