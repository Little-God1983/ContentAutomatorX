using System.Text.RegularExpressions;
using Markdig;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>One fixed, email-safe template (spec decision: per-tenant templates are a later
/// nicety). Inline styles only; raw HTML in the markdown is escaped, not passed through.</summary>
public static partial class EmailHtmlRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UsePipeTables()
        .DisableHtml() // raw HTML → escaped text; keeps the campaign script/iframe-free
        .Build();

    private static readonly string[] AllowedHrefSchemes = ["http://", "https://", "mailto:"];
    private static readonly string[] AllowedSrcSchemes = ["http://", "https://"];

    public const string DefaultAccent = "#1e88e5";

    /// <summary>Markdown → inline-styled HTML fragment (no document wrapper). The default
    /// accent (#1e88e5) baked into InlineStyles is recolored when a custom accent is given.</summary>
    public static string RenderFragment(string markdown, string accentHex = DefaultAccent)
    {
        var body = Markdown.ToHtml(markdown ?? "", Pipeline);
        body = InlineStyles(body);
        return accentHex == DefaultAccent
            ? body
            : body.Replace($"color:{DefaultAccent};", $"color:{accentHex};")
                  .Replace($"border-left:3px solid {DefaultAccent};", $"border-left:3px solid {accentHex};");
    }

    public static string Render(string markdown, string title)
    {
        var body = RenderFragment(markdown);
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>{safeTitle}</title></head>
        <body style="margin:0;padding:0;background:#f4f4f4;">
          <div style="max-width:640px;margin:0 auto;padding:24px;background:#ffffff;
                      font-family:Segoe UI,Arial,sans-serif;font-size:16px;line-height:1.6;color:#222222;">
        {body}
          </div>
        </body>
        </html>
        """;
    }

    private static string InlineStyles(string html)
    {
        html = AnchorRegex().Replace(html, StyleAnchor);
        html = ImgRegex().Replace(html, StyleImg);
        html = OrderedListRegex().Replace(html, "<ol${attrs} style=\"margin:0 0 14px;padding-left:24px;\">");
        html = TableCellRegex().Replace(html, StyleTableCell);
        return html
            .Replace("<h1>", "<h1 style=\"font-size:26px;margin:24px 0 12px;color:#111111;\">")
            .Replace("<h2>", "<h2 style=\"font-size:21px;margin:20px 0 10px;color:#111111;\">")
            .Replace("<h3>", "<h3 style=\"font-size:18px;margin:16px 0 8px;color:#111111;\">")
            .Replace("<p>", "<p style=\"margin:0 0 14px;\">")
            .Replace("<ul>", "<ul style=\"margin:0 0 14px;padding-left:24px;\">")
            .Replace("<li>", "<li style=\"margin:0 0 6px;\">")
            .Replace("<table>", "<table style=\"border-collapse:collapse;width:100%;margin:0 0 14px;\">")
            .Replace("<blockquote>", "<blockquote style=\"margin:0 0 14px;padding:8px 16px;border-left:3px solid #1e88e5;color:#444444;\">")
            .Replace("<hr />", "<hr style=\"border:none;border-top:1px solid #dddddd;margin:20px 0;\" />");
    }

    // Markdig emits bare <th>/<td> for unaligned columns and <th style="text-align: center;">
    // (etc.) for columns declared with :---:/---: — either shape must still get the email's
    // border/padding treatment, and any declared alignment must survive into the final style.
    private static string StyleTableCell(Match m)
    {
        var tag = m.Groups["tag"].Value;
        var align = m.Groups["align"].Success ? m.Groups["align"].Value : null;
        return tag == "th"
            ? $"<th style=\"border:1px solid #dddddd;padding:6px 10px;text-align:{align ?? "left"};background:#f7f7f7;\">"
            : $"<td style=\"border:1px solid #dddddd;padding:6px 10px;{(align is null ? "" : $"text-align:{align};")}\">";
    }

    [GeneratedRegex("""<(?<tag>th|td)(?: style="text-align: (?<align>left|center|right);")?>""")]
    private static partial Regex TableCellRegex();

    [GeneratedRegex("<ol(?<attrs>[^>]*)>")]
    private static partial Regex OrderedListRegex();

    // Anchors carrying a non-http(s)/mailto scheme (e.g. javascript:) are rendered inert —
    // the href is dropped entirely rather than passed through to a clickable preview/campaign.
    private static string StyleAnchor(Match m)
    {
        var url = m.Groups["href"].Value.Trim();
        var rest = m.Groups["rest"].Value;
        var safe = AllowedHrefSchemes.Any(scheme => url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));
        return safe
            ? $"<a style=\"color:#1e88e5;\" href=\"{m.Groups["href"].Value}\"{rest}>"
            : "<a style=\"color:#1e88e5;\" href=\"#\">";
    }

    [GeneratedRegex("<a href=\"(?<href>[^\"]*)\"(?<rest>[^>]*)>")]
    private static partial Regex AnchorRegex();

    // Same rule as StyleAnchor, applied to Markdig's image syntax. A non-http(s) src (e.g.
    // javascript: or data:) never reaches a mail client or browser today, but the renderer's
    // scheme check must hold for every URL-bearing value, images included — not anchors only.
    // This also rejects relative and protocol-relative URLs (/images/a.png, //cdn.example.com/a.png)
    // deliberately: a sent email has no base URL to resolve them against, so they would never load
    // for a subscriber — matching the pre-existing anchor check, which rejects relative hrefs too.
    private static string StyleImg(Match m)
    {
        var url = m.Groups["src"].Value.Trim();
        var safe = AllowedSrcSchemes.Any(scheme => url.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));
        return safe
            ? m.Value
            : "<img src=\"\" />";
    }

    // No "rest" capture here (unlike AnchorRegex/StyleAnchor): StyleImg never needs the trailing
    // attributes on their own — the safe branch returns m.Value (the whole original tag) verbatim,
    // and the unsafe branch replaces the tag wholesale, so there is nothing for a separate group to
    // feed back into the replacement.
    [GeneratedRegex("<img src=\"(?<src>[^\"]*)\"[^>]*>")]
    private static partial Regex ImgRegex();
}
