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
        if (!parsed.Blocks.TryGetValue(TemplateBlocks.Shell, out var shell))
            // Backstop that should never fire: TemplateValidator's E3 rule blocks saving a template
            // with no BLOCK: shell, so reaching this needs a validator bug or a row written straight
            // to the database. Returning "" here used to bypass EnsureUnsubscribeLink entirely —
            // PostService.PushAsync takes whatever this returns unconditionally when the issue has
            // sections, so an empty string became an empty campaign body with no unsubscribe link at
            // all. Falling back to the built-in renderer instead guarantees the issue still goes out
            // with real content and its own unsubscribe footer, the same guarantee EnsureUnsubscribeLink
            // exists to provide for every other template defect.
            return SectionHtmlRenderer.Render(sections, tenant, title);

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
        return EnsureUnsubscribeLink(RenderBlock(shell, shellValues));
    }

    /// <summary>Backstop only — not the normal path. TemplateValidator is the primary gate that
    /// guarantees every saved template carries a {{unsubscribe_url}} that always renders; this method
    /// exists because two independent adversarial reviews each found a different way to make that
    /// validator accept a template that does not actually guarantee the token (see TemplateValidator's
    /// HasUnsubscribeOutsideIf history). Sending commercial email with no unsubscribe link is a legal
    /// violation, not a cosmetic bug, so that guarantee should not rest on the validator being perfect.
    /// If the assembled HTML genuinely has no UnsubscribeToken anywhere — whatever the reason, bad
    /// template, a section list built by some path that skips EnsureSectionsAsync's header/footer
    /// seeding, a future validator bug — append a minimal, plainly-styled unsubscribe line so the
    /// token is always present before this leaves the renderer. A template that already carries the
    /// token is untouched: this must never alter the output of a correct template.</summary>
    private static string EnsureUnsubscribeLink(string html)
    {
        var comments = CommentScanner.Find(html);
        if (HasUnsubscribeOutsideComments(html, comments)) return html;

        // Same visual weight as SectionHtmlRenderer.Render's own footer line — small, muted, unobtrusive.
        var paragraph = "<p style=\"margin:0;font-size:12px;color:#888888;\">"
            + $"<a href=\"{SectionHtmlRenderer.UnsubscribeToken}\" style=\"color:#888888;\">Unsubscribe</a></p>";

        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        var insertAt = bodyClose >= 0 ? bodyClose : html.Length;

        // The naive "</body>" search above finds the LAST such tag textually, but a comment span
        // (terminated or not) can still cover that index — e.g. an unterminated "<!--" started before
        // it (eof-in-comment swallows to end of document), or a perfectly well-formed "<!-- ... -->"
        // whose own closer happens to sit at or after the last "</body>" (an ordinary explanatory
        // comment placed after </body></html>, or an IF region's collapse dragging a comment's closer
        // past it, or a comment whose text itself contains a literal "</body>" that drags this
        // LastIndexOf search inside the comment). Whichever the cause, the fix is the same: find the
        // one comment span that actually contains insertAt, not just "is there some unterminated span
        // before it" — a terminated span hides the insertion point just as completely as an
        // unterminated one does.
        var host = comments.FirstOrDefault(c => insertAt >= c.Start && insertAt < c.End);
        if (host != default)
        {
            if (host.Terminated)
                // Step out past the comment's own closing "-->" instead of inserting inside it —
                // the fallback then lands in real markup right after the comment that was hiding it.
                insertAt = host.End;
            else
                // Unterminated comment (Input A/B): nothing can close it but us. Emitting "-->" first
                // closes it right there, so the fallback that follows is actually visible instead of
                // being hidden inside the same dangling comment it exists to work around.
                paragraph = "-->" + paragraph;
        }

        return insertAt < html.Length ? html.Insert(insertAt, paragraph) : html + paragraph;
    }

    /// <summary>True when UnsubscribeToken (the already-substituted literal, not the {{placeholder}} —
    /// this runs after RenderBlock) appears somewhere that is not inside an HTML comment. A template
    /// author can comment out a block while editing (e.g. "<!-- <a href="%%UNSUBSCRIBE%%">Unsub</a>
    /// -->") and TemplateValidator's HasUnsubscribeOutsideIf rejects that at save time — but this is
    /// the render-time backstop for the same defeat, so it must apply the identical rule: a token's
    /// mere presence in the assembled HTML is not enough, since a naive Contains() check (the
    /// previous implementation) is satisfied by a token that a subscriber can never actually see or
    /// click. Matches on the rendered text and rejects by index span, same reasoning as
    /// TemplateValidator's comment/region checks — never rewrite the text being scanned.</summary>
    private static bool HasUnsubscribeOutsideComments(string html, IReadOnlyList<CommentScanner.CommentSpan> comments)
    {
        var token = SectionHtmlRenderer.UnsubscribeToken;
        var searchFrom = 0;
        while (true)
        {
            var found = html.IndexOf(token, searchFrom, StringComparison.Ordinal);
            if (found < 0) return false;
            if (!CommentScanner.IsInside(comments, found)) return true;
            searchFrom = found + token.Length;
        }
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
            if (!close.Success)
            {
                // unclosed: drop everything from the marker to the end of the block, rather than
                // emitting the marker verbatim and letting its placeholders be substituted.
                sb.Append(text, cursor, open.Index - cursor);
                cursor = text.Length;
                break;
            }

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
        var header = sections.OrderBy(s => s.Position).FirstOrDefault(s => s.Type == SectionTypes.Header);
        if (string.IsNullOrWhiteSpace(header?.BodyMd)) return "";
        var text = MarkdownSyntaxRegex().Replace(header.BodyMd, "");
        text = MarkdownEmphasisRegex().Replace(text, "").Replace('\n', ' ').Trim();
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

    [GeneratedRegex(@"[#`]+")]
    private static partial Regex MarkdownSyntaxRegex();

    // * and _ carry markdown meaning (bold/italic) only at a word boundary — same reasoning and
    // pattern as ReadingTime.EmphasisRegex. An intra-word underscore ("well_known") has word
    // characters on both immediate sides and must be left alone, or ordinary prose gets mangled in
    // the inbox preview line.
    [GeneratedRegex(@"(?<!\w)[*_]+|[*_]+(?!\w)")]
    private static partial Regex MarkdownEmphasisRegex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex AccentRegex();
}
