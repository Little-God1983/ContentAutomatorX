namespace ContentAutomatorX.Domain.Models;

/// <summary>The distinct LLM jobs a tenant can override independently. Each is a key stored in
/// <c>TenantLlmSetting.Job</c>; a <c>null</c> Job is the tenant's default that every job falls back
/// to (see <c>ILlmSettingsProvider</c>). The jobs do not have equal appetites — subject lines are
/// fine on a cheap model, research and drafting want a strong one — which is the whole reason a
/// tenant may want a different tier per job rather than one tier for everything.
///
/// <para>Values are kebab-case so the persisted column stays readable and shares one vocabulary with
/// the AI Studio job-binding table. Lives in Domain (like <c>SourceTypes</c>) so both the Application
/// call sites and the Web UI reference the same constants; friendly labels are a Web concern.</para>
/// </summary>
public static class LlmJobs
{
    /// <summary>Bulk topic-blurb generation (IssueComposerService.GenerateTopicsAsync).</summary>
    public const string TopicBlurbs = "topic-blurbs";

    /// <summary>Rewrite of a single section (IssueComposerService.RegenerateSectionAsync).</summary>
    public const string RegenerateSection = "regenerate-section";

    /// <summary>The issue-composer chat turn (IssueChatService.RunTurnAsync).</summary>
    public const string ComposerChat = "composer-chat";

    /// <summary>Email subject-line ideas (PostService.SubjectIdeasAsync) — trivial, a cheap model is plenty.</summary>
    public const string SubjectIdeas = "subject-ideas";

    /// <summary>Full draft from a recipe (GenerationPipeline.RunCoreAsync) — the heavy one.</summary>
    public const string RecipeDraft = "recipe-draft";

    /// <summary>Web research → JSON (LlmResearchConnector.FetchAsync) — wants a strong model.</summary>
    public const string Research = "research";

    /// <summary>Every job key, in the order the AI Studio job-binding table renders them.</summary>
    public static readonly IReadOnlyList<string> All =
        [TopicBlurbs, RegenerateSection, ComposerChat, SubjectIdeas, RecipeDraft, Research];
}
