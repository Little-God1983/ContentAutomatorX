using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class FakeConnector(string type, Func<Source, IReadOnlyList<FetchedItem>> fetch) : ISourceConnector
{
    public string Type => type;
    public Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default) =>
        Task.FromResult(fetch(source));
}

/// <summary>
/// Wraps a real <see cref="AppDbContext"/> and forwards every <see cref="IAppDbContext"/> member to it,
/// except that <see cref="SaveChangesAsync"/> runs <paramref name="onFirstMatchingSave"/> immediately
/// before delegating, the one time it observes pending Added <see cref="ContentItem"/> entries for
/// <paramref name="targetSourceId"/>. Used to inject a conflicting write from a separate DbContext at the
/// exact moment the pipeline is about to save newly-fetched items, so the pipeline's own SaveChangesAsync
/// fails with a unique-constraint DbUpdateException. Fires at most once.
/// </summary>
public sealed class SaveHookDbContext(AppDbContext inner, Guid targetSourceId, Action onFirstMatchingSave) : IAppDbContext
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
    public DbSet<TenantLlmSetting> TenantLlmSettings => inner.TenantLlmSettings;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (_armed && inner.ChangeTracker.Entries<ContentItem>().Any(e =>
                e.State == EntityState.Added && e.Entity.SourceId == targetSourceId))
        {
            _armed = false;
            onFirstMatchingSave();
        }
        return inner.SaveChangesAsync(ct);
    }
}

public class IngestionPipelineTests
{
    private static (Tenant, Source) Seed(TestDb test)
    {
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        test.Db.SaveChanges();
        return (tenant, source);
    }

    [Fact]
    public async Task Stores_new_items_and_dedups_on_refetch()
    {
        using var test = TestDb.Create();
        var (tenant, source) = Seed(test);
        var connector = new FakeConnector(SourceTypes.Rss, _ =>
        [
            new FetchedItem("e1", "One", null, null, "b1", "{}", null),
            new FetchedItem("e2", "Two", null, null, "b2", "{}", null)
        ]);
        var pipeline = new IngestionPipeline(test.Db, [connector]);

        var run1 = await pipeline.RunAsync(tenant.Id);
        var run2 = await pipeline.RunAsync(tenant.Id);

        Assert.Equal(RunStatus.Succeeded, run1.Status);
        Assert.Equal(RunStatus.Succeeded, run2.Status);
        Assert.Equal(2, await test.Db.ContentItems.CountAsync());
        Assert.NotNull((await test.Db.Sources.SingleAsync()).LastFetchedAt);
    }

    [Fact]
    public async Task Failing_source_yields_partial_and_does_not_block_others()
    {
        using var test = TestDb.Create();
        var (tenant, source) = Seed(test);
        var badSource = new Source { TenantId = tenant.Id, Type = SourceTypes.Reddit, DisplayName = "bad" };
        test.Db.Sources.Add(badSource);
        test.Db.SaveChanges();

        var good = new FakeConnector(SourceTypes.Rss, _ => [new FetchedItem("e1", "One", null, null, "b", "{}", null)]);
        var bad = new FakeConnector(SourceTypes.Reddit, _ => throw new HttpRequestException("boom"));
        var pipeline = new IngestionPipeline(test.Db, [good, bad]);

        var run = await pipeline.RunAsync(tenant.Id);

        Assert.Equal(RunStatus.Partial, run.Status);
        Assert.Equal(1, await test.Db.ContentItems.CountAsync());
        Assert.Contains("boom", run.LogJson);
    }

    [Fact]
    public async Task Duplicate_external_ids_within_one_batch_are_deduped()
    {
        using var test = TestDb.Create();
        var (tenant, source) = Seed(test);
        var connector = new FakeConnector(SourceTypes.Rss, _ =>
        [
            new FetchedItem("dup", "First", null, null, "a", "{}", null),
            new FetchedItem("dup", "Second", null, null, "b", "{}", null)
        ]);
        var pipeline = new IngestionPipeline(test.Db, [connector]);

        var run = await pipeline.RunAsync(tenant.Id);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(1, await test.Db.ContentItems.CountAsync());
    }

