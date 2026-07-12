# ContentAutomatorX Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Multi-tenant content automation: ingest Reddit/RSS into SQLite, generate drafts (newsletter/social post/video script) per Recipe via the `claude` CLI, deliver Markdown files to per-tenant sync folders, all controllable via Blazor Server UI and an exposed MCP server.

**Architecture:** Modular monolith, one ASP.NET Core host. Dependencies point inward: Web → Application → Domain; Infrastructure implements Domain abstractions and is wired via DI in Web. Application accesses persistence through `IAppDbContext` (EF Core abstraction defined in Application, implemented in Infrastructure).

**Tech Stack:** .NET 10 / C# (SDK 10.0.301 installed), ASP.NET Core + Blazor Server, MudBlazor, EF Core + SQLite, `System.ServiceModel.Syndication` (RSS/Atom), Cronos (cron parsing), Serilog, `ModelContextProtocol.AspNetCore` (prerelease MCP SDK), xUnit. Spec: `docs/superpowers/specs/2026-07-12-contentautomatorx-phase1-design.md`.

## Global Constraints

- Target framework `net10.0` for all projects; nullable enabled, implicit usings on (SDK defaults).
- Repo root: `E:\Repos\ContentAutomatorX`. All commands below run from repo root unless stated.
- SQLite DB file: `data/contentx.db` under the Web project's content root (created on startup via `Database.Migrate()`); path configurable via `appsettings.json` key `Database:Path`.
- Web host binds localhost only: `http://localhost:5090` (Phase 1 — no auth).
- MCP endpoint at `/mcp` (streamable HTTP), tools are thin wrappers over Application services — no DB access in the MCP layer.
- Claude CLI invocation: `claude -p --output-format json` with the prompt piped to stdin; configurable timeout default 300s; exactly one retry on failure.
- Enum-like extensibility values (`Source.Type`, `Draft.Kind`, `PipelineRun` kinds/triggers) are strings with constants classes — never C# enums (spec: extensible without migrations). `ContentItemStatus`, `DraftStatus`, `RunStatus` are C# enums stored as strings.
- Dedup constraint: unique index on `ContentItem(SourceId, ExternalId)`.
- Draft files: write to temp file then `File.Move` — never half-written files.
- Tests: xUnit, no mocking library (hand-rolled fakes). Unit tests must not hit network or real `claude`.
- Commit after every task (conventional commits, e.g. `feat: ...`, `test: ...`).

## File Structure (end state)

```
src/ContentAutomatorX.Domain/
  Entities/{Tenant,Source,ContentItem,Recipe,Draft,PipelineRun,PromptTemplate}.cs
  Constants.cs                      # SourceTypes, DraftKinds, RunKinds, RunTriggers, enums
  Abstractions/{ISourceConnector,ILlmBackend,IDraftDelivery,IPlatformConnector}.cs
  Models/{FetchedItem,LlmResult,SelectionRules,RecipeOutput}.cs
src/ContentAutomatorX.Application/
  Persistence/IAppDbContext.cs
  Pipelines/{IngestionPipeline,GenerationPipeline}.cs
  Services/{TenantService,SourceService,RecipeService,ContentService,DraftService,RunService}.cs
  Generation/{ItemSelector,PromptBuilder,DefaultTemplates}.cs
  Scheduling/CronDue.cs
src/ContentAutomatorX.Infrastructure/
  Persistence/AppDbContext.cs + Migrations/
  Sources/{RssConnector,RedditConnector}.cs
  Llm/{ClaudeCliBackend,IProcessRunner,ProcessRunner}.cs
  Delivery/FileShareDraftDelivery.cs
src/ContentAutomatorX.Web/
  Program.cs, appsettings.json
  Jobs/SchedulerService.cs
  Mcp/ContentXTools.cs
  Components/ (Blazor: Layout, Dashboard, Tenants, Sources, Recipes, Content, Drafts, Runs)
tests/ContentAutomatorX.UnitTests/       # parsers, selector, prompt builder, cron, claude backend
tests/ContentAutomatorX.IntegrationTests/ # EF + pipelines + delivery + MCP tool layer (temp SQLite)
```

---

### Task 1: Solution scaffold

**Files:**
- Create: solution, 4 src projects, 2 test projects, `.gitignore`

**Interfaces:**
- Consumes: nothing
- Produces: compiling solution `ContentAutomatorX.sln` with reference graph Web→{Application,Infrastructure}, Infrastructure→{Domain,Application}, Application→Domain, tests→{Application,Infrastructure,Domain}

- [ ] **Step 1: Create solution and projects**

```bash
cd /e/Repos/ContentAutomatorX
dotnet new gitignore
dotnet new sln -n ContentAutomatorX
dotnet new classlib -n ContentAutomatorX.Domain -o src/ContentAutomatorX.Domain -f net10.0
dotnet new classlib -n ContentAutomatorX.Application -o src/ContentAutomatorX.Application -f net10.0
dotnet new classlib -n ContentAutomatorX.Infrastructure -o src/ContentAutomatorX.Infrastructure -f net10.0
dotnet new blazor -n ContentAutomatorX.Web -o src/ContentAutomatorX.Web -f net10.0 -int Server -e
dotnet new xunit -n ContentAutomatorX.UnitTests -o tests/ContentAutomatorX.UnitTests -f net10.0
dotnet new xunit -n ContentAutomatorX.IntegrationTests -o tests/ContentAutomatorX.IntegrationTests -f net10.0
dotnet sln add src/ContentAutomatorX.Domain src/ContentAutomatorX.Application src/ContentAutomatorX.Infrastructure src/ContentAutomatorX.Web tests/ContentAutomatorX.UnitTests tests/ContentAutomatorX.IntegrationTests
rm src/ContentAutomatorX.Domain/Class1.cs src/ContentAutomatorX.Application/Class1.cs src/ContentAutomatorX.Infrastructure/Class1.cs
```

- [ ] **Step 2: Wire references and packages**

```bash
dotnet add src/ContentAutomatorX.Application reference src/ContentAutomatorX.Domain
dotnet add src/ContentAutomatorX.Infrastructure reference src/ContentAutomatorX.Domain src/ContentAutomatorX.Application
dotnet add src/ContentAutomatorX.Web reference src/ContentAutomatorX.Application src/ContentAutomatorX.Infrastructure
dotnet add tests/ContentAutomatorX.UnitTests reference src/ContentAutomatorX.Application src/ContentAutomatorX.Infrastructure
dotnet add tests/ContentAutomatorX.IntegrationTests reference src/ContentAutomatorX.Application src/ContentAutomatorX.Infrastructure

dotnet add src/ContentAutomatorX.Application package Microsoft.EntityFrameworkCore
dotnet add src/ContentAutomatorX.Application package Cronos
dotnet add src/ContentAutomatorX.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/ContentAutomatorX.Infrastructure package System.ServiceModel.Syndication
dotnet add src/ContentAutomatorX.Web package Microsoft.EntityFrameworkCore.Design
dotnet add src/ContentAutomatorX.Web package MudBlazor
dotnet add src/ContentAutomatorX.Web package Serilog.AspNetCore
dotnet add src/ContentAutomatorX.Web package ModelContextProtocol.AspNetCore --prerelease
dotnet add tests/ContentAutomatorX.IntegrationTests package Microsoft.EntityFrameworkCore.Sqlite
```

- [ ] **Step 3: Verify build**

Run: `dotnet build`
Expected: `Build succeeded` (0 errors; warnings OK).

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "chore: scaffold solution (Domain/Application/Infrastructure/Web + tests)"
```

---

### Task 2: Domain entities, constants, abstractions

**Files:**
- Create: `src/ContentAutomatorX.Domain/Entities/Tenant.cs`, `Source.cs`, `ContentItem.cs`, `Recipe.cs`, `Draft.cs`, `PipelineRun.cs`, `PromptTemplate.cs`
- Create: `src/ContentAutomatorX.Domain/Constants.cs`
- Create: `src/ContentAutomatorX.Domain/Models/FetchedItem.cs`, `LlmResult.cs`, `SelectionRules.cs`, `RecipeOutput.cs`
- Create: `src/ContentAutomatorX.Domain/Abstractions/ISourceConnector.cs`, `ILlmBackend.cs`, `IDraftDelivery.cs`, `IPlatformConnector.cs`

**Interfaces:**
- Consumes: nothing
- Produces: all entity types and abstractions used by every later task, exactly as below. Pure data — no unit tests for this task (behavior gets tested where it exists).

- [ ] **Step 1: Write entity classes**

`src/ContentAutomatorX.Domain/Entities/Tenant.cs`:
```csharp
namespace ContentAutomatorX.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string VoiceProfile { get; set; } = "";
    public string OutputFolderPath { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
```

`src/ContentAutomatorX.Domain/Entities/Source.cs`:
```csharp
namespace ContentAutomatorX.Domain.Entities;

public class Source
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Type { get; set; }          // SourceTypes.*
    public required string DisplayName { get; set; }
    public string ConfigJson { get; set; } = "{}";     // Reddit: {subreddit,sort,timeframe}; Rss: {feedUrl}
    public string? ScheduleCron { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastFetchedAt { get; set; }
}
```

`src/ContentAutomatorX.Domain/Entities/ContentItem.cs`:
```csharp
namespace ContentAutomatorX.Domain.Entities;

public enum ContentItemStatus { New, Selected, Ignored, Used }

public class ContentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SourceId { get; set; }
    public required string ExternalId { get; set; }
    public required string Title { get; set; }
    public string? Url { get; set; }
    public string? Author { get; set; }
    public string Body { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";   // e.g. {"score":123}
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
    public ContentItemStatus Status { get; set; } = ContentItemStatus.New;
}
```

`src/ContentAutomatorX.Domain/Entities/Recipe.cs`:
```csharp
namespace ContentAutomatorX.Domain.Entities;

public class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }          // DraftKinds.*
    public bool IsEnabled { get; set; } = true;
    public string SourceIdsJson { get; set; } = "[]";  // empty array = all tenant sources
    public string SelectionJson { get; set; } = "{}";  // SelectionRules
    public Guid PromptTemplateId { get; set; }
    public string? ToneModifiers { get; set; }
    public string? LengthTarget { get; set; }
    public string? Language { get; set; }
    public string OutputJson { get; set; } = "{}";     // RecipeOutput
    public string? ScheduleCron { get; set; }          // null = manual only
    public DateTimeOffset? LastRunAt { get; set; }
}
```

`src/ContentAutomatorX.Domain/Entities/Draft.cs`:
```csharp
namespace ContentAutomatorX.Domain.Entities;

public enum DraftStatus { Generated, Delivered }

public class Draft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RecipeId { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public string Body { get; set; } = "";
    public string? TargetPlatform { get; set; }
    public string SourceItemIdsJson { get; set; } = "[]";
    public string? FilePath { get; set; }
    public DraftStatus Status { get; set; } = DraftStatus.Generated;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ModelUsed { get; set; }
}
```

`src/ContentAutomatorX.Domain/Entities/PipelineRun.cs`:
```csharp
namespace ContentAutomatorX.Domain.Entities;

public enum RunStatus { Running, Succeeded, Failed, Partial }

public class PipelineRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Kind { get; set; }          // RunKinds.*
    public required string Trigger { get; set; }       // RunTriggers.*
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public string LogJson { get; set; } = "[]";        // array of step messages
}
```

`src/ContentAutomatorX.Domain/Entities/PromptTemplate.cs`:
```csharp
namespace ContentAutomatorX.Domain.Entities;

public class PromptTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TenantId { get; set; }                // null = system default
    public required string Kind { get; set; }
    public required string Template { get; set; }      // {voice_profile} {tone_modifiers} {items} {extra_instructions}
}
```

- [ ] **Step 2: Write constants and models**

`src/ContentAutomatorX.Domain/Constants.cs`:
```csharp
namespace ContentAutomatorX.Domain;

public static class SourceTypes
{
    public const string Reddit = "Reddit";
    public const string Rss = "Rss";
}

public static class DraftKinds
{
    public const string Newsletter = "Newsletter";
    public const string SocialPost = "SocialPost";
    public const string VideoScript = "VideoScript";
    public static readonly string[] All = [Newsletter, SocialPost, VideoScript];
}

public static class RunKinds
{
    public const string Ingestion = "Ingestion";
    public const string Generation = "Generation";
}

public static class RunTriggers
{
    public const string Scheduled = "Scheduled";
    public const string Manual = "Manual";
    public const string Mcp = "Mcp";
}
```

`src/ContentAutomatorX.Domain/Models/FetchedItem.cs`:
```csharp
namespace ContentAutomatorX.Domain.Models;

public record FetchedItem(
    string ExternalId,
    string Title,
    string? Url,
    string? Author,
    string Body,
    string MetadataJson,
    DateTimeOffset? PublishedAt);
```

`src/ContentAutomatorX.Domain/Models/LlmResult.cs`:
```csharp
namespace ContentAutomatorX.Domain.Models;

public record LlmResult(string Text, string Model);
```

`src/ContentAutomatorX.Domain/Models/SelectionRules.cs`:
```csharp
namespace ContentAutomatorX.Domain.Models;

public class SelectionRules
{
    public int? TimeWindowDays { get; set; }
    public int? MinScore { get; set; }
    public int MaxItems { get; set; } = 10;
    public string[] IncludeKeywords { get; set; } = [];
    public string[] ExcludeKeywords { get; set; } = [];
}
```

`src/ContentAutomatorX.Domain/Models/RecipeOutput.cs`:
```csharp
namespace ContentAutomatorX.Domain.Models;

public class RecipeOutput
{
    public string? Subfolder { get; set; }
    public string? FilenamePattern { get; set; }   // tokens: {date} {kind} {slug}; default "{date}-{kind}-{slug}.md"
    public string? TargetPlatform { get; set; }
}
```

- [ ] **Step 3: Write abstractions**

`src/ContentAutomatorX.Domain/Abstractions/ISourceConnector.cs`:
```csharp
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ISourceConnector
{
    string Type { get; }   // matches SourceTypes.*
    Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default);
}
```

`src/ContentAutomatorX.Domain/Abstractions/ILlmBackend.cs`:
```csharp
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ILlmBackend
{
    string Name { get; }
    Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default);
}
```

`src/ContentAutomatorX.Domain/Abstractions/IDraftDelivery.cs`:
```csharp
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface IDraftDelivery
{
    /// <summary>Writes the draft file and returns the absolute file path.</summary>
    Task<string> DeliverAsync(Tenant tenant, RecipeOutput output, Draft draft, CancellationToken ct = default);
}
```

`src/ContentAutomatorX.Domain/Abstractions/IPlatformConnector.cs`:
```csharp
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Domain.Abstractions;

/// <summary>Phase 2 seam (YouTube/Patreon/Civitai/Ko-fi). No implementations in Phase 1.</summary>
public interface IPlatformConnector
{
    string Platform { get; }
    Task PublishAsync(Tenant tenant, Draft draft, CancellationToken ct = default);
}
```

- [ ] **Step 4: Build and commit**

Run: `dotnet build` → Expected: `Build succeeded`.

```bash
git add -A && git commit -m "feat: domain entities, constants, and abstractions"
```

---

### Task 3: Persistence — IAppDbContext, AppDbContext, initial migration

**Files:**
- Create: `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`
- Create: `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`, `DesignTimeFactory.cs`
- Create: `tests/ContentAutomatorX.IntegrationTests/TestDb.cs`, `PersistenceTests.cs`
- Create (generated): `src/ContentAutomatorX.Infrastructure/Migrations/*`

**Interfaces:**
- Consumes: Task 2 entities
- Produces: `IAppDbContext` (all `DbSet`s + `Task<int> SaveChangesAsync(CancellationToken)`), `AppDbContext(DbContextOptions<AppDbContext>)`, and test helper `TestDb.Create()` returning a disposable wrapper with a real temp-file SQLite `AppDbContext` (migrations applied) plus `NewContext()` for fresh contexts on the same DB. All later pipeline/service tasks depend on these exact names.

- [ ] **Step 1: Write the interface (Application)**

`src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`:
```csharp
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Persistence;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Source> Sources { get; }
    DbSet<ContentItem> ContentItems { get; }
    DbSet<Recipe> Recipes { get; }
    DbSet<Draft> Drafts { get; }
    DbSet<PipelineRun> PipelineRuns { get; }
    DbSet<PromptTemplate> PromptTemplates { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Write AppDbContext + design-time factory (Infrastructure)**

```bash
dotnet add src/ContentAutomatorX.Infrastructure package Microsoft.EntityFrameworkCore.Design
```

`src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`:
```csharp
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>().HasIndex(t => t.Slug).IsUnique();
        b.Entity<ContentItem>().HasIndex(i => new { i.SourceId, i.ExternalId }).IsUnique();
        b.Entity<ContentItem>().Property(i => i.Status).HasConversion<string>();
        b.Entity<Draft>().Property(d => d.Status).HasConversion<string>();
        b.Entity<PipelineRun>().Property(r => r.Status).HasConversion<string>();
    }
}
```

`src/ContentAutomatorX.Infrastructure/Persistence/DesignTimeFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ContentAutomatorX.Infrastructure.Persistence;

public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design.db").Options);
}
```

- [ ] **Step 3: Generate the initial migration**

```bash
dotnet new tool-manifest --force
dotnet tool install dotnet-ef
dotnet ef migrations add InitialCreate --project src/ContentAutomatorX.Infrastructure
```

Expected: `Migrations/` folder appears in Infrastructure; `dotnet build` succeeds.

- [ ] **Step 4: Write failing integration tests**

`tests/ContentAutomatorX.IntegrationTests/TestDb.cs`:
```csharp
using ContentAutomatorX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public sealed class TestDb : IDisposable
{
    public AppDbContext Db { get; }
    private readonly string _path;

    private TestDb(AppDbContext db, string path) { Db = db; _path = path; }

    public static TestDb Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"contentx-test-{Guid.NewGuid():N}.db");
        var db = NewContext(path);
        db.Database.Migrate();
        return new TestDb(db, path);
    }

    public AppDbContext NewContext() => NewContext(_path);

    private static AppDbContext NewContext(string path) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path}").Options);

    public void Dispose()
    {
        Db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { /* best effort */ }
    }
}
```

`tests/ContentAutomatorX.IntegrationTests/PersistenceTests.cs`:
```csharp
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
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests`
Expected: both tests PASS (schema comes from Steps 2-3; failures mean the schema is wrong — fix before continuing).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: EF Core persistence with SQLite, initial migration, dedup constraint"
```

