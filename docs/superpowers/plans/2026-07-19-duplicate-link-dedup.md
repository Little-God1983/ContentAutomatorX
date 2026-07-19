# Duplicate-Link Protection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent the ingestion pipeline from importing the same link twice (tenant-wide, across sources and messy URLs) and record every skipped duplicate in the pipeline run log.

**Architecture:** A pure static `UrlNormalizer` produces a canonical form of each item's URL. `ContentItem` gains a nullable `NormalizedUrl` column with a filtered unique index on `(TenantId, NormalizedUrl)` as a DB backstop. `IngestionPipeline` dedups on normalized URL (tenant-wide) after its existing per-source `ExternalId` check and writes skip details into `PipelineRun.LogJson`. A one-time startup backfill normalizes pre-existing rows.

**Tech Stack:** .NET 10, EF Core + SQLite, xUnit. Spec: `docs/superpowers/specs/2026-07-19-duplicate-link-dedup-design.md`.

## Global Constraints

- Normalizer must never throw; unparseable input returns `null`.
- Tracking params removed: any `utm_*` prefix, plus `fbclid`, `gclid`, `ref`, `ref_src`, `igshid`, `mc_cid`, `mc_eid` (case-insensitive names).
- Items with `null` normalized URL are exempt from URL dedup; per-source `ExternalId` dedup still applies to them.
- Backfill collisions: oldest row (by `FetchedAt`) keeps the `NormalizedUrl`; later rows stay `null`. No rows are deleted.
- No UI changes. No cross-tenant dedup.
- All test commands run from repo root `e:\Repos\ContentAutomatorX`.

---

### Task 1: UrlNormalizer

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/UrlNormalizer.cs`
- Test: `tests/ContentAutomatorX.UnitTests/UrlNormalizerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static string? UrlNormalizer.Normalize(string? url)` in namespace `ContentAutomatorX.Application.Services`. Tasks 3 and 4 call it.

- [ ] **Step 1: Write the failing tests**

Create `tests/ContentAutomatorX.UnitTests/UrlNormalizerTests.cs`:

```csharp
using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.UnitTests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void Garbage_input_returns_null(string? input) =>
        Assert.Null(UrlNormalizer.Normalize(input));

    [Fact]
    public void Lowercases_scheme_and_host_but_not_path() =>
        Assert.Equal("https://example.com/Some/Path",
            UrlNormalizer.Normalize("HTTPS://EXAMPLE.COM/Some/Path"));

    [Fact]
    public void Drops_fragment() =>
        Assert.Equal("https://example.com/post",
            UrlNormalizer.Normalize("https://example.com/post#section-2"));

    [Theory]
    [InlineData("https://example.com/p?utm_source=rss&utm_medium=feed")]
    [InlineData("https://example.com/p?fbclid=abc123")]
    [InlineData("https://example.com/p?gclid=xyz&IGSHID=99")]
    [InlineData("https://example.com/p?ref=hn&mc_cid=1&mc_eid=2&ref_src=tw")]
    public void Strips_tracking_params(string input) =>
        Assert.Equal("https://example.com/p", UrlNormalizer.Normalize(input));

    [Fact]
    public void Keeps_and_sorts_real_query_params() =>
        Assert.Equal("https://example.com/search?a=2&q=hello",
            UrlNormalizer.Normalize("https://example.com/search?q=hello&utm_campaign=x&a=2"));

    [Fact]
    public void Trims_trailing_slash_but_keeps_root() =>
        Assert.Equal("https://example.com/blog/post",
            UrlNormalizer.Normalize("https://example.com/blog/post/"));

    [Fact]
    public void Root_url_normalizes_to_single_slash()
    {
        Assert.Equal("https://example.com/", UrlNormalizer.Normalize("https://example.com"));
        Assert.Equal("https://example.com/", UrlNormalizer.Normalize("https://example.com/"));
    }

    [Fact]
    public void Preserves_non_default_port_drops_default_port()
    {
        Assert.Equal("https://example.com:8443/x", UrlNormalizer.Normalize("https://example.com:8443/x"));
        Assert.Equal("https://example.com/x", UrlNormalizer.Normalize("https://example.com:443/x"));
    }

    [Fact]
    public void Equivalent_messy_urls_converge()
    {
        var a = UrlNormalizer.Normalize("HTTPS://Example.com/post/?utm_source=a#top");
        var b = UrlNormalizer.Normalize("https://example.com/post?utm_medium=b");
        Assert.NotNull(a);
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter "FullyQualifiedName~UrlNormalizerTests"`
Expected: build FAILS with "The type or namespace name 'UrlNormalizer' does not exist".

- [ ] **Step 3: Write the implementation**

Create `src/ContentAutomatorX.Application/Services/UrlNormalizer.cs`:

```csharp
using System.Text;

namespace ContentAutomatorX.Application.Services;

/// <summary>
/// Canonicalizes URLs so the same page fetched with different tracking params,
/// casing, or trailing slashes dedups to one <c>NormalizedUrl</c>. Deliberately
/// conservative: path casing and non-tracking query values are preserved so
/// genuinely different pages are never merged.
/// </summary>
public static class UrlNormalizer
{
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
        { "fbclid", "gclid", "ref", "ref_src", "igshid", "mc_cid", "mc_eid" };

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.HostNameType is UriHostNameType.Unknown || uri.Host.Length == 0) return null;

        var query = uri.Query.TrimStart('?');
        var kept = query.Length == 0
            ? []
            : query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(p =>
                {
                    var name = p.Split('=', 2)[0];
                    return !name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                        && !TrackingParams.Contains(name);
                })
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();

        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : uri.AbsolutePath;
        if (path.Length == 0) path = "/";

        var sb = new StringBuilder();
        sb.Append(uri.Scheme).Append("://").Append(uri.Host);
        if (!uri.IsDefaultPort) sb.Append(':').Append(uri.Port);
        sb.Append(path);
        if (kept.Count > 0) sb.Append('?').Append(string.Join('&', kept));
        return sb.ToString();
    }
}
```

Notes for the implementer: `Uri.Scheme` and `Uri.Host` are already lowercase; `Uri` drops the fragment because we never append `uri.Fragment`. `Uri.TryCreate("not a url", Absolute, ...)` can surprisingly succeed on some platforms by treating it as a file-ish URI — the `HostNameType`/`Host` guard rejects those.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter "FullyQualifiedName~UrlNormalizerTests"`
Expected: PASS, 16 tests (theories expand).

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Application/Services/UrlNormalizer.cs tests/ContentAutomatorX.UnitTests/UrlNormalizerTests.cs
git commit -m "feat: add UrlNormalizer for canonical link comparison"
```

---

### Task 2: ContentItem.NormalizedUrl column, index, and migration

**Files:**
- Modify: `src/ContentAutomatorX.Domain/Entities/ContentItem.cs` (add one property)
- Modify: `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs:22` (add index config)
- Create (generated): `src/ContentAutomatorX.Infrastructure/Migrations/<timestamp>_ContentItemNormalizedUrl.cs` + `.Designer.cs`, updated `AppDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `ContentItem.NormalizedUrl` (`string?` property, default `null`); filtered unique DB index on `(TenantId, NormalizedUrl)`. Tasks 3 and 4 read/write this property.

