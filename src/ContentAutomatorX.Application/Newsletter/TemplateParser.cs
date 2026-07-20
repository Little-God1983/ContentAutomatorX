using System.Text.RegularExpressions;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Carves a template document into named blocks. Structural only — whether a block's
/// placeholders make sense is TemplateValidator's business. Text outside any block is ignored on
/// purpose: the reference template opens with a long explanatory comment that must survive.</summary>
public static partial class TemplateParser
{
    public static ParsedTemplate Parse(string html)
    {
        var blocks = new Dictionary<string, TemplateBlock>(StringComparer.Ordinal);
        var issues = new List<TemplateIssue>();

        string? openName = null;
        var openLine = 0;
        var contentStart = 0;

        foreach (Match match in MarkerRegex().Matches(html ?? ""))
        {
            var line = LineOf(html!, match.Index);
            var isOpen = match.Groups["open"].Success;

            if (isOpen)
            {
                var name = match.Groups["name"].Value.ToLowerInvariant();
                if (openName is not null)
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"BLOCK: {name} starts while BLOCK: {openName} is already open — blocks cannot nest."));
                    continue;
                }
                if (!TemplateBlocks.All.Contains(name))
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"Unknown block name '{name}'. Valid names: {string.Join(", ", TemplateBlocks.All.Order())}."));
                    // Still opened, so its matching close is consumed rather than reported as stray.
                }
                openName = name;
                openLine = line;
                contentStart = match.Index + match.Length;
            }
            else
            {
                if (openName is null)
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        "A closing <!-- /BLOCK --> appears where no block is open."));
                    continue;
                }
                var content = html![contentStart..match.Index];
                if (!TemplateBlocks.All.Contains(openName))
                {
                    // Unknown name already reported at the opening marker; drop the content.
                }
                else if (blocks.ContainsKey(openName))
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, openLine,
                        $"BLOCK: {openName} is defined more than once. Only the first is used."));
                }
                else
                {
                    var trimmedContent = content.Trim('\r', '\n');
                    var leadingTrimmed = content.Length - content.TrimStart('\r', '\n').Length;
                    var contentLine = LineOf(html, contentStart + leadingTrimmed);
                    blocks[openName] = new TemplateBlock(openName, trimmedContent, openLine, contentLine);
                }
                openName = null;
            }
        }

        if (openName is not null)
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, openLine,
                $"BLOCK: {openName} is never closed — add <!-- /BLOCK -->."));

        return new ParsedTemplate(blocks, issues);
    }

    /// <summary>1-based line number of a character index.</summary>
    public static int LineOf(string text, int index) =>
        text.AsSpan(0, Math.Min(index, text.Length)).Count('\n') + 1;

    [GeneratedRegex(@"<!--\s*(?:(?<open>BLOCK)\s*:\s*(?<name>[A-Za-z_]+)|/BLOCK)\s*-->",
        RegexOptions.IgnoreCase)]
    private static partial Regex MarkerRegex();
}
