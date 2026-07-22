using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class NewsletterTemplateServiceTests
{
    [Fact]
    public async Task Migration_creates_the_table_and_columns()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();

        t.Db.NewsletterTemplates.Add(new NewsletterTemplate
        {
            TenantId = tenantId, Name = "Into the Latent", Html = "<!-- BLOCK: shell -->{{sections}}<!-- /BLOCK -->",
            IsDefault = true
        });
        await t.Db.SaveChangesAsync();

        var stored = await t.Db.NewsletterTemplates.SingleAsync(x => x.TenantId == tenantId);
        Assert.Equal("Into the Latent", stored.Name);
        Assert.True(stored.IsDefault);
        Assert.NotEqual(default, stored.UpdatedAt);
    }

    [Fact]
    public async Task New_columns_round_trip_on_existing_entities()
    {
        using var t = TestDb.Create();
        var templateId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        // IssueSection and IssueSectionProposal both FK to Post with cascade delete; SQLite does
        // enforce that here, so a real Post row is required (see brief note).
        t.Db.Posts.Add(new Post
        {
            Id = postId, TenantId = Guid.NewGuid(), PlatformId = Guid.NewGuid(),
            Kind = "Newsletter", Title = "Issue"
        });

        t.Db.Recipes.Add(new Recipe
        {
            TenantId = Guid.NewGuid(), Name = "Monthly", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = templateId
        });
        t.Db.IssueSections.Add(new IssueSection
        {
            PostId = postId, Position = 0, Type = "Topic", Category = "Tutorial"
        });
        t.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = postId, SectionId = Guid.NewGuid(), BaselineBodyMd = "",
            ProposedCategory = "News", BaselineCategory = "Tutorial"
        });
        await t.Db.SaveChangesAsync();

        Assert.Equal(templateId, (await t.Db.Recipes.SingleAsync()).NewsletterTemplateId);
        Assert.Equal("Tutorial", (await t.Db.IssueSections.SingleAsync()).Category);
        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal("News", proposal.ProposedCategory);
        Assert.Equal("Tutorial", proposal.BaselineCategory);
    }

    private const string MinimalHtml =
        "<!-- BLOCK: shell -->{{sections}}<a href=\"{{unsubscribe_url}}\">u</a><!-- /BLOCK -->";

    private static NewsletterTemplateService Service(TestDb t) => new(t.Db);

    [Fact]
    public async Task Setting_default_clears_it_on_the_tenants_other_templates()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);

        var first = new NewsletterTemplate { TenantId = tenantId, Name = "A", Html = MinimalHtml, IsDefault = true };
        await service.SaveAsync(first, tenantId);
        var second = new NewsletterTemplate { TenantId = tenantId, Name = "B", Html = MinimalHtml, IsDefault = true };
        await service.SaveAsync(second, tenantId);

        var all = await service.ListAsync(tenantId);
        Assert.Single(all, x => x.IsDefault);
        Assert.Equal("B", all.Single(x => x.IsDefault).Name);
    }

    [Theory]
    [InlineData(true)]   // wrong tenant id — belongs to a different (real) tenant
    [InlineData(false)]  // empty tenant id — as if the UI form omitted the field
    public async Task Setting_default_uses_the_rows_real_tenant_even_if_the_caller_passes_a_bad_one(bool wrongNotEmpty)
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);

        var first = new NewsletterTemplate { TenantId = tenantId, Name = "A", Html = MinimalHtml, IsDefault = true };
        await service.SaveAsync(first, tenantId);
        var second = new NewsletterTemplate { TenantId = tenantId, Name = "B", Html = MinimalHtml };
        await service.SaveAsync(second, tenantId);

        // Simulate a caller (e.g. the template editor UI) that hands SaveAsync an entity whose own
        // TenantId field does not match the row's real owner — either mangled to another tenant, or
        // left at the default — while the explicit tenantId parameter (the authoritative source,
        // e.g. TenantContext.Active.Id) is correct. The entity's own field must still be ignored.
        var bogusTenantId = wrongNotEmpty ? Guid.NewGuid() : Guid.Empty;
        var updatePayload = new NewsletterTemplate
        {
            Id = second.Id, TenantId = bogusTenantId, Name = "B", Html = MinimalHtml, IsDefault = true
        };
        await service.SaveAsync(updatePayload, tenantId);

        var all = await service.ListAsync(tenantId);
        var defaults = all.Where(x => x.IsDefault).ToList();
        Assert.Single(defaults);
        Assert.Equal("B", defaults.Single().Name);
        Assert.Equal(tenantId, defaults.Single().TenantId);
    }

    [Fact]
    public async Task Default_is_scoped_to_one_tenant()
    {
        using var t = TestDb.Create();
        var service = Service(t);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await service.SaveAsync(new NewsletterTemplate { TenantId = mine, Name = "Mine", Html = MinimalHtml, IsDefault = true }, mine);
        await service.SaveAsync(new NewsletterTemplate { TenantId = theirs, Name = "Theirs", Html = MinimalHtml, IsDefault = true }, theirs);

        Assert.Single(await service.ListAsync(mine));
        Assert.True((await service.ListAsync(theirs)).Single().IsDefault);
    }

    [Fact]
    public async Task Save_rejects_a_template_with_validation_errors()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var template = new NewsletterTemplate
        {
            TenantId = tenantId, Name = "Broken",
            Html = "<!-- BLOCK: shell -->no slot, no unsubscribe<!-- /BLOCK -->"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t).SaveAsync(template, tenantId));
        Assert.Empty(await t.Db.NewsletterTemplates.ToListAsync());
    }

    [Fact]
    public async Task Delete_clears_the_reference_from_every_recipe_that_used_it()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);
        var template = new NewsletterTemplate { TenantId = tenantId, Name = "A", Html = MinimalHtml };
        await service.SaveAsync(template, tenantId);

        t.Db.Recipes.Add(new Recipe
        {
            TenantId = tenantId, Name = "Monthly", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = template.Id
        });
        await t.Db.SaveChangesAsync();

        await service.DeleteAsync(template.Id, tenantId);

        Assert.Empty(await t.Db.NewsletterTemplates.ToListAsync());
        Assert.Null((await t.Db.Recipes.SingleAsync()).NewsletterTemplateId);
    }

    [Fact]
    public async Task Resolution_prefers_the_recipes_template()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);
        var chosen = new NewsletterTemplate { TenantId = tenantId, Name = "Chosen", Html = MinimalHtml };
        var fallback = new NewsletterTemplate { TenantId = tenantId, Name = "Default", Html = MinimalHtml, IsDefault = true };
        await service.SaveAsync(fallback, tenantId);
        await service.SaveAsync(chosen, tenantId);

        var recipe = new Recipe
        {
            TenantId = tenantId, Name = "R", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = chosen.Id
        };
        t.Db.Recipes.Add(recipe);
        var post = new Post { TenantId = tenantId, PlatformId = Guid.NewGuid(), RecipeId = recipe.Id,
            Kind = "Newsletter", Title = "Issue" };
        t.Db.Posts.Add(post);
        await t.Db.SaveChangesAsync();

        Assert.Equal("Chosen", (await service.ResolveForPostAsync(post.Id))!.Name);
    }

    [Theory]
    [InlineData(true)]   // recipe exists but points at nothing
    [InlineData(false)]  // post has no recipe at all
    public async Task Resolution_falls_back_to_the_tenant_default(bool withRecipe)
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);
        await service.SaveAsync(new NewsletterTemplate
            { TenantId = tenantId, Name = "Default", Html = MinimalHtml, IsDefault = true }, tenantId);

        Guid? recipeId = null;
        if (withRecipe)
        {
            var recipe = new Recipe { TenantId = tenantId, Name = "R", Kind = "Newsletter",
                PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = null };
            t.Db.Recipes.Add(recipe);
            recipeId = recipe.Id;
        }
        var post = new Post { TenantId = tenantId, PlatformId = Guid.NewGuid(), RecipeId = recipeId,
            Kind = "Newsletter", Title = "Issue" };
        t.Db.Posts.Add(post);
        await t.Db.SaveChangesAsync();

        Assert.Equal("Default", (await service.ResolveForPostAsync(post.Id))!.Name);
    }

    [Fact]
    public async Task A_template_belonging_to_another_tenant_is_ignored()
    {
        using var t = TestDb.Create();
        var mine = Guid.NewGuid();
        var service = Service(t);
        var theirsTenantId = Guid.NewGuid();
        var theirs = new NewsletterTemplate { TenantId = theirsTenantId, Name = "Theirs", Html = MinimalHtml };
        await service.SaveAsync(theirs, theirsTenantId);

        var recipe = new Recipe { TenantId = mine, Name = "R", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = theirs.Id };
        t.Db.Recipes.Add(recipe);
        var post = new Post { TenantId = mine, PlatformId = Guid.NewGuid(), RecipeId = recipe.Id,
            Kind = "Newsletter", Title = "Issue" };
        t.Db.Posts.Add(post);
        await t.Db.SaveChangesAsync();

        Assert.Null(await service.ResolveForPostAsync(post.Id));
    }

    [Fact]
    public async Task With_no_template_anywhere_resolution_returns_null()
    {
        using var t = TestDb.Create();
        var post = new Post { TenantId = Guid.NewGuid(), PlatformId = Guid.NewGuid(),
            Kind = "Newsletter", Title = "Issue" };
        t.Db.Posts.Add(post);
        await t.Db.SaveChangesAsync();

        Assert.Null(await Service(t).ResolveForPostAsync(post.Id));
    }

    // Finding F3 — GetAsync, SaveAsync's existing-row lookup and DeleteAsync located a row by Id
    // alone with no tenant predicate. Only ListAsync was scoped. Not currently reachable through the
    // UI (every id it sees comes from a tenant-scoped ListAsync call first), but the spec asserts
    // tenant scoping is enforced by an explicit .Where in the service, and it was true of one method
    // in four. These two tests cover SaveAsync and DeleteAsync now that both take an explicit
    // tenantId. GetAsync had zero callers anywhere in the codebase and was deleted rather than fixed
    // — see the report for the reasoning.

    [Fact] // F3 — SaveAsync's existing-row lookup must be scoped by the explicit tenantId, not just
    // Id, so a caller that supplies another tenant's template id cannot overwrite that tenant's row
    // by passing its own tenantId alongside it.
    public async Task SaveAsync_does_not_let_a_caller_overwrite_another_tenants_template_by_id()
    {
        using var t = TestDb.Create();
        var ownerTenantId = Guid.NewGuid();
        var attackerTenantId = Guid.NewGuid();
        var service = Service(t);

        var original = new NewsletterTemplate { TenantId = ownerTenantId, Name = "Original", Html = MinimalHtml };
        await service.SaveAsync(original, ownerTenantId);

        var hijack = new NewsletterTemplate
            { Id = original.Id, TenantId = attackerTenantId, Name = "Hijacked", Html = MinimalHtml };
        // Rejecting the attempt outright (e.g. a duplicate-key exception, since the tenant-scoped
        // lookup finds nothing and falls through to insert a row whose id is already taken) is an
        // acceptable outcome here — the only thing that must never happen is a silent overwrite.
        try { await service.SaveAsync(hijack, attackerTenantId); } catch { }

        var stored = await t.Db.NewsletterTemplates.SingleAsync(x => x.Id == original.Id);
        Assert.Equal("Original", stored.Name);
        Assert.Equal(ownerTenantId, stored.TenantId);
    }

    [Fact] // F3 — DeleteAsync must be scoped by tenant too: a caller that knows another tenant's
    // template id must not be able to delete it by passing its own tenantId.
    public async Task DeleteAsync_does_not_delete_a_row_owned_by_another_tenant()
    {
        using var t = TestDb.Create();
        var ownerTenantId = Guid.NewGuid();
        var attackerTenantId = Guid.NewGuid();
        var service = Service(t);
        var template = new NewsletterTemplate { TenantId = ownerTenantId, Name = "Mine", Html = MinimalHtml };
        await service.SaveAsync(template, ownerTenantId);

        await service.DeleteAsync(template.Id, attackerTenantId);

        Assert.NotNull(await t.Db.NewsletterTemplates.SingleOrDefaultAsync(x => x.Id == template.Id));
    }
}
