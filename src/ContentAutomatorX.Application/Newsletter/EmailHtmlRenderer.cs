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

    public static string Render(string markdown, string title)
    {
        var body = Markdown.ToHtml(markdown ?? "", Pipeline);
        body = InlineStyles(body);
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
        return html
            .Replace("<h1>", "<h1 style=\"font-size:26px;margin:24px 0 12px;color:#111111;\">")
            .Replace("<h2>", "<h2 style=\"font-size:21px;margin:20px 0 10px;color:#111111;\">")
            .Replace("<h3>", "<h3 style=\"font-size:18px;margin:16px 0 8px;color:#111111;\">")
            .Replace("<p>", "<p style=\"margin:0 0 14px;\">")
            .Replace("<ul>", "<ul style=\"margin:0 0 14px;padding-left:24px;\">")
            .Replace("<li>", "<li style=\"margin:0 0 6px;\">")
            .Replace("<blockquote>", "<blockquote style=\"margin:0 0 14px;padding:8px 16px;border-left:3px solid #1e88e5;color:#444444;\">")
            .Replace("<hr />", "<hr style=\"border:none;border-top:1px solid #dddddd;margin:20px 0;\" />");
    }

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
}
