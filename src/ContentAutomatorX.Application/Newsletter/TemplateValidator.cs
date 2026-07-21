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
                var line = block.ContentLine + TemplateParser.LineOf(block.Content, m.Index) - 1;

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

        // This is a legal requirement, not a style rule: commercial email must carry an unsubscribe
        // link. A token's mere presence anywhere in the source is not enough — it must be guaranteed
        // to survive into every rendered email. That means: (1) it must sit in a block that always
        // renders for every issue — shell or footer, since an issue always has exactly one header and
        // one footer and neither can be deleted — and (2) it must sit outside any <!-- IF --> region,
        // since a region collapses to nothing when its field is empty and would take the token with
        // it. A token only inside sponsor/video/button (sections an issue may simply not have) or only
        // inside a collapsible IF region does not satisfy this. If the template defines no BLOCK:
        // footer at all, the footer section falls back to SectionHtmlRenderer.RenderSection, which
        // never emits the built-in unsubscribe link (that comes from Render's document footer, which
        // the template path never calls) — so in that case only the shell can carry the requirement.
        // Recognition goes through PlaceholderRegex — the same rule as every other placeholder in
        // this file — so {{ unsubscribe_url }} (internal whitespace) is accepted like any other
        // placeholder, rather than being falsely rejected by a stricter literal-string check. The
        // captured name is compared with ordinal equality (the default for ==) deliberately: the
        // renderer's value dictionary lookup is also Ordinal (StringComparer.Ordinal), so a mis-cased
        // token such as {{UNSUBSCRIBE_URL}} would resolve to "" at render time and must not be
        // accepted here as satisfying the rule either — do not change this to OrdinalIgnoreCase.
        var shellHasUnsubscribe = shell is not null && HasUnsubscribeOutsideIf(shell.Content);
        var footerHasUnsubscribe = parsed.Blocks.TryGetValue(TemplateBlocks.Footer, out var footerBlock)
            && HasUnsubscribeOutsideIf(footerBlock.Content);
        if (!shellHasUnsubscribe && !footerHasUnsubscribe)
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1,
                "The template has no {{unsubscribe_url}} that is guaranteed to render: place it in "
                + "BLOCK: shell, or in BLOCK: footer outside any <!-- IF --> region — commercial email "
                + "must always carry an unsubscribe link."));

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
            var line = block.ContentLine + TemplateParser.LineOf(block.Content, m.Index) - 1;
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

    /// <summary>True when {{unsubscribe_url}} appears in content with the bodies of every
    /// <!-- IF -->...<!-- /IF --> region removed first — i.e. the token sits somewhere that is not
    /// conditional on a field being non-empty. IF regions are validated elsewhere to be non-nested,
    /// so a single non-recursive strip is sufficient; a malformed (unclosed) region simply fails to
    /// match here and is reported separately by ValidateRegions.</summary>
    private static bool HasUnsubscribeOutsideIf(string content) =>
        PlaceholderRegex().Matches(IfRegionRegex().Replace(content, ""))
            .Any(m => m.Groups["name"].Value == "unsubscribe_url");

    // IgnoreCase so a mis-capitalised placeholder is still matched and reported as unknown — the
    // vocabulary itself stays lowercase-only (name is NOT normalised), so {{Title}} is seen but
    // fails the allowed-set check rather than silently passing or being silently accepted.
    [GeneratedRegex(@"\{\{\s*(?<name>[a-z_]+)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"<!--\s*(?:(?<open>IF)\s*:\s*(?<cond>[A-Za-z_]+)|/IF)\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex RegionRegex();

    [GeneratedRegex(@"<!--\s*IF\s*:\s*[A-Za-z_]+\s*-->.*?<!--\s*/IF\s*-->",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex IfRegionRegex();
}
