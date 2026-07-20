namespace ContentAutomatorX.Application.Services;

/// <summary>Models routinely wrap JSON in a ``` fence despite being told not to. Both reply
/// parsers strip it the same way; this is that one way.</summary>
public static class MarkdownFence
{
    public static string Strip(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }
}
