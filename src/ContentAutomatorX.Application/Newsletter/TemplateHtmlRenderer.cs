using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Renders an issue into a tenant's own template. Never throws on a template problem:
/// a missing block falls back to the built-in design for that section, an unresolvable placeholder
/// resolves empty. Validation is a save-time concern (TemplateValidator), not a render-time one —
/// a template edit must never be able to fail a scheduled send.</summary>
public static partial class TemplateHtmlRenderer
{
    public static string Render(IReadOnlyList<IssueSection> sections, Tenant tenant, string title,
        string templateHtml, DateTimeOffset issueDate)
    {
        var parsed = TemplateParser.Parse(templateHtml);
        if (!parsed.Blocks.TryGetValue(TemplateBlocks.Shell, out var shell)) return "";

        var branding = TenantBranding.Parse(tenant.BrandingJson);
        var accent = SafeAccent(branding.AccentColorHex);
        var globals = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant_name"] = WebUtility.HtmlEncode(tenant.Name),
            ["accent"] = accent,
            ["issue_title"] = WebUtility.HtmlEncode(title),
            ["issue_date"] = issueDate.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ["unsubscribe_url"] = SectionHtmlRenderer.UnsubscribeToken
        };

        var body = new StringBuilder();
        foreach (var section in sections.OrderBy(s => s.Position))
        {
            var blockName = TemplateBlocks.ForSectionType(section.Type);
            if (blockName is not null && parsed.Blocks.TryGetValue(blockName, out var block))
                body.AppendLine(RenderBlock(block, SectionValues(section, tenant, accent, globals)));
            else
                body.AppendLine(SectionHtmlRenderer.RenderSection(section, accent));
        }

        var shellValues = new Dictionary<string, string>(globals, StringComparer.Ordinal)
        {
            ["preheader"] = Preheader(sections),
            ["sections"] = body.ToString()
        };
        return RenderBlock(shell, shellValues);
    }

    /// <summary>Regions first, then placeholders: a dropped region's placeholders should never be
    /// substituted, and substituting first would let a value's own text disturb region matching.</summary>
    private static string RenderBlock(TemplateBlock block, IReadOnlyDictionary<string, string> values)
    {
        var text = ApplyRegions(block.Content, values);
        // IgnoreCase (see PlaceholderRegex) means a mis-cased token such as {{Title}} is still
        // recognised as a placeholder attempt rather than left as literal text in a sent email —
        // but the dictionary lookup below stays case-sensitive against the (always lowercase)
        // known vocabulary, so a mis-cased name simply resolves empty rather than being guessed
        // at. TemplateValidator already rejects {{Title}} at save time; this is defence in depth
        // for templates that reach render some other way.
        return PlaceholderRegex().Replace(text, m =>
            values.TryGetValue(m.Groups["name"].Value, out var value) ? value : "");
    }

    private static string ApplyRegions(string text, IReadOnlyDictionary<string, string> values)
    {
        var sb = new StringBuilder();
        var cursor = 0;

        while (true)
        {
            var open = RegionOpenRegex().Match(text, cursor);
            if (!open.Success) break;

            var close = RegionCloseRegex().Match(text, open.Index + open.Length);
            if (!close.Success) break;   // unclosed: leave the rest verbatim rather than truncating

            sb.Append(text, cursor, open.Index - cursor);

            var target = TemplatePlaceholders.TargetOf(open.Groups["cond"].Value.ToLowerInvariant());
            var keep = target is not null
                && values.TryGetValue(target, out var value)
                && !string.IsNullOrWhiteSpace(value);
            if (keep)
                sb.Append(text, open.Index + open.Length, close.Index - (open.Index + open.Length));

            cursor = close.Index + close.Length;
        }

        sb.Append(text, cursor, text.Length - cursor);
        return sb.ToString();
    }

    private static Dictionary<string, string> SectionValues(IssueSection section, Tenant tenant,
        string accent, IReadOnlyDictionary<string, string> globals)
    {
        var values = new Dictionary<string, string>(globals, StringComparer.Ordinal)
        {
            ["title"] = WebUtility.HtmlEncode(section.Title ?? ""),
            ["body_html"] = EmailHtmlRenderer.RenderFragment(section.BodyMd ?? "", accent),
            ["category"] = WebUtility.HtmlEncode(section.Category ?? ""),
            ["reading_time"] = ReadingTime.Describe(section.BodyMd),
            ["link_url"] = SafeUrl(section.LinkUrl, allowMailto: true),
            ["link_text"] = WebUtility.HtmlEncode(section.LinkText ?? DefaultLinkText(section.Type)),
            ["sender_identity"] = WebUtility.HtmlEncode(tenant.SenderIdentity ?? "")
        };

        if (section.Type == SectionTypes.Video)
        {
            values["video_url"] = values["link_url"];
            values["thumbnail_url"] = SafeUrl(SectionHtmlRenderer.VideoThumbnail(section), allowMailto: false);
        }
        else
        {
            values["image_url"] = SafeUrl(section.ImageUrl, allowMailto: false);
        }
        return values;
    }

    private static string DefaultLinkText(string sectionType) => sectionType switch
    {
        SectionTypes.Topic => "Read more →",
        SectionTypes.Video => "Watch on YouTube →",
        SectionTypes.Sponsor => "Learn more",
        _ => "Open"
    };

    /// <summary>A rejected URL resolves to empty, not to '#', so the enclosing IF region collapses
    /// and no broken image or dead link is emitted at all.</summary>
    private static string SafeUrl(string? url, bool allowMailto)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var ok = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
              || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || (allowMailto && url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));
        return ok ? WebUtility.HtmlEncode(url) : "";
    }

    private static string Preheader(IReadOnlyList<IssueSection> sections)
    {
        var header = sections.FirstOrDefault(s => s.Type == SectionTypes.Header);
        if (string.IsNullOrWhiteSpace(header?.BodyMd)) return "";
        var text = MarkdownSyntaxRegex().Replace(header.BodyMd, "").Replace('\n', ' ').Trim();
        if (text.Length > 200) text = text[..200];
        return WebUtility.HtmlEncode(text);
    }

    private static string SafeAccent(string? hex) =>
        hex is not null && AccentRegex().IsMatch(hex) ? hex : EmailHtmlRenderer.DefaultAccent;

    // IgnoreCase: see the comment on RenderBlock. Matching is deliberately looser than lookup.
    [GeneratedRegex(@"\{\{\s*(?<name>[a-z_]+)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"<!--\s*IF\s*:\s*(?<cond>[A-Za-z_]+)\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex RegionOpenRegex();

    [GeneratedRegex(@"<!--\s*/IF\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex RegionCloseRegex();

    [GeneratedRegex(@"[#*_`]+")]
    private static partial Regex MarkdownSyntaxRegex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex AccentRegex();
}
