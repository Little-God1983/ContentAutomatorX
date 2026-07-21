using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class NewsletterTemplateService(IAppDbContext db)
{
    public async Task<List<NewsletterTemplate>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.NewsletterTemplates.Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.Name).ToListAsync(ct);

    public Task<NewsletterTemplate?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.NewsletterTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <summary>Creates or updates. Validation runs here rather than in the UI so a template can
    /// never reach the database in a state that would send an email with no unsubscribe link.</summary>
    public async Task SaveAsync(NewsletterTemplate template, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
            throw new InvalidOperationException("Give the template a name.");

        var issues = TemplateValidator.Validate(template.Html);
        if (TemplateValidator.HasErrors(issues))
            throw new InvalidOperationException("Fix the template errors first: "
                + string.Join(" ", issues.Where(i => i.Level == TemplateIssueLevel.Error).Select(i => i.Message)));

        var existing = await db.NewsletterTemplates.FirstOrDefaultAsync(t => t.Id == template.Id, ct);
        if (existing is null)
        {
            template.UpdatedAt = DateTimeOffset.UtcNow;
            db.NewsletterTemplates.Add(template);
        }
        else
        {
            // template.TenantId is never trusted for an update — existing.TenantId (the row's real
            // owner) is what drives the sibling-clearing query below, so a caller-supplied TenantId
            // that is missing or wrong cannot misdirect it.
            existing.Name = template.Name;
            existing.Html = template.Html;
            existing.IsDefault = template.IsDefault;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // At most one default per tenant, enforced here because the EF SQLite provider has no
        // filtered unique index and this service is the only writer. Filter by the authoritative
        // tenant — existing.TenantId on update, template.TenantId only on insert — never by the
        // caller-supplied template.TenantId on an update, which the UI's form state could omit or
        // mangle.
        var ownerTenantId = existing?.TenantId ?? template.TenantId;
        if (template.IsDefault)
            foreach (var other in await db.NewsletterTemplates
                         .Where(t => t.TenantId == ownerTenantId && t.Id != template.Id).ToListAsync(ct))
                other.IsDefault = false;

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.NewsletterTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return;

        // No FK from Recipe, so nothing clears these for us. Left dangling they would fall through
        // to the tenant default anyway, but an explicit null is honest about what happened.
        foreach (var recipe in await db.Recipes.Where(r => r.NewsletterTemplateId == id).ToListAsync(ct))
            recipe.NewsletterTemplateId = null;

        db.NewsletterTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Which template an issue renders with: the recipe's, else the tenant's default,
    /// else none — in which case the caller uses the built-in renderer. A dangling or cross-tenant
    /// id falls through rather than failing.</summary>
    public async Task<NewsletterTemplate?> ResolveForPostAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null) return null;

        if (post.RecipeId is Guid recipeId)
        {
            var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, ct);
            if (recipe?.NewsletterTemplateId is Guid templateId)
            {
                var chosen = await db.NewsletterTemplates
                    .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == post.TenantId, ct);
                if (chosen is not null) return chosen;
            }
        }

        return await db.NewsletterTemplates
            .FirstOrDefaultAsync(t => t.TenantId == post.TenantId && t.IsDefault, ct);
    }
}
