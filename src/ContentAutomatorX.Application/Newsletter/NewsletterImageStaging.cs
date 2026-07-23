using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Staging-store request path + the two image-source resolvers the renderers take.
/// Preview points at the local staging endpoint; push (PR 1) omits staged images and keeps
/// only pasted hotlinks — PR 2 replaces the push resolver with the R2 one.</summary>
public static class NewsletterImageStaging
{
    /// <summary>App-relative path the staging directory is served under (see Program.cs).
    /// Single source of the string; the Web store and static-file mapping reuse it.</summary>
    public const string RequestPath = "/newsletter-images";

    /// <summary>Composer preview: a staged upload resolves to its local endpoint; otherwise a
    /// pasted/auto-metadata hotlink; otherwise null (the renderer then tries the YouTube still).</summary>
    public static string? PreviewSrc(IssueSection s) =>
        !string.IsNullOrEmpty(s.ImageKey) ? $"{RequestPath}/{s.ImageKey}"
        : SectionHtmlRenderer.IsHttpUrl(s.ImageUrl) ? s.ImageUrl
        : null;

    /// <summary>Push/send (PR 1): a staged image has no external host yet, so it is omitted;
    /// only pasted hotlinks render. Equals the renderer's default resolver.</summary>
    public static string? PushSrc(IssueSection s) =>
        SectionHtmlRenderer.IsHttpUrl(s.ImageUrl) ? s.ImageUrl : null;
}
