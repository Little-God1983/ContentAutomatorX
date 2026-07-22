using System.Text.Json;
using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class PostService(IAppDbContext db, GenerationPipeline generation, ILlmBackend llm,
    PlatformService platforms, IMailerLiteClient mailerLite, ILlmSettingsProvider llmSettings,
    NewsletterTemplateService templates)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> SuggestTitleAsync(Guid recipeId, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId, ct);
        var n = await db.Posts.CountAsync(p => p.RecipeId == recipeId, ct) + 1;
        return $"{recipe.Name} #{n}";
    }

    public async Task<Post> CreateIssueAsync(Guid tenantId, Guid recipeId, int windowDays,
        IReadOnlyList<Guid>? sourceIds, string title, CancellationToken ct = default)
    {
        var platform = await platforms.GetOrCreateMailerLiteAsync(tenantId, ct);
        var post = new Post
        {
            TenantId = tenantId, PlatformId = platform.Id, RecipeId = recipeId,
            Kind = DraftKinds.Newsletter, Title = title, WindowDays = windowDays,
            SourceIdsJson = sourceIds is null ? null : JsonSerializer.Serialize(sourceIds)
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);
        return post;
    }

    public Task<Post?> GetAsync(Guid postId, CancellationToken ct = default) =>
        db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);

    // A cross-scope compose (see IssueEditor) creates a NEW draft under a fresh DbContext and
    // updates the Post row's DraftId there. This circuit's own context may already be tracking the
    // OLD Post instance, and EF's change tracker won't notice the row changed underneath it — so a
    // later save through this context would silently write into the orphaned old draft. Reloading
    // the tracked entity's values from the database (when this really is a DbContext, i.e. never in
    // the pipeline's own save path) keeps the two in sync.
    public async Task<Post?> GetFreshAsync(Guid postId, CancellationToken ct = default)
    {
        var tracked = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (tracked is not null && db is Microsoft.EntityFrameworkCore.DbContext ctx)
            await ctx.Entry(tracked).ReloadAsync(ct);
        return tracked;
    }

    public Task<Draft?> GetDraftAsync(Guid draftId, CancellationToken ct = default) =>
        db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId, ct);

    // SQLite cannot ORDER BY DateTimeOffset server-side, so materialize the filtered
    // query first and sort client-side (acceptable at this app's per-tenant scale).
    public async Task<List<Post>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        var list = await db.Posts.Where(p => p.TenantId == tenantId).ToListAsync(ct);
        return list.OrderByDescending(p => p.CreatedAt).ToList();
    }

    public async Task<List<Post>> ReviewQueueAsync(Guid tenantId, CancellationToken ct = default)
    {
        var list = await db.Posts.Where(p => p.TenantId == tenantId &&
                (p.NeedsReview || p.Status == PostStatus.Pushed || p.Status == PostStatus.Failed) &&
                p.Status != PostStatus.Published)
            .ToListAsync(ct);
        return list.OrderBy(p => p.CreatedAt).ToList();
    }

    public async Task<IReadOnlyList<Guid>> GetIssueSourceIdsAsync(Post post, CancellationToken ct = default)
    {
        if (post.SourceIdsJson is not null)
            return JsonSerializer.Deserialize<Guid[]>(post.SourceIdsJson) ?? [];
        var recipe = await db.Recipes.SingleAsync(r => r.Id == post.RecipeId, ct);
        var fromRecipe = JsonSerializer.Deserialize<Guid[]>(recipe.SourceIdsJson) ?? [];
        if (fromRecipe.Length > 0) return fromRecipe;
        return await db.Sources.Where(s => s.TenantId == post.TenantId).Select(s => s.Id).ToArrayAsync(ct);
    }

    public async Task SetIssueSourcesAsync(Post post, IReadOnlyList<Guid> sourceIds, CancellationToken ct = default)
    {
        post.SourceIdsJson = JsonSerializer.Serialize(sourceIds);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ContentItem>> GetCandidatesAsync(Post post, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.SingleAsync(r => r.Id == post.RecipeId, ct);
        var sourceIds = (await GetIssueSourceIdsAsync(post, ct)).ToHashSet();

        var candidates = await db.ContentItems
            .Where(i => i.TenantId == post.TenantId && sourceIds.Contains(i.SourceId))
            .ToListAsync(ct);

        var priorDrafts = await db.Drafts.Where(d => d.RecipeId == recipe.Id)
            .Select(d => d.SourceItemIdsJson).ToListAsync(ct);
        var used = priorDrafts.SelectMany(j => JsonSerializer.Deserialize<string[]>(j) ?? [])
            .Select(Guid.Parse).ToHashSet();

        var rules = JsonSerializer.Deserialize<SelectionRules>(recipe.SelectionJson, JsonOpts) ?? new SelectionRules();
        rules.TimeWindowDays = post.WindowDays;
        rules.MaxItems = 50; // generous pool; the human checks what compose actually gets
        return ItemSelector.Select(candidates, rules, used, DateTimeOffset.UtcNow);
    }

    public async Task<(PipelineRun Run, Post Post)> ComposeAsync(Guid postId, IReadOnlyList<Guid> itemIds,
        string? extraInstructions, CancellationToken ct = default)
    {
        if (itemIds.Count == 0) throw new InvalidOperationException("Pick at least one item to compose from.");
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var recipeId = post.RecipeId ?? throw new InvalidOperationException("Issue has no automation to compose with.");

        // The issue's own Post row already exists — don't let the pipeline park a second one.
        var (run, draft) = await generation.RunAsync(recipeId, itemIds, extraInstructions, RunTriggers.Manual,
            createReviewPost: false, ct: ct);
        if (draft is not null)
        {
            post.DraftId = draft.Id;
            post.Title = draft.Title;
            post.Subject ??= draft.Title;
            await db.SaveChangesAsync(ct);
        }
        return (run, post);
    }

    public async Task SaveIssueAsync(Guid postId, string title, string body, string? subject,
        string? previewText, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        Draft draft;
        if (post.DraftId is Guid draftId)
        {
            draft = await db.Drafts.SingleAsync(d => d.Id == draftId, ct);
        }
        else
        {
            draft = new Draft
            {
                TenantId = post.TenantId, RecipeId = post.RecipeId ?? Guid.Empty,
                Kind = post.Kind, Title = title
            };
            db.Drafts.Add(draft);
            post.DraftId = draft.Id;
        }
        draft.Title = title;
        draft.Body = body;
        post.Title = title;
        post.Subject = subject;
        post.PreviewText = previewText;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Title/subject/preview for a sectioned issue — body lives in IssueSections,
    /// so unlike SaveIssueAsync this never creates or touches a Draft.</summary>
    public async Task SaveIssueMetaAsync(Guid postId, string title, string? subject,
        string? previewText, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        post.Title = title;
        post.Subject = subject;
        post.PreviewText = previewText;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> SubjectIdeasAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var sections = await db.IssueSections.Where(s => s.PostId == postId)
            .OrderBy(s => s.Position).ToListAsync(ct);
        string body;
        if (sections.Count > 0)
        {
            body = SectionHtmlRenderer.ToMarkdown(sections);
        }
        else
        {
            var draft = post.DraftId is Guid id ? await db.Drafts.SingleAsync(d => d.Id == id, ct)
                : throw new InvalidOperationException("Nothing to write subjects for yet.");
            body = draft.Body;
        }
        var excerpt = body.Length <= 4000 ? body : body[..4000];
        var settings = await llmSettings.GetAsync(post.TenantId, LlmJobs.SubjectIdeas, ct);
        var prompt = $"""
            Write 5 email subject lines for this newsletter issue. Punchy, concrete, <60 chars, no clickbait.
            Respond with ONLY a JSON array of 5 strings, no prose, no markdown fences.

            Issue body:
            {excerpt}
            """;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var reply = await llm.GenerateAsync(attempt == 1 ? prompt
                : prompt + "\nYour previous reply was not valid JSON. ONLY the JSON array.", settings, ct);
            if (TryParseStringArray(reply.Text, out var subjects)) return subjects!;
        }
        throw new InvalidOperationException("Model did not return subject lines as JSON.");
    }

    public async Task<Post> PushAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        if (post.Status == PostStatus.Published)
            throw new InvalidOperationException("This issue was already sent — create a new issue instead.");
        var platform = await db.Platforms.SingleAsync(p => p.Id == post.PlatformId, ct);
        var config = platforms.GetConfig(platform);
        var apiKey = await platforms.GetApiKeyAsync(platform, ct);
        if (apiKey is null || config.GroupId is null || config.FromName is null || config.FromEmail is null)
            throw new InvalidOperationException("MailerLite is not fully configured — finish setup on the Platforms page.");

        var subject = string.IsNullOrWhiteSpace(post.Subject) ? post.Title : post.Subject;
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("Set a subject (or title) before pushing.");
        if (subject.Length > 255)
            throw new InvalidOperationException("Subject must be 255 characters or fewer (MailerLite limit).");

        var sections = await db.IssueSections.Where(s => s.PostId == postId)
            .OrderBy(s => s.Position).ToListAsync(ct);
        string html;
        if (sections.Count > 0)
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
            var template = await templates.ResolveForPostAsync(post.Id, ct);
            html = (template is null
                    ? SectionHtmlRenderer.Render(sections, tenant, post.Title)
                    : TemplateHtmlRenderer.Render(sections, tenant, post.Title, template.Html, post.CreatedAt))
                .Replace(SectionHtmlRenderer.UnsubscribeToken, "{$unsubscribe}"); // MailerLite's variable
        }
        else // legacy free-markdown issue
        {
            var draft = post.DraftId is Guid id ? await db.Drafts.SingleAsync(d => d.Id == id, ct)
                : throw new InvalidOperationException("Compose or write the issue first.");
            html = EmailHtmlRenderer.Render(draft.Body, post.Title);
        }
        try
        {
            var campaignId = await mailerLite.PushDraftAsync(apiKey, new MailerLiteDraft(
                Name: post.Title, Subject: subject, PreviewText: post.PreviewText,
                FromName: config.FromName, FromEmail: config.FromEmail, GroupId: config.GroupId, Html: html),
                post.ExternalId, ct);
            post.ExternalId = campaignId;
            post.ExternalUrl = $"https://dashboard.mailerlite.com/campaigns/{campaignId}";
            post.Status = PostStatus.Pushed;
            post.NeedsReview = false;
            await db.SaveChangesAsync(ct);
            return post;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            post.Status = PostStatus.Failed;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task MarkReviewedAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        post.NeedsReview = false;
        await db.SaveChangesAsync(ct);
    }

    internal static bool TryParseStringArray(string text, out List<string>? values)
    {
        values = null;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }
        try
        {
            values = JsonSerializer.Deserialize<List<string>>(trimmed);
            return values is { Count: > 0 };
        }
        catch (JsonException) { return false; }
    }
}