- [ ] **Step 1: Add the property**

In `src/ContentAutomatorX.Domain/Entities/ContentItem.cs`, after the `Url` property (line 12), add:

```csharp
    public string? NormalizedUrl { get; set; }
```

- [ ] **Step 2: Add the filtered unique index**

In `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`, in `OnModelCreating`, directly after the existing line
`b.Entity<ContentItem>().HasIndex(i => new { i.SourceId, i.ExternalId }).IsUnique();` add:

```csharp
        b.Entity<ContentItem>().HasIndex(i => new { i.TenantId, i.NormalizedUrl }).IsUnique()
            .HasFilter("\"NormalizedUrl\" IS NOT NULL");
```

(SQLite treats NULLs as distinct in unique indexes anyway; the filter makes the intent explicit and keeps the index small.)

- [ ] **Step 3: Generate the migration**

Run: `dotnet ef migrations add ContentItemNormalizedUrl --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web`
Expected: "Done." and three changed/new files under `src/ContentAutomatorX.Infrastructure/Migrations/`.
If `dotnet ef` is not found, install it first: `dotnet tool install --global dotnet-ef`.

Inspect the generated `Up()`: it must contain `AddColumn<string>(name: "NormalizedUrl", table: "ContentItems", nullable: true)` and a `CreateIndex` for `IX_ContentItems_TenantId_NormalizedUrl` with `unique: true` and `filter: "\"NormalizedUrl\" IS NOT NULL"`. Nothing else should be in it.

- [ ] **Step 4: Verify the whole solution still builds and existing tests pass**