---

### Task 4: RSS connector

**Files:**
- Create: `src/ContentAutomatorX.Infrastructure/Sources/RssConnector.cs`
- Create: `tests/ContentAutomatorX.UnitTests/StubHttpHandler.cs`, `RssConnectorTests.cs`, `Fixtures/sample-rss.xml`

**Interfaces:**
- Consumes: `ISourceConnector`, `FetchedItem`, `Source` (Task 2)
- Produces: `RssConnector(HttpClient)` with `Type == SourceTypes.Rss`; reads `Source.ConfigJson` shape `{"feedUrl":"https://..."}`. Also `StubHttpHandler` test helper reused by Task 5.

- [ ] **Step 1: Add fixture and test helper**

`tests/ContentAutomatorX.UnitTests/Fixtures/sample-rss.xml`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<rss version="2.0">
  <channel>
    <title>Example Blog</title>
    <link>https://example.com</link>
    <item>
      <guid>post-1</guid>
      <title>First Post</title>
      <link>https://example.com/1</link>
      <author>alice</author>
      <description>Body of the first post.</description>
      <pubDate>Mon, 06 Jul 2026 10:00:00 GMT</pubDate>
    </item>
    <item>
      <guid>post-2</guid>
      <title>Second Post</title>
      <link>https://example.com/2</link>
      <description>Body of the second post.</description>
      <pubDate>Tue, 07 Jul 2026 10:00:00 GMT</pubDate>
    </item>
  </channel>
</rss>
```

Edit `tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj` — add inside `<Project>`:
```xml
<ItemGroup>
  <None Include="Fixtures/**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

`tests/ContentAutomatorX.UnitTests/StubHttpHandler.cs`:
```csharp
using System.Net;

namespace ContentAutomatorX.UnitTests;

public class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static StubHttpHandler ReturningFile(string path, string mediaType) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(File.ReadAllText(path), System.Text.Encoding.UTF8, mediaType)
        });
}
```

- [ ] **Step 2: Write the failing test**

`tests/ContentAutomatorX.UnitTests/RssConnectorTests.cs`:
```csharp
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Sources;

namespace ContentAutomatorX.UnitTests;

public class RssConnectorTests
{
    [Fact]
    public async Task Parses_rss_items_with_external_ids()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-rss.xml", "application/rss+xml");
        var connector = new RssConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Rss, DisplayName = "blog",
            ConfigJson = """{"feedUrl":"https://example.com/feed"}"""
        };

        var items = await connector.FetchAsync(source);

        Assert.Equal(2, items.Count);
        Assert.Equal("post-1", items[0].ExternalId);
        Assert.Equal("First Post", items[0].Title);
        Assert.Equal("https://example.com/1", items[0].Url);
        Assert.Contains("first post", items[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(items[0].PublishedAt);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter RssConnectorTests`
Expected: FAIL — `RssConnector` does not exist (compile error).

- [ ] **Step 4: Implement RssConnector**

`src/ContentAutomatorX.Infrastructure/Sources/RssConnector.cs`:
```csharp
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Xml;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class RssConnector(HttpClient http) : ISourceConnector
{
    public string Type => SourceTypes.Rss;

    private record RssConfig(string FeedUrl);

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<RssConfig>(source.ConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid RSS config");

        await using var stream = await http.GetStreamAsync(config.FeedUrl, ct);
        using var reader = XmlReader.Create(stream);
        var feed = SyndicationFeed.Load(reader);

        return feed.Items.Select(item => new FetchedItem(
            ExternalId: item.Id ?? item.Links.FirstOrDefault()?.Uri.ToString() ?? item.Title.Text,
            Title: item.Title?.Text ?? "(untitled)",
            Url: item.Links.FirstOrDefault()?.Uri.ToString(),
            Author: item.Authors.FirstOrDefault()?.Name ?? item.Authors.FirstOrDefault()?.Email,
            Body: (item.Summary?.Text ?? (item.Content as TextSyndicationContent)?.Text ?? "").Trim(),
            MetadataJson: "{}",
            PublishedAt: item.PublishDate == default ? null : item.PublishDate)).ToList();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter RssConnectorTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: RSS/Atom source connector"
```

---

### Task 5: Reddit connector

**Files:**
- Create: `src/ContentAutomatorX.Infrastructure/Sources/RedditConnector.cs`
- Create: `tests/ContentAutomatorX.UnitTests/RedditConnectorTests.cs`, `Fixtures/sample-reddit.json`

**Interfaces:**
- Consumes: `ISourceConnector`, `FetchedItem`, `StubHttpHandler` (Task 4)
- Produces: `RedditConnector(HttpClient)` with `Type == SourceTypes.Reddit`; `Source.ConfigJson` shape `{"subreddit":"StableDiffusion","sort":"hot","timeframe":"week","limit":25}` (sort/timeframe/limit optional; defaults hot/week/25). Sends a User-Agent; `MetadataJson` carries `{"score":N}`.

- [ ] **Step 1: Add fixture**

`tests/ContentAutomatorX.UnitTests/Fixtures/sample-reddit.json`:
```json
{
  "kind": "Listing",
  "data": {
    "children": [
      {
        "kind": "t3",
        "data": {
          "id": "abc123",
          "title": "New model released",
          "permalink": "/r/StableDiffusion/comments/abc123/new_model_released/",
          "author": "bob",
          "selftext": "Details about the model.",
          "score": 456,
          "created_utc": 1783850000
        }
      },
      {
        "kind": "t3",
        "data": {
          "id": "def456",
          "title": "Workflow question",
          "permalink": "/r/StableDiffusion/comments/def456/workflow_question/",
          "author": "carol",
          "selftext": "",
          "score": 12,
          "created_utc": 1783936400
        }
      }
    ]
  }
}
```

- [ ] **Step 2: Write the failing test**

`tests/ContentAutomatorX.UnitTests/RedditConnectorTests.cs`:
```csharp
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Sources;

namespace ContentAutomatorX.UnitTests;

public class RedditConnectorTests
{
    [Fact]
    public async Task Parses_posts_and_builds_correct_url()
    {
        var handler = StubHttpHandler.ReturningFile("Fixtures/sample-reddit.json", "application/json");
        var connector = new RedditConnector(new HttpClient(handler));
        var source = new Source
        {
            Type = SourceTypes.Reddit, DisplayName = "sd",
            ConfigJson = """{"subreddit":"StableDiffusion","sort":"top","timeframe":"week"}"""
        };

        var items = await connector.FetchAsync(source);

        Assert.Equal(2, items.Count);
        Assert.Equal("abc123", items[0].ExternalId);
        Assert.Equal("New model released", items[0].Title);
        Assert.StartsWith("https://www.reddit.com/r/StableDiffusion/", items[0].Url);
        Assert.Contains("\"score\":456", items[0].MetadataJson);
        Assert.Equal("bob", items[0].Author);

        var requestUrl = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("/r/StableDiffusion/top.json", requestUrl);
        Assert.Contains("t=week", requestUrl);
        Assert.True(handler.Requests[0].Headers.UserAgent.Count > 0, "must send a User-Agent");
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter RedditConnectorTests`
Expected: FAIL — `RedditConnector` does not exist.

- [ ] **Step 4: Implement RedditConnector**

`src/ContentAutomatorX.Infrastructure/Sources/RedditConnector.cs`:
```csharp
using System.Text.Json;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class RedditConnector(HttpClient http) : ISourceConnector
{
    public string Type => SourceTypes.Reddit;

    private record RedditConfig(string Subreddit, string? Sort = null, string? Timeframe = null, int? Limit = null);

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<RedditConfig>(source.ConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid Reddit config");

        var sort = config.Sort ?? "hot";
        var url = $"https://www.reddit.com/r/{config.Subreddit}/{sort}.json?limit={config.Limit ?? 25}&t={config.Timeframe ?? "week"}&raw_json=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("windows:ContentAutomatorX:v1.0 (content aggregation)");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var results = new List<FetchedItem>();
        foreach (var child in doc.RootElement.GetProperty("data").GetProperty("children").EnumerateArray())
        {
            var d = child.GetProperty("data");
            var score = d.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
            var createdUtc = d.TryGetProperty("created_utc", out var c)
                ? DateTimeOffset.FromUnixTimeSeconds((long)c.GetDouble())
                : (DateTimeOffset?)null;
            results.Add(new FetchedItem(
                ExternalId: d.GetProperty("id").GetString()!,
                Title: d.GetProperty("title").GetString() ?? "(untitled)",
                Url: "https://www.reddit.com" + (d.TryGetProperty("permalink", out var p) ? p.GetString() : ""),
                Author: d.TryGetProperty("author", out var a) ? a.GetString() : null,
                Body: d.TryGetProperty("selftext", out var t) ? (t.GetString() ?? "") : "",
                MetadataJson: $"{{\"score\":{score}}}",
                PublishedAt: createdUtc));
        }
        return results;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter RedditConnectorTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: Reddit source connector (public JSON endpoints)"
```

---

### Task 6: Ingestion pipeline

**Files:**
- Create: `src/ContentAutomatorX.Application/Pipelines/TenantLocks.cs`, `IngestionPipeline.cs`
- Create: `tests/ContentAutomatorX.IntegrationTests/IngestionPipelineTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext` (Task 3), `ISourceConnector` (Task 2)
- Produces: `IngestionPipeline(IAppDbContext db, IEnumerable<ISourceConnector> connectors)` with `Task<PipelineRun> RunAsync(Guid tenantId, Guid? sourceId = null, string trigger = RunTriggers.Manual, CancellationToken ct = default)`; `TenantLocks.Get(Guid tenantId)` returning a `SemaphoreSlim(1,1)` shared per tenant (used by Task 11 too).

- [ ] **Step 1: Write failing tests**

`tests/ContentAutomatorX.IntegrationTests/IngestionPipelineTests.cs`:
```csharp
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter IngestionPipelineTests`
Expected: FAIL — `IngestionPipeline` does not exist.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Application/Pipelines/TenantLocks.cs`:
```csharp
using System.Collections.Concurrent;

namespace ContentAutomatorX.Application.Pipelines;

/// <summary>One pipeline run per tenant at a time (spec: concurrency rules).</summary>
public static class TenantLocks
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();
    public static SemaphoreSlim Get(Guid tenantId) => Locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
}
```

`src/ContentAutomatorX.Application/Pipelines/IngestionPipeline.cs`:
```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Pipelines;

public class IngestionPipeline(IAppDbContext db, IEnumerable<ISourceConnector> connectors)
{
    public async Task<PipelineRun> RunAsync(Guid tenantId, Guid? sourceId = null,
        string trigger = RunTriggers.Manual, CancellationToken ct = default)
    {
        var gate = TenantLocks.Get(tenantId);
        await gate.WaitAsync(ct);
        try { return await RunCoreAsync(tenantId, sourceId, trigger, ct); }
        finally { gate.Release(); }
    }

