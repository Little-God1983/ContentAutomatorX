using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Renders an ordered issue-section list into one email-safe HTML document —
/// single 600px column, nested tables, inline styles only. ESP-neutral: the unsubscribe
/// link is emitted as UnsubscribeToken; the pushing connector substitutes its own variable
/// (MailerLite: {$unsubscribe}). The preview substitutes '#'.</summary>
public static partial class SectionHtmlRenderer
{
    public const string UnsubscribeToken = "%%UNSUBSCRIBE%%";

    public static string Render(IReadOnlyList<IssueSection> sections, Tenant tenant, string title)
    {
        var branding = TenantBranding.Parse(tenant.BrandingJson);
        var accent = SafeAccent(branding.AccentColorHex);
        var font = EmailFonts.Stack(branding.FontKey);
        var safeTitle = WebUtility.HtmlEncode(title);

        var sb = new StringBuilder();
        sb.AppendLine($"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><title>{safeTitle}</title></head>
            <body style="margin:0;padding:0;background:#f4f4f4;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f4;"><tr><td align="center" style="padding:24px 8px;">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;font-family:{font};font-size:16px;line-height:1.6;color:#222222;"><tr><td style="padding:24px;">
            """);
        if (IsHttpUrl(branding.LogoUrl))
            sb.AppendLine($"""<img src="{WebUtility.HtmlEncode(branding.LogoUrl)}" alt="{WebUtility.HtmlEncode(tenant.Name)}" style="max-width:200px;height:auto;border:0;display:block;margin:0 auto 16px;" />""");
        sb.AppendLine($"""<h1 style="font-size:26px;margin:0 0 16px;color:#111111;">{safeTitle}</h1>""");

        foreach (var section in sections.OrderBy(s => s.Position))
            AppendSection(sb, section, accent);

        sb.AppendLine($"""
            <hr style="border:none;border-top:1px solid #dddddd;margin:24px 0 12px;" />
            <p style="margin:0 0 6px;font-size:12px;color:#888888;">{WebUtility.HtmlEncode(tenant.SenderIdentity)}</p>
            <p style="margin:0;font-size:12px;color:#888888;"><a href="{UnsubscribeToken}" style="color:#888888;">Unsubscribe</a></p>
              </td></tr></table>
              </td></tr></table>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    /// <summary>The built-in markup for one section, used as TemplateHtmlRenderer's per-section
    /// fallback when a template has no block for that type. Assumes it sits inside a 600px table
    /// cell, which the template's shell provides.</summary>
    public static string RenderSection(IssueSection section, string accent)
    {
        var sb = new StringBuilder();
        AppendSection(sb, section, accent);
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, IssueSection s, string accent)
    {
        var title = WebUtility.HtmlEncode(s.Title ?? "");
        switch (s.Type)
        {
            case SectionTypes.Header:
            case SectionTypes.Footer:
            case SectionTypes.LegacyBody:
                sb.AppendLine(EmailHtmlRenderer.RenderFragment(s.BodyMd ?? "", accent));
                break;

            case SectionTypes.Topic:
                if (title.Length > 0)
                    sb.AppendLine($"""<h2 style="font-size:21px;margin:20px 0 10px;color:{accent};">{title}</h2>""");
                if (IsHttpUrl(s.ImageUrl))
                    sb.AppendLine($"""<img src="{WebUtility.HtmlEncode(s.ImageUrl)}" alt="{title}" style="max-width:100%;height:auto;border:0;display:block;margin:0 0 10px;" />""");
                sb.AppendLine(EmailHtmlRenderer.RenderFragment(s.BodyMd ?? "", accent));
                if (IsHttpUrl(s.LinkUrl))
                    sb.AppendLine($"""<p style="margin:0 0 14px;"><a href="{WebUtility.HtmlEncode(s.LinkUrl)}" style="color:{accent};">Read more &rarr;</a></p>""");
                break;

            case SectionTypes.Video:
                if (title.Length > 0)
                    sb.AppendLine($"""<h2 style="font-size:21px;margin:20px 0 10px;color:{accent};">{title}</h2>""");
                var thumbnail = VideoThumbnail(s);
                if (thumbnail is not null)
                {
                    var img = $"""<img src="{WebUtility.HtmlEncode(thumbnail)}" alt="{title}" style="max-width:100%;height:auto;border:0;display:block;margin:0 0 10px;" />""";
                    sb.AppendLine(IsHttpUrl(s.LinkUrl)
                        ? $"""<a href="{WebUtility.HtmlEncode(s.LinkUrl)}">{img}</a>"""
                        : img);
                }
                sb.AppendLine(EmailHtmlRenderer.RenderFragment(s.BodyMd ?? "", accent));
                if (IsHttpUrl(s.LinkUrl))
                    AppendButton(sb, s.LinkUrl!, s.LinkText ?? "Watch on YouTube", accent);
                break;

            case SectionTypes.Sponsor:
                sb.AppendLine("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 14px;"><tr><td style="border:1px solid #dddddd;background:#f9f9f9;padding:16px;">""");
                sb.AppendLine("""<p style="margin:0 0 8px;font-size:11px;letter-spacing:1px;color:#888888;">SPONSORED</p>""");
                if (IsHttpUrl(s.ImageUrl))
                    sb.AppendLine($"""<img src="{WebUtility.HtmlEncode(s.ImageUrl)}" alt="{title}" style="max-height:40px;height:auto;border:0;display:block;margin:0 0 8px;" />""");
                if (title.Length > 0)
                    sb.AppendLine($"""<h3 style="font-size:18px;margin:0 0 8px;color:#111111;">{title}</h3>""");
                sb.AppendLine(EmailHtmlRenderer.RenderFragment(s.BodyMd ?? "", accent));
                if (IsHttpUrl(s.LinkUrl))
                    AppendButton(sb, s.LinkUrl!, s.LinkText ?? "Learn more", accent);
                sb.AppendLine("</td></tr></table>");
                break;

            case SectionTypes.Button:
                if (IsHttpUrl(s.LinkUrl))
                    AppendButton(sb, s.LinkUrl!, s.LinkText ?? "Open", accent);
                break;

            case SectionTypes.Divider:
                sb.AppendLine("""<hr style="border:none;border-top:1px solid #dddddd;margin:20px 0;" />""");
                break;
        }
    }

    // "Bulletproof" table button — renders in Outlook and every major client.
    private static void AppendButton(StringBuilder sb, string url, string text, string accent) =>
        sb.AppendLine($"""
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 auto 14px;"><tr><td style="border-radius:4px;background:{accent};">
            <a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;padding:10px 22px;font-size:16px;color:#ffffff;text-decoration:none;">{WebUtility.HtmlEncode(text)}</a>
            </td></tr></table>
            """);

    public static string ToMarkdown(IReadOnlyList<IssueSection> sections)
    {
        var sb = new StringBuilder();
        foreach (var s in sections.OrderBy(x => x.Position))
        {
            switch (s.Type)
            {
                case SectionTypes.Header:
                case SectionTypes.Footer:
                case SectionTypes.LegacyBody:
                    AppendMd(sb, s.BodyMd);
                    break;
                case SectionTypes.Topic:
                    if (!string.IsNullOrWhiteSpace(s.Title)) AppendMd(sb, $"## {s.Title}");
                    AppendMd(sb, s.BodyMd);
                    if (!string.IsNullOrWhiteSpace(s.LinkUrl)) AppendMd(sb, $"[Read more]({s.LinkUrl})");
                    break;
                case SectionTypes.Video:
                    if (!string.IsNullOrWhiteSpace(s.Title)) AppendMd(sb, $"## {s.Title}");
                    AppendMd(sb, s.BodyMd);
                    if (!string.IsNullOrWhiteSpace(s.LinkUrl))
                        AppendMd(sb, $"[{s.LinkText ?? "Watch on YouTube"}]({s.LinkUrl})");
                    break;
                case SectionTypes.Sponsor:
                    AppendMd(sb, $"**Sponsored{(string.IsNullOrWhiteSpace(s.Title) ? "" : $": {s.Title}")}**");
                    AppendMd(sb, s.BodyMd);
                    if (!string.IsNullOrWhiteSpace(s.LinkUrl)) AppendMd(sb, $"[{s.LinkText ?? "Learn more"}]({s.LinkUrl})");
                    break;
                case SectionTypes.Button:
                    if (!string.IsNullOrWhiteSpace(s.LinkUrl)) AppendMd(sb, $"[{s.LinkText ?? "Open"}]({s.LinkUrl})");
                    break;
                case SectionTypes.Divider:
                    AppendMd(sb, "---");
                    break;
            }
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendMd(StringBuilder sb, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        sb.AppendLine(text);
        sb.AppendLine();
    }

    private static string SafeAccent(string? hex) =>
        hex is not null && AccentRegex().IsMatch(hex) ? hex : EmailHtmlRenderer.DefaultAccent;

    internal static bool IsHttpUrl(string? url) =>
        url is not null &&
        (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>Override wins; otherwise derive from the YouTube URL. Null when neither works.</summary>
    internal static string? VideoThumbnail(IssueSection s) =>
        IsHttpUrl(s.ImageUrl) ? s.ImageUrl
        : YouTubeUrl.TryGetVideoId(s.LinkUrl, out var id) ? YouTubeUrl.FallbackThumbnail(id)
        : null;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex AccentRegex();
}
