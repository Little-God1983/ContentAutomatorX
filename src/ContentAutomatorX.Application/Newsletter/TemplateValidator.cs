using System.Text;
using System.Text.RegularExpressions;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Everything checked before a template may be saved. Rendering never consults this —
/// a template that somehow reaches render in a bad state still produces sendable HTML.</summary>
public static partial class TemplateValidator
{
    public const int MaxBytes = 512 * 1024;

    public static bool HasErrors(IReadOnlyList<TemplateIssue> issues) =>
        issues.Any(i => i.Level == TemplateIssueLevel.Error);

    public static IReadOnlyList<TemplateIssue> Validate(string html)
    {
        var issues = new List<TemplateIssue>();

        if (string.IsNullOrWhiteSpace(html))
        {
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1, "The template is empty."));
            return issues;
        }
        if (Encoding.UTF8.GetByteCount(html) > MaxBytes)
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1,
                $"The template is too large — {MaxBytes / 1024} KB is the limit."));

        var parsed = TemplateParser.Parse(html);
        issues.AddRange(parsed.Issues);

        if (!parsed.Blocks.ContainsKey(TemplateBlocks.Shell))
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1,
                "The template must contain a BLOCK: shell — it is the document everything else sits inside."));

        foreach (var block in parsed.Blocks.Values)
        {
            var allowed = TemplatePlaceholders.For(block.Name);
            var conditions = TemplatePlaceholders.Conditions(block.Name);
            var used = 0;

            foreach (Match m in PlaceholderRegex().Matches(block.Content))
            {
                used++;
                var name = m.Groups["name"].Value;
                var line = block.Line + TemplateParser.LineOf(block.Content, m.Index) - 1;

                if (name == "sections" && block.Name != TemplateBlocks.Shell)
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        "{{sections}} may appear only in the shell block."));
                else if (!allowed.Contains(name))
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"Unknown placeholder {{{{{name}}}}} in BLOCK: {block.Name}. "
                        + $"Available here: {string.Join(", ", allowed.Order())}."));
            }

            ValidateRegions(block, conditions, issues);

            // Divider is the one block with nothing to substitute, by design.
            if (used == 0 && block.Name != TemplateBlocks.Divider)
                issues.Add(new TemplateIssue(TemplateIssueLevel.Warning, block.Line,
                    $"BLOCK: {block.Name} contains no placeholders — it will render the same markup every time."));
        }

        if (parsed.Blocks.TryGetValue(TemplateBlocks.Shell, out var shell)
            && !PlaceholderRegex().Matches(shell.Content).Any(m => m.Groups["name"].Value == "sections"))
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, shell.Line,
                "BLOCK: shell must contain {{sections}} — otherwise no section is ever emitted."));

        // Checked across the whole template rather than inside the footer, so a design that puts
        // unsubscribe in the shell still passes. This is a legal requirement, not a style rule.
        if (!parsed.Blocks.Values.Any(b => b.Content.Contains("{{unsubscribe_url}}", StringComparison.Ordinal)))
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1,
                "The template contains no {{unsubscribe_url}} — commercial email must carry an unsubscribe link."));

        foreach (var name in TemplateBlocks.Optional.Where(n => !parsed.Blocks.ContainsKey(n)))
            issues.Add(new TemplateIssue(TemplateIssueLevel.Warning, 1,
                $"No BLOCK: {name} — those sections will use the built-in design."));

        return issues.OrderBy(i => i.Line).ToList();
    }

    private static void ValidateRegions(TemplateBlock block, IReadOnlySet<string> conditions,
        List<TemplateIssue> issues)
    {
        string? open = null;
        var openLine = 0;

        foreach (Match m in RegionRegex().Matches(block.Content))
        {
            var line = block.Line + TemplateParser.LineOf(block.Content, m.Index) - 1;
            if (m.Groups["open"].Success)
            {
                var condition = m.Groups["cond"].Value.ToLowerInvariant();
                if (open is not null)
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"IF: {condition} starts inside IF: {open} — IF regions cannot nest."));
                    continue;
                }
                if (!conditions.Contains(condition))
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"Unknown condition '{condition}' in BLOCK: {block.Name}. "
                        + $"Available here: {string.Join(", ", conditions.Order())}."));
                open = condition;
                openLine = line;
            }
            else if (open is null)
            {
                issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                    "A closing <!-- /IF --> appears where no IF is open."));
            }
            else
            {
                open = null;
            }
        }

        if (open is not null)
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, openLine,
                $"<!-- IF: {open} --> is never closed — add <!-- /IF -->."));
    }

    [GeneratedRegex(@"\{\{\s*(?<name>[a-z_]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"<!--\s*(?:(?<open>IF)\s*:\s*(?<cond>[A-Za-z_]+)|/IF)\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex RegionRegex();
}