    private async Task<PipelineRun> RunCoreAsync(Guid tenantId, Guid? sourceId, string trigger, CancellationToken ct)
    {
        var run = new PipelineRun { TenantId = tenantId, Kind = RunKinds.Ingestion, Trigger = trigger };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var log = new List<string>();
        var sources = await db.Sources
            .Where(s => s.TenantId == tenantId && s.IsEnabled && (sourceId == null || s.Id == sourceId))
            .ToListAsync(ct);

        int failed = 0;
        foreach (var source in sources)
        {
            try
            {
                var connector = connectors.FirstOrDefault(c => c.Type == source.Type)
                    ?? throw new InvalidOperationException($"No connector for type '{source.Type}'");
                var fetched = await connector.FetchAsync(source, ct);

                var externalIds = fetched.Select(f => f.ExternalId).ToList();
                var existing = await db.ContentItems
                    .Where(i => i.SourceId == source.Id && externalIds.Contains(i.ExternalId))
                    .Select(i => i.ExternalId)
                    .ToListAsync(ct);

                var fresh = fetched.Where(f => !existing.Contains(f.ExternalId)).ToList();
                foreach (var f in fresh)
                {
                    db.ContentItems.Add(new ContentItem
                    {
                        TenantId = tenantId, SourceId = source.Id, ExternalId = f.ExternalId,
                        Title = f.Title, Url = f.Url, Author = f.Author, Body = f.Body,
                        MetadataJson = f.MetadataJson, PublishedAt = f.PublishedAt
                    });
                }
                source.LastFetchedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                log.Add($"{source.DisplayName}: fetched {fetched.Count}, new {fresh.Count}");
            }
            catch (Exception ex)
            {
                failed++;
                log.Add($"{source.DisplayName}: FAILED - {ex.Message}");
            }
        }

        run.FinishedAt = DateTimeOffset.UtcNow;
        run.Status = failed == 0 ? RunStatus.Succeeded
                   : failed == sources.Count && sources.Count > 0 ? RunStatus.Failed
                   : RunStatus.Partial;
        run.LogJson = JsonSerializer.Serialize(log);
        await db.SaveChangesAsync(ct);
        return run;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter IngestionPipelineTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: ingestion pipeline with dedup, per-source error isolation, run audit"
```

---

### Task 7: Item selector (recipe selection rules)

**Files:**
- Create: `src/ContentAutomatorX.Application/Generation/ItemSelector.cs`
- Create: `tests/ContentAutomatorX.UnitTests/ItemSelectorTests.cs`

**Interfaces:**
- Consumes: `ContentItem`, `SelectionRules` (Task 2)
- Produces: `static List<ContentItem> ItemSelector.Select(IEnumerable<ContentItem> candidates, SelectionRules rules, IReadOnlySet<Guid> usedByRecipe, DateTimeOffset now)`. Semantics: excludes `Ignored` items and ids in `usedByRecipe` (per-recipe usage, from prior drafts of the same recipe — NOT global `Used` status, so one item can feed multiple recipes); applies time window (on `PublishedAt ?? FetchedAt`), min score (parsed from `MetadataJson.score`, missing = 0), include/exclude keywords (case-insensitive, title+body; include = must contain at least one); orders by score desc then published desc; takes `MaxItems`.

- [ ] **Step 1: Write failing tests**

`tests/ContentAutomatorX.UnitTests/ItemSelectorTests.cs`:
```csharp
using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.UnitTests;

public class ItemSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private static ContentItem Item(string title, int score = 0, int ageDays = 1,
        ContentItemStatus status = ContentItemStatus.New, string body = "")
        => new()
        {
            Title = title, ExternalId = title, Body = body, Status = status,
            MetadataJson = $"{{\"score\":{score}}}",
            PublishedAt = Now.AddDays(-ageDays)
        };

    [Fact]
    public void Applies_window_score_keywords_order_and_max()
    {
        var items = new[]
        {
            Item("old high", score: 900, ageDays: 30),
            Item("fresh low", score: 5),
            Item("fresh high", score: 500),
            Item("fresh mid", score: 100),
            Item("ignored", score: 999, status: ContentItemStatus.Ignored),
            Item("excluded word crypto", score: 800)
        };
        var rules = new SelectionRules
        {
            TimeWindowDays = 7, MinScore = 10, MaxItems = 2,
            ExcludeKeywords = ["crypto"]
        };

        var result = ItemSelector.Select(items, rules, new HashSet<Guid>(), Now);

        Assert.Equal(["fresh high", "fresh mid"], result.Select(i => i.Title).ToArray());
    }

    [Fact]
    public void Excludes_items_already_used_by_this_recipe_but_not_globally_used()
    {
        var usedByRecipe = Item("used by recipe", score: 50);
        var usedGlobally = Item("used by other recipe", score: 40, status: ContentItemStatus.Used);
        var items = new[] { usedByRecipe, usedGlobally };

        var result = ItemSelector.Select(items, new SelectionRules(), new HashSet<Guid> { usedByRecipe.Id }, Now);

        Assert.Equal(["used by other recipe"], result.Select(i => i.Title).ToArray());
    }

    [Fact]
    public void Include_keywords_require_a_match()
    {
        var items = new[] { Item("about comfyui nodes"), Item("about something else") };
        var rules = new SelectionRules { IncludeKeywords = ["ComfyUI"] };

        var result = ItemSelector.Select(items, rules, new HashSet<Guid>(), Now);

        Assert.Single(result);
        Assert.Equal("about comfyui nodes", result[0].Title);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter ItemSelectorTests`
Expected: FAIL — `ItemSelector` does not exist.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Application/Generation/ItemSelector.cs`:
```csharp
using System.Text.Json;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Application.Generation;

public static class ItemSelector
{
    public static List<ContentItem> Select(IEnumerable<ContentItem> candidates, SelectionRules rules,
        IReadOnlySet<Guid> usedByRecipe, DateTimeOffset now)
    {
        var query = candidates
            .Where(i => i.Status != ContentItemStatus.Ignored)
            .Where(i => !usedByRecipe.Contains(i.Id));

        if (rules.TimeWindowDays is int days)
        {
            var cutoff = now.AddDays(-days);
            query = query.Where(i => (i.PublishedAt ?? i.FetchedAt) >= cutoff);
        }
        if (rules.MinScore is int min)
            query = query.Where(i => Score(i) >= min);
        if (rules.IncludeKeywords.Length > 0)
            query = query.Where(i => rules.IncludeKeywords.Any(k => Matches(i, k)));
        if (rules.ExcludeKeywords.Length > 0)
            query = query.Where(i => !rules.ExcludeKeywords.Any(k => Matches(i, k)));

        return query
            .OrderByDescending(Score)
            .ThenByDescending(i => i.PublishedAt ?? i.FetchedAt)
            .Take(rules.MaxItems)
            .ToList();
    }

    private static bool Matches(ContentItem i, string keyword) =>
        i.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        i.Body.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static int Score(ContentItem i)
    {
        try
        {
            using var doc = JsonDocument.Parse(i.MetadataJson);
            return doc.RootElement.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
        }
        catch { return 0; }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter ItemSelectorTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: recipe item selector (window, score, keywords, per-recipe used exclusion)"
```

---

### Task 8: Prompt builder and default templates

**Files:**
- Create: `src/ContentAutomatorX.Application/Generation/PromptBuilder.cs`, `DefaultTemplates.cs`
- Create: `tests/ContentAutomatorX.UnitTests/PromptBuilderTests.cs`

**Interfaces:**
- Consumes: `Tenant`, `Recipe`, `ContentItem`, `PromptTemplate` (Task 2)
- Produces: `static string PromptBuilder.Build(string template, Tenant tenant, Recipe recipe, IReadOnlyList<ContentItem> items, string? extraInstructions)`; `static string DefaultTemplates.GetFor(string kind)` (throws on unknown kind) plus `DefaultTemplates.Newsletter/SocialPost/VideoScript` constants. Placeholders replaced: `{voice_profile}`, `{tone_modifiers}`, `{items}`, `{extra_instructions}`.

- [ ] **Step 1: Write failing tests**

`tests/ContentAutomatorX.UnitTests/PromptBuilderTests.cs`:
```csharp
using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.UnitTests;

public class PromptBuilderTests
{
    [Fact]
    public void Replaces_all_placeholders()
    {
        var tenant = new Tenant { Name = "Chan", Slug = "chan", VoiceProfile = "Friendly expert voice." };
        var recipe = new Recipe
        {
            TenantId = tenant.Id, Name = "Weekly", Kind = DraftKinds.Newsletter,
            ToneModifiers = "Slightly humorous.", LengthTarget = "800 words", Language = "English"
        };
        var items = new List<ContentItem>
        {
            new() { ExternalId = "1", Title = "Big News", Url = "https://x/1", Body = "Something happened.", MetadataJson = "{\"score\":42}" }
        };

        var prompt = PromptBuilder.Build(
            "VOICE:{voice_profile}|TONE:{tone_modifiers}|ITEMS:{items}|EXTRA:{extra_instructions}",
            tenant, recipe, items, "Mention the discord.");

        Assert.Contains("Friendly expert voice.", prompt);
        Assert.Contains("Slightly humorous.", prompt);
        Assert.Contains("800 words", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains("Big News", prompt);
        Assert.Contains("https://x/1", prompt);
        Assert.Contains("Mention the discord.", prompt);
        Assert.DoesNotContain("{voice_profile}", prompt);
        Assert.DoesNotContain("{items}", prompt);
    }

    [Fact]
    public void Long_item_bodies_are_truncated()
    {
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "R", Kind = DraftKinds.SocialPost };
        var items = new List<ContentItem>
        {
            new() { ExternalId = "1", Title = "Long", Body = new string('x', 5000) }
        };

        var prompt = PromptBuilder.Build("{items}", tenant, recipe, items, null);

        Assert.True(prompt.Length < 3000, $"prompt was {prompt.Length} chars");
        Assert.Contains("[truncated]", prompt);
    }

    [Fact]
    public void Default_templates_exist_for_all_kinds()
    {
        foreach (var kind in DraftKinds.All)
        {
            var template = DefaultTemplates.GetFor(kind);
            Assert.Contains("{voice_profile}", template);
            Assert.Contains("{items}", template);
            Assert.Contains("{extra_instructions}", template);
        }
        Assert.Throws<ArgumentException>(() => DefaultTemplates.GetFor("Nope"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter PromptBuilderTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Application/Generation/PromptBuilder.cs`:
```csharp
using System.Text;
using System.Text.Json;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Application.Generation;

public static class PromptBuilder
{
    private const int MaxBodyChars = 2000;

    public static string Build(string template, Tenant tenant, Recipe recipe,
        IReadOnlyList<ContentItem> items, string? extraInstructions)
    {
        var tone = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(recipe.ToneModifiers)) tone.AppendLine(recipe.ToneModifiers);
        if (!string.IsNullOrWhiteSpace(recipe.LengthTarget)) tone.AppendLine($"Target length: {recipe.LengthTarget}");
        if (!string.IsNullOrWhiteSpace(recipe.Language)) tone.AppendLine($"Write in: {recipe.Language}");

        return template
            .Replace("{voice_profile}", tenant.VoiceProfile)
            .Replace("{tone_modifiers}", tone.ToString().TrimEnd())
            .Replace("{items}", FormatItems(items))
            .Replace("{extra_instructions}", extraInstructions ?? "(none)");
    }

    private static string FormatItems(IReadOnlyList<ContentItem> items)
    {
        var sb = new StringBuilder();
        for (int n = 0; n < items.Count; n++)
        {
            var i = items[n];
            sb.AppendLine($"--- Item {n + 1} ---");
            sb.AppendLine($"Title: {i.Title}");
            if (i.Url is not null) sb.AppendLine($"URL: {i.Url}");
            if (Score(i) is int s and > 0) sb.AppendLine($"Score: {s}");
            var body = i.Body.Length > MaxBodyChars ? i.Body[..MaxBodyChars] + " [truncated]" : i.Body;
            if (body.Length > 0) sb.AppendLine(body);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static int? Score(ContentItem i)
    {
        try
        {
            using var doc = JsonDocument.Parse(i.MetadataJson);
            return doc.RootElement.TryGetProperty("score", out var s) ? s.GetInt32() : null;
        }
        catch { return null; }
    }
}
```

`src/ContentAutomatorX.Application/Generation/DefaultTemplates.cs`:
```csharp
using ContentAutomatorX.Domain;

namespace ContentAutomatorX.Application.Generation;

public static class DefaultTemplates
{
    public const string Newsletter = """
        You are writing a newsletter issue for a content creator.

        Voice and audience:
        {voice_profile}

        Style directives:
        {tone_modifiers}

        Write a complete newsletter in Markdown based on the source items below.
        Structure: a short personal intro, 3-5 topic sections (each with a heading,
        a summary in the creator's voice, and the source link), and a brief outro
        with a call to action. Do not invent facts not present in the items.

        Source items:
        {items}

        Additional instructions: {extra_instructions}

        Output ONLY the newsletter Markdown, starting with a # title line.
        """;

    public const string SocialPost = """
        You are writing a social media post (e.g. Patreon or Ko-fi update) for a content creator.

        Voice and audience:
        {voice_profile}

        Style directives:
        {tone_modifiers}

        Write ONE engaging post in Markdown based on the source items below.
        Keep it punchy: a hook line, 2-4 short paragraphs or bullets, and a
        closing question or call to action. Include at most 2 links.
        Do not invent facts not present in the items.

        Source items:
        {items}

        Additional instructions: {extra_instructions}

        Output ONLY the post Markdown, starting with a # title line.
        """;

    public const string VideoScript = """
        You are writing a YouTube video script for a content creator.

        Voice and audience:
        {voice_profile}

        Style directives:
        {tone_modifiers}

        Write a complete video script in Markdown based on the source items below.
        Structure with these headings: ## Hook (first 15 seconds), ## Intro,
        ## Section 1..N (one per major topic, with spoken narration text),
        ## Outro, ## CTA. Mark visual/B-roll suggestions as blockquotes.
        Do not invent facts not present in the items.

        Source items:
        {items}

        Additional instructions: {extra_instructions}

        Output ONLY the script Markdown, starting with a # title line.
        """;

    public static string GetFor(string kind) => kind switch
    {
        DraftKinds.Newsletter => Newsletter,
        DraftKinds.SocialPost => SocialPost,
        DraftKinds.VideoScript => VideoScript,
        _ => throw new ArgumentException($"No default template for kind '{kind}'", nameof(kind))
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter PromptBuilderTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: prompt builder and system default templates per draft kind"
```

---

### Task 9: Claude CLI backend

**Files:**
- Create: `src/ContentAutomatorX.Infrastructure/Llm/IProcessRunner.cs`, `ProcessRunner.cs`, `ClaudeCliBackend.cs`
- Create: `tests/ContentAutomatorX.UnitTests/ClaudeCliBackendTests.cs`

**Interfaces:**
- Consumes: `ILlmBackend`, `LlmResult` (Task 2)
- Produces:
  - `IProcessRunner` with `Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin, TimeSpan timeout, CancellationToken ct = default)`; `record ProcessResult(int ExitCode, string StdOut, string StdErr)` (both in `IProcessRunner.cs`)
  - `ProcessRunner : IProcessRunner` (real implementation)
  - `ClaudeCliOptions { string Command = "claude"; string? Model; int TimeoutSeconds = 300; }` (in `ClaudeCliBackend.cs`)
  - `ClaudeCliBackend(IProcessRunner runner, ClaudeCliOptions options) : ILlmBackend`, `Name == "claude-cli"`. Runs `claude -p --output-format json` (plus `--model <x>` when set), prompt piped via stdin, parses the JSON field `result`; throws `InvalidOperationException` after one retry if the CLI fails (non-zero exit, `is_error: true`, or unparseable output).

- [ ] **Step 1: Write failing tests**

`tests/ContentAutomatorX.UnitTests/ClaudeCliBackendTests.cs`:
```csharp
using ContentAutomatorX.Infrastructure.Llm;

namespace ContentAutomatorX.UnitTests;

public class FakeProcessRunner(params ProcessResult[] results) : IProcessRunner
{
    public int Calls { get; private set; }
    public string? LastStdin { get; private set; }
    public string? LastArguments { get; private set; }

    public Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default)
    {
        LastStdin = stdin;
        LastArguments = arguments;
        var result = results[Math.Min(Calls, results.Length - 1)];
        Calls++;
        return Task.FromResult(result);
    }
}

public class ClaudeCliBackendTests
{
    private const string GoodJson = """{"type":"result","result":"# Draft\nHello.","is_error":false}""";

    [Fact]
    public async Task Returns_result_text_and_pipes_prompt_via_stdin()
    {
        var runner = new FakeProcessRunner(new ProcessResult(0, GoodJson, ""));
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions { Model = "claude-sonnet-5" });

        var result = await backend.GenerateAsync("write things");

        Assert.StartsWith("# Draft", result.Text);
        Assert.Equal("write things", runner.LastStdin);
        Assert.Contains("--output-format json", runner.LastArguments);
        Assert.Contains("--model claude-sonnet-5", runner.LastArguments);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task Retries_once_then_succeeds()
    {
        var runner = new FakeProcessRunner(
            new ProcessResult(1, "", "transient failure"),
            new ProcessResult(0, GoodJson, ""));
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions());

        var result = await backend.GenerateAsync("p");

        Assert.StartsWith("# Draft", result.Text);
        Assert.Equal(2, runner.Calls);
    }

    [Fact]
    public async Task Fails_after_two_attempts()
    {
        var runner = new FakeProcessRunner(new ProcessResult(1, "", "dead"));
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.GenerateAsync("p"));
        Assert.Equal(2, runner.Calls);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter ClaudeCliBackendTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Infrastructure/Llm/IProcessRunner.cs`:
```csharp
namespace ContentAutomatorX.Infrastructure.Llm;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default);
}
```

`src/ContentAutomatorX.Infrastructure/Llm/ProcessRunner.cs`:
```csharp
using System.Diagnostics;

namespace ContentAutomatorX.Infrastructure.Llm;

public class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        process.Start();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return new ProcessResult(-1, "", $"timed out after {timeout.TotalSeconds}s");
        }

        return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
    }
}
```

`src/ContentAutomatorX.Infrastructure/Llm/ClaudeCliBackend.cs`:
```csharp
using System.Text.Json;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Llm;

public class ClaudeCliOptions
{
    /// <summary>Executable name/path. On Windows, if plain "claude" fails to start,
    /// set the full path (e.g. %LOCALAPPDATA%\...\claude.exe) in appsettings Claude:Command.</summary>
    public string Command { get; set; } = "claude";
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
}

public class ClaudeCliBackend(IProcessRunner runner, ClaudeCliOptions options) : ILlmBackend
{
    public string Name => "claude-cli";

    public async Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var args = "-p --output-format json";
        if (!string.IsNullOrWhiteSpace(options.Model)) args += $" --model {options.Model}";
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        string lastError = "";
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var result = await runner.RunAsync(options.Command, args, prompt, timeout, ct);
            if (result.ExitCode == 0 && TryParse(result.StdOut, out var text))
                return new LlmResult(text, options.Model ?? "claude-default");
            lastError = $"exit={result.ExitCode} stderr={result.StdErr} stdout={Truncate(result.StdOut)}";
        }
        throw new InvalidOperationException($"claude CLI failed after 2 attempts: {lastError}");
    }

    private static bool TryParse(string stdout, out string text)
    {
        text = "";
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("is_error", out var e) && e.GetBoolean()) return false;
            if (!root.TryGetProperty("result", out var r)) return false;
            text = r.GetString() ?? "";
            return text.Length > 0;
        }
        catch (JsonException) { return false; }
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter ClaudeCliBackendTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: Claude CLI LLM backend with timeout and single retry"
```

---

### Task 10: File-share draft delivery

**Files:**
- Create: `src/ContentAutomatorX.Infrastructure/Delivery/FileShareDraftDelivery.cs`
- Create: `tests/ContentAutomatorX.IntegrationTests/FileShareDraftDeliveryTests.cs`

**Interfaces:**
- Consumes: `IDraftDelivery`, `Tenant`, `Draft`, `RecipeOutput` (Task 2)
- Produces: `FileShareDraftDelivery : IDraftDelivery` (parameterless ctor). Writes `<OutputFolderPath>/<Subfolder?>/<filename>` where filename comes from `FilenamePattern ?? "{date}-{kind}-{slug}.md"` (tokens `{date}` = `draft.CreatedAt:yyyy-MM-dd`, `{kind}` = lowercased kind, `{slug}` = slugified title, max 60 chars). YAML front-matter + body. Temp-file-then-move. On name collision appends `-2`, `-3`, … Returns absolute path.

- [ ] **Step 1: Write failing tests**

`tests/ContentAutomatorX.IntegrationTests/FileShareDraftDeliveryTests.cs`:
```csharp
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Delivery;

namespace ContentAutomatorX.IntegrationTests;

public class FileShareDraftDeliveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"contentx-out-{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private (Tenant, Draft) Make() =>
        (new Tenant { Name = "T", Slug = "my-channel", OutputFolderPath = _dir },
         new Draft
         {
             TenantId = Guid.NewGuid(), RecipeId = Guid.NewGuid(), Kind = DraftKinds.Newsletter,
             Title = "Big News: AI Everywhere!", Body = "# Big News\nContent here.",
             ModelUsed = "claude-cli", CreatedAt = new DateTimeOffset(2026, 7, 12, 8, 0, 0, TimeSpan.Zero),
             SourceItemIdsJson = "[\"11111111-1111-1111-1111-111111111111\"]"
         });

    [Fact]
    public async Task Writes_markdown_with_front_matter_into_subfolder()
    {
        var (tenant, draft) = Make();
        var delivery = new FileShareDraftDelivery();

        var path = await delivery.DeliverAsync(tenant, new RecipeOutput { Subfolder = "newsletter" }, draft);

        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(_dir, "newsletter", "2026-07-12-newsletter-big-news-ai-everywhere.md"), path);
        var content = await File.ReadAllTextAsync(path);
        Assert.StartsWith("---", content);
        Assert.Contains("tenant: my-channel", content);
        Assert.Contains("kind: Newsletter", content);
        Assert.Contains("model: claude-cli", content);
        Assert.Contains("# Big News", content);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "newsletter"), "*.tmp"));
    }

    [Fact]
    public async Task Collision_appends_counter()
    {
        var (tenant, draft) = Make();
        var delivery = new FileShareDraftDelivery();

        var p1 = await delivery.DeliverAsync(tenant, new RecipeOutput(), draft);
        var p2 = await delivery.DeliverAsync(tenant, new RecipeOutput(), draft);

        Assert.NotEqual(p1, p2);
        Assert.EndsWith("-2.md", p2);
    }

    [Fact]
    public async Task Unreachable_output_path_throws_so_pipeline_can_record_failure()
    {
        var (tenant, draft) = Make();
        tenant.OutputFolderPath = @"Q:\no-such-drive\x";
        var delivery = new FileShareDraftDelivery();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            delivery.DeliverAsync(tenant, new RecipeOutput(), draft));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter FileShareDraftDeliveryTests`
Expected: FAIL — `FileShareDraftDelivery` does not exist.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Infrastructure/Delivery/FileShareDraftDelivery.cs`:
```csharp
using System.Text;
using System.Text.Json;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Delivery;

public class FileShareDraftDelivery : IDraftDelivery
{
    public async Task<string> DeliverAsync(Tenant tenant, RecipeOutput output, Draft draft, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant.OutputFolderPath))
            throw new InvalidOperationException($"Tenant '{tenant.Slug}' has no OutputFolderPath configured");

        var folder = string.IsNullOrWhiteSpace(output.Subfolder)
            ? tenant.OutputFolderPath
            : Path.Combine(tenant.OutputFolderPath, output.Subfolder);
        Directory.CreateDirectory(folder);

        var pattern = string.IsNullOrWhiteSpace(output.FilenamePattern) ? "{date}-{kind}-{slug}.md" : output.FilenamePattern;
        var baseName = pattern
            .Replace("{date}", draft.CreatedAt.ToString("yyyy-MM-dd"))
            .Replace("{kind}", draft.Kind.ToLowerInvariant())
            .Replace("{slug}", Slugify(draft.Title));

        var path = Path.Combine(folder, baseName);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder,
                Path.GetFileNameWithoutExtension(baseName) + $"-{n}" + Path.GetExtension(baseName));

        var content = BuildContent(tenant, draft);
        var tmp = path + ".tmp";
        await File.WriteAllTextAsync(tmp, content, new UTF8Encoding(false), ct);
        File.Move(tmp, path);
        return Path.GetFullPath(path);
    }