Run: `dotnet test`
Expected: PASS — no existing test touches `NormalizedUrl`; `TestDb.Create()` runs `Database.Migrate()` so the new migration is exercised by every integration test.

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Domain/Entities/ContentItem.cs src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs src/ContentAutomatorX.Infrastructure/Migrations/
git commit -m "feat: add ContentItem.NormalizedUrl with filtered unique index"
```

---

### Task 3: Tenant-wide URL dedup + skip logging in IngestionPipeline

**Files:**
- Modify: `src/ContentAutomatorX.Application/Pipelines/IngestionPipeline.cs:55-66`
- Test: `tests/ContentAutomatorX.IntegrationTests/IngestionPipelineTests.cs` (append tests)

**Interfaces:**
- Consumes: `UrlNormalizer.Normalize(string?)` (Task 1), `ContentItem.NormalizedUrl` (Task 2).
- Produces: log format relied on by users:
  - per-source line: `"{DisplayName}: fetched {N}, new {X}, skipped {Y} duplicate link(s)"`
  - per skip: `"  duplicate: {url} (already imported {yyyy-MM-dd} via {source DisplayName})"` or `"  duplicate: {url} (duplicate within this fetch)"`

- [ ] **Step 1: Write the failing tests**

Append to `tests/ContentAutomatorX.IntegrationTests/IngestionPipelineTests.cs` (inside the `IngestionPipelineTests` class):

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IngestionPipelineTests"`
Expected: the four new tests FAIL (e.g. 2 items instead of 1; log line mismatch). The four pre-existing tests still pass.

- [ ] **Step 3: Implement the dedup in the pipeline**

In `src/ContentAutomatorX.Application/Pipelines/IngestionPipeline.cs`, add to the usings:

```csharp
using ContentAutomatorX.Application.Services;
```

Then replace lines 55–66 (from `var fresh = ...` through the `log.Add($"{source.DisplayName}: fetched ...` line) with:

```csharp
                var existingIds = existing.Select(i => i.ExternalId).ToHashSet();
                var fresh = items.Where(f => !existingIds.Contains(f.ExternalId)).ToList();

                // tenant-wide duplicate-link check on normalized URLs (cross-source; null = exempt)
                var normalized = fresh.Select(f => (Item: f, Norm: UrlNormalizer.Normalize(f.Url))).ToList();
                var norms = normalized.Where(x => x.Norm != null).Select(x => x.Norm!).Distinct().ToList();
                var owners = await db.ContentItems
                    .Where(i => i.TenantId == tenantId && i.NormalizedUrl != null && norms.Contains(i.NormalizedUrl))
                    .Select(i => new { i.NormalizedUrl, i.FetchedAt, i.SourceId })
                    .ToListAsync(ct);
                var ownerSourceIds = owners.Select(o => o.SourceId).Distinct().ToList();
                var ownerNames = await db.Sources
                    .Where(s => ownerSourceIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);

                var skipped = new List<string>();
                var seen = new HashSet<string>();
                var toAdd = new List<(Domain.Models.FetchedItem Item, string? Norm)>();
                foreach (var (f, norm) in normalized)
                {
                    if (norm == null) { toAdd.Add((f, null)); continue; }
                    if (owners.FirstOrDefault(o => o.NormalizedUrl == norm) is { } owner)
                    {
                        var via = ownerNames.GetValueOrDefault(owner.SourceId, "unknown source");
                        skipped.Add($"  duplicate: {f.Url} (already imported {owner.FetchedAt:yyyy-MM-dd} via {via})");
                        continue;
                    }
                    if (!seen.Add(norm))
                    {
                        skipped.Add($"  duplicate: {f.Url} (duplicate within this fetch)");
                        continue;
                    }
                    toAdd.Add((f, norm));
                }

                added = toAdd.Select(x => new ContentItem
                {
                    TenantId = tenantId, SourceId = source.Id, ExternalId = x.Item.ExternalId,
                    Title = x.Item.Title, Url = x.Item.Url, Author = x.Item.Author, Body = x.Item.Body,
                    MetadataJson = x.Item.MetadataJson, PublishedAt = x.Item.PublishedAt,
                    NormalizedUrl = x.Norm
                }).ToList();
                foreach (var item in added) db.ContentItems.Add(item);

                source.LastFetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                log.Add($"{source.DisplayName}: fetched {fetched.Count}, new {added.Count}, skipped {skipped.Count} duplicate link(s)");
                log.AddRange(skipped);
```

(The `existingIds`/`fresh` lines are unchanged from today; they're included so the replacement region is unambiguous. Sources run sequentially under the per-tenant lock and each source saves before the next starts, so a link imported by source #1 is visible to source #2's `owners` query within the same run.)

