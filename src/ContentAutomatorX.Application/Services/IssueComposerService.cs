using System.Text;
using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public record TopicBlurb(Guid ItemId, string Title, string Blurb, string? Category);

/// <summary>Owns the structured-issue composer: section lifecycle (Task 4) and AI topic
/// generation (Task 5). An issue always has exactly one Header (first) and one Footer (last);
/// positions stay 0-based and contiguous after every mutation.</summary>
public class IssueComposerService(IAppDbContext db, ILlmBackend llm, PostService posts,
    ILlmSettingsProvider llmSettings, IssueHistoryService history, NewsletterTemplateService templates)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<IssueSection>> GetSectionsAsync(Guid postId, CancellationToken ct = default) =>
        await db.IssueSections.Where(s => s.PostId == postId).OrderBy(s => s.Position).ToListAsync(ct);

    public async Task<Post> CreateFromItemsAsync(Guid tenantId, Guid recipeId, IReadOnlyList<Guid> itemIds,
        string title, CancellationToken ct = default)
    {
        if (itemIds.Count == 0) throw new InvalidOperationException("Select at least one inbox item.");
        var post = await posts.CreateIssueAsync(tenantId, recipeId, 7, null, title, ct);
        await EnsureSectionsAsync(post.Id, ct);
        await AddTopicsFromItemsAsync(post.Id, itemIds, ct);
        return post;
    }

    public async Task EnsureSectionsAsync(Guid postId, CancellationToken ct = default)
    {
        if (await db.IssueSections.AnyAsync(s => s.PostId == postId, ct)) return;
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        db.IssueSections.Add(new IssueSection
            { PostId = postId, Position = 0, Type = SectionTypes.Header, BodyMd = tenant.DefaultHeaderMd });
        var position = 1;
        if (post.DraftId is Guid draftId) // legacy free-markdown issue → keep its body editable
        {
            var draft = await db.Drafts.SingleAsync(d => d.Id == draftId, ct);
            db.IssueSections.Add(new IssueSection
                { PostId = postId, Position = position++, Type = SectionTypes.LegacyBody, BodyMd = draft.Body });
        }
        db.IssueSections.Add(new IssueSection
            { PostId = postId, Position = position, Type = SectionTypes.Footer, BodyMd = tenant.DefaultFooterMd });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IssueSection> AddSectionAsync(Guid postId, string type, CancellationToken ct = default)
    {
        if (type is SectionTypes.Header or SectionTypes.Footer)
            throw new InvalidOperationException("An issue has exactly one header and one footer.");
        await history.SnapshotAsync(postId, "Add section", ct);
        var sections = await GetSectionsAsync(postId, ct);
        var section = new IssueSection { PostId = postId, Type = type };
        sections.Insert(Math.Max(sections.Count - 1, 0), section); // above the footer
        Renumber(sections);
        db.IssueSections.Add(section);
        await db.SaveChangesAsync(ct);
        return section;
    }

    public async Task AddTopicsFromItemsAsync(Guid postId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default)
    {
        await history.SnapshotAsync(postId, "Add topics", ct);
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var byId = (await db.ContentItems
                .Where(i => i.TenantId == post.TenantId && itemIds.Contains(i.Id)).ToListAsync(ct))
            .ToDictionary(i => i.Id);
        var sections = await GetSectionsAsync(postId, ct);
        var insertAt = Math.Max(sections.Count - 1, 0); // above the footer
        foreach (var id in itemIds) // preserve the caller's order
        {
            if (!byId.TryGetValue(id, out var item)) continue;
            var topic = new IssueSection
            {
                PostId = postId, Type = SectionTypes.Topic, Title = item.Title,
                LinkUrl = item.Url, SourceItemId = item.Id, ImageUrl = MetadataImage(item)
            };
            sections.Insert(insertAt++, topic);
            db.IssueSections.Add(topic);
        }
        Renumber(sections);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateSectionAsync(Guid sectionId, string? title, string? bodyMd,
        string? imageUrl, string? linkUrl, string? linkText, string? category,
        CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        await history.SnapshotAsync(section.PostId, "Edit section", ct);
        section.Title = title;
        section.BodyMd = bodyMd;
        section.ImageUrl = imageUrl;
        section.LinkUrl = linkUrl;
        section.LinkText = linkText;
        section.Category = category;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Points a section at a staged image (upload or URL import). Clears any pasted
    /// hotlink so a section has exactly one image source. Returns the previous ImageKey, if any,
    /// so the caller can delete its now-orphaned staging file.</summary>
    public async Task<string?> SetSectionImageKeyAsync(Guid sectionId, string key, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        await history.SnapshotAsync(section.PostId, "Set image", ct);
        var previous = section.ImageKey;
        section.ImageKey = key;
        section.ImageUrl = null;                 // uploaded/imported image replaces any hotlink
        await db.SaveChangesAsync(ct);
        return previous;
    }

    /// <summary>Removes a section's image entirely (staged key and any hotlink). Returns the
    /// previous ImageKey, if any, for the caller to delete its staging file.</summary>
    public async Task<string?> ClearSectionImageAsync(Guid sectionId, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        await history.SnapshotAsync(section.PostId, "Remove image", ct);
        var previous = section.ImageKey;
        section.ImageKey = null;
        section.ImageUrl = null;
        await db.SaveChangesAsync(ct);
        return previous;
    }

    public async Task RemoveSectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        if (section.Type is SectionTypes.Header or SectionTypes.Footer)
            throw new InvalidOperationException("Header and footer cannot be removed — edit them instead.");
        await history.SnapshotAsync(section.PostId, "Delete section", ct);
        var sections = await GetSectionsAsync(section.PostId, ct);
        sections.RemoveAll(s => s.Id == sectionId);
        db.IssueSections.Remove(section);
        // The proposal has no FK to the section — only to the post — so nothing removes it for us.
        // Orphaned it is invisible, since no card exists to render it, and unreachable once the
        // undo entry that could restore the section is trimmed off the stack.
        db.IssueSectionProposals.RemoveRange(
            await db.IssueSectionProposals.Where(p => p.SectionId == sectionId).ToListAsync(ct));
        Renumber(sections);
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveSectionAsync(Guid sectionId, int direction, CancellationToken ct = default)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        if (section.Type is SectionTypes.Header or SectionTypes.Footer) return;
        var sections = await GetSectionsAsync(section.PostId, ct);
        var index = sections.FindIndex(s => s.Id == sectionId);
        var target = index + direction;
        if (target <= 0 || target >= sections.Count - 1) return; // stay between header and footer
        await history.SnapshotAsync(section.PostId, "Move section", ct);
        (sections[index], sections[target]) = (sections[target], sections[index]);
        Renumber(sections);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> ExportMarkdownAsync(Guid postId, CancellationToken ct = default)
    {
        var sections = await GetSectionsAsync(postId, ct);
        if (sections.Count == 0) throw new InvalidOperationException("Nothing to export yet.");
        return SectionHtmlRenderer.ToMarkdown(sections);
    }

    public async Task<string> RenderPreviewAsync(Guid postId, string title, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        var sections = await GetSectionsAsync(postId, ct);
        var template = await templates.ResolveForPostAsync(postId, ct);
        // Preview resolves a staged upload to its local endpoint; the send path (PostService) keeps
        // the default resolver, which omits staged images until PR 2 hosts them on R2.
        var html = template is null
            ? SectionHtmlRenderer.Render(sections, tenant, title, NewsletterImageStaging.PreviewSrc)
            : TemplateHtmlRenderer.Render(sections, tenant, title, template.Html, post.CreatedAt, NewsletterImageStaging.PreviewSrc);
        return html.Replace(SectionHtmlRenderer.UnsubscribeToken, "#");
    }

    private static void Renumber(List<IssueSection> ordered)
    {
        for (var n = 0; n < ordered.Count; n++) ordered[n].Position = n;
    }

    private static string? MetadataImage(ContentItem item)
    {
        try
        {
            using var doc = JsonDocument.Parse(item.MetadataJson);
            return doc.RootElement.TryGetProperty("image", out var v) ? v.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task<int> GenerateTopicsAsync(Guid postId, string? extraInstructions, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        var recipe = post.RecipeId is Guid recipeId
            ? await db.Recipes.SingleAsync(r => r.Id == recipeId, ct) : null;
        var skeletons = (await GetSectionsAsync(postId, ct))
            .Where(s => s.Type == SectionTypes.Topic && string.IsNullOrWhiteSpace(s.BodyMd) && s.SourceItemId is not null)
            .ToList();
        if (skeletons.Count == 0) return 0;

        var itemIds = skeletons.Select(s => s.SourceItemId!.Value).ToList();
        var items = await db.ContentItems.Where(i => itemIds.Contains(i.Id)).ToListAsync(ct);
        var prompt = BuildTopicsPrompt(tenant, recipe, items, extraInstructions);
        var settings = await llmSettings.GetAsync(tenant.Id, LlmJobs.TopicBlurbs, ct);

        List<TopicBlurb>? topics = null;
        for (var attempt = 1; attempt <= 2 && topics is null; attempt++)
        {
            var reply = await llm.GenerateAsync(attempt == 1 ? prompt
                : prompt + "\nYour previous reply was not valid JSON. Respond with ONLY the JSON array.",
                settings, ct);
            TryParseTopics(reply.Text, out topics);
        }
        if (topics is null)
            throw new InvalidOperationException("The model did not return topic blurbs as JSON — try again.");

        // Snapshot only once the model has actually produced usable topics. Taken before the call,
        // a failed generation would leave an undo entry that restores the state the issue is
        // already in — and would clear the redo stack on its way.
        await history.SnapshotAsync(postId, "Generate topics", ct);
        var byItem = topics.ToDictionary(t => t.ItemId);
        var filled = 0;
        var filledItemIds = new HashSet<Guid>();
        foreach (var section in skeletons)
        {
            if (!byItem.TryGetValue(section.SourceItemId!.Value, out var topic)) continue;
            section.BodyMd = topic.Blurb;
            if (!string.IsNullOrWhiteSpace(topic.Title)) section.Title = topic.Title;
            if (!string.IsNullOrWhiteSpace(topic.Category)) section.Category = topic.Category;
            filled++;
            filledItemIds.Add(section.SourceItemId!.Value);
        }
        foreach (var item in items.Where(i => filledItemIds.Contains(i.Id))) item.Status = ContentItemStatus.Used;
        await db.SaveChangesAsync(ct);
        return filled;
    }

    public async Task RegenerateSectionAsync(Guid sectionId, string? instruction, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        var post = await db.Posts.SingleAsync(p => p.Id == section.PostId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        var settings = await llmSettings.GetAsync(tenant.Id, LlmJobs.RegenerateSection, ct);
        var voice = string.IsNullOrWhiteSpace(tenant.VoiceProfile) ? "" : $"Voice: {tenant.VoiceProfile}\n";
        var extra = string.IsNullOrWhiteSpace(instruction) ? "" : $"Extra instructions: {instruction}\n";
        string prompt;
        if (section.Type == SectionTypes.Header)
        {
            var topicTitles = (await GetSectionsAsync(section.PostId, ct))
                .Where(s => s.Type == SectionTypes.Topic && !string.IsNullOrWhiteSpace(s.Title))
                .Select(s => $"- {s.Title}");
            prompt = $"""
                Write a 2-3 sentence newsletter intro greeting the readers and teasing these topics.
                {voice}{extra}Topics:
                {string.Join("\n", topicTitles)}
                Respond with ONLY the intro markdown, no heading, no fences.
                """;
        }
        else if (section.Type == SectionTypes.Topic)
        {
            var item = section.SourceItemId is Guid itemId
                ? await db.ContentItems.FirstOrDefaultAsync(i => i.Id == itemId, ct) : null;
            var material = item is null ? section.BodyMd ?? ""
                : item.Body.Length > 2000 ? item.Body[..2000] : item.Body;
            prompt = $"""
                Rewrite this newsletter topic blurb (2-4 sentences, markdown, no heading).
                {voice}{extra}Topic: {section.Title}
                Material:
                {material}
                Respond with ONLY the blurb markdown, no fences.
                """;
        }
        else
        {
            throw new InvalidOperationException("Only topics and the header can be regenerated.");
        }
        var reply = await llm.GenerateAsync(prompt, settings, ct);
        // After the call, not before: a failed rewrite must not leave an undo entry that restores
        // what is already on screen.
        await history.SnapshotAsync(section.PostId, "Rewrite section", ct);
        section.BodyMd = reply.Text.Trim();
        await db.SaveChangesAsync(ct);
    }

    private static string BuildTopicsPrompt(Tenant tenant, Recipe? recipe,
        IReadOnlyList<ContentItem> items, string? extraInstructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You write newsletter topic blurbs.");
        if (!string.IsNullOrWhiteSpace(tenant.VoiceProfile)) sb.AppendLine($"Voice: {tenant.VoiceProfile}");
        if (!string.IsNullOrWhiteSpace(recipe?.ToneModifiers)) sb.AppendLine($"Tone: {recipe.ToneModifiers}");
        if (!string.IsNullOrWhiteSpace(recipe?.Language)) sb.AppendLine($"Write in: {recipe.Language}");
        if (!string.IsNullOrWhiteSpace(extraInstructions)) sb.AppendLine($"Extra instructions: {extraInstructions}");
        sb.AppendLine();
        sb.AppendLine("Write one short markdown blurb (2-4 sentences) per item below. Improve the title when it helps.");
        sb.AppendLine("""Respond with ONLY a JSON array, no prose, no markdown fences: [{"itemId":"<id>","title":"...","blurb":"...","category":"..."}]""");
        sb.AppendLine("category is a one- or two-word label for the piece, such as Tutorial, News or Release.");
        foreach (var item in items)
        {
            sb.AppendLine($"--- itemId: {item.Id} ---");
            sb.AppendLine($"Title: {item.Title}");
            if (item.Url is not null) sb.AppendLine($"URL: {item.Url}");
            var body = item.Body.Length > 2000 ? item.Body[..2000] + " [truncated]" : item.Body;
            if (body.Length > 0) sb.AppendLine(body);
        }
        return sb.ToString();
    }

    // Public because the unit-test project asserts the contract directly (no InternalsVisibleTo in this repo).
    public static bool TryParseTopics(string text, out List<TopicBlurb>? topics)
    {
        topics = null;
        var trimmed = MarkdownFence.Strip(text);
        try
        {
            var parsed = JsonSerializer.Deserialize<List<TopicBlurb>>(trimmed, JsonOpts);
            if (parsed is { Count: > 0 } &&
                parsed.All(t => t.ItemId != Guid.Empty && !string.IsNullOrWhiteSpace(t.Blurb)))
            {
                topics = parsed;
                return true;
            }
            return false;
        }
        catch (JsonException) { return false; }
    }
}
