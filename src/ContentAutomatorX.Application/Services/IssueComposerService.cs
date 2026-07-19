using System.Text;
using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

/// <summary>Owns the structured-issue composer: section lifecycle (Task 4) and AI topic
/// generation (Task 5). An issue always has exactly one Header (first) and one Footer (last);
/// positions stay 0-based and contiguous after every mutation.</summary>
public class IssueComposerService(IAppDbContext db, ILlmBackend llm, PostService posts)
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
        string? imageUrl, string? linkUrl, string? linkText, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        section.Title = title;
        section.BodyMd = bodyMd;
        section.ImageUrl = imageUrl;
        section.LinkUrl = linkUrl;
        section.LinkText = linkText;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveSectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        if (section.Type is SectionTypes.Header or SectionTypes.Footer)
            throw new InvalidOperationException("Header and footer cannot be removed — edit them instead.");
        var sections = await GetSectionsAsync(section.PostId, ct);
        sections.RemoveAll(s => s.Id == sectionId);
        db.IssueSections.Remove(section);
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
        return SectionHtmlRenderer.Render(sections, tenant, title)
            .Replace(SectionHtmlRenderer.UnsubscribeToken, "#");
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
}