If the top-level `Domain.Models` qualifier on the tuple type collides with anything, `using ContentAutomatorX.Domain.Models;` is already imported via `ContentAutomatorX.Domain` — check the file's usings and use the bare `FetchedItem` if it resolves.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IngestionPipelineTests"`
Expected: PASS, 8 tests (4 pre-existing + 4 new). Note `Mid_save_conflict_recovers_tracker_and_pipeline_continues` asserts `StartsWith("good: fetched")` — still satisfied by the new line format.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS. Other integration suites (`ServiceTests`, `GenerationPipelineTests`, …) create `ContentItem`s directly and are unaffected because `NormalizedUrl` defaults to `null`.

- [ ] **Step 6: Commit**

```bash
git add src/ContentAutomatorX.Application/Pipelines/IngestionPipeline.cs tests/ContentAutomatorX.IntegrationTests/IngestionPipelineTests.cs
git commit -m "feat: tenant-wide duplicate-link dedup with skip logging in ingestion"
```

---

### Task 4: Backfill NormalizedUrl for pre-existing rows at startup

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/NormalizedUrlBackfill.cs`
- Modify: `src/ContentAutomatorX.Web/Program.cs:97` (call after `Migrate()`/WAL pragma)
- Test: `tests/ContentAutomatorX.IntegrationTests/NormalizedUrlBackfillTests.cs`

**Interfaces:**
- Consumes: `UrlNormalizer.Normalize(string?)` (Task 1), `ContentItem.NormalizedUrl` (Task 2), `IAppDbContext` (existing, in `ContentAutomatorX.Application.Persistence`).
- Produces: `static Task NormalizedUrlBackfill.RunAsync(IAppDbContext db, CancellationToken ct = default)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ContentAutomatorX.IntegrationTests/NormalizedUrlBackfillTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~NormalizedUrlBackfillTests"`
Expected: build FAILS with "The type or namespace name 'NormalizedUrlBackfill' does not exist".

- [ ] **Step 3: Write the implementation**

Create `src/ContentAutomatorX.Application/Services/NormalizedUrlBackfill.cs`:

```csharp
using ContentAutomatorX.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

/// <summary>
/// One-time (idempotent) startup pass that normalizes URLs of rows created before the
/// NormalizedUrl column existed. Collisions within a tenant keep the oldest row's value;
/// losers stay null so the filtered unique index can be satisfied without deleting data.
/// </summary>
public static class NormalizedUrlBackfill
{
    public static async Task RunAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var pending = await db.ContentItems
            .Where(i => i.NormalizedUrl == null && i.Url != null)
            .OrderBy(i => i.FetchedAt)
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        var taken = (await db.ContentItems
                .Where(i => i.NormalizedUrl != null)
                .Select(i => new { i.TenantId, i.NormalizedUrl })
                .ToListAsync(ct))
            .Select(x => (x.TenantId, Norm: x.NormalizedUrl!))
            .ToHashSet();

        foreach (var item in pending)
        {
            var norm = UrlNormalizer.Normalize(item.Url);
            if (norm != null && taken.Add((item.TenantId, norm)))
                item.NormalizedUrl = norm;
        }
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Wire into startup**

In `src/ContentAutomatorX.Web/Program.cs`, inside the existing migrate-and-seed scope, directly after `db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");` (line 97), add:

```csharp
    await NormalizedUrlBackfill.RunAsync(db);
```

Add to the file's usings if not resolvable: `using ContentAutomatorX.Application.Services;`. (Program.cs uses top-level statements, so `await` is available; if the enclosing `using (var scope = ...)` block somehow rejects await, use `NormalizedUrlBackfill.RunAsync(db).GetAwaiter().GetResult();` instead.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~NormalizedUrlBackfillTests"`
Expected: PASS, 3 tests.

- [ ] **Step 6: Run the full suite and build the web app**

Run: `dotnet test && dotnet build src/ContentAutomatorX.Web`
Expected: all tests PASS, web app builds (verifies the Program.cs edit compiles).

- [ ] **Step 7: Commit**

```bash
git add src/ContentAutomatorX.Application/Services/NormalizedUrlBackfill.cs src/ContentAutomatorX.Web/Program.cs tests/ContentAutomatorX.IntegrationTests/NormalizedUrlBackfillTests.cs
git commit -m "feat: backfill NormalizedUrl for pre-existing content items at startup"
```