    [Fact]
    public async Task Mid_save_conflict_recovers_tracker_and_pipeline_continues()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();

        // Seed "bad" then "good" in separate SaveChanges calls so "bad" gets a lower rowid.
        // The pipeline's Sources query has no ORDER BY, and an unindexed SQLite table scan
        // returns rows in rowid (insertion) order, so "bad" is processed first below - which
        // is what lets this test prove a failure on the first source doesn't poison the second.
        var badSource = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "bad" };
        test.Db.Sources.Add(badSource);
        await test.Db.SaveChangesAsync();

        var goodSource = new Source { TenantId = tenant.Id, Type = SourceTypes.Reddit, DisplayName = "good" };
        test.Db.Sources.Add(goodSource);
        await test.Db.SaveChangesAsync();

        var badConnector = new FakeConnector(SourceTypes.Rss, _ =>
            [new FetchedItem("e1", "One", null, null, "b1", "{}", null)]);
        var goodConnector = new FakeConnector(SourceTypes.Reddit, _ =>
            [new FetchedItem("g1", "Good one", null, null, "b2", "{}", null)]);

        // Right as the pipeline is about to save its newly-added ContentItem for "bad", race a
        // competing insert of the same (SourceId, ExternalId) through a separate context so the
        // pipeline's own SaveChangesAsync hits the unique index (ContentItems.SourceId/ExternalId).
        var hookedDb = new SaveHookDbContext(test.Db, badSource.Id, () =>
        {
            using var competitor = test.NewContext();
            competitor.ContentItems.Add(new ContentItem
            {
                TenantId = tenant.Id, SourceId = badSource.Id, ExternalId = "e1",
                Title = "planted by competing insert", Body = "x"
            });
            competitor.SaveChanges();
        });

        var pipeline = new IngestionPipeline(hookedDb, [badConnector, goodConnector]);

        var run = await pipeline.RunAsync(tenant.Id);

        // (a) the conflict is recorded as a failed source, not an unhandled exception out of RunAsync.
        Assert.Equal(RunStatus.Partial, run.Status);
        var log = JsonSerializer.Deserialize<List<string>>(run.LogJson)!;
        Assert.Equal(2, log.Count);
        Assert.StartsWith("bad: FAILED - ", log[0]);
        Assert.Contains("An error occurred while saving the entity changes", log[0]);
        Assert.StartsWith("good: fetched", log[1]);

        using var verify = test.NewContext();

        // (b) tracker not poisoned: the catch block's recovery save still landed LastFetchedAt
        // for the failing source. Before the fix, RemoveRange(added) was missing and this second
        // SaveChangesAsync retried the same doomed insert, throwing out of RunAsync entirely.
        var reloadedBad = await verify.Sources.SingleAsync(s => s.Id == badSource.Id);
        Assert.NotNull(reloadedBad.LastFetchedAt);

        var conflictedRows = await verify.ContentItems
            .Where(i => i.SourceId == badSource.Id && i.ExternalId == "e1")
            .ToListAsync();
        var survivor = Assert.Single(conflictedRows);
        Assert.Equal("planted by competing insert", survivor.Title);

        // (c) continuation: the source processed after the failing one still landed its item.
        Assert.Equal(1, await verify.ContentItems.CountAsync(i => i.SourceId == goodSource.Id));
        var reloadedGood = await verify.Sources.SingleAsync(s => s.Id == goodSource.Id);
        Assert.NotNull(reloadedGood.LastFetchedAt);
    }

    [Fact]
    public async Task Same_link_from_second_source_is_skipped_and_logged()
    {
        using var test = TestDb.Create();
        var (tenant, rssSource) = Seed(test);
        var redditSource = new Source { TenantId = tenant.Id, Type = SourceTypes.Reddit, DisplayName = "sub" };
        test.Db.Sources.Add(redditSource);
        test.Db.SaveChanges();

        var rss = new FakeConnector(SourceTypes.Rss, _ =>
            [new FetchedItem("rss-1", "Post", "https://example.com/post", null, "b", "{}", null)]);
        var reddit = new FakeConnector(SourceTypes.Reddit, _ =>
            [new FetchedItem("red-1", "Post", "https://example.com/post/?utm_source=reddit", null, "b", "{}", null)]);
        var pipeline = new IngestionPipeline(test.Db, [rss, reddit]);

        var run = await pipeline.RunAsync(tenant.Id);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        var item = Assert.Single(await test.Db.ContentItems.ToListAsync());
        Assert.Equal("rss-1", item.ExternalId);
        Assert.Equal("https://example.com/post", item.NormalizedUrl);

        var log = JsonSerializer.Deserialize<List<string>>(run.LogJson)!;
        Assert.Contains("feed: fetched 1, new 1, skipped 0 duplicate link(s)", log);
        Assert.Contains("sub: fetched 1, new 0, skipped 1 duplicate link(s)", log);
        Assert.Contains(log, l => l.Contains("duplicate: https://example.com/post/?utm_source=reddit")
                               && l.Contains("via feed"));
    }

    [Fact]
    public async Task Refetch_with_rotated_tracking_params_is_skipped()
    {
        using var test = TestDb.Create();
        var (tenant, source) = Seed(test);
        var call = 0;
        var connector = new FakeConnector(SourceTypes.Rss, _ => ++call == 1
            ? [new FetchedItem("https://ex.com/p?utm_s=1", "P", "https://ex.com/p?utm_s=1", null, "b", "{}", null)]
            : [new FetchedItem("https://ex.com/p?utm_s=2", "P", "https://ex.com/p?utm_s=2", null, "b", "{}", null)]);
        var pipeline = new IngestionPipeline(test.Db, [connector]);

        await pipeline.RunAsync(tenant.Id);
        var run2 = await pipeline.RunAsync(tenant.Id);

        Assert.Equal(1, await test.Db.ContentItems.CountAsync());
        Assert.Contains("skipped 1 duplicate link(s)", run2.LogJson);
    }

    [Fact]
    public async Task Same_link_twice_in_one_fetch_keeps_first_occurrence()
    {
        using var test = TestDb.Create();
        var (tenant, source) = Seed(test);
        var connector = new FakeConnector(SourceTypes.Rss, _ =>
        [
            new FetchedItem("a", "First", "https://ex.com/p", null, "b", "{}", null),
            new FetchedItem("b", "Second", "https://ex.com/p#comments", null, "b", "{}", null)
        ]);
        var pipeline = new IngestionPipeline(test.Db, [connector]);

        var run = await pipeline.RunAsync(tenant.Id);

        var item = Assert.Single(await test.Db.ContentItems.ToListAsync());
        Assert.Equal("a", item.ExternalId);
        Assert.Contains("duplicate within this fetch", run.LogJson);
    }

    [Fact]
    public async Task Items_without_urls_are_not_treated_as_duplicates_of_each_other()
    {
        using var test = TestDb.Create();
        var (tenant, source) = Seed(test);
        var connector = new FakeConnector(SourceTypes.Rss, _ =>
        [
            new FetchedItem("a", "One", null, null, "b", "{}", null),
            new FetchedItem("b", "Two", null, null, "b", "{}", null),
            new FetchedItem("c", "Three", "not a url", null, "b", "{}", null)
        ]);
        var pipeline = new IngestionPipeline(test.Db, [connector]);

        var run = await pipeline.RunAsync(tenant.Id);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(3, await test.Db.ContentItems.CountAsync());
        Assert.Contains("skipped 0 duplicate link(s)", run.LogJson);
    }
}