    private static string BuildContent(Tenant tenant, Draft draft)
    {
        var itemIds = JsonSerializer.Deserialize<string[]>(draft.SourceItemIdsJson) ?? [];
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"tenant: {tenant.Slug}");
        sb.AppendLine($"recipe: {draft.RecipeId}");
        sb.AppendLine($"kind: {draft.Kind}");
        sb.AppendLine($"created: {draft.CreatedAt:O}");
        sb.AppendLine($"model: {draft.ModelUsed}");
        sb.AppendLine($"source_items: [{string.Join(", ", itemIds)}]");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(draft.Body);
        return sb.ToString();
    }

    private static string Slugify(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "draft" : slug[..Math.Min(slug.Length, 60)].Trim('-');
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter FileShareDraftDeliveryTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: file-share draft delivery (front-matter, temp-then-move, collision-safe)"
```

---

### Task 11: Generation pipeline

**Files:**
- Create: `src/ContentAutomatorX.Application/Pipelines/GenerationPipeline.cs`
- Create: `tests/ContentAutomatorX.IntegrationTests/GenerationPipelineTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext` (Task 3), `ILlmBackend`/`IDraftDelivery` (Task 2), `ItemSelector` (Task 7), `PromptBuilder`/`DefaultTemplates` (Task 8), `TenantLocks` (Task 6)
- Produces: `GenerationPipeline(IAppDbContext db, ILlmBackend llm, IDraftDelivery delivery)` with `Task<(PipelineRun Run, Draft? Draft)> RunAsync(Guid recipeId, IReadOnlyList<Guid>? itemIds = null, string? extraInstructions = null, string trigger = RunTriggers.Manual, CancellationToken ct = default)`. Behavior: resolves recipe→tenant→template (recipe's `PromptTemplateId`, falling back to the system default row `TenantId == null && Kind == recipe.Kind`); candidates limited to `SourceIdsJson` (empty = all tenant sources); per-recipe used-ids from prior drafts' `SourceItemIdsJson`; no items → `Failed` run, null draft; LLM error → `Failed` run, null draft; delivery error → `Partial` run, draft stays `Generated` with `FilePath = null`; success → draft `Delivered`, items flipped to `Used`, `Recipe.LastRunAt` set.

- [ ] **Step 1: Write failing tests**

`tests/ContentAutomatorX.IntegrationTests/GenerationPipelineTests.cs`:
```csharp
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Delivery;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class FakeLlm(string reply = "# Generated Title\nGenerated body.") : ILlmBackend
{
    public string Name => "fake";
    public string? LastPrompt { get; private set; }
    public Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        LastPrompt = prompt;
        return Task.FromResult(new LlmResult(reply, "fake-model"));
    }
}

public class FailingLlm : ILlmBackend
{
    public string Name => "failing";
    public Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default) =>
        throw new InvalidOperationException("llm down");
}

