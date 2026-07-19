using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

// Note: FakeMailerLite and InMemoryCredentials already exist in PostServiceTests.cs (same
// namespace) — not redeclared here to avoid a duplicate-type compile error.

/// <summary>Wraps a real context and, on the first SaveChangesAsync, plants a competing
/// (TenantId, Type) row via a second context and commits it BEFORE letting the wrapped save
/// proceed — reproducing the two-circuits-race the unique index guards against, deterministically,
/// in a single thread. Mirrors the SaveHookDbContext pattern used in IngestionPipelineTests.</summary>
public sealed class RacingPlatformDbContext(AppDbContext inner, TestDb test, Guid tenantId) : IAppDbContext
{
    private bool _armed = true;

    public DbSet<Tenant> Tenants => inner.Tenants;
    public DbSet<Source> Sources => inner.Sources;
    public DbSet<ContentItem> ContentItems => inner.ContentItems;
    public DbSet<Recipe> Recipes => inner.Recipes;
    public DbSet<Draft> Drafts => inner.Drafts;
    public DbSet<PipelineRun> PipelineRuns => inner.PipelineRuns;
    public DbSet<PromptTemplate> PromptTemplates => inner.PromptTemplates;
    public DbSet<Platform> Platforms => inner.Platforms;
    public DbSet<Post> Posts => inner.Posts;
    public DbSet<IssueSection> IssueSections => inner.IssueSections;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (_armed)
        {
            _armed = false;
            using var competitor = test.NewContext();
            competitor.Platforms.Add(new Platform { TenantId = tenantId, Type = PlatformTypes.MailerLite, DisplayName = "Winner" });
            competitor.SaveChanges();
        }
        return inner.SaveChangesAsync(ct);
    }
}

/// <summary>Wraps a real context and, on the first SaveChangesAsync, throws a DbUpdateException
/// that is NOT caused by a lost unique-index race — no competing row is ever inserted. Used to
/// prove GetOrCreateMailerLiteAsync doesn't blindly assume every DbUpdateException is a race.</summary>
public sealed class FailingSaveDbContext(AppDbContext inner) : IAppDbContext
{
    private bool _armed = true;

    public DbSet<Tenant> Tenants => inner.Tenants;
    public DbSet<Source> Sources => inner.Sources;
    public DbSet<ContentItem> ContentItems => inner.ContentItems;
    public DbSet<Recipe> Recipes => inner.Recipes;
    public DbSet<Draft> Drafts => inner.Drafts;
    public DbSet<PipelineRun> PipelineRuns => inner.PipelineRuns;
    public DbSet<PromptTemplate> PromptTemplates => inner.PromptTemplates;
    public DbSet<Platform> Platforms => inner.Platforms;
    public DbSet<Post> Posts => inner.Posts;
    public DbSet<IssueSection> IssueSections => inner.IssueSections;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (_armed)
        {
            _armed = false;
            throw new DbUpdateException("disk I/O error");
        }
        return inner.SaveChangesAsync(ct);
    }
}

public class PlatformServiceTests
{
    [Fact]
    public async Task GetOrCreate_is_idempotent_for_the_same_tenant()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-platform-idempotent" };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();
        var service = new PlatformService(test.Db, new InMemoryCredentials(), new FakeMailerLite());

        var first = await service.GetOrCreateMailerLiteAsync(tenant.Id);
        var second = await service.GetOrCreateMailerLiteAsync(tenant.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await test.Db.Platforms.CountAsync());
    }

    [Fact]
    public async Task GetOrCreate_recovers_the_winning_row_when_a_concurrent_insert_wins_the_unique_index_race()
    {
        // Two circuits both find no existing platform and both try to insert. With the unique
        // index on (TenantId, Type), the loser's Add+SaveChangesAsync throws DbUpdateException;
        // GetOrCreateMailerLiteAsync must catch that, drop its own local insert, and return the
        // row that actually won — not bubble the exception up to the caller.
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-platform-race" };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();

        var racingDb = new RacingPlatformDbContext(test.Db, test, tenant.Id);
        var service = new PlatformService(racingDb, new InMemoryCredentials(), new FakeMailerLite());

        var resolved = await service.GetOrCreateMailerLiteAsync(tenant.Id);

        Assert.Equal("Winner", resolved.DisplayName);
        using var verify = test.NewContext();
        Assert.Equal(1, await verify.Platforms.CountAsync(p => p.TenantId == tenant.Id));
    }

    [Fact]
    public async Task Non_uniqueness_save_failure_is_rethrown_not_masked_as_a_race()
    {
        // The DbUpdateException here isn't a lost unique-index race — no competing row exists.
        // GetOrCreateMailerLiteAsync must verify a winner actually exists before treating this as
        // recoverable; otherwise it should surface the real DbUpdateException, not a confusing
        // InvalidOperationException from SingleAsync finding no rows.
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-platform-save-failure" };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();

        var failingDb = new FailingSaveDbContext(test.Db);
        var service = new PlatformService(failingDb, new InMemoryCredentials(), new FakeMailerLite());

        await Assert.ThrowsAsync<DbUpdateException>(() => service.GetOrCreateMailerLiteAsync(tenant.Id));
    }

    [Fact]
    public async Task Test_returns_false_when_no_api_key_is_stored()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-platform-test-no-key" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "MailerLite" };
        test.Db.Tenants.Add(tenant);
        test.Db.Platforms.Add(platform);
        await test.Db.SaveChangesAsync();
        var mailerLite = new FakeMailerLite();
        var service = new PlatformService(test.Db, new InMemoryCredentials(), mailerLite);

        var result = await service.TestAsync(platform);

        Assert.False(result);
        Assert.Equal(0, mailerLite.TestCalls);
    }

    [Fact]
    public async Task List_groups_without_api_key_throws_actionable_message()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-platform-list-groups-no-key" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "MailerLite" };
        test.Db.Tenants.Add(tenant);
        test.Db.Platforms.Add(platform);
        await test.Db.SaveChangesAsync();
        var service = new PlatformService(test.Db, new InMemoryCredentials(), new FakeMailerLite());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListGroupsAsync(platform));

        Assert.Contains("No API key stored", ex.Message);
    }
}
