using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class SourceServiceTests
{
    private static Source NewSource(Guid tenantId, string name = "sd") =>
        new() { TenantId = tenantId, Type = SourceTypes.Reddit, DisplayName = name };

    private static ContentItem NewItem(Source source, string externalId) =>
        new() { TenantId = source.TenantId, SourceId = source.Id, ExternalId = externalId, Title = externalId };

    [Fact]
    public async Task Delete_removes_source_and_its_inbox_items()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = NewSource(tenant.Id);
        var other = NewSource(tenant.Id, "other");
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.AddRange(source, other);
        test.Db.ContentItems.AddRange(NewItem(source, "a"), NewItem(source, "b"), NewItem(other, "c"));
        await test.Db.SaveChangesAsync();

        var removed = await new SourceService(test.Db).DeleteAsync(source.Id);

        Assert.Equal(2, removed);
        Assert.Null(await test.Db.Sources.FirstOrDefaultAsync(s => s.Id == source.Id));
        Assert.Equal("c", (await test.Db.ContentItems.SingleAsync()).ExternalId);
    }

    [Fact]
    public async Task Delete_blocked_when_automation_references_source()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = NewSource(tenant.Id);
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        test.Db.Recipes.Add(new Recipe
        {
            TenantId = tenant.Id, Name = "Weekly", Kind = DraftKinds.Newsletter,
            SourceIdsJson = $"[\"{source.Id}\"]"
        });
        await test.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SourceService(test.Db).DeleteAsync(source.Id));

        Assert.Contains("automation 'Weekly'", ex.Message);
        Assert.NotNull(await test.Db.Sources.FirstOrDefaultAsync(s => s.Id == source.Id));
    }

    [Fact]
    public async Task Delete_blocked_by_open_issue_but_not_published_one()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = NewSource(tenant.Id);
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        test.Db.Platforms.Add(platform);
        test.Db.Posts.Add(new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Issue #1", Status = PostStatus.Draft, SourceIdsJson = $"[\"{source.Id}\"]"
        });
        await test.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SourceService(test.Db).DeleteAsync(source.Id));
        Assert.Contains("issue 'Issue #1'", ex.Message);

        var post = await test.Db.Posts.SingleAsync();
        post.Status = PostStatus.Published;
        await test.Db.SaveChangesAsync();

        await new SourceService(test.Db).DeleteAsync(source.Id);
        Assert.Null(await test.Db.Sources.FirstOrDefaultAsync(s => s.Id == source.Id));
    }
}
