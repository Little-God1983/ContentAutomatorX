using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class NormalizedUrlBackfillTests
{
    private static ContentItem Item(Guid tenantId, Guid sourceId, string externalId, string? url, DateTimeOffset fetchedAt) =>
        new()
        {
            TenantId = tenantId, SourceId = sourceId, ExternalId = externalId,
            Title = externalId, Url = url, FetchedAt = fetchedAt
        };

    [Fact]
    public async Task Backfills_normalized_urls_oldest_wins_on_collision()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = new Source { TenantId = tenant.Id, Type = "rss", DisplayName = "s" };
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        var t0 = DateTimeOffset.UtcNow.AddDays(-2);
        test.Db.ContentItems.AddRange(
            Item(tenant.Id, source.Id, "old", "https://ex.com/p?utm_source=a", t0),
            Item(tenant.Id, source.Id, "newer", "https://ex.com/p?utm_source=b", t0.AddDays(1)),
            Item(tenant.Id, source.Id, "other", "https://ex.com/q", t0),
            Item(tenant.Id, source.Id, "nourl", null, t0));
        await test.Db.SaveChangesAsync();

        await NormalizedUrlBackfill.RunAsync(test.Db);

        using var verify = test.NewContext();
        var byId = await verify.ContentItems.ToDictionaryAsync(i => i.ExternalId);
        Assert.Equal("https://ex.com/p", byId["old"].NormalizedUrl);
        Assert.Null(byId["newer"].NormalizedUrl);            // collision loser stays null, row kept
        Assert.Equal("https://ex.com/q", byId["other"].NormalizedUrl);
        Assert.Null(byId["nourl"].NormalizedUrl);
        Assert.Equal(4, await verify.ContentItems.CountAsync());
    }

    [Fact]
    public async Task Running_twice_is_idempotent()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = new Source { TenantId = tenant.Id, Type = "rss", DisplayName = "s" };
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        test.Db.ContentItems.Add(Item(tenant.Id, source.Id, "a", "https://ex.com/p", DateTimeOffset.UtcNow));
        await test.Db.SaveChangesAsync();

        await NormalizedUrlBackfill.RunAsync(test.Db);
        await NormalizedUrlBackfill.RunAsync(test.Db);

        using var verify = test.NewContext();
        Assert.Equal("https://ex.com/p", (await verify.ContentItems.SingleAsync()).NormalizedUrl);
    }

    [Fact]
    public async Task Same_url_in_different_tenants_both_get_normalized()
    {
        using var test = TestDb.Create();
        var t1 = new Tenant { Name = "A", Slug = "a" };
        var t2 = new Tenant { Name = "B", Slug = "b" };
        var s1 = new Source { TenantId = t1.Id, Type = "rss", DisplayName = "s1" };
        var s2 = new Source { TenantId = t2.Id, Type = "rss", DisplayName = "s2" };
        test.Db.Tenants.AddRange(t1, t2);
        test.Db.Sources.AddRange(s1, s2);
        test.Db.ContentItems.AddRange(
            Item(t1.Id, s1.Id, "x", "https://ex.com/p", DateTimeOffset.UtcNow),
            Item(t2.Id, s2.Id, "y", "https://ex.com/p", DateTimeOffset.UtcNow));
        await test.Db.SaveChangesAsync();

        await NormalizedUrlBackfill.RunAsync(test.Db);

        using var verify = test.NewContext();
        Assert.Equal(2, await verify.ContentItems.CountAsync(i => i.NormalizedUrl == "https://ex.com/p"));
    }
}
