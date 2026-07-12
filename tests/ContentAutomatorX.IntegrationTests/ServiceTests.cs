using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Delivery;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class ServiceTests
{
    [Fact]
    public async Task RecipeService_create_clones_system_default_template()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        test.Db.Tenants.Add(tenant);
        test.Db.PromptTemplates.Add(new PromptTemplate { TenantId = null, Kind = DraftKinds.Newsletter, Template = "SYS {items} {voice_profile} {extra_instructions}" });
        await test.Db.SaveChangesAsync();

        var service = new RecipeService(test.Db);
        var recipe = await service.CreateAsync(new Recipe { TenantId = tenant.Id, Name = "R", Kind = DraftKinds.Newsletter });

        Assert.NotEqual(Guid.Empty, recipe.PromptTemplateId);
        var clone = await test.Db.PromptTemplates.SingleAsync(p => p.Id == recipe.PromptTemplateId);
        Assert.Equal(tenant.Id, clone.TenantId);
        Assert.StartsWith("SYS", clone.Template);
    }

    [Fact]
    public async Task ContentService_marks_items()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "f" };
        var item = new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "e", Title = "i" };
        test.Db.Tenants.Add(tenant); test.Db.Sources.Add(source); test.Db.ContentItems.Add(item);
        await test.Db.SaveChangesAsync();

        var service = new ContentService(test.Db);
        await service.MarkAsync(item.Id, ContentItemStatus.Ignored);

        Assert.Equal(ContentItemStatus.Ignored, (await test.Db.ContentItems.SingleAsync()).Status);
    }

    [Fact]
    public async Task DraftService_retry_delivery_delivers_generated_draft()
    {
        using var test = TestDb.Create();
        var dir = Path.Combine(Path.GetTempPath(), $"contentx-svc-{Guid.NewGuid():N}");
        var tenant = new Tenant { Name = "T", Slug = "t", OutputFolderPath = dir };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "R", Kind = DraftKinds.SocialPost };
        var draft = new Draft { TenantId = tenant.Id, RecipeId = recipe.Id, Kind = recipe.Kind, Title = "Post", Body = "# Post" };
        test.Db.Tenants.Add(tenant); test.Db.Recipes.Add(recipe); test.Db.Drafts.Add(draft);
        await test.Db.SaveChangesAsync();

        var service = new DraftService(test.Db, new FileShareDraftDelivery());
        var delivered = await service.RetryDeliveryAsync(draft.Id);

        Assert.Equal(DraftStatus.Delivered, delivered.Status);
        Assert.True(File.Exists(delivered.FilePath));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task RecipeService_create_falls_back_to_DefaultTemplates_when_no_system_default_template_exists()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-no-default" };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();

        var service = new RecipeService(test.Db);
        var recipe = await service.CreateAsync(new Recipe { TenantId = tenant.Id, Name = "R", Kind = DraftKinds.SocialPost });

        Assert.NotEqual(Guid.Empty, recipe.PromptTemplateId);
        var clone = await test.Db.PromptTemplates.SingleAsync(p => p.Id == recipe.PromptTemplateId);
        Assert.Equal(tenant.Id, clone.TenantId);
        Assert.Equal(DefaultTemplates.GetFor(DraftKinds.SocialPost), clone.Template);
    }

    [Fact]
    public async Task DraftService_retry_delivery_uses_default_output_when_recipe_row_is_missing()
    {
        using var test = TestDb.Create();
        var dir = Path.Combine(Path.GetTempPath(), $"contentx-svc-{Guid.NewGuid():N}");
        var tenant = new Tenant { Name = "T", Slug = "t-deleted-recipe", OutputFolderPath = dir };
        // RecipeId points at a recipe that was never created (equivalent to one that was since deleted).
        var draft = new Draft { TenantId = tenant.Id, RecipeId = Guid.NewGuid(), Kind = DraftKinds.SocialPost, Title = "Post", Body = "# Post" };
        test.Db.Tenants.Add(tenant); test.Db.Drafts.Add(draft);
        await test.Db.SaveChangesAsync();

        var service = new DraftService(test.Db, new FileShareDraftDelivery());
        var delivered = await service.RetryDeliveryAsync(draft.Id);

        Assert.Equal(DraftStatus.Delivered, delivered.Status);
        Assert.True(File.Exists(delivered.FilePath));
        Directory.Delete(dir, true);
    }
}