public class GenerationPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"contentx-gen-{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private (Tenant tenant, Source source, Recipe recipe) Seed(TestDb test)
    {
        var tenant = new Tenant { Name = "T", Slug = "t", VoiceProfile = "Casual.", OutputFolderPath = _dir };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        var template = new PromptTemplate { TenantId = null, Kind = DraftKinds.Newsletter,
            Template = "V:{voice_profile} T:{tone_modifiers} I:{items} E:{extra_instructions}" };
        var recipe = new Recipe
        {
            TenantId = tenant.Id, Name = "Weekly", Kind = DraftKinds.Newsletter,
            PromptTemplateId = template.Id, SelectionJson = """{"maxItems":5}"""
        };
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        test.Db.PromptTemplates.Add(template);
        test.Db.Recipes.Add(recipe);
        for (int n = 1; n <= 3; n++)
            test.Db.ContentItems.Add(new ContentItem
            {
                TenantId = tenant.Id, SourceId = source.Id, ExternalId = $"e{n}",
                Title = $"Item {n}", Body = "body", MetadataJson = $"{{\"score\":{n * 10}}}"
            });
        test.Db.SaveChanges();
        return (tenant, source, recipe);
    }

    [Fact]
    public async Task Happy_path_delivers_file_and_marks_items_used()
    {
        using var test = TestDb.Create();
        var (tenant, _, recipe) = Seed(test);
        var llm = new FakeLlm();
        var pipeline = new GenerationPipeline(test.Db, llm, new FileShareDraftDelivery());

        var (run, draft) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.NotNull(draft);
        Assert.Equal(DraftStatus.Delivered, draft.Status);
        Assert.True(File.Exists(draft.FilePath));
        Assert.Contains("Item 3", llm.LastPrompt);            // items reached the prompt
        Assert.Contains("Casual.", llm.LastPrompt);            // voice profile reached the prompt
        Assert.Equal("fake-model", draft.ModelUsed);
        Assert.Equal(3, await test.Db.ContentItems.CountAsync(i => i.Status == ContentItemStatus.Used));
        Assert.NotNull((await test.Db.Recipes.SingleAsync()).LastRunAt);
    }

    [Fact]
    public async Task Second_run_finds_no_new_items_and_fails_cleanly()
    {
        using var test = TestDb.Create();
        var (_, _, recipe) = Seed(test);
        var pipeline = new GenerationPipeline(test.Db, new FakeLlm(), new FileShareDraftDelivery());

        await pipeline.RunAsync(recipe.Id);
        var (run2, draft2) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Failed, run2.Status);
        Assert.Null(draft2);
        Assert.Contains("no items", run2.LogJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Llm_failure_yields_failed_run_and_no_draft()
    {
        using var test = TestDb.Create();
        var (_, _, recipe) = Seed(test);
        var pipeline = new GenerationPipeline(test.Db, new FailingLlm(), new FileShareDraftDelivery());

        var (run, draft) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Null(draft);
        Assert.Equal(0, await test.Db.Drafts.CountAsync());
        Assert.Equal(0, await test.Db.ContentItems.CountAsync(i => i.Status == ContentItemStatus.Used));
    }

    [Fact]
    public async Task Delivery_failure_keeps_draft_generated_and_run_partial()
    {
        using var test = TestDb.Create();
        var (tenant, _, recipe) = Seed(test);
        var fresh = test.NewContext();
        var t = fresh.Tenants.Single();
        t.OutputFolderPath = "";     // unconfigured folder → delivery throws
        fresh.SaveChanges();
        var pipeline = new GenerationPipeline(test.NewContext(), new FakeLlm(), new FileShareDraftDelivery());

        var (run, draft) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Partial, run.Status);
        Assert.NotNull(draft);
        Assert.Equal(DraftStatus.Generated, draft.Status);
        Assert.Null(draft.FilePath);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter GenerationPipelineTests`
Expected: FAIL — `GenerationPipeline` does not exist.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Application/Pipelines/GenerationPipeline.cs`:
```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Pipelines;

public class GenerationPipeline(IAppDbContext db, ILlmBackend llm, IDraftDelivery delivery)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<(PipelineRun Run, Draft? Draft)> RunAsync(Guid recipeId, IReadOnlyList<Guid>? itemIds = null,
        string? extraInstructions = null, string trigger = RunTriggers.Manual, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId, ct);
        var gate = TenantLocks.Get(recipe.TenantId);
        await gate.WaitAsync(ct);
        try { return await RunCoreAsync(recipe, itemIds, extraInstructions, trigger, ct); }
        finally { gate.Release(); }
    }

    private async Task<(PipelineRun, Draft?)> RunCoreAsync(Recipe recipe, IReadOnlyList<Guid>? itemIds,
        string? extraInstructions, string trigger, CancellationToken ct)
    {
        var run = new PipelineRun { TenantId = recipe.TenantId, Kind = RunKinds.Generation, Trigger = trigger };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);
        var log = new List<string> { $"recipe: {recipe.Name} ({recipe.Kind})" };

        try
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == recipe.TenantId, ct);
            var template = await db.PromptTemplates.FirstOrDefaultAsync(p => p.Id == recipe.PromptTemplateId, ct)
                ?? await db.PromptTemplates.FirstOrDefaultAsync(p => p.TenantId == null && p.Kind == recipe.Kind, ct);
            var templateText = template?.Template ?? DefaultTemplates.GetFor(recipe.Kind);

            var items = await SelectItemsAsync(recipe, itemIds, ct);
            if (items.Count == 0)
                return (await Finish(run, RunStatus.Failed, log, "no items matched the recipe selection", ct), null);
            log.Add($"selected {items.Count} items");

            var prompt = PromptBuilder.Build(templateText, tenant, recipe, items, extraInstructions);
            log.Add($"prompt: {prompt.Length} chars");

            LlmResult result;
            try { result = await llm.GenerateAsync(prompt, ct); }
            catch (Exception ex)
            {
                return (await Finish(run, RunStatus.Failed, log, $"LLM failed: {ex.Message}", ct), null);
            }

            var output = JsonSerializer.Deserialize<RecipeOutput>(recipe.OutputJson, JsonOpts) ?? new RecipeOutput();
            var draft = new Draft
            {
                TenantId = recipe.TenantId, RecipeId = recipe.Id, Kind = recipe.Kind,
                Title = ExtractTitle(result.Text) ?? $"{recipe.Name} — {DateTimeOffset.UtcNow:yyyy-MM-dd}",
                Body = result.Text, ModelUsed = result.Model,
                TargetPlatform = output.TargetPlatform,
                SourceItemIdsJson = JsonSerializer.Serialize(items.Select(i => i.Id.ToString()))
            };
            db.Drafts.Add(draft);
            foreach (var item in items) item.Status = ContentItemStatus.Used;
            recipe.LastRunAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            try
            {
                draft.FilePath = await delivery.DeliverAsync(tenant, output, draft, ct);
                draft.Status = DraftStatus.Delivered;
                log.Add($"delivered: {draft.FilePath}");
                return (await Finish(run, RunStatus.Succeeded, log, null, ct), draft);
            }
            catch (Exception ex)
            {
                return (await Finish(run, RunStatus.Partial, log, $"delivery failed: {ex.Message}", ct), draft);
            }
        }
        catch (Exception ex)
        {
            return (await Finish(run, RunStatus.Failed, log, $"unexpected: {ex.Message}", ct), null);
        }
    }

    private async Task<List<ContentItem>> SelectItemsAsync(Recipe recipe, IReadOnlyList<Guid>? itemIds, CancellationToken ct)
    {
        if (itemIds is { Count: > 0 })
            return await db.ContentItems
                .Where(i => i.TenantId == recipe.TenantId && itemIds.Contains(i.Id))
                .ToListAsync(ct);

        var sourceIds = JsonSerializer.Deserialize<Guid[]>(recipe.SourceIdsJson) ?? [];
        var candidatesQuery = db.ContentItems.Where(i => i.TenantId == recipe.TenantId);
        if (sourceIds.Length > 0)
            candidatesQuery = candidatesQuery.Where(i => sourceIds.Contains(i.SourceId));
        var candidates = await candidatesQuery.ToListAsync(ct);

        var priorDrafts = await db.Drafts
            .Where(d => d.RecipeId == recipe.Id)
            .Select(d => d.SourceItemIdsJson)
            .ToListAsync(ct);
        var used = priorDrafts
            .SelectMany(j => JsonSerializer.Deserialize<string[]>(j) ?? [])
            .Select(Guid.Parse)
            .ToHashSet();

        var rules = JsonSerializer.Deserialize<SelectionRules>(recipe.SelectionJson, JsonOpts) ?? new SelectionRules();
        return ItemSelector.Select(candidates, rules, used, DateTimeOffset.UtcNow);
    }

    private static string? ExtractTitle(string markdown)
    {
        var firstLine = markdown.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith('#'));
        return firstLine?.TrimStart('#', ' ').Trim() is { Length: > 0 } t ? t : null;
    }

    private async Task<PipelineRun> Finish(PipelineRun run, RunStatus status, List<string> log, string? message, CancellationToken ct)
    {
        if (message is not null) log.Add(message);
        run.Status = status;
        run.FinishedAt = DateTimeOffset.UtcNow;
        run.LogJson = JsonSerializer.Serialize(log);
        await db.SaveChangesAsync(ct);
        return run;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter GenerationPipelineTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: recipe-driven generation pipeline (select, prompt, LLM, deliver, audit)"
```

---

### Task 12: Cron due-check helper

**Files:**
- Create: `src/ContentAutomatorX.Application/Scheduling/CronDue.cs`
- Create: `tests/ContentAutomatorX.UnitTests/CronDueTests.cs`

**Interfaces:**
- Consumes: Cronos package (Task 1)
- Produces: `static bool CronDue.IsDue(string cron, DateTimeOffset? lastRun, DateTimeOffset now)`. Semantics: `lastRun == null` → `true` (first-ever check runs immediately); invalid cron → `false` (never crashes the scheduler); otherwise true iff the next occurrence strictly after `lastRun` is `<= now`. Used by Task 14's `SchedulerService` for both `Source.ScheduleCron`/`LastFetchedAt` and `Recipe.ScheduleCron`/`LastRunAt`.

- [ ] **Step 1: Write failing tests**

`tests/ContentAutomatorX.UnitTests/CronDueTests.cs`:
```csharp
using ContentAutomatorX.Application.Scheduling;

namespace ContentAutomatorX.UnitTests;

public class CronDueTests
{
    private static readonly DateTimeOffset MondayNoon = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Null_last_run_is_due_immediately() =>
        Assert.True(CronDue.IsDue("0 8 * * MON", null, MondayNoon));

    [Fact]
    public void Due_when_occurrence_passed_since_last_run() =>
        // last ran Sunday; Monday 08:00 occurrence has passed by Monday noon
        Assert.True(CronDue.IsDue("0 8 * * MON", MondayNoon.AddDays(-1), MondayNoon));

    [Fact]
    public void Not_due_when_already_ran_after_occurrence() =>
        // last ran Monday 09:00; next occurrence is next Monday
        Assert.False(CronDue.IsDue("0 8 * * MON", MondayNoon.AddHours(-3), MondayNoon));

    [Fact]
    public void Invalid_cron_is_never_due() =>
        Assert.False(CronDue.IsDue("not a cron", MondayNoon.AddDays(-1), MondayNoon));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter CronDueTests`
Expected: FAIL — `CronDue` does not exist.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Application/Scheduling/CronDue.cs`:
```csharp
using Cronos;

namespace ContentAutomatorX.Application.Scheduling;

public static class CronDue
{
    public static bool IsDue(string cron, DateTimeOffset? lastRun, DateTimeOffset now)
    {
        if (lastRun is null) return true;
        try
        {
            var expression = CronExpression.Parse(cron);
            var next = expression.GetNextOccurrence(lastRun.Value, TimeZoneInfo.Utc);
            return next is not null && next <= now;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter CronDueTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: cron due-check helper for scheduler"
```

---

### Task 13: Application services

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/TenantService.cs`, `SourceService.cs`, `RecipeService.cs`, `ContentService.cs`, `DraftService.cs`, `RunService.cs`
- Create: `tests/ContentAutomatorX.IntegrationTests/ServiceTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext` (Task 3), `DefaultTemplates` (Task 8), `IDraftDelivery` (Task 2)
- Produces (exact signatures — UI Tasks 16-19 and MCP Task 15 call these):
  - `TenantService(IAppDbContext)`: `Task<List<Tenant>> ListAsync()`, `Task<Tenant?> GetAsync(Guid id)`, `Task<Tenant> CreateAsync(Tenant t)`, `Task UpdateAsync()`, `Task DeleteAsync(Guid id)`
  - `SourceService(IAppDbContext)`: `Task<List<Source>> ListAsync(Guid tenantId)`, `Task<Source> CreateAsync(Source s)`, `Task UpdateAsync()`, `Task DeleteAsync(Guid id)`
  - `RecipeService(IAppDbContext)`: `Task<List<Recipe>> ListAsync(Guid tenantId)`, `Task<Recipe?> GetAsync(Guid id)`, `Task<Recipe> CreateAsync(Recipe r)` (when `r.PromptTemplateId == Guid.Empty`, clones the system default template for `r.Kind` into a tenant-owned `PromptTemplate` and assigns it), `Task<PromptTemplate?> GetTemplateAsync(Guid id)`, `Task UpdateAsync()`, `Task DeleteAsync(Guid id)`
  - `ContentService(IAppDbContext)`: `Task<List<ContentItem>> ListAsync(Guid tenantId, ContentItemStatus? status = null, DateTimeOffset? since = null)`, `Task MarkAsync(Guid itemId, ContentItemStatus status)`
  - `DraftService(IAppDbContext, IDraftDelivery)`: `Task<List<Draft>> ListAsync(Guid tenantId, string? kind = null, DraftStatus? status = null)`, `Task<Draft?> GetAsync(Guid id)`, `Task<Draft> RetryDeliveryAsync(Guid draftId)`
  - `RunService(IAppDbContext)`: `Task<List<PipelineRun>> ListAsync(Guid tenantId, int limit = 50)`
  - `UpdateAsync()` on these services is just `SaveChangesAsync` over tracked entities (Blazor edits tracked instances within a scope).

- [ ] **Step 1: Write failing tests**

`tests/ContentAutomatorX.IntegrationTests/ServiceTests.cs`:
```csharp
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter ServiceTests`
Expected: FAIL — services do not exist.

- [ ] **Step 3: Implement the six services**

`src/ContentAutomatorX.Application/Services/TenantService.cs`:
```csharp
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class TenantService(IAppDbContext db)
{
    public Task<List<Tenant>> ListAsync() => db.Tenants.OrderBy(t => t.Name).ToListAsync();
    public Task<Tenant?> GetAsync(Guid id) => db.Tenants.FirstOrDefaultAsync(t => t.Id == id);

    public async Task<Tenant> CreateAsync(Tenant tenant)
    {
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    public Task UpdateAsync() => db.SaveChangesAsync();

    public async Task DeleteAsync(Guid id)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id);
        if (tenant is null) return;
        db.Tenants.Remove(tenant);
        await db.SaveChangesAsync();
    }
}
```

`src/ContentAutomatorX.Application/Services/SourceService.cs`:
```csharp
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class SourceService(IAppDbContext db)
{
    public Task<List<Source>> ListAsync(Guid tenantId) =>
        db.Sources.Where(s => s.TenantId == tenantId).OrderBy(s => s.DisplayName).ToListAsync();

    public async Task<Source> CreateAsync(Source source)
    {
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    public Task UpdateAsync() => db.SaveChangesAsync();

    public async Task DeleteAsync(Guid id)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == id);
        if (source is null) return;
        db.Sources.Remove(source);
        await db.SaveChangesAsync();
    }
}
```

`src/ContentAutomatorX.Application/Services/RecipeService.cs`:
```csharp
using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class RecipeService(IAppDbContext db)
{
    public Task<List<Recipe>> ListAsync(Guid tenantId) =>
        db.Recipes.Where(r => r.TenantId == tenantId).OrderBy(r => r.Name).ToListAsync();

    public Task<Recipe?> GetAsync(Guid id) => db.Recipes.FirstOrDefaultAsync(r => r.Id == id);

    public Task<PromptTemplate?> GetTemplateAsync(Guid id) =>
        db.PromptTemplates.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Recipe> CreateAsync(Recipe recipe)
    {
        if (recipe.PromptTemplateId == Guid.Empty)
        {
            var systemDefault = await db.PromptTemplates
                .FirstOrDefaultAsync(p => p.TenantId == null && p.Kind == recipe.Kind);
            var clone = new PromptTemplate
            {
                TenantId = recipe.TenantId,
                Kind = recipe.Kind,
                Template = systemDefault?.Template ?? DefaultTemplates.GetFor(recipe.Kind)
            };
            db.PromptTemplates.Add(clone);
            recipe.PromptTemplateId = clone.Id;
        }
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();
        return recipe;
    }

    public Task UpdateAsync() => db.SaveChangesAsync();

    public async Task DeleteAsync(Guid id)
    {
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == id);
        if (recipe is null) return;
        db.Recipes.Remove(recipe);
        await db.SaveChangesAsync();
    }
}
```

`src/ContentAutomatorX.Application/Services/ContentService.cs`:
```csharp
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class ContentService(IAppDbContext db)
{
    public Task<List<ContentItem>> ListAsync(Guid tenantId, ContentItemStatus? status = null, DateTimeOffset? since = null)
    {
        var query = db.ContentItems.Where(i => i.TenantId == tenantId);
        if (status is not null) query = query.Where(i => i.Status == status);
        if (since is not null) query = query.Where(i => i.FetchedAt >= since);
        return query.OrderByDescending(i => i.PublishedAt ?? i.FetchedAt).ToListAsync();
    }

    public async Task MarkAsync(Guid itemId, ContentItemStatus status)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Content item {itemId} not found");
        item.Status = status;
        await db.SaveChangesAsync();
    }
}
```

`src/ContentAutomatorX.Application/Services/DraftService.cs`:
```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class DraftService(IAppDbContext db, IDraftDelivery delivery)
{
    public Task<List<Draft>> ListAsync(Guid tenantId, string? kind = null, DraftStatus? status = null)
    {
        var query = db.Drafts.Where(d => d.TenantId == tenantId);
        if (kind is not null) query = query.Where(d => d.Kind == kind);
        if (status is not null) query = query.Where(d => d.Status == status);
        return query.OrderByDescending(d => d.CreatedAt).ToListAsync();
    }

    public Task<Draft?> GetAsync(Guid id) => db.Drafts.FirstOrDefaultAsync(d => d.Id == id);

    public async Task<Draft> RetryDeliveryAsync(Guid draftId)
    {
        var draft = await db.Drafts.FirstAsync(d => d.Id == draftId);
        var tenant = await db.Tenants.FirstAsync(t => t.Id == draft.TenantId);
        var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == draft.RecipeId);
        var output = recipe is null ? new RecipeOutput()
            : JsonSerializer.Deserialize<RecipeOutput>(recipe.OutputJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new RecipeOutput();

        draft.FilePath = await delivery.DeliverAsync(tenant, output, draft);
        draft.Status = DraftStatus.Delivered;
        await db.SaveChangesAsync();
        return draft;
    }
}
```

`src/ContentAutomatorX.Application/Services/RunService.cs`:
```csharp
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class RunService(IAppDbContext db)
{
    public Task<List<PipelineRun>> ListAsync(Guid tenantId, int limit = 50) =>
        db.PipelineRuns.Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.StartedAt).Take(limit).ToListAsync();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter ServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: application services (tenants, sources, recipes, content, drafts, runs)"
```

---

### Task 14: Web host wiring — Program.cs, config, scheduler, seeding

**Files:**
- Modify: `src/ContentAutomatorX.Web/Program.cs` (replace template content), `src/ContentAutomatorX.Web/appsettings.json`
- Create: `src/ContentAutomatorX.Web/Jobs/SchedulerService.cs`
- Modify: `src/ContentAutomatorX.Web/Components/App.razor` (MudBlazor assets)

**Interfaces:**
- Consumes: everything from Tasks 3-13
- Produces: a booting host at `http://localhost:5090` with: migrated DB + seeded system default `PromptTemplate` rows (one per `DraftKinds.All` where missing), DI registrations used by all UI/MCP tasks, `SchedulerService` ticking every 60s. Config keys: `Database:Path`, `Claude:Command`, `Claude:Model`, `Claude:TimeoutSeconds`.

- [ ] **Step 1: Add resilience package**

```bash
dotnet add src/ContentAutomatorX.Web package Microsoft.Extensions.Http.Resilience
```

- [ ] **Step 2: Write appsettings.json**

`src/ContentAutomatorX.Web/appsettings.json` (replace):
```json
{
  "Urls": "http://localhost:5090",
  "Database": { "Path": "" },
  "Claude": { "Command": "claude", "Model": "", "TimeoutSeconds": 300 },
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*"
}
```

- [ ] **Step 3: Write Program.cs**

`src/ContentAutomatorX.Web/Program.cs` (replace):
```csharp
using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Delivery;
using ContentAutomatorX.Infrastructure.Llm;
using ContentAutomatorX.Infrastructure.Persistence;
using ContentAutomatorX.Infrastructure.Sources;
using ContentAutomatorX.Web.Components;
using ContentAutomatorX.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/contentx-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14));

// --- persistence ---
var dbPath = builder.Configuration["Database:Path"];
if (string.IsNullOrWhiteSpace(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "contentx.db");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

// --- source connectors (HTTP + retry/backoff) ---
builder.Services.AddHttpClient<RssConnector>().AddStandardResilienceHandler();
builder.Services.AddHttpClient<RedditConnector>().AddStandardResilienceHandler();
builder.Services.AddTransient<ISourceConnector>(sp => sp.GetRequiredService<RssConnector>());
builder.Services.AddTransient<ISourceConnector>(sp => sp.GetRequiredService<RedditConnector>());

// --- LLM backend ---
var claudeOptions = new ClaudeCliOptions();
builder.Configuration.GetSection("Claude").Bind(claudeOptions);
if (string.IsNullOrWhiteSpace(claudeOptions.Model)) claudeOptions.Model = null;
builder.Services.AddSingleton(claudeOptions);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<ILlmBackend, ClaudeCliBackend>();

// --- delivery, pipelines, services ---
builder.Services.AddSingleton<IDraftDelivery, FileShareDraftDelivery>();
builder.Services.AddScoped<IngestionPipeline>();
builder.Services.AddScoped<GenerationPipeline>();
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<SourceService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<ContentService>();
builder.Services.AddScoped<DraftService>();
builder.Services.AddScoped<RunService>();

// --- scheduler ---
builder.Services.AddHostedService<SchedulerService>();

// --- UI + MCP ---
builder.Services.AddMudServices();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

var app = builder.Build();

// migrate + seed system default templates
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    foreach (var kind in DraftKinds.All)
        if (!db.PromptTemplates.Any(p => p.TenantId == null && p.Kind == kind))
            db.PromptTemplates.Add(new PromptTemplate { TenantId = null, Kind = kind, Template = DefaultTemplates.GetFor(kind) });
    db.SaveChanges();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapMcp("/mcp");

app.Run();
```

- [ ] **Step 4: Write SchedulerService**

`src/ContentAutomatorX.Web/Jobs/SchedulerService.cs`:
```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Scheduling;
using ContentAutomatorX.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Web.Jobs;

public class SchedulerService(IServiceScopeFactory scopeFactory, ILogger<SchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        do
        {
            try { await TickAsync(ct); }
            catch (Exception ex) { logger.LogError(ex, "scheduler tick failed"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // due sources → ingestion
        List<(Guid TenantId, Guid SourceId)> dueSources;
        List<Guid> dueRecipes;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            dueSources = (await db.Sources.AsNoTracking()
                    .Where(s => s.IsEnabled && s.ScheduleCron != null)
                    .ToListAsync(ct))
                .Where(s => CronDue.IsDue(s.ScheduleCron!, s.LastFetchedAt, now))
                .Select(s => (s.TenantId, s.Id))
                .ToList();
            dueRecipes = (await db.Recipes.AsNoTracking()
                    .Where(r => r.IsEnabled && r.ScheduleCron != null)
                    .ToListAsync(ct))
                .Where(r => CronDue.IsDue(r.ScheduleCron!, r.LastRunAt, now))
                .Select(r => r.Id)
                .ToList();
        }

        foreach (var (tenantId, sourceId) in dueSources)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();
                await ingestion.RunAsync(tenantId, sourceId, RunTriggers.Scheduled, ct);
            }
            catch (Exception ex) { logger.LogError(ex, "scheduled ingestion failed for source {SourceId}", sourceId); }
        }

        foreach (var recipeId in dueRecipes)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
                var recipe = await db.Recipes.AsNoTracking().SingleAsync(r => r.Id == recipeId, ct);

                // full auto: ingest the recipe's sources first, then generate
                var sourceIds = JsonSerializer.Deserialize<Guid[]>(recipe.SourceIdsJson) ?? [];
                var ingestion = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();
                if (sourceIds.Length == 0)
                    await ingestion.RunAsync(recipe.TenantId, null, RunTriggers.Scheduled, ct);
                else
                    foreach (var sourceId in sourceIds)
                        await ingestion.RunAsync(recipe.TenantId, sourceId, RunTriggers.Scheduled, ct);

                var generation = scope.ServiceProvider.GetRequiredService<GenerationPipeline>();
                await generation.RunAsync(recipeId, trigger: RunTriggers.Scheduled, ct: ct);
            }
            catch (Exception ex) { logger.LogError(ex, "scheduled recipe run failed for {RecipeId}", recipeId); }
        }
    }
}
```

- [ ] **Step 5: Add MudBlazor assets to App.razor**

In `src/ContentAutomatorX.Web/Components/App.razor`, inside `<head>` add:
```html
<link href="https://fonts.googleapis.com/css?family=Roboto:300,400,500,700&display=swap" rel="stylesheet" />
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
```
and before `</body>` add:
```html
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

- [ ] **Step 6: Verify the host boots**

Run: `dotnet run --project src/ContentAutomatorX.Web --no-launch-profile` (stop with Ctrl+C after checking)
Expected: console shows `Now listening on: http://localhost:5090`, no exceptions; `data/contentx.db` file exists afterwards. Also verify all tests still pass: `dotnet test`.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: web host wiring - DI, Serilog, migrations+template seeding, scheduler"
```

---

### Task 15: Exposed MCP server tools

**Files:**
- Create: `src/ContentAutomatorX.Web/Mcp/ContentXTools.cs`
- Create: `tests/ContentAutomatorX.IntegrationTests/McpToolsTests.cs`

**Interfaces:**
- Consumes: services (Task 13), pipelines (Tasks 6, 11)
- Produces: MCP tools (names below) served at `http://localhost:5090/mcp`. `tests/ContentAutomatorX.IntegrationTests` needs a project reference to Web:
```bash
dotnet add tests/ContentAutomatorX.IntegrationTests reference src/ContentAutomatorX.Web
```
Tool list (exact names): `list_tenants`, `get_tenant`, `list_sources`, `trigger_ingestion`, `list_content_items`, `mark_item`, `list_recipes`, `get_recipe`, `run_recipe`, `list_drafts`, `get_draft`, `get_pipeline_runs`. All return JSON strings; Guid params are strings; MCP layer never touches `IAppDbContext` directly.

- [ ] **Step 1: Write failing tests (tool layer called directly, services on TestDb)**

`tests/ContentAutomatorX.IntegrationTests/McpToolsTests.cs`:
```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Delivery;
using ContentAutomatorX.Web.Mcp;

namespace ContentAutomatorX.IntegrationTests;

public class McpToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"contentx-mcp-{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task List_tenants_returns_json_array()
    {
        using var test = TestDb.Create();
        test.Db.Tenants.Add(new Tenant { Name = "Chan", Slug = "chan" });
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.ListTenants(new TenantService(test.Db));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("chan", doc.RootElement[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Run_recipe_generates_and_reports_file_path()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t", OutputFolderPath = _dir };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "f" };
        var template = new PromptTemplate { TenantId = null, Kind = DraftKinds.SocialPost, Template = "{items}{voice_profile}{tone_modifiers}{extra_instructions}" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "R", Kind = DraftKinds.SocialPost, PromptTemplateId = template.Id };
        test.Db.Tenants.Add(tenant); test.Db.Sources.Add(source);
        test.Db.PromptTemplates.Add(template); test.Db.Recipes.Add(recipe);
        test.Db.ContentItems.Add(new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "e", Title = "News" });
        await test.Db.SaveChangesAsync();

        var pipeline = new GenerationPipeline(test.Db, new FakeLlm(), new FileShareDraftDelivery());
        var json = await ContentXTools.RunRecipe(pipeline, recipe.Id.ToString(), null, null);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Succeeded", doc.RootElement.GetProperty("runStatus").GetString());
        Assert.True(File.Exists(doc.RootElement.GetProperty("filePath").GetString()));
    }

    [Fact]
    public async Task Mark_item_changes_status()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "f" };
        var item = new ContentItem { TenantId = tenant.Id, SourceId = source.Id, ExternalId = "e", Title = "i" };
        test.Db.Tenants.Add(tenant); test.Db.Sources.Add(source); test.Db.ContentItems.Add(item);
        await test.Db.SaveChangesAsync();

        var json = await ContentXTools.MarkItem(new ContentService(test.Db), item.Id.ToString(), "Selected");

        Assert.Contains("Selected", json);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter McpToolsTests`
Expected: FAIL — `ContentXTools` does not exist.

- [ ] **Step 3: Implement the tools**

`src/ContentAutomatorX.Web/Mcp/ContentXTools.cs`:
```csharp
using System.ComponentModel;
using System.Text.Json;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ModelContextProtocol.Server;

namespace ContentAutomatorX.Web.Mcp;

[McpServerToolType]
public static class ContentXTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string ToJson(object value) => JsonSerializer.Serialize(value, Json);

    [McpServerTool(Name = "list_tenants"), Description("List all tenants (channels/brands) with ids, slugs and voice profiles.")]
    public static async Task<string> ListTenants(TenantService tenants) => ToJson(await tenants.ListAsync());

    [McpServerTool(Name = "get_tenant"), Description("Get one tenant by id.")]
    public static async Task<string> GetTenant(TenantService tenants, [Description("Tenant id (GUID)")] string tenantId) =>
        ToJson(await tenants.GetAsync(Guid.Parse(tenantId)) as object ?? "not found");

    [McpServerTool(Name = "list_sources"), Description("List a tenant's content sources (Reddit subreddits, RSS feeds).")]
    public static async Task<string> ListSources(SourceService sources, [Description("Tenant id (GUID)")] string tenantId) =>
        ToJson(await sources.ListAsync(Guid.Parse(tenantId)));

    [McpServerTool(Name = "trigger_ingestion"), Description("Fetch new items now for a tenant (optionally one source). Returns the pipeline run.")]
    public static async Task<string> TriggerIngestion(IngestionPipeline pipeline,
        [Description("Tenant id (GUID)")] string tenantId,
        [Description("Optional source id (GUID) to fetch only that source")] string? sourceId = null)
    {
        var run = await pipeline.RunAsync(Guid.Parse(tenantId),
            sourceId is null ? null : Guid.Parse(sourceId), RunTriggers.Mcp);
        return ToJson(new { runStatus = run.Status.ToString(), log = run.LogJson });
    }

    [McpServerTool(Name = "list_content_items"), Description("Browse gathered content items for a tenant. Status: New|Selected|Ignored|Used.")]
    public static async Task<string> ListContentItems(ContentService content,
        [Description("Tenant id (GUID)")] string tenantId,
        [Description("Optional status filter: New|Selected|Ignored|Used")] string? status = null,
        [Description("Optional: only items fetched in the last N days")] int? sinceDays = null)
    {
        ContentItemStatus? parsed = status is null ? null : Enum.Parse<ContentItemStatus>(status, ignoreCase: true);
        var since = sinceDays is int d ? DateTimeOffset.UtcNow.AddDays(-d) : (DateTimeOffset?)null;
        return ToJson(await content.ListAsync(Guid.Parse(tenantId), parsed, since));
    }

    [McpServerTool(Name = "mark_item"), Description("Curate a content item: set status Selected or Ignored (or back to New).")]
    public static async Task<string> MarkItem(ContentService content,
        [Description("Content item id (GUID)")] string itemId,
        [Description("New status: New|Selected|Ignored")] string status)
    {
        var parsed = Enum.Parse<ContentItemStatus>(status, ignoreCase: true);
        await content.MarkAsync(Guid.Parse(itemId), parsed);
        return ToJson(new { itemId, status = parsed.ToString() });
    }

    [McpServerTool(Name = "list_recipes"), Description("List a tenant's recipes (drafting configurations).")]
    public static async Task<string> ListRecipes(RecipeService recipes, [Description("Tenant id (GUID)")] string tenantId) =>
        ToJson(await recipes.ListAsync(Guid.Parse(tenantId)));

    [McpServerTool(Name = "get_recipe"), Description("Get one recipe by id, including selection rules and output config.")]
    public static async Task<string> GetRecipe(RecipeService recipes, [Description("Recipe id (GUID)")] string recipeId) =>
        ToJson(await recipes.GetAsync(Guid.Parse(recipeId)) as object ?? "not found");

    [McpServerTool(Name = "run_recipe"), Description("Run a recipe's generation pipeline now: select items, generate the draft via LLM, deliver the file. Returns run status, draft id and file path.")]
    public static async Task<string> RunRecipe(GenerationPipeline pipeline,
        [Description("Recipe id (GUID)")] string recipeId,
        [Description("Optional comma-separated content item ids (GUIDs) to use instead of the recipe's selection rules")] string? itemIds = null,
        [Description("Optional extra instructions for this run")] string? extraInstructions = null)
    {
        var ids = itemIds?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse).ToList();
        var (run, draft) = await pipeline.RunAsync(Guid.Parse(recipeId), ids, extraInstructions, RunTriggers.Mcp);
        return ToJson(new
        {
            runStatus = run.Status.ToString(),
            draftId = draft?.Id,
            title = draft?.Title,
            filePath = draft?.FilePath,
            log = run.LogJson
        });
    }

    [McpServerTool(Name = "list_drafts"), Description("List generated drafts for a tenant. Kind: Newsletter|SocialPost|VideoScript. Status: Generated|Delivered.")]
    public static async Task<string> ListDrafts(DraftService drafts,
        [Description("Tenant id (GUID)")] string tenantId,
        [Description("Optional kind filter")] string? kind = null,
        [Description("Optional status filter: Generated|Delivered")] string? status = null)
    {
        DraftStatus? parsed = status is null ? null : Enum.Parse<DraftStatus>(status, ignoreCase: true);
        var list = await drafts.ListAsync(Guid.Parse(tenantId), kind, parsed);
        return ToJson(list.Select(d => new { d.Id, d.Kind, d.Title, d.Status, d.FilePath, d.CreatedAt }));
    }

    [McpServerTool(Name = "get_draft"), Description("Get one draft by id including its full Markdown body.")]
    public static async Task<string> GetDraft(DraftService drafts, [Description("Draft id (GUID)")] string draftId) =>
        ToJson(await drafts.GetAsync(Guid.Parse(draftId)) as object ?? "not found");

    [McpServerTool(Name = "get_pipeline_runs"), Description("Recent pipeline runs (ingestion/generation) for a tenant, newest first.")]
    public static async Task<string> GetPipelineRuns(RunService runs,
        [Description("Tenant id (GUID)")] string tenantId,
        [Description("Max entries (default 20)")] int limit = 20) =>
        ToJson(await runs.ListAsync(Guid.Parse(tenantId), limit));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter McpToolsTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Manual smoke check of the live endpoint**

Run: `dotnet run --project src/ContentAutomatorX.Web --no-launch-profile` in one terminal, then:
```bash
claude mcp add --transport http contentx http://localhost:5090/mcp
claude -p "Use the contentx list_tenants tool and show me the raw result." --output-format text
claude mcp remove contentx
```
Expected: tool call succeeds and returns `[]` (no tenants yet). Stop the host.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: exposed MCP server tools over application services"
```

---

### Task 16: UI shell + Tenants + Sources pages

UI tasks have no unit tests (Blazor pages are thin bindings over already-tested services); each ends with a build + manual browser check.

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/_Imports.razor`, `Components/Layout/MainLayout.razor`
- Create: `src/ContentAutomatorX.Web/Components/Pages/Tenants.razor`, `Sources.razor`
- Delete: any template sample pages except `Home.razor` (Dashboard fills it in Task 19; leave the file in place until then)

**Interfaces:**
- Consumes: `TenantService`, `SourceService`, `IngestionPipeline` (DI from Task 14)
- Produces: nav shell used by all pages; routes `/tenants`, `/sources`

- [ ] **Step 1: Replace _Imports.razor**

`src/ContentAutomatorX.Web/Components/_Imports.razor`:
```razor
@using System.Net.Http
@using System.Text.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.JSInterop
@using MudBlazor
@using ContentAutomatorX.Web
@using ContentAutomatorX.Web.Components
@using ContentAutomatorX.Domain
@using ContentAutomatorX.Domain.Entities
@using ContentAutomatorX.Domain.Models
@using ContentAutomatorX.Application.Services
@using ContentAutomatorX.Application.Pipelines
```

- [ ] **Step 2: Replace MainLayout.razor**

`src/ContentAutomatorX.Web/Components/Layout/MainLayout.razor`:
```razor
@inherits LayoutComponentBase

<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="1">
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start"
                       OnClick="@(() => _drawerOpen = !_drawerOpen)" />
        <MudText Typo="Typo.h6">ContentAutomatorX</MudText>
    </MudAppBar>
    <MudDrawer @bind-Open="_drawerOpen" Elevation="2">
        <MudNavMenu>
            <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">Dashboard</MudNavLink>
            <MudNavLink Href="/tenants" Icon="@Icons.Material.Filled.People">Tenants</MudNavLink>
            <MudNavLink Href="/sources" Icon="@Icons.Material.Filled.RssFeed">Sources</MudNavLink>
            <MudNavLink Href="/recipes" Icon="@Icons.Material.Filled.Receipt">Recipes</MudNavLink>
            <MudNavLink Href="/content" Icon="@Icons.Material.Filled.Article">Content</MudNavLink>
            <MudNavLink Href="/drafts" Icon="@Icons.Material.Filled.Drafts">Drafts</MudNavLink>
            <MudNavLink Href="/runs" Icon="@Icons.Material.Filled.History">Runs</MudNavLink>
        </MudNavMenu>
    </MudDrawer>
    <MudMainContent Class="pa-6">
        @Body
    </MudMainContent>
</MudLayout>

@code {
    private bool _drawerOpen = true;
}
```

- [ ] **Step 3: Create Tenants page**

`src/ContentAutomatorX.Web/Components/Pages/Tenants.razor`:
```razor
@page "/tenants"
@rendermode InteractiveServer
@inject TenantService TenantSvc
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Tenants</MudText>

<MudPaper Class="pa-4 mb-4">
    <MudText Typo="Typo.h6">@(_editing is null ? "New tenant" : $"Edit: {_editing.Name}")</MudText>
    <MudTextField @bind-Value="_name" Label="Name" />
    <MudTextField @bind-Value="_slug" Label="Slug (short id, used in draft front-matter)" />
    <MudTextField @bind-Value="_voice" Label="Voice profile (tone, style, audience — goes into every prompt)" Lines="5" />
    <MudTextField @bind-Value="_folder" Label="Output folder (local OneDrive/Mega synced path)" />
    <MudSwitch T="bool" @bind-Value="_active" Label="Active" Color="Color.Primary" />
    <div class="mt-2">
        <MudButton OnClick="VerifyFolder" Variant="Variant.Outlined" Class="mr-2">Verify folder</MudButton>
        <MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary" Class="mr-2">Save</MudButton>
        @if (_editing is not null)
        {
            <MudButton OnClick="Reset">Cancel</MudButton>
        }
    </div>
</MudPaper>

<MudTable Items="_tenants" Hover="true">
    <HeaderContent>
        <MudTh>Name</MudTh><MudTh>Slug</MudTh><MudTh>Output folder</MudTh><MudTh>Active</MudTh><MudTh></MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.Name</MudTd>
        <MudTd>@context.Slug</MudTd>
        <MudTd>@context.OutputFolderPath</MudTd>
        <MudTd>@(context.IsActive ? "yes" : "no")</MudTd>
        <MudTd>
            <MudButton Size="Size.Small" OnClick="@(() => Edit(context))">Edit</MudButton>
            <MudButton Size="Size.Small" Color="Color.Error" OnClick="@(() => Delete(context))">Delete</MudButton>
        </MudTd>
    </RowTemplate>
</MudTable>

@code {
    private List<Tenant> _tenants = [];
    private Tenant? _editing;
    private string _name = "", _slug = "", _voice = "", _folder = "";
    private bool _active = true;

    protected override async Task OnInitializedAsync() => _tenants = await TenantSvc.ListAsync();

    private void Edit(Tenant t)
    {
        _editing = t;
        (_name, _slug, _voice, _folder, _active) = (t.Name, t.Slug, t.VoiceProfile, t.OutputFolderPath, t.IsActive);
    }

    private void Reset()
    {
        _editing = null;
        (_name, _slug, _voice, _folder, _active) = ("", "", "", "", true);
    }

    private void VerifyFolder()
    {
        try
        {
            if (!Directory.Exists(_folder)) { Snackbar.Add("Folder does not exist", Severity.Error); return; }
            var probe = Path.Combine(_folder, $".contentx-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            Snackbar.Add("Folder is writable", Severity.Success);
        }
        catch (Exception ex) { Snackbar.Add($"Not writable: {ex.Message}", Severity.Error); }
    }

    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(_name) || string.IsNullOrWhiteSpace(_slug))
        { Snackbar.Add("Name and slug are required", Severity.Warning); return; }

        if (_editing is null)
        {
            await TenantSvc.CreateAsync(new Tenant
            { Name = _name, Slug = _slug, VoiceProfile = _voice, OutputFolderPath = _folder, IsActive = _active });
        }
        else
        {
            (_editing.Name, _editing.Slug, _editing.VoiceProfile, _editing.OutputFolderPath, _editing.IsActive)
                = (_name, _slug, _voice, _folder, _active);
            await TenantSvc.UpdateAsync();
        }
        Reset();
        _tenants = await TenantSvc.ListAsync();
        Snackbar.Add("Saved", Severity.Success);
    }

    private async Task Delete(Tenant t)
    {
        await TenantSvc.DeleteAsync(t.Id);
        _tenants = await TenantSvc.ListAsync();
    }
}
```

- [ ] **Step 4: Create Sources page**

`src/ContentAutomatorX.Web/Components/Pages/Sources.razor`:
```razor
@page "/sources"
@rendermode InteractiveServer
@inject TenantService TenantSvc
@inject SourceService SourceSvc
@inject IngestionPipeline Ingestion
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Sources</MudText>

<MudSelect T="Guid?" Value="_tenantId" ValueChanged="OnTenantChanged" Label="Tenant" Class="mb-4">
    @foreach (var t in _tenants)
    {
        <MudSelectItem T="Guid?" Value="@((Guid?)t.Id)">@t.Name</MudSelectItem>
    }
</MudSelect>

@if (_tenantId is not null)
{
    <MudPaper Class="pa-4 mb-4">
        <MudText Typo="Typo.h6">@(_editing is null ? "New source" : $"Edit: {_editing.DisplayName}")</MudText>
        <MudSelect T="string" @bind-Value="_type" Label="Type">
            <MudSelectItem T="string" Value="@SourceTypes.Reddit">Reddit</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Rss">RSS/Atom feed</MudSelectItem>
        </MudSelect>
        <MudTextField @bind-Value="_displayName" Label="Display name" />
        @if (_type == SourceTypes.Reddit)
        {
            <MudTextField @bind-Value="_subreddit" Label="Subreddit (without r/)" />
            <MudTextField @bind-Value="_sort" Label="Sort (hot|new|top|rising)" />
            <MudTextField @bind-Value="_timeframe" Label="Timeframe for top (hour|day|week|month|year|all)" />
        }
        else
        {
            <MudTextField @bind-Value="_feedUrl" Label="Feed URL" />
        }
        <MudTextField @bind-Value="_cron" Label="Schedule (cron, UTC; e.g. '0 6 * * *' = daily 06:00; empty = manual only)" />
        <MudSwitch T="bool" @bind-Value="_enabled" Label="Enabled" Color="Color.Primary" />
        <div class="mt-2">
            <MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary" Class="mr-2">Save</MudButton>
            @if (_editing is not null)
            {
                <MudButton OnClick="Reset">Cancel</MudButton>
            }
        </div>
    </MudPaper>

    <MudTable Items="_sources" Hover="true">
        <HeaderContent>
            <MudTh>Name</MudTh><MudTh>Type</MudTh><MudTh>Schedule</MudTh><MudTh>Enabled</MudTh><MudTh>Last fetched</MudTh><MudTh></MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>@context.DisplayName</MudTd>
            <MudTd>@context.Type</MudTd>
            <MudTd>@(context.ScheduleCron ?? "manual")</MudTd>
            <MudTd>@(context.IsEnabled ? "yes" : "no")</MudTd>
            <MudTd>@(context.LastFetchedAt?.ToLocalTime().ToString("g") ?? "never")</MudTd>
            <MudTd>
                <MudButton Size="Size.Small" OnClick="@(() => FetchNow(context))">Fetch now</MudButton>
                <MudButton Size="Size.Small" OnClick="@(() => Edit(context))">Edit</MudButton>
                <MudButton Size="Size.Small" Color="Color.Error" OnClick="@(() => Delete(context))">Delete</MudButton>
            </MudTd>
        </RowTemplate>
    </MudTable>
}

@code {
    private List<Tenant> _tenants = [];
    private List<Source> _sources = [];
    private Guid? _tenantId;
    private Source? _editing;
    private string _type = SourceTypes.Reddit, _displayName = "", _subreddit = "", _sort = "hot",
        _timeframe = "week", _feedUrl = "", _cron = "";
    private bool _enabled = true;

    protected override async Task OnInitializedAsync() => _tenants = await TenantSvc.ListAsync();

    private async Task OnTenantChanged(Guid? id)
    {
        _tenantId = id;
        Reset();
        _sources = id is null ? [] : await SourceSvc.ListAsync(id.Value);
    }

    private void Edit(Source s)
    {
        _editing = s;
        _type = s.Type; _displayName = s.DisplayName; _cron = s.ScheduleCron ?? ""; _enabled = s.IsEnabled;
        using var doc = JsonDocument.Parse(s.ConfigJson);
        if (s.Type == SourceTypes.Reddit)
        {
            _subreddit = doc.RootElement.TryGetProperty("subreddit", out var v) ? v.GetString() ?? "" : "";
            _sort = doc.RootElement.TryGetProperty("sort", out var so) ? so.GetString() ?? "hot" : "hot";
            _timeframe = doc.RootElement.TryGetProperty("timeframe", out var tf) ? tf.GetString() ?? "week" : "week";
        }
        else
        {
            _feedUrl = doc.RootElement.TryGetProperty("feedUrl", out var f) ? f.GetString() ?? "" : "";
        }
    }

    private void Reset()
    {
        _editing = null;
        (_type, _displayName, _subreddit, _sort, _timeframe, _feedUrl, _cron, _enabled)
            = (SourceTypes.Reddit, "", "", "hot", "week", "", "", true);
    }

    private string BuildConfig() => _type == SourceTypes.Reddit
        ? JsonSerializer.Serialize(new { subreddit = _subreddit, sort = _sort, timeframe = _timeframe })
        : JsonSerializer.Serialize(new { feedUrl = _feedUrl });

    private async Task Save()
    {
        if (_tenantId is null || string.IsNullOrWhiteSpace(_displayName)) return;
        if (_editing is null)
        {
            await SourceSvc.CreateAsync(new Source
            {
                TenantId = _tenantId.Value, Type = _type, DisplayName = _displayName,
                ConfigJson = BuildConfig(), ScheduleCron = string.IsNullOrWhiteSpace(_cron) ? null : _cron,
                IsEnabled = _enabled
            });
        }
        else
        {
            _editing.Type = _type; _editing.DisplayName = _displayName; _editing.ConfigJson = BuildConfig();
            _editing.ScheduleCron = string.IsNullOrWhiteSpace(_cron) ? null : _cron; _editing.IsEnabled = _enabled;
            await SourceSvc.UpdateAsync();
        }
        Reset();
        _sources = await SourceSvc.ListAsync(_tenantId.Value);
        Snackbar.Add("Saved", Severity.Success);
    }

    private async Task Delete(Source s)
    {
        await SourceSvc.DeleteAsync(s.Id);
        _sources = await SourceSvc.ListAsync(_tenantId!.Value);
    }

    private async Task FetchNow(Source s)
    {
        Snackbar.Add($"Fetching {s.DisplayName}...", Severity.Info);
        var run = await Ingestion.RunAsync(s.TenantId, s.Id);
        Snackbar.Add($"Fetch {run.Status}: {run.LogJson}",
            run.Status == RunStatus.Succeeded ? Severity.Success : Severity.Error);
        _sources = await SourceSvc.ListAsync(_tenantId!.Value);
    }
}
```

- [ ] **Step 5: Build, run, manual check**

Run: `dotnet build` → `Build succeeded`.
Run: `dotnet run --project src/ContentAutomatorX.Web --no-launch-profile`, open `http://localhost:5090/tenants`:
- create a tenant (any local folder as output; Verify folder → "Folder is writable")
- open `/sources`, pick the tenant, add an RSS source (e.g. `https://github.blog/feed/`), click **Fetch now** → success snackbar, "Last fetched" fills in.
Stop the host.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat: UI shell, tenants and sources pages"
```

---

### Task 17: Recipes page

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor`

**Interfaces:**
- Consumes: `RecipeService`, `SourceService`, `TenantService`, `GenerationPipeline` (Task 14 DI)
- Produces: route `/recipes` — create/edit/clone/run recipes including selection rules and the prompt template editor.

- [ ] **Step 1: Create Recipes page**

`src/ContentAutomatorX.Web/Components/Pages/Recipes.razor`:
```razor
@page "/recipes"
@rendermode InteractiveServer
@inject TenantService TenantSvc
@inject SourceService SourceSvc
@inject RecipeService RecipeSvc
@inject GenerationPipeline Generation
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Recipes</MudText>

<MudSelect T="Guid?" Value="_tenantId" ValueChanged="OnTenantChanged" Label="Tenant" Class="mb-4">
    @foreach (var t in _tenants)
    {
        <MudSelectItem T="Guid?" Value="@((Guid?)t.Id)">@t.Name</MudSelectItem>
    }
</MudSelect>

@if (_tenantId is not null)
{
    <MudPaper Class="pa-4 mb-4">
        <MudText Typo="Typo.h6">@(_editing is null ? "New recipe" : $"Edit: {_editing.Name}")</MudText>
        <MudTextField @bind-Value="_name" Label="Name (e.g. 'Weekly AI News Newsletter')" />
        <MudSelect T="string" @bind-Value="_kind" Label="Kind" Disabled="@(_editing is not null)">
            @foreach (var k in DraftKinds.All)
            {
                <MudSelectItem T="string" Value="@k">@k</MudSelectItem>
            }
        </MudSelect>
        <MudSelect T="Guid" MultiSelection="true" @bind-SelectedValues="_selectedSourceIds"
                   Label="Sources feeding this recipe (none selected = all)">
            @foreach (var s in _sources)
            {
                <MudSelectItem T="Guid" Value="@s.Id">@s.DisplayName</MudSelectItem>
            }
        </MudSelect>

        <MudText Typo="Typo.subtitle1" Class="mt-3">Selection rules</MudText>
        <MudNumericField T="int?" @bind-Value="_windowDays" Label="Time window (days, empty = any age)" />
        <MudNumericField T="int?" @bind-Value="_minScore" Label="Min score (Reddit upvotes, empty = any)" />
        <MudNumericField T="int" @bind-Value="_maxItems" Label="Max items" />
        <MudTextField @bind-Value="_includeKeywords" Label="Include keywords (comma separated, empty = all)" />
        <MudTextField @bind-Value="_excludeKeywords" Label="Exclude keywords (comma separated)" />

        <MudText Typo="Typo.subtitle1" Class="mt-3">Style</MudText>
        <MudTextField @bind-Value="_tone" Label="Tone modifiers (added to tenant voice profile)" Lines="2" />
        <MudTextField @bind-Value="_length" Label="Length target (e.g. '800 words')" />
        <MudTextField @bind-Value="_language" Label="Language (e.g. 'English')" />

        <MudText Typo="Typo.subtitle1" Class="mt-3">Output</MudText>
        <MudTextField @bind-Value="_subfolder" Label="Subfolder in tenant output folder (optional)" />
        <MudTextField @bind-Value="_filenamePattern" Label="Filename pattern (default {date}-{kind}-{slug}.md)" />
        <MudTextField @bind-Value="_targetPlatform" Label="Target platform label (optional, e.g. Patreon, Ko-fi)" />
        <MudTextField @bind-Value="_cron" Label="Schedule (cron, UTC; empty = manual only). Scheduled runs ingest sources first." />
        <MudSwitch T="bool" @bind-Value="_enabled" Label="Enabled" Color="Color.Primary" />

        @if (_editing is not null && _template is not null)
        {
            <MudText Typo="Typo.subtitle1" Class="mt-3">Prompt template (placeholders: {voice_profile} {tone_modifiers} {items} {extra_instructions})</MudText>
            <MudTextField @bind-Value="_template.Template" Lines="14" Variant="Variant.Outlined" />
        }

        <div class="mt-2">
            <MudButton OnClick="Save" Variant="Variant.Filled" Color="Color.Primary" Class="mr-2">Save</MudButton>
            @if (_editing is not null)
            {
                <MudButton OnClick="Reset">Cancel</MudButton>
            }
        </div>
    </MudPaper>

    <MudTable Items="_recipes" Hover="true">
        <HeaderContent>
            <MudTh>Name</MudTh><MudTh>Kind</MudTh><MudTh>Schedule</MudTh><MudTh>Enabled</MudTh><MudTh>Last run</MudTh><MudTh></MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>@context.Name</MudTd>
            <MudTd>@context.Kind</MudTd>
            <MudTd>@(context.ScheduleCron ?? "manual")</MudTd>
            <MudTd>@(context.IsEnabled ? "yes" : "no")</MudTd>
            <MudTd>@(context.LastRunAt?.ToLocalTime().ToString("g") ?? "never")</MudTd>
            <MudTd>
                <MudButton Size="Size.Small" Color="Color.Primary" Disabled="_running" OnClick="@(() => RunNow(context))">Run now</MudButton>
                <MudButton Size="Size.Small" OnClick="@(() => Edit(context))">Edit</MudButton>
                <MudButton Size="Size.Small" Color="Color.Error" OnClick="@(() => Delete(context))">Delete</MudButton>
            </MudTd>
        </RowTemplate>
    </MudTable>
}

@code {
    private List<Tenant> _tenants = [];
    private List<Source> _sources = [];
    private List<Recipe> _recipes = [];
    private Guid? _tenantId;
    private Recipe? _editing;
    private PromptTemplate? _template;
    private bool _running;

    private string _name = "", _kind = DraftKinds.Newsletter, _includeKeywords = "", _excludeKeywords = "",
        _tone = "", _length = "", _language = "", _subfolder = "", _filenamePattern = "", _targetPlatform = "", _cron = "";
    private int? _windowDays = 7, _minScore;
    private int _maxItems = 10;
    private bool _enabled = true;
    private IEnumerable<Guid> _selectedSourceIds = [];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected override async Task OnInitializedAsync() => _tenants = await TenantSvc.ListAsync();

    private async Task OnTenantChanged(Guid? id)
    {
        _tenantId = id;
        Reset();
        _sources = id is null ? [] : await SourceSvc.ListAsync(id.Value);
        _recipes = id is null ? [] : await RecipeSvc.ListAsync(id.Value);
    }

    private async Task Edit(Recipe r)
    {
        _editing = r;
        _name = r.Name; _kind = r.Kind; _cron = r.ScheduleCron ?? ""; _enabled = r.IsEnabled;
        _tone = r.ToneModifiers ?? ""; _length = r.LengthTarget ?? ""; _language = r.Language ?? "";
        _selectedSourceIds = JsonSerializer.Deserialize<Guid[]>(r.SourceIdsJson) ?? [];
        var rules = JsonSerializer.Deserialize<SelectionRules>(r.SelectionJson, JsonOpts) ?? new SelectionRules();
        _windowDays = rules.TimeWindowDays; _minScore = rules.MinScore; _maxItems = rules.MaxItems;
        _includeKeywords = string.Join(", ", rules.IncludeKeywords);
        _excludeKeywords = string.Join(", ", rules.ExcludeKeywords);
        var output = JsonSerializer.Deserialize<RecipeOutput>(r.OutputJson, JsonOpts) ?? new RecipeOutput();
        _subfolder = output.Subfolder ?? ""; _filenamePattern = output.FilenamePattern ?? "";
        _targetPlatform = output.TargetPlatform ?? "";
        _template = await RecipeSvc.GetTemplateAsync(r.PromptTemplateId);
    }

    private void Reset()
    {
        _editing = null; _template = null;
        _name = ""; _kind = DraftKinds.Newsletter; _includeKeywords = ""; _excludeKeywords = "";
        _tone = ""; _length = ""; _language = ""; _subfolder = ""; _filenamePattern = ""; _targetPlatform = "";
        _cron = ""; _windowDays = 7; _minScore = null; _maxItems = 10; _enabled = true;
        _selectedSourceIds = [];
    }

    private static string[] SplitCsv(string csv) =>
        csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private async Task Save()
    {
        if (_tenantId is null || string.IsNullOrWhiteSpace(_name)) return;
        var selection = JsonSerializer.Serialize(new SelectionRules
        {
            TimeWindowDays = _windowDays, MinScore = _minScore, MaxItems = _maxItems,
            IncludeKeywords = SplitCsv(_includeKeywords), ExcludeKeywords = SplitCsv(_excludeKeywords)
        }, JsonOpts);
        var output = JsonSerializer.Serialize(new RecipeOutput
        {
            Subfolder = string.IsNullOrWhiteSpace(_subfolder) ? null : _subfolder,
            FilenamePattern = string.IsNullOrWhiteSpace(_filenamePattern) ? null : _filenamePattern,
            TargetPlatform = string.IsNullOrWhiteSpace(_targetPlatform) ? null : _targetPlatform
        }, JsonOpts);
        var sourceIds = JsonSerializer.Serialize(_selectedSourceIds.ToArray());

        if (_editing is null)
        {
            await RecipeSvc.CreateAsync(new Recipe
            {
                TenantId = _tenantId.Value, Name = _name, Kind = _kind, SourceIdsJson = sourceIds,
                SelectionJson = selection, OutputJson = output,
                ToneModifiers = _tone, LengthTarget = _length, Language = _language,
                ScheduleCron = string.IsNullOrWhiteSpace(_cron) ? null : _cron, IsEnabled = _enabled
            });
        }
        else
        {
            _editing.Name = _name; _editing.SourceIdsJson = sourceIds; _editing.SelectionJson = selection;
            _editing.OutputJson = output; _editing.ToneModifiers = _tone; _editing.LengthTarget = _length;
            _editing.Language = _language; _editing.ScheduleCron = string.IsNullOrWhiteSpace(_cron) ? null : _cron;
            _editing.IsEnabled = _enabled;
            await RecipeSvc.UpdateAsync();   // also persists the edited _template.Template (tracked)
        }
        Reset();
        _recipes = await RecipeSvc.ListAsync(_tenantId.Value);
        Snackbar.Add("Saved", Severity.Success);
    }

    private async Task Delete(Recipe r)
    {
        await RecipeSvc.DeleteAsync(r.Id);
        _recipes = await RecipeSvc.ListAsync(_tenantId!.Value);
    }

    private async Task RunNow(Recipe r)
    {
        _running = true;
        Snackbar.Add($"Running recipe '{r.Name}' — this calls the LLM and can take a few minutes...", Severity.Info);
        try
        {
            var (run, draft) = await Generation.RunAsync(r.Id);
            if (run.Status == RunStatus.Succeeded)
                Snackbar.Add($"Draft delivered: {draft!.FilePath}", Severity.Success);
            else
                Snackbar.Add($"Run {run.Status}: {run.LogJson}", Severity.Error);
        }
        finally
        {
            _running = false;
            _recipes = await RecipeSvc.ListAsync(_tenantId!.Value);
        }
    }
}
```

- [ ] **Step 2: Build, run, manual check**

Run: `dotnet build` → `Build succeeded`.
Run the host, open `/recipes`: pick tenant → create a recipe (Newsletter, leave sources empty = all) → Edit it → template editor shows the seeded default → **Run now** (needs items from Task 16's fetch + working `claude` CLI) → success snackbar with file path, file exists in the tenant output folder.
Stop the host.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: recipes page with selection rules, template editor, run-now"
```

---

### Task 18: Content & Drafts pages

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Pages/Content.razor`, `Drafts.razor`

**Interfaces:**
- Consumes: `ContentService`, `DraftService`, `RecipeService`, `TenantService`, `GenerationPipeline` (Task 14 DI)
- Produces: routes `/content` (browse/curate items, hand-picked recipe run) and `/drafts` (list, preview, open folder, retry delivery)

- [ ] **Step 1: Create Content page**

`src/ContentAutomatorX.Web/Components/Pages/Content.razor`:
```razor
@page "/content"
@rendermode InteractiveServer
@inject TenantService TenantSvc
@inject ContentService ContentSvc
@inject RecipeService RecipeSvc
@inject GenerationPipeline Generation
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Content items</MudText>

<MudSelect T="Guid?" Value="_tenantId" ValueChanged="OnTenantChanged" Label="Tenant" Class="mb-2">
    @foreach (var t in _tenants)
    {
        <MudSelectItem T="Guid?" Value="@((Guid?)t.Id)">@t.Name</MudSelectItem>
    }
</MudSelect>

@if (_tenantId is not null)
{
    <MudSelect T="string" Value="_statusFilter" ValueChanged="OnFilterChanged" Label="Status filter" Class="mb-4">
        <MudSelectItem T="string" Value="@("all")">All</MudSelectItem>
        @foreach (var s in Enum.GetNames<ContentItemStatus>())
        {
            <MudSelectItem T="string" Value="@s">@s</MudSelectItem>
        }
    </MudSelect>

    <MudPaper Class="pa-4 mb-4">
        <MudText Typo="Typo.subtitle1">Generate from hand-picked items (@_selectedItems.Count selected)</MudText>
        <MudSelect T="Guid?" @bind-Value="_recipeId" Label="Recipe">
            @foreach (var r in _recipes)
            {
                <MudSelectItem T="Guid?" Value="@((Guid?)r.Id)">@r.Name (@r.Kind)</MudSelectItem>
            }
        </MudSelect>
        <MudTextField @bind-Value="_extraInstructions" Label="Extra instructions for this run (optional)" />
        <MudButton OnClick="RunWithSelection" Variant="Variant.Filled" Color="Color.Primary" Class="mt-2"
                   Disabled="@(_recipeId is null || _selectedItems.Count == 0 || _running)">Generate draft</MudButton>
    </MudPaper>

    <MudTable T="ContentItem" Items="_items" Hover="true" MultiSelection="true" @bind-SelectedItems="_selectedItems">
        <HeaderContent>
            <MudTh>Title</MudTh><MudTh>Published</MudTh><MudTh>Status</MudTh><MudTh></MudTh>
        </HeaderContent>
        <RowTemplate>
            <MudTd>
                @if (context.Url is not null)
                {
                    <MudLink Href="@context.Url" Target="_blank">@context.Title</MudLink>
                }
                else
                {
                    @context.Title
                }
            </MudTd>
            <MudTd>@((context.PublishedAt ?? context.FetchedAt).ToLocalTime().ToString("g"))</MudTd>
            <MudTd>@context.Status</MudTd>
            <MudTd>
                <MudButton Size="Size.Small" OnClick="@(() => Mark(context, ContentItemStatus.Selected))">Select</MudButton>
                <MudButton Size="Size.Small" OnClick="@(() => Mark(context, ContentItemStatus.Ignored))">Ignore</MudButton>
            </MudTd>
        </RowTemplate>
    </MudTable>
}

@code {
    private List<Tenant> _tenants = [];
    private List<ContentItem> _items = [];
    private List<Recipe> _recipes = [];
    private HashSet<ContentItem> _selectedItems = [];
    private Guid? _tenantId;
    private Guid? _recipeId;
    private string _statusFilter = "all";
    private string _extraInstructions = "";
    private bool _running;

    protected override async Task OnInitializedAsync() => _tenants = await TenantSvc.ListAsync();

    private async Task OnTenantChanged(Guid? id)
    {
        _tenantId = id;
        _selectedItems = [];
        _recipes = id is null ? [] : await RecipeSvc.ListAsync(id.Value);
        await Reload();
    }

    private async Task OnFilterChanged(string filter)
    {
        _statusFilter = filter;
        await Reload();
    }

    private async Task Reload()
    {
        if (_tenantId is null) { _items = []; return; }
        ContentItemStatus? status = _statusFilter == "all" ? null : Enum.Parse<ContentItemStatus>(_statusFilter);
        _items = await ContentSvc.ListAsync(_tenantId.Value, status);
    }

    private async Task Mark(ContentItem item, ContentItemStatus status)
    {
        await ContentSvc.MarkAsync(item.Id, status);
        await Reload();
    }

    private async Task RunWithSelection()
    {
        _running = true;
        Snackbar.Add("Generating draft from selected items...", Severity.Info);
        try
        {
            var ids = _selectedItems.Select(i => i.Id).ToList();
            var (run, draft) = await Generation.RunAsync(_recipeId!.Value, ids, 
                string.IsNullOrWhiteSpace(_extraInstructions) ? null : _extraInstructions);
            if (run.Status == RunStatus.Succeeded)
                Snackbar.Add($"Draft delivered: {draft!.FilePath}", Severity.Success);
            else
                Snackbar.Add($"Run {run.Status}: {run.LogJson}", Severity.Error);
        }
        finally
        {
            _running = false;
            _selectedItems = [];
            await Reload();
        }
    }
}
```

- [ ] **Step 2: Create Drafts page**

`src/ContentAutomatorX.Web/Components/Pages/Drafts.razor`:
```razor
@page "/drafts"
@rendermode InteractiveServer
@inject TenantService TenantSvc
@inject DraftService DraftSvc
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Drafts</MudText>

<MudSelect T="Guid?" Value="_tenantId" ValueChanged="OnTenantChanged" Label="Tenant" Class="mb-4">
    @foreach (var t in _tenants)
    {
        <MudSelectItem T="Guid?" Value="@((Guid?)t.Id)">@t.Name</MudSelectItem>
    }
</MudSelect>

@if (_tenantId is not null)
{
    <MudExpansionPanels MultiExpansion="false">
        @foreach (var draft in _drafts)
        {
            <MudExpansionPanel>
                <TitleContent>
                    <div class="d-flex align-center" style="gap:12px">
                        <MudText>@draft.CreatedAt.ToLocalTime().ToString("g")</MudText>
                        <MudText Color="Color.Primary">@draft.Kind</MudText>
                        <MudText>@draft.Title</MudText>
                        <MudText Color="@(draft.Status == DraftStatus.Delivered ? Color.Success : Color.Warning)">
                            @draft.Status
                        </MudText>
                    </div>
                </TitleContent>
                <ChildContent>
                    @if (draft.FilePath is not null)
                    {
                        <MudText Typo="Typo.body2" Class="mb-2">@draft.FilePath</MudText>
                        <MudButton Size="Size.Small" OnClick="@(() => OpenFolder(draft))" Class="mb-2">Open folder</MudButton>
                    }
                    else
                    {
                        <MudButton Size="Size.Small" Color="Color.Warning" OnClick="@(() => RetryDelivery(draft))"
                                   Class="mb-2">Retry delivery</MudButton>
                    }
                    <MudPaper Class="pa-4" Style="white-space: pre-wrap; font-family: monospace; max-height: 480px; overflow-y: auto;">
                        @draft.Body
                    </MudPaper>
                </ChildContent>
            </MudExpansionPanel>
        }
    </MudExpansionPanels>
}

@code {
    private List<Tenant> _tenants = [];
    private List<Draft> _drafts = [];
    private Guid? _tenantId;

    protected override async Task OnInitializedAsync() => _tenants = await TenantSvc.ListAsync();

    private async Task OnTenantChanged(Guid? id)
    {
        _tenantId = id;
        _drafts = id is null ? [] : await DraftSvc.ListAsync(id.Value);
    }

    private void OpenFolder(Draft draft)
    {
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{draft.FilePath}\"");
        }
        catch (Exception ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task RetryDelivery(Draft draft)
    {
        try
        {
            var delivered = await DraftSvc.RetryDeliveryAsync(draft.Id);
            Snackbar.Add($"Delivered: {delivered.FilePath}", Severity.Success);
        }
        catch (Exception ex) { Snackbar.Add($"Delivery failed again: {ex.Message}", Severity.Error); }
        _drafts = await DraftSvc.ListAsync(_tenantId!.Value);
    }
}
```

- [ ] **Step 3: Build, run, manual check**

Run: `dotnet build` → `Build succeeded`. Run the host:
- `/content`: items from earlier fetches listed; Select/Ignore updates status; multi-select + recipe + **Generate draft** works.
- `/drafts`: drafts listed; expanding shows body; **Open folder** opens Explorer at the file.
Stop the host.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: content browser and drafts pages"
```

---

### Task 19: Dashboard, Runs page, README, final verification

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Home.razor` (Dashboard)
- Create: `src/ContentAutomatorX.Web/Components/Pages/Runs.razor`, `README.md`

**Interfaces:**
- Consumes: all services
- Produces: routes `/` and `/runs`; README with setup + manual E2E checklist

- [ ] **Step 1: Replace Home.razor with the dashboard**

`src/ContentAutomatorX.Web/Components/Pages/Home.razor`:
```razor
@page "/"
@rendermode InteractiveServer
@inject TenantService TenantSvc
@inject ContentService ContentSvc
@inject DraftService DraftSvc
@inject RunService RunSvc

<MudText Typo="Typo.h4" Class="mb-4">Dashboard</MudText>

<MudGrid>
    @foreach (var card in _cards)
    {
        <MudItem xs="12" md="6" lg="4">
            <MudCard>
                <MudCardHeader>
                    <MudText Typo="Typo.h6">@card.Tenant.Name</MudText>
                </MudCardHeader>
                <MudCardContent>
                    <MudText>New items: <b>@card.NewItems</b></MudText>
                    <MudText>Drafts: <b>@card.Drafts</b></MudText>
                    <MudText>
                        Last run:
                        @if (card.LastRun is null)
                        {
                            <span>none yet</span>
                        }
                        else
                        {
                            <MudText Inline="true"
                                     Color="@(card.LastRun.Status == RunStatus.Succeeded ? Color.Success : Color.Error)">
                                @card.LastRun.Kind @card.LastRun.Status
                            </MudText>
                            <span> (@card.LastRun.StartedAt.ToLocalTime().ToString("g"))</span>
                        }
                    </MudText>
                    @if (card.RecentErrors > 0)
                    {
                        <MudText Color="Color.Error">@card.RecentErrors failed/partial runs in the last 10</MudText>
                    }
                </MudCardContent>
            </MudCard>
        </MudItem>
    }
</MudGrid>

@code {
    private record TenantCard(Tenant Tenant, int NewItems, int Drafts, PipelineRun? LastRun, int RecentErrors);
    private List<TenantCard> _cards = [];

    protected override async Task OnInitializedAsync()
    {
        foreach (var tenant in await TenantSvc.ListAsync())
        {
            var items = await ContentSvc.ListAsync(tenant.Id, ContentItemStatus.New);
            var drafts = await DraftSvc.ListAsync(tenant.Id);
            var runs = await RunSvc.ListAsync(tenant.Id, 10);
            _cards.Add(new TenantCard(tenant, items.Count, drafts.Count, runs.FirstOrDefault(),
                runs.Count(r => r.Status is RunStatus.Failed or RunStatus.Partial)));
        }
    }
}
```

- [ ] **Step 2: Create Runs page**

`src/ContentAutomatorX.Web/Components/Pages/Runs.razor`:
```razor
@page "/runs"
@rendermode InteractiveServer
@inject TenantService TenantSvc
@inject RunService RunSvc

<MudText Typo="Typo.h4" Class="mb-4">Pipeline runs</MudText>

<MudSelect T="Guid?" Value="_tenantId" ValueChanged="OnTenantChanged" Label="Tenant" Class="mb-4">
    @foreach (var t in _tenants)
    {
        <MudSelectItem T="Guid?" Value="@((Guid?)t.Id)">@t.Name</MudSelectItem>
    }
</MudSelect>

@if (_tenantId is not null)
{
    <MudExpansionPanels>
        @foreach (var run in _runs)
        {
            <MudExpansionPanel>
                <TitleContent>
                    <div class="d-flex align-center" style="gap:12px">
                        <MudText>@run.StartedAt.ToLocalTime().ToString("g")</MudText>
                        <MudText>@run.Kind</MudText>
                        <MudText>@run.Trigger</MudText>
                        <MudText Color="@StatusColor(run.Status)">@run.Status</MudText>
                    </div>
                </TitleContent>
                <ChildContent>
                    <MudPaper Class="pa-4" Style="white-space: pre-wrap; font-family: monospace;">
                        @FormatLog(run.LogJson)
                    </MudPaper>
                </ChildContent>
            </MudExpansionPanel>
        }
    </MudExpansionPanels>
}

@code {
    private List<Tenant> _tenants = [];
    private List<PipelineRun> _runs = [];
    private Guid? _tenantId;

    protected override async Task OnInitializedAsync() => _tenants = await TenantSvc.ListAsync();

    private async Task OnTenantChanged(Guid? id)
    {
        _tenantId = id;
        _runs = id is null ? [] : await RunSvc.ListAsync(id.Value);
    }

    private static Color StatusColor(RunStatus status) => status switch
    {
        RunStatus.Succeeded => Color.Success,
        RunStatus.Partial => Color.Warning,
        RunStatus.Failed => Color.Error,
        _ => Color.Info
    };

    private static string FormatLog(string logJson)
    {
        try
        {
            var lines = JsonSerializer.Deserialize<string[]>(logJson) ?? [];
            return string.Join("\n", lines);
        }
        catch { return logJson; }
    }
}
```

- [ ] **Step 3: Write README.md**

`README.md`:
```markdown
# ContentAutomatorX

Multi-tenant content automation: pulls material from Reddit and RSS feeds,
generates drafts (newsletters, social posts, YouTube scripts) per configurable
**recipe** using the `claude` CLI, and delivers Markdown files into per-tenant
sync folders (OneDrive, Mega). Exposes an MCP server so Claude Code / LM Studio
can drive the whole system.

Docs: `docs/superpowers/specs/` (design) and `docs/superpowers/plans/` (implementation plan).

## Requirements

- .NET 10 SDK
- [Claude Code CLI](https://claude.com/claude-code) installed and logged in (`claude --version`)
- A local sync client folder (OneDrive / Mega) per tenant for delivered drafts

## Run

    dotnet run --project src/ContentAutomatorX.Web

Open http://localhost:5090. Data lives in `src/ContentAutomatorX.Web/data/contentx.db`;
logs in `src/ContentAutomatorX.Web/logs/`.

## Configure (appsettings.json)

| Key | Meaning | Default |
|---|---|---|
| `Urls` | listen address | `http://localhost:5090` |
| `Database:Path` | SQLite file path | `data/contentx.db` under the Web project |
| `Claude:Command` | claude executable | `claude` (set full path if not on PATH) |
| `Claude:Model` | model override | empty = CLI default |
| `Claude:TimeoutSeconds` | per-generation timeout | `300` |

## Quick start

1. **Tenants** → create a tenant; set its voice profile and output folder (Verify folder).
2. **Sources** → add a subreddit and/or RSS feed; **Fetch now**.
3. **Recipes** → create a recipe (kind, selection rules, optional schedule); **Run now**.
4. The draft lands as Markdown in the tenant's output folder and under **Drafts**.

Cron schedules are UTC (Cronos syntax, e.g. `0 8 * * MON` = Mondays 08:00 UTC).
A scheduled recipe ingests its sources first, then generates — full auto.

## MCP

The app exposes MCP (streamable HTTP) at `http://localhost:5090/mcp` with tools:
`list_tenants`, `get_tenant`, `list_sources`, `trigger_ingestion`, `list_content_items`,
`mark_item`, `list_recipes`, `get_recipe`, `run_recipe`, `list_drafts`, `get_draft`,
`get_pipeline_runs`.

Connect Claude Code:

    claude mcp add --transport http contentx http://localhost:5090/mcp

## Tests

    dotnet test
```

- [ ] **Step 4: Full verification**

Run: `dotnet test`
Expected: all unit + integration tests PASS.

Manual E2E checklist (real machine, real claude CLI):
1. `dotnet run --project src/ContentAutomatorX.Web` → dashboard loads at http://localhost:5090
2. Create tenant with your real OneDrive folder → Verify folder OK
3. Add source `r/StableDiffusion` (top/week) → Fetch now → items on /content
4. Create Newsletter recipe → Run now → snackbar success → `.md` file appears in OneDrive folder with front-matter
5. /runs shows the ingestion + generation runs with logs
6. `claude mcp add --transport http contentx http://localhost:5090/mcp` then in a fresh `claude` session: "list my contentx tenants" → works
7. Set a recipe schedule 2-3 minutes in the future (`*/2 * * * *` for a quick test), wait → scheduler ingests + generates automatically; remove the test schedule afterwards

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: dashboard, runs page, README"
```

---

## Plan Self-Review Notes (for the implementer)

- Package versions are intentionally unpinned (`dotnet add package` latest); `ModelContextProtocol.AspNetCore` requires `--prerelease`. If its API surface changed vs. this plan (`AddMcpServer().WithHttpTransport().WithToolsFromAssembly()`, `[McpServerToolType]`, `[McpServerTool]`, `app.MapMcp(...)`), consult the package README — the tool bodies stay the same.
- If `dotnet new blazor -int Server -e` produces slightly different template file names in .NET 10, keep the template's `App.razor`/`Routes.razor` and only apply the documented modifications.
- `System.ServiceModel.Syndication` handles both RSS 2.0 and Atom via `SyndicationFeed.Load`.
- Windows note: if `ProcessRunner` can't start `claude` (Win32Exception), set `Claude:Command` in appsettings.json to the full path of `claude.exe`.
