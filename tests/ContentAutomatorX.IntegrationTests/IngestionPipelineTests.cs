using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class FakeConnector(string type, Func<Source, IReadOnlyList<FetchedItem>> fetch) : ISourceConnector
{
    public string Type => type;
    public Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default) =>
        Task.FromResult(fetch(source));
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
}
