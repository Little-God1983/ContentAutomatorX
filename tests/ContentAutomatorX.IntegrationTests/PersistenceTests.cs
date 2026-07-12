using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class PersistenceTests
{
    [Fact]
    public async Task Duplicate_ExternalId_per_source_is_rejected()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        test.Db.ContentItems.Add(new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "x1", Title = "a" });
        await test.Db.SaveChangesAsync();

        test.Db.ContentItems.Add(new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "x1", Title = "b" });
        await Assert.ThrowsAsync<DbUpdateException>(() => test.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Unique_index_rejects_duplicate_ExternalId_even_across_separate_DbContexts()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t3" };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        await test.Db.SaveChangesAsync();

        // Simulate two independent processes/threads racing to insert the same item,
        // each with its own DbContext and no shared change tracker.
        using var ctx1 = test.NewContext();
        ctx1.ContentItems.Add(new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "dup-x", Title = "a" });
        await ctx1.SaveChangesAsync();

        using var ctx2 = test.NewContext();
        ctx2.ContentItems.Add(new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "dup-x", Title = "b" });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());

        Assert.Equal(1, await test.Db.ContentItems.CountAsync());
    }

    [Fact]
    public async Task Enums_round_trip_as_strings()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t2" };
        test.Db.Tenants.Add(tenant);
        test.Db.Drafts.Add(new Draft { TenantId = tenant.Id, RecipeId = Guid.NewGuid(), Kind = DraftKinds.Newsletter, Title = "d", Status = DraftStatus.Delivered });
        await test.Db.SaveChangesAsync();

        using var fresh = test.NewContext();
        var draft = await fresh.Drafts.SingleAsync();
        Assert.Equal(DraftStatus.Delivered, draft.Status);
    }
}
