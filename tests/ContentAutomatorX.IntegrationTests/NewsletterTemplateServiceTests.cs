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
        await service.SaveAsync(first);
        var second = new NewsletterTemplate { TenantId = tenantId, Name = "B", Html = MinimalHtml, IsDefault = true };
        await service.SaveAsync(second);

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
        await service.SaveAsync(first);
        var second = new NewsletterTemplate { TenantId = tenantId, Name = "B", Html = MinimalHtml };
        await service.SaveAsync(second);

        // Simulate a caller (e.g. the template editor UI) that hands SaveAsync a TenantId that does
        // not match the row's real owner — either mangled to another tenant, or left at the default.
        var bogusTenantId = wrongNotEmpty ? Guid.NewGuid() : Guid.Empty;
        var updatePayload = new NewsletterTemplate
        {
            Id = second.Id, TenantId = bogusTenantId, Name = "B", Html = MinimalHtml, IsDefault = true
        };
        await service.SaveAsync(updatePayload);

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

        await service.SaveAsync(new NewsletterTemplate { TenantId = mine, Name = "Mine", Html = MinimalHtml, IsDefault = true });
        await service.SaveAsync(new NewsletterTemplate { TenantId = theirs, Name = "Theirs", Html = MinimalHtml, IsDefault = true });

        Assert.Single(await service.ListAsync(mine));
        Assert.True((await service.ListAsync(theirs)).Single().IsDefault);
    }

    [Fact]
    public async Task Save_rejects_a_template_with_validation_errors()
    {
        using var t = TestDb.Create();
        var template = new NewsletterTemplate
        {
            TenantId = Guid.NewGuid(), Name = "Broken",
            Html = "<!-- BLOCK: shell -->no slot, no unsubscribe<!-- /BLOCK -->"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t).SaveAsync(template));
        Assert.Empty(await t.Db.NewsletterTemplates.ToListAsync());
    }

    [Fact]
    public async Task Delete_clears_the_reference_from_every_recipe_that_used_it()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);
        var template = new NewsletterTemplate { TenantId = tenantId, Name = "A", Html = MinimalHtml };
        await service.SaveAsync(template);

        t.Db.Recipes.Add(new Recipe
        {
            TenantId = tenantId, Name = "Monthly", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = template.Id
        });
        await t.Db.SaveChangesAsync();

        await service.DeleteAsync(template.Id);

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
        await service.SaveAsync(fallback);
        await service.SaveAsync(chosen);

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
            { TenantId = tenantId, Name = "Default", Html = MinimalHtml, IsDefault = true });

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
        var theirs = new NewsletterTemplate { TenantId = Guid.NewGuid(), Name = "Theirs", Html = MinimalHtml };
        await service.SaveAsync(theirs);

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
}
