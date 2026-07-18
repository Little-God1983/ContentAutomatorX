# Newsletter-First (Phase 2a) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The weekly newsletter runs end-to-end with one human step — hitting Send in MailerLite: gather (feeds/sites/LLM research) → compose ✨ → review in the issue editor → push as MailerLite draft campaign → Sent detection + stats.

**Architecture:** Everything rides Phase 1 seams: two new `ISourceConnector`s, a minimal `Platform`/`Post` model (the future cross-platform entities, newsletter first), an `IMailerLiteClient` abstraction in Domain with an HTTP implementation in Infrastructure, Application services (`PlatformService`, `PostService`, `PostSyncService`) that the Blazor pages and MCP tools both call. `GenerationPipeline` gains one optional step (create a review-queue Post). Secrets go through a new `ICredentialStore` (DPAPI), never SQLite.

**Tech Stack:** .NET 10, EF Core + SQLite (code-first migrations), Blazor Server + MudBlazor 9, xUnit, AngleSharp (HTML extraction), Markdig (markdown→email HTML), System.Security.Cryptography.ProtectedData (DPAPI), MailerLite REST API (`connect.mailerlite.com/api`), Claude CLI via existing `ILlmBackend`.

**Spec:** `docs/superpowers/specs/2026-07-18-newsletter-first-design.md` · **Mockups:** `docs/mockups/08-newsletter-flow.md`, `08a-newsletter-issue-walkthrough.md`

## Global Constraints

- Dependencies point inward only: Web → Application → Domain; Infrastructure implements Domain abstractions; DI wiring in `Program.cs`.
- Every tenant-owned row carries `TenantId`.
- No platform credentials in SQLite — `ICredentialStore` (DPAPI) only.
- Send is never automated — the connector only creates/updates **draft** campaigns.
- Enum properties stored as strings (`HasConversion<string>()` in `AppDbContext.OnModelCreating`).
- JSON config columns (`ConfigJson`, `StatsJson`, `SourceIdsJson`) keep schema stable; parse with `PropertyNameCaseInsensitive = true`.
- Build/tests must run in **Release** (`-c Release`) while a dev instance runs from Visual Studio (Debug DLLs are locked). Never `dotnet run` for UI verification — publish per `.claude/skills/verify` (dev-server static assets 500).
- Tests: unit tests in `tests/ContentAutomatorX.UnitTests` (fixtures under `Fixtures/`, copied to output), integration tests in `tests/ContentAutomatorX.IntegrationTests` using `TestDb.Create()` (real migrations on temp SQLite).
- Run tests: `dotnet test ContentAutomatorX.slnx -c Release --nologo -v q` (or filter: `--filter FullyQualifiedName~<TestClass>`).
- EF migrations: `dotnet ef migrations add <Name> --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web` (design-time factory exists).
- Commit after every green task; messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Platform & Post entities + migration

**Files:**
- Create: `src/ContentAutomatorX.Domain/Entities/Platform.cs`
- Create: `src/ContentAutomatorX.Domain/Entities/Post.cs`
- Modify: `src/ContentAutomatorX.Domain/Constants.cs` (append `PlatformTypes`)
- Modify: `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs` (two DbSets)
- Modify: `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs` (DbSets + model config)
- Modify: `tests/ContentAutomatorX.IntegrationTests/IngestionPipelineTests.cs:28-50` (`SaveHookDbContext` gains the two DbSet pass-throughs — it implements `IAppDbContext`)
- Test: `tests/ContentAutomatorX.IntegrationTests/PersistenceTests.cs` (append tests)
- Generated: `src/ContentAutomatorX.Infrastructure/Migrations/*_NewsletterPlatformPost.cs`

**Interfaces:**
- Consumes: existing `IAppDbContext`, `TestDb`.
- Produces: `Platform` (Id, TenantId, Type, DisplayName, ColorHex, ConfigJson, CredentialRef, IsEnabled), `Post` (Id, TenantId, PlatformId, RecipeId?, DraftId?, Kind, Title, Subject?, PreviewText?, Status: `PostStatus { Draft, Pushed, Published, Failed }`, NeedsReview, SourceIdsJson?, WindowDays, ScheduledAt?, PublishedAt?, ExternalId?, ExternalUrl?, StatsJson, CreatedAt), `PlatformTypes.MailerLite`, `db.Platforms`, `db.Posts`. All later tasks depend on these exact names.

- [ ] **Step 1: Write the failing persistence test**

Append to `tests/ContentAutomatorX.IntegrationTests/PersistenceTests.cs`:

```csharp
[Fact]
public async Task Platform_and_post_round_trip_with_string_status()
{
    using var test = TestDb.Create();
    var tenant = new Tenant { Name = "T", Slug = "t-plat" };
    var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "MailerLite" };
    var post = new Post
    {
        TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
        Title = "AI Weekly #1", Status = PostStatus.Pushed, NeedsReview = true,
        SourceIdsJson = "[\"" + Guid.NewGuid() + "\"]", WindowDays = 7
    };
    test.Db.Tenants.Add(tenant);
    test.Db.Platforms.Add(platform);
    test.Db.Posts.Add(post);
    await test.Db.SaveChangesAsync();

    using var fresh = test.NewContext();
    var loaded = await fresh.Posts.SingleAsync(p => p.Id == post.Id);
    Assert.Equal(PostStatus.Pushed, loaded.Status);
    Assert.True(loaded.NeedsReview);
    Assert.Equal(7, loaded.WindowDays);
    var statusText = await fresh.Database.SqlQuery<string>(
        $"SELECT Status AS Value FROM Posts WHERE Id = {post.Id}").SingleAsync();
    Assert.Equal("Pushed", statusText); // enum stored as string
}
```

(Match the file's existing usings; it already uses `TestDb`, `Microsoft.EntityFrameworkCore`, Domain entities.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests -c Release --filter FullyQualifiedName~PersistenceTests --nologo`
Expected: COMPILE ERROR — `Platform`/`Post`/`PostStatus`/`Platforms` do not exist.

- [ ] **Step 3: Create the entities and constants**

`src/ContentAutomatorX.Domain/Entities/Platform.cs`:

```csharp
namespace ContentAutomatorX.Domain.Entities;

public class Platform
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Type { get; set; }            // PlatformTypes.*
    public required string DisplayName { get; set; }
    public string ColorHex { get; set; } = "#1e88e5";
    public string ConfigJson { get; set; } = "{}";        // MailerLite: {groupId, groupName, fromName, fromEmail}
    public string? CredentialRef { get; set; }            // ICredentialStore blob name, e.g. "mailerlite:<id>"
    public bool IsEnabled { get; set; } = true;
}
```

`src/ContentAutomatorX.Domain/Entities/Post.cs`:

```csharp
namespace ContentAutomatorX.Domain.Entities;

public enum PostStatus { Draft, Pushed, Published, Failed }

public class Post
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PlatformId { get; set; }
    public Guid? RecipeId { get; set; }        // the automation this issue is based on
    public Guid? DraftId { get; set; }         // content payload; null until composed/typed
    public required string Kind { get; set; }  // DraftKinds.*
    public required string Title { get; set; }
    public string? Subject { get; set; }
    public string? PreviewText { get; set; }
    public PostStatus Status { get; set; } = PostStatus.Draft;
    public bool NeedsReview { get; set; }
    public string? SourceIdsJson { get; set; } // this issue's source set; null = automation's set
    public int WindowDays { get; set; } = 7;   // material window for candidate selection
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ExternalId { get; set; }    // MailerLite campaign id
    public string? ExternalUrl { get; set; }
    public string StatsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

Append to `src/ContentAutomatorX.Domain/Constants.cs`:

```csharp
public static class PlatformTypes
{
    public const string MailerLite = "MailerLite";
}
```

- [ ] **Step 4: Wire DbSets**

`IAppDbContext.cs` — add inside the interface:

```csharp
DbSet<Platform> Platforms { get; }
DbSet<Post> Posts { get; }
```

`AppDbContext.cs` — add properties:

```csharp
public DbSet<Platform> Platforms => Set<Platform>();
public DbSet<Post> Posts => Set<Post>();
```

and in `OnModelCreating` append:

```csharp
b.Entity<Post>().Property(p => p.Status).HasConversion<string>();
b.Entity<Post>().HasIndex(p => new { p.TenantId, p.Status });
b.Entity<Platform>().HasIndex(p => new { p.TenantId, p.Type });
```

`IngestionPipelineTests.cs` — `SaveHookDbContext` implements `IAppDbContext`; add the two pass-throughs next to the existing ones:

```csharp
public DbSet<Platform> Platforms => inner.Platforms;
public DbSet<Post> Posts => inner.Posts;
```

- [ ] **Step 5: Add the migration**

Run: `dotnet ef migrations add NewsletterPlatformPost --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web`
Expected: new migration creating tables `Platforms` and `Posts`. Inspect it — no changes to existing tables.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests -c Release --filter FullyQualifiedName~PersistenceTests --nologo`
Expected: PASS (all tests in class).

- [ ] **Step 7: Full test suite + commit**

Run: `dotnet test ContentAutomatorX.slnx -c Release --nologo -v q`
Expected: all green.

```bash
git add -A
git commit -m "feat: Platform and Post entities with migration (newsletter foundation)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: ICredentialStore + DPAPI implementation

**Files:**
- Create: `src/ContentAutomatorX.Domain/Abstractions/ICredentialStore.cs`
- Create: `src/ContentAutomatorX.Infrastructure/Security/DpapiCredentialStore.cs`
- Modify: `src/ContentAutomatorX.Infrastructure/ContentAutomatorX.Infrastructure.csproj` (package)
- Modify: `src/ContentAutomatorX.Web/Program.cs` (DI)
- Test: `tests/ContentAutomatorX.UnitTests/DpapiCredentialStoreTests.cs`

**Interfaces:**
- Produces: `ICredentialStore { Task SetAsync(string name, string secret, CancellationToken ct = default); Task<string?> GetAsync(string name, CancellationToken ct = default); Task DeleteAsync(string name, CancellationToken ct = default); }` and `DpapiCredentialStore(string? rootDir = null)` (default root `%LOCALAPPDATA%/ContentAutomatorX/secrets`). Tasks 7/10 consume `ICredentialStore`.

- [ ] **Step 1: Write the failing tests**

`tests/ContentAutomatorX.UnitTests/DpapiCredentialStoreTests.cs`:

```csharp
using ContentAutomatorX.Infrastructure.Security;

namespace ContentAutomatorX.UnitTests;

public class DpapiCredentialStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cax-secrets-{Guid.NewGuid():N}");

    [Fact]
    public async Task Round_trips_a_secret_and_stores_it_encrypted()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync("mailerlite:abc", "s3cret-key");

        Assert.Equal("s3cret-key", await store.GetAsync("mailerlite:abc"));
        var file = Directory.GetFiles(_dir).Single();
        Assert.DoesNotContain("s3cret-key", await File.ReadAllTextAsync(file)); // not plaintext
    }

    [Fact]
    public async Task Get_missing_returns_null_and_delete_is_idempotent()
    {
        var store = new DpapiCredentialStore(_dir);
        Assert.Null(await store.GetAsync("nope"));
        await store.DeleteAsync("nope"); // must not throw
        await store.SetAsync("a", "1");
        await store.DeleteAsync("a");
        Assert.Null(await store.GetAsync("a"));
    }

    [Fact]
    public async Task Name_with_separator_chars_is_sanitized_to_a_safe_filename()
    {
        var store = new DpapiCredentialStore(_dir);
        await store.SetAsync(@"weird:name/with\chars", "v");
        Assert.Equal("v", await store.GetAsync(@"weird:name/with\chars"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter FullyQualifiedName~DpapiCredentialStoreTests --nologo`
Expected: COMPILE ERROR — `DpapiCredentialStore` not found.

- [ ] **Step 3: Implement**

Add to `ContentAutomatorX.Infrastructure.csproj` ItemGroup with packages:

```xml
<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.0.0" />
```

(If `dotnet restore` reports the version unavailable, use the newest 8.x/9.x listed by `dotnet package search System.Security.Cryptography.ProtectedData` — any recent version works.)

`src/ContentAutomatorX.Domain/Abstractions/ICredentialStore.cs`:

```csharp
namespace ContentAutomatorX.Domain.Abstractions;

/// <summary>Secrets live outside SQLite (Phase 1 decision). Names are logical, e.g. "mailerlite:{platformId}".</summary>
public interface ICredentialStore
{
    Task SetAsync(string name, string secret, CancellationToken ct = default);
    Task<string?> GetAsync(string name, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
}
```

`src/ContentAutomatorX.Infrastructure/Security/DpapiCredentialStore.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using ContentAutomatorX.Domain.Abstractions;

namespace ContentAutomatorX.Infrastructure.Security;

/// <summary>DPAPI (CurrentUser) blobs, one file per secret. Windows-only by design for the
/// local phase; a server deployment later swaps this implementation behind ICredentialStore.</summary>
public class DpapiCredentialStore(string? rootDir = null) : ICredentialStore
{
    private readonly string _root = rootDir ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ContentAutomatorX", "secrets");

    public async Task SetAsync(string name, string secret, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_root);
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(PathFor(name), blob, ct);
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return null;
        var blob = await File.ReadAllBytesAsync(path, ct);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
    }

    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string name)
    {
        var safe = new StringBuilder(name.Length);
        foreach (var c in name)
            safe.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return Path.Combine(_root, safe + ".bin");
    }
}
```

DI in `Program.cs`, next to the other singletons (after the `ILlmBackend` line):

```csharp
builder.Services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
```

(`ICredentialStore` is in `ContentAutomatorX.Domain.Abstractions`, already imported; add `using ContentAutomatorX.Infrastructure.Security;`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter FullyQualifiedName~DpapiCredentialStoreTests --nologo`
Expected: 3 PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: ICredentialStore with DPAPI implementation

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: WebsiteConnector (site watch source)

**Files:**
- Create: `src/ContentAutomatorX.Infrastructure/Sources/WebsiteConnector.cs`
- Modify: `src/ContentAutomatorX.Domain/Constants.cs` (`SourceTypes.Website`)
- Modify: `src/ContentAutomatorX.Infrastructure/ContentAutomatorX.Infrastructure.csproj` (AngleSharp)
- Modify: `src/ContentAutomatorX.Web/Program.cs` (HttpClient + connector DI)
- Create: `tests/ContentAutomatorX.UnitTests/Fixtures/sample-site-listing.html`
- Create: `tests/ContentAutomatorX.UnitTests/Fixtures/sample-site-article.html`
- Test: `tests/ContentAutomatorX.UnitTests/WebsiteConnectorTests.cs`

**Interfaces:**
- Consumes: `ISourceConnector`, `FetchedItem`, `StubHttpHandler`.
- Produces: `SourceTypes.Website = "Website"`; connector config JSON `{ "url": string, "mode": "auto"|"selector", "itemSelector": string?, "maxItems": int (default 10) }`. ExternalId = absolute item URL.

- [ ] **Step 1: Create fixtures**

`tests/ContentAutomatorX.UnitTests/Fixtures/sample-site-listing.html`:

```html
<!DOCTYPE html>
<html><body>
<main>
  <article><h2><a href="/posts/alpha">Alpha release notes for the new engine</a></h2><p>teaser</p></article>
  <article><h2><a href="https://blog.example.com/posts/beta">Beta workflow deep dive tutorial</a></h2></article>
  <a href="/nav">nav</a>
  <div class="card"><a href="/posts/gamma">Gamma model comparison megathread</a></div>
</main>
</body></html>
```

`tests/ContentAutomatorX.UnitTests/Fixtures/sample-site-article.html`:

```html
<!DOCTYPE html>
<html><body><main><h1>Alpha release notes</h1>
<p>The quick brown fox paragraph with the real article body text.</p></main></body></html>
```

(The csproj already copies `Fixtures/**` to output.)

- [ ] **Step 2: Write the failing tests**

`tests/ContentAutomatorX.UnitTests/WebsiteConnectorTests.cs`:

```csharp
using System.Net;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Infrastructure.Sources;

namespace ContentAutomatorX.UnitTests;

public class WebsiteConnectorTests
{
    private static StubHttpHandler SiteHandler() => new(req =>
    {
        var path = req.RequestUri!.AbsolutePath;
        var file = path == "/blog" ? "Fixtures/sample-site-listing.html" : "Fixtures/sample-site-article.html";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(File.ReadAllText(file), System.Text.Encoding.UTF8, "text/html")
        };
    });

    private static Source Site(string config) => new()
    {
        Type = SourceTypes.Website, DisplayName = "blog", ConfigJson = config
    };

    [Fact]
    public async Task Auto_mode_extracts_article_links_with_absolute_urls_and_bodies()
    {
        var connector = new WebsiteConnector(new HttpClient(SiteHandler()));
        var items = await connector.FetchAsync(Site("""{"url":"https://blog.example.com/blog","mode":"auto"}"""));

        Assert.Contains(items, i => i.ExternalId == "https://blog.example.com/posts/alpha");
        Assert.Contains(items, i => i.ExternalId == "https://blog.example.com/posts/beta");
        var alpha = items.Single(i => i.ExternalId.EndsWith("/posts/alpha"));
        Assert.Equal("Alpha release notes for the new engine", alpha.Title);
        Assert.Contains("quick brown fox", alpha.Body);
        Assert.DoesNotContain(items, i => i.ExternalId.EndsWith("/nav")); // short link text filtered
    }

    [Fact]
    public async Task Selector_mode_uses_the_configured_css_selector()
    {
        var connector = new WebsiteConnector(new HttpClient(SiteHandler()));
        var items = await connector.FetchAsync(Site(
            """{"url":"https://blog.example.com/blog","mode":"selector","itemSelector":".card a"}"""));

        var item = Assert.Single(items);
        Assert.Equal("https://blog.example.com/posts/gamma", item.ExternalId);
        Assert.Equal("Gamma model comparison megathread", item.Title);
    }

    [Fact]
    public async Task Body_fetch_failure_still_yields_the_item_with_empty_body()
    {
        var handler = new StubHttpHandler(req =>
            req.RequestUri!.AbsolutePath == "/blog"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(File.ReadAllText("Fixtures/sample-site-listing.html"),
                        System.Text.Encoding.UTF8, "text/html")
                }
                : new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var connector = new WebsiteConnector(new HttpClient(handler));

        var items = await connector.FetchAsync(Site("""{"url":"https://blog.example.com/blog","mode":"auto"}"""));

        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Equal("", i.Body));
    }

    [Fact]
    public async Task MaxItems_caps_the_result()
    {
        var connector = new WebsiteConnector(new HttpClient(SiteHandler()));
        var items = await connector.FetchAsync(Site(
            """{"url":"https://blog.example.com/blog","mode":"auto","maxItems":1}"""));
        Assert.Single(items);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter FullyQualifiedName~WebsiteConnectorTests --nologo`
Expected: COMPILE ERROR — `WebsiteConnector`/`SourceTypes.Website` not found.

- [ ] **Step 4: Implement**

Append to `SourceTypes` in `Constants.cs`:

```csharp
public const string Website = "Website";
```

Add to Infrastructure csproj packages:

```xml
<PackageReference Include="AngleSharp" Version="1.1.2" />
```

`src/ContentAutomatorX.Infrastructure/Sources/WebsiteConnector.cs`:

```csharp
using System.Text.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

public class WebsiteConnector(HttpClient http) : ISourceConnector
{
    public string Type => SourceTypes.Website;

    private const int BodyMaxChars = 8000;
    private const int MinLinkTextLength = 20; // auto mode: skip nav/chrome links

    private record WebsiteConfig(string Url, string? Mode, string? ItemSelector, int MaxItems = 10);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly HtmlParser Parser = new();

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<WebsiteConfig>(source.ConfigJson, JsonOpts)
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid Website config");
        var baseUri = new Uri(config.Url);

        var listingHtml = await http.GetStringAsync(config.Url, ct);
        using var doc = await Parser.ParseDocumentAsync(listingHtml, ct);

        var anchors = (config.Mode == "selector" && !string.IsNullOrWhiteSpace(config.ItemSelector)
                ? doc.QuerySelectorAll(config.ItemSelector).OfType<IHtmlAnchorElement>()
                    .Concat(doc.QuerySelectorAll(config.ItemSelector)
                        .SelectMany(e => e.QuerySelectorAll("a").OfType<IHtmlAnchorElement>()))
                : doc.QuerySelectorAll("article a[href], main a[href]").OfType<IHtmlAnchorElement>()
                    .Where(a => (a.TextContent?.Trim().Length ?? 0) >= MinLinkTextLength))
            .Where(a => !string.IsNullOrWhiteSpace(a.GetAttribute("href")))
            .ToList();

        var items = new List<FetchedItem>();
        var seen = new HashSet<string>();
        foreach (var a in anchors)
        {
            if (items.Count >= config.MaxItems) break;
            if (!Uri.TryCreate(baseUri, a.GetAttribute("href"), out var abs)) continue;
            var url = abs.GetLeftPart(UriPartial.Path); // canonical: strip query/fragment
            if (!seen.Add(url)) continue;

            var title = a.TextContent.Trim();
            if (title.Length == 0) continue;
            items.Add(new FetchedItem(
                ExternalId: url, Title: title, Url: url, Author: null,
                Body: await FetchBodyAsync(url, ct),
                MetadataJson: "{}", PublishedAt: null));
        }
        return items;
    }

    private async Task<string> FetchBodyAsync(string url, CancellationToken ct)
    {
        try
        {
            var html = await http.GetStringAsync(url, ct);
            using var doc = await Parser.ParseDocumentAsync(html, ct);
            var text = (doc.QuerySelector("main") ?? doc.QuerySelector("article") ?? doc.Body)?
                .TextContent ?? "";
            text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length <= BodyMaxChars ? text : text[..BodyMaxChars];
        }
        catch
        {
            return ""; // item still lands with title+url; per-source failure isolation stays in the pipeline
        }
    }
}
```

DI in `Program.cs` next to the other connectors:

```csharp
builder.Services.AddHttpClient<WebsiteConnector>().AddStandardResilienceHandler();
builder.Services.AddTransient<ISourceConnector>(sp => sp.GetRequiredService<WebsiteConnector>());
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter FullyQualifiedName~WebsiteConnectorTests --nologo`
Expected: 4 PASS. If the selector-mode test double-matches (anchor selected directly AND via descendant scan), dedup by `seen` already collapses it — assert stays valid.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: Website source connector (auto + CSS-selector extraction)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: LlmResearchConnector + Claude CLI extra args

**Files:**
- Create: `src/ContentAutomatorX.Infrastructure/Sources/LlmResearchConnector.cs`
- Modify: `src/ContentAutomatorX.Domain/Constants.cs` (`SourceTypes.LlmResearch`)
- Modify: `src/ContentAutomatorX.Infrastructure/Llm/ClaudeCliBackend.cs:7-24` (ExtraArgs option)
- Modify: `src/ContentAutomatorX.Web/Program.cs` (connector DI)
- Modify: `src/ContentAutomatorX.Web/appsettings.json` (document `Claude:ExtraArgs`)
- Test: `tests/ContentAutomatorX.UnitTests/LlmResearchConnectorTests.cs`
- Test: `tests/ContentAutomatorX.UnitTests/ClaudeCliBackendTests.cs` (append one test)

**Interfaces:**
- Consumes: `ILlmBackend.GenerateAsync(string, CancellationToken)` → `LlmResult(string Text, string Model)`.
- Produces: `SourceTypes.LlmResearch = "LlmResearch"`; config JSON `{ "prompt": string, "maxItems": int (default 10) }`; `ClaudeCliOptions.ExtraArgs` (string?, appended to CLI args — set `"Claude": { "ExtraArgs": "--allowedTools WebSearch" }` to enable web search).

- [ ] **Step 1: Write the failing tests**

`tests/ContentAutomatorX.UnitTests/LlmResearchConnectorTests.cs`:

```csharp
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Sources;

namespace ContentAutomatorX.UnitTests;

public class QueueLlm(params string[] replies) : ILlmBackend
{
    private readonly Queue<string> _replies = new(replies);
    public List<string> Prompts { get; } = [];
    public string Name => "fake";
    public Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        Prompts.Add(prompt);
        return Task.FromResult(new LlmResult(_replies.Dequeue(), "fake-model"));
    }
}

public class LlmResearchConnectorTests
{
    private static Source Research(int maxItems = 10) => new()
    {
        Type = SourceTypes.LlmResearch, DisplayName = "sweep",
        ConfigJson = $$"""{"prompt":"top AI image-gen news this week","maxItems":{{maxItems}}}"""
    };

    private const string GoodJson =
        """[{"title":"Flux 2 rumors","url":"https://ex.com/1","summary":"what we know","source":"ex.com"}]""";

    [Fact]
    public async Task Parses_items_from_strict_json_reply()
    {
        var connector = new LlmResearchConnector(new QueueLlm(GoodJson));
        var items = await connector.FetchAsync(Research());

        var item = Assert.Single(items);
        Assert.Equal("https://ex.com/1", item.ExternalId);
        Assert.Equal("Flux 2 rumors", item.Title);
        Assert.Equal("what we know", item.Body);
        Assert.Contains("llm-research", item.MetadataJson);
    }

    [Fact]
    public async Task Strips_markdown_fences_before_parsing()
    {
        var fenced = "```json\n" + GoodJson + "\n```";
        var connector = new LlmResearchConnector(new QueueLlm(fenced));
        Assert.Single(await connector.FetchAsync(Research()));
    }

    [Fact]
    public async Task Retries_once_on_malformed_json_then_succeeds()
    {
        var llm = new QueueLlm("Sure! Here are the news items I found:", GoodJson);
        var connector = new LlmResearchConnector(llm);

        var items = await connector.FetchAsync(Research());

        Assert.Single(items);
        Assert.Equal(2, llm.Prompts.Count);
        Assert.Contains("ONLY the JSON array", llm.Prompts[1]);
    }

    [Fact]
    public async Task Caps_at_max_items_and_skips_entries_without_url_or_title()
    {
        var many = """
        [{"title":"a","url":"https://ex.com/a","summary":"s"},
         {"title":"","url":"https://ex.com/empty","summary":"s"},
         {"title":"no-url","url":"","summary":"s"},
         {"title":"b","url":"https://ex.com/b","summary":"s"},
         {"title":"c","url":"https://ex.com/c","summary":"s"}]
        """;
        var connector = new LlmResearchConnector(new QueueLlm(many));
        var items = await connector.FetchAsync(Research(maxItems: 2));
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.StartsWith("https://ex.com/", i.ExternalId));
    }
}
```

Append to `ClaudeCliBackendTests.cs` (match its existing fake `IProcessRunner` pattern — the file already fakes runner results; follow the same helper it uses):

```csharp
[Fact]
public async Task Extra_args_are_appended_to_the_cli_invocation()
{
    // Arrange a runner fake capturing args (reuse the file's existing fake/capture helper),
    // options with ExtraArgs = "--allowedTools WebSearch".
    // Act GenerateAsync("hi").
    // Assert captured args contain "-p --output-format json" and end with "--allowedTools WebSearch".
}
```

(Write the body against the file's actual fake — it exists at `tests/ContentAutomatorX.UnitTests/ClaudeCliBackendTests.cs`; read it first and mirror its arrange helpers. The assertion contract above is fixed.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter "FullyQualifiedName~LlmResearchConnectorTests|FullyQualifiedName~ClaudeCliBackendTests" --nologo`
Expected: COMPILE ERROR — `LlmResearchConnector` / `ExtraArgs` not found.

- [ ] **Step 3: Implement**

`Constants.cs` — append to `SourceTypes`:

```csharp
public const string LlmResearch = "LlmResearch";
```

`ClaudeCliBackend.cs` — add to `ClaudeCliOptions`:

```csharp
/// <summary>Appended verbatim to the CLI args. E.g. "--allowedTools WebSearch" lets
/// LlmResearch sources actually search the web. Configured via appsettings Claude:ExtraArgs.</summary>
public string? ExtraArgs { get; set; }
```

and in `GenerateAsync` after the model append:

```csharp
if (!string.IsNullOrWhiteSpace(options.ExtraArgs)) args += $" {options.ExtraArgs}";
```

`src/ContentAutomatorX.Infrastructure/Sources/LlmResearchConnector.cs`:

```csharp
using System.Text.Json;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

/// <summary>"AI as a source": one LLM call (ideally with web search enabled via
/// Claude:ExtraArgs) returns found articles as strict JSON; each becomes a ContentItem.
/// It finds material — it never writes the newsletter.</summary>
public class LlmResearchConnector(ILlmBackend llm) : ISourceConnector
{
    public string Type => SourceTypes.LlmResearch;

    private record ResearchConfig(string Prompt, int MaxItems = 10);
    private record ResearchItem(string? Title, string? Url, string? Summary, string? Source);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<ResearchConfig>(source.ConfigJson, JsonOpts)
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid LlmResearch config");

        var prompt = BuildPrompt(config, retry: false);
        var reply = await llm.GenerateAsync(prompt, ct);
        if (!TryParse(reply.Text, out var parsed))
        {
            reply = await llm.GenerateAsync(BuildPrompt(config, retry: true), ct);
            if (!TryParse(reply.Text, out parsed))
                throw new InvalidOperationException(
                    $"LlmResearch source {source.DisplayName}: model did not return valid JSON after retry");
        }

        return parsed!
            .Where(i => !string.IsNullOrWhiteSpace(i.Url) && !string.IsNullOrWhiteSpace(i.Title))
            .Take(config.MaxItems)
            .Select(i => new FetchedItem(
                ExternalId: i.Url!.Trim(), Title: i.Title!.Trim(), Url: i.Url!.Trim(),
                Author: i.Source, Body: i.Summary?.Trim() ?? "",
                MetadataJson: """{"via":"llm-research"}""", PublishedAt: null))
            .ToList();
    }

    private static string BuildPrompt(ResearchConfig config, bool retry) =>
        $"""
        You are a research assistant with web access. Task: {config.Prompt}

        Find up to {config.MaxItems} relevant, recent items. Respond with ONLY a JSON array,
        no prose, no markdown fences: [{{"title": "...", "url": "...", "summary": "1-2 sentences", "source": "site name"}}]
        {(retry ? "\nYour previous reply was not valid JSON. Return ONLY the JSON array this time." : "")}
        """;

    private static bool TryParse(string text, out List<ResearchItem>? items)
    {
        items = null;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }
        try
        {
            items = JsonSerializer.Deserialize<List<ResearchItem>>(trimmed, JsonOpts);
            return items is { Count: >= 0 };
        }
        catch (JsonException) { return false; }
    }
}
```

DI in `Program.cs` (no HttpClient — it rides `ILlmBackend`):

```csharp
builder.Services.AddTransient<ISourceConnector, LlmResearchConnector>();
```

`appsettings.json` — inside the existing `"Claude"` section add (or create the key commented for discoverability):

```json
"ExtraArgs": ""
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter "FullyQualifiedName~LlmResearchConnectorTests|FullyQualifiedName~ClaudeCliBackendTests" --nologo`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: LlmResearch source connector + Claude CLI ExtraArgs (web search)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: EmailHtmlRenderer (markdown → email-safe HTML)

**Files:**
- Create: `src/ContentAutomatorX.Application/Newsletter/EmailHtmlRenderer.cs`
- Modify: `src/ContentAutomatorX.Application/ContentAutomatorX.Application.csproj` (Markdig)
- Test: `tests/ContentAutomatorX.UnitTests/EmailHtmlRendererTests.cs`

**Interfaces:**
- Produces: `static string EmailHtmlRenderer.Render(string markdown, string title)` — full HTML document, single-column, inline styles only. Consumed by Task 7 (`PostService.PushAsync`) and Task 13 (editor preview).

- [ ] **Step 1: Write the failing tests**

`tests/ContentAutomatorX.UnitTests/EmailHtmlRendererTests.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class EmailHtmlRendererTests
{
    [Fact]
    public void Renders_headings_paragraphs_and_links_with_inline_styles()
    {
        var html = EmailHtmlRenderer.Render(
            "# Top stories\n\nBig [thing](https://ex.com) happened.\n\n- one\n- two", "AI Weekly #1");

        Assert.Contains("<html", html);
        Assert.Contains("AI Weekly #1", html);
        Assert.Contains("Top stories", html);
        Assert.Contains("href=\"https://ex.com\"", html);
        Assert.Contains("<li", html);
        Assert.Contains("style=", html);           // inline styles present
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Escapes_raw_html_in_the_markdown()
    {
        var html = EmailHtmlRenderer.Render("hello <script>alert(1)</script> world", "t");
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter FullyQualifiedName~EmailHtmlRendererTests --nologo`
Expected: COMPILE ERROR.

- [ ] **Step 3: Implement**

Add to Application csproj packages:

```xml
<PackageReference Include="Markdig" Version="0.37.0" />
```

`src/ContentAutomatorX.Application/Newsletter/EmailHtmlRenderer.cs`:

```csharp
using System.Text.RegularExpressions;
using Markdig;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>One fixed, email-safe template (spec decision: per-tenant templates are a later
/// nicety). Inline styles only; raw HTML in the markdown is escaped, not passed through.</summary>
public static partial class EmailHtmlRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UsePipeTables()
        .DisableHtml() // raw HTML → escaped text; keeps the campaign script/iframe-free
        .Build();

    public static string Render(string markdown, string title)
    {
        var body = Markdown.ToHtml(markdown ?? "", Pipeline);
        body = InlineStyles(body);
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>{safeTitle}</title></head>
        <body style="margin:0;padding:0;background:#f4f4f4;">
          <div style="max-width:640px;margin:0 auto;padding:24px;background:#ffffff;
                      font-family:Segoe UI,Arial,sans-serif;font-size:16px;line-height:1.6;color:#222222;">
        {body}
          </div>
        </body>
        </html>
        """;
    }

    // InlineStyles implemented below the class snippet
}
```

with `InlineStyles` (anchors first — Markdig emits `<a href="...">`):

```csharp
private static string InlineStyles(string html)
{
    html = AnchorRegex().Replace(html, "<a style=\"color:#1e88e5;\" $1>");
    return html
        .Replace("<h1>", "<h1 style=\"font-size:26px;margin:24px 0 12px;color:#111111;\">")
        .Replace("<h2>", "<h2 style=\"font-size:21px;margin:20px 0 10px;color:#111111;\">")
        .Replace("<h3>", "<h3 style=\"font-size:18px;margin:16px 0 8px;color:#111111;\">")
        .Replace("<p>", "<p style=\"margin:0 0 14px;\">")
        .Replace("<ul>", "<ul style=\"margin:0 0 14px;padding-left:24px;\">")
        .Replace("<li>", "<li style=\"margin:0 0 6px;\">")
        .Replace("<blockquote>", "<blockquote style=\"margin:0 0 14px;padding:8px 16px;border-left:3px solid #1e88e5;color:#444444;\">")
        .Replace("<hr />", "<hr style=\"border:none;border-top:1px solid #dddddd;margin:20px 0;\" />");
}

[GeneratedRegex("<a (href=\"[^\"]*\")>")]
private static partial Regex AnchorRegex();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter FullyQualifiedName~EmailHtmlRendererTests --nologo`
Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: email-safe HTML renderer for newsletter issues (Markdig)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: IMailerLiteClient + HTTP implementation

**Files:**
- Create: `src/ContentAutomatorX.Domain/Abstractions/IMailerLiteClient.cs`
- Create: `src/ContentAutomatorX.Domain/Models/MailerLiteModels.cs`
- Create: `src/ContentAutomatorX.Infrastructure/Platforms/MailerLiteClient.cs`
- Modify: `src/ContentAutomatorX.Web/Program.cs` (DI)
- Test: `tests/ContentAutomatorX.UnitTests/MailerLiteClientTests.cs`

**Interfaces:**
- Produces (Domain):

```csharp
public record MailerLiteGroup(string Id, string Name);
public record MailerLiteCampaignStatus(string Status, int? Sent, int? OpensCount, int? ClicksCount);
public record MailerLiteDraft(string Name, string Subject, string? PreviewText,
    string FromName, string FromEmail, string GroupId, string Html);

public interface IMailerLiteClient
{
    Task<bool> TestAsync(string apiKey, CancellationToken ct = default);
    Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(string apiKey, CancellationToken ct = default);
    /// <returns>Campaign id (creates when existingCampaignId is null, else updates).</returns>
    Task<string> PushDraftAsync(string apiKey, MailerLiteDraft draft, string? existingCampaignId, CancellationToken ct = default);
    Task<MailerLiteCampaignStatus> GetStatusAsync(string apiKey, string campaignId, CancellationToken ct = default);
}
```

- Consumed by Tasks 7, 9, 10. API key flows in per call (multi-tenant-safe, no client state).

- [ ] **Step 1: Write the failing tests**

`tests/ContentAutomatorX.UnitTests/MailerLiteClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Platforms;

namespace ContentAutomatorX.UnitTests;

public class MailerLiteClientTests
{
    private static readonly MailerLiteDraft Draft = new(
        Name: "AI Weekly #1", Subject: "subj", PreviewText: "pv",
        FromName: "AIVisions", FromEmail: "news@example.com", GroupId: "g1", Html: "<html></html>");

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ListGroups_sends_bearer_and_parses_groups()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":[{"id":"g1","name":"Main list"}]}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var groups = await client.ListGroupsAsync("KEY");

        var g = Assert.Single(groups);
        Assert.Equal(("g1", "Main list"), (g.Id, g.Name));
        var req = Assert.Single(handler.Requests);
        Assert.Equal("Bearer KEY", req.Headers.Authorization!.ToString());
        Assert.EndsWith("/groups", req.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PushDraft_creates_a_campaign_and_returns_its_id()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"draft"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var id = await client.PushDraftAsync("KEY", Draft, existingCampaignId: null);

        Assert.Equal("c42", id);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.EndsWith("/campaigns", req.RequestUri!.AbsolutePath);
        var body = await req.Content!.ReadAsStringAsync();
        Assert.Contains("\"subject\":\"subj\"", body);
        Assert.Contains("\"groups\":[\"g1\"]", body);
        Assert.Contains("\"type\":\"regular\"", body);
    }

    [Fact]
    public async Task PushDraft_with_existing_id_updates_via_put()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"draft"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var id = await client.PushDraftAsync("KEY", Draft, existingCampaignId: "c42");

        Assert.Equal("c42", id);
        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.EndsWith("/campaigns/c42", req.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetStatus_parses_sent_status_with_stats()
    {
        var handler = new StubHttpHandler(_ => Json(
            """{"data":{"id":"c42","status":"sent","stats":{"sent":1204,"opens_count":577,"clicks_count":89}}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));

        var status = await client.GetStatusAsync("KEY", "c42");

        Assert.Equal("sent", status.Status);
        Assert.Equal(1204, status.Sent);
        Assert.Equal(577, status.OpensCount);
        Assert.Equal(89, status.ClicksCount);
    }

    [Fact]
    public async Task GetStatus_without_stats_yields_nulls()
    {
        var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"draft"}}"""));
        var client = new MailerLiteClient(new HttpClient(handler));
        var status = await client.GetStatusAsync("KEY", "c42");
        Assert.Equal("draft", status.Status);
        Assert.Null(status.Sent);
    }

    [Fact]
    public async Task Unauthorized_throws_with_actionable_message()
    {
        var handler = new StubHttpHandler(_ => Json("""{"message":"Unauthenticated."}""", HttpStatusCode.Unauthorized));
        var client = new MailerLiteClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListGroupsAsync("BAD"));
        Assert.Contains("401", ex.Message);
        Assert.False(await client.TestAsync("BAD")); // TestAsync swallows into false
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter FullyQualifiedName~MailerLiteClientTests --nologo`
Expected: COMPILE ERROR.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Domain/Models/MailerLiteModels.cs` and `src/ContentAutomatorX.Domain/Abstractions/IMailerLiteClient.cs`: exactly the records/interface from the Interfaces block above (namespaces `ContentAutomatorX.Domain.Models` / `ContentAutomatorX.Domain.Abstractions`; the interface file needs `using ContentAutomatorX.Domain.Models;`).

`src/ContentAutomatorX.Infrastructure/Platforms/MailerLiteClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Platforms;

/// <summary>MailerLite "new" API (https://connect.mailerlite.com/api). Creates/updates DRAFT
/// campaigns only — sending stays a human act in MailerLite (spec: Send is never automated).</summary>
public class MailerLiteClient(HttpClient http) : IMailerLiteClient
{
    public const string BaseUrl = "https://connect.mailerlite.com/api";

    public async Task<bool> TestAsync(string apiKey, CancellationToken ct = default)
    {
        try { await ListGroupsAsync(apiKey, ct); return true; }
        catch { return false; }
    }

    public async Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(string apiKey, CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, "/groups", apiKey, payload: null, ct);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(g => new MailerLiteGroup(
                g.GetProperty("id").ToString(),
                g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""))
            .ToList();
    }

    public async Task<string> PushDraftAsync(string apiKey, MailerLiteDraft draft,
        string? existingCampaignId, CancellationToken ct = default)
    {
        var payload = new
        {
            name = draft.Name,
            type = "regular",
            groups = new[] { draft.GroupId },
            emails = new[]
            {
                new
                {
                    subject = draft.Subject,
                    preview_text = draft.PreviewText,
                    from_name = draft.FromName,
                    from = draft.FromEmail,
                    content = draft.Html
                }
            }
        };
        var (method, path) = existingCampaignId is null
            ? (HttpMethod.Post, "/campaigns")
            : (HttpMethod.Put, $"/campaigns/{existingCampaignId}");
        using var doc = await SendAsync(method, path, apiKey, payload, ct);
        return doc.RootElement.GetProperty("data").GetProperty("id").ToString();
    }

    public async Task<MailerLiteCampaignStatus> GetStatusAsync(string apiKey, string campaignId,
        CancellationToken ct = default)
    {
        using var doc = await SendAsync(HttpMethod.Get, $"/campaigns/{campaignId}", apiKey, payload: null, ct);
        var data = doc.RootElement.GetProperty("data");
        var status = data.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
        int? sent = null, opens = null, clicks = null;
        if (data.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            sent = TryInt(stats, "sent");
            opens = TryInt(stats, "opens_count");
            clicks = TryInt(stats, "clicks_count");
        }
        return new MailerLiteCampaignStatus(status, sent, opens, clicks);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, string apiKey,
        object? payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (payload is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"MailerLite {method} {path} failed: {(int)response.StatusCode} {Truncate(body)}");
        return JsonDocument.Parse(body);
    }

    private static int? TryInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : null;

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300] + "...";
}
```

DI in `Program.cs` (next to the connector HttpClients):

```csharp
builder.Services.AddHttpClient<MailerLiteClient>().AddStandardResilienceHandler();
builder.Services.AddTransient<IMailerLiteClient>(sp => sp.GetRequiredService<MailerLiteClient>());
```

(add `using ContentAutomatorX.Infrastructure.Platforms;`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests -c Release --filter FullyQualifiedName~MailerLiteClientTests --nologo`
Expected: 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: MailerLite client (groups, draft campaigns, status/stats)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: PlatformService + PostService (the newsletter use cases)

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/PlatformService.cs`
- Create: `src/ContentAutomatorX.Application/Services/PostService.cs`
- Create: `src/ContentAutomatorX.Application/Newsletter/MailerLiteConfig.cs`
- Modify: `src/ContentAutomatorX.Web/Program.cs` (DI: two scoped services)
- Test: `tests/ContentAutomatorX.IntegrationTests/PostServiceTests.cs`

**Interfaces:**
- Consumes: `IAppDbContext`, `ICredentialStore`, `IMailerLiteClient`, `ILlmBackend`, `GenerationPipeline`, `ItemSelector`, `EmailHtmlRenderer`, entities from Task 1.
- Produces (exact signatures later tasks call):

```csharp
public record MailerLiteConfig(string? GroupId, string? GroupName, string? FromName, string? FromEmail); // Application/Newsletter

public class PlatformService(IAppDbContext db, ICredentialStore credentials, IMailerLiteClient mailerLite)
{
    public Task<Platform> GetOrCreateMailerLiteAsync(Guid tenantId, CancellationToken ct = default);
    public Task SaveConfigAsync(Platform platform, MailerLiteConfig config, string? colorHex = null, CancellationToken ct = default);
    public MailerLiteConfig GetConfig(Platform platform);
    public Task SetApiKeyAsync(Platform platform, string apiKey, CancellationToken ct = default);
    public Task<string?> GetApiKeyAsync(Platform platform, CancellationToken ct = default);
    public Task<bool> TestAsync(Platform platform, CancellationToken ct = default);
    public Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(Platform platform, CancellationToken ct = default);
}

public class PostService(IAppDbContext db, GenerationPipeline generation, ILlmBackend llm,
    PlatformService platforms, IMailerLiteClient mailerLite)
{
    public Task<string> SuggestTitleAsync(Guid recipeId, CancellationToken ct = default);   // "{recipe.Name} #{n}"
    public Task<Post> CreateIssueAsync(Guid tenantId, Guid recipeId, int windowDays,
        IReadOnlyList<Guid>? sourceIds, string title, CancellationToken ct = default);
    public Task<Post?> GetAsync(Guid postId, CancellationToken ct = default);
    public Task<List<Post>> ListAsync(Guid tenantId, CancellationToken ct = default);
    public Task<List<Post>> ReviewQueueAsync(Guid tenantId, CancellationToken ct = default); // NeedsReview || Pushed
    public Task<IReadOnlyList<Guid>> GetIssueSourceIdsAsync(Post post, CancellationToken ct = default);
    public Task SetIssueSourcesAsync(Post post, IReadOnlyList<Guid> sourceIds, CancellationToken ct = default);
    public Task<List<ContentItem>> GetCandidatesAsync(Post post, CancellationToken ct = default);
    public Task<(PipelineRun Run, Post Post)> ComposeAsync(Guid postId, IReadOnlyList<Guid> itemIds,
        string? extraInstructions, CancellationToken ct = default);
    public Task SaveIssueAsync(Guid postId, string title, string body, string? subject,
        string? previewText, CancellationToken ct = default);
    public Task<IReadOnlyList<string>> SubjectIdeasAsync(Guid postId, CancellationToken ct = default);
    public Task<Post> PushAsync(Guid postId, CancellationToken ct = default);
    public Task MarkReviewedAsync(Guid postId, CancellationToken ct = default);
}
```

Behavioral contract (implement exactly):
- `CreateIssueAsync`: resolves the tenant's MailerLite platform via `PlatformService.GetOrCreateMailerLiteAsync`; stores `SourceIdsJson = JsonSerializer.Serialize(sourceIds)` when `sourceIds` is non-null (null → inherit automation set); `Kind = DraftKinds.Newsletter`, `NeedsReview = false`, `RecipeId = recipeId`.
- `GetCandidatesAsync`: candidate items = tenant items filtered to the issue's source set (post.SourceIdsJson → else recipe.SourceIdsJson; empty array on the recipe means "all tenant sources"), then `ItemSelector.Select(candidates, rules, used, now)` where `rules` = recipe's `SelectionRules` with `TimeWindowDays = post.WindowDays` and `MaxItems = 50` (the editor shows a generous pool; compose gets the user's checked subset), `used` = item ids referenced by prior drafts of this recipe (same query as `GenerationPipeline.SelectItemsAsync`).
- `ComposeAsync`: guards `itemIds` non-empty; calls `generation.RunAsync(post.RecipeId!.Value, itemIds, extraInstructions, RunTriggers.Manual, ct)`; on a produced draft sets `post.DraftId`, `post.Title = draft.Title`, `post.Subject ??= draft.Title`; returns (run, post). A failed run leaves the post untouched.
- `PushAsync`: requires `post.DraftId` (throw `InvalidOperationException("compose or write the issue first")`), platform config complete (groupId, fromName, fromEmail) and API key present (throw with message naming the Platforms page). Renders via `EmailHtmlRenderer.Render(draft.Body, post.Title)`; `MailerLiteDraft.Name = post.Title`, `Subject = post.Subject ?? post.Title`; passes `post.ExternalId` for re-push. Success → `Status = Pushed`, `ExternalId = id`, `ExternalUrl = $"https://dashboard.mailerlite.com/campaigns/{id}"`, `NeedsReview = false`. Failure → `Status = Failed` and rethrow (UI snackbars the message; the post row shows Failed with retry = push again).
- `SubjectIdeasAsync`: one `llm.GenerateAsync` call — prompt: subject-line brief + first 4000 chars of the draft body, demanding a strict JSON array of 5 strings; parse with the same fence-stripping approach as Task 4 (extract a small `internal static bool JsonArrayParser.TryParseStringArray(string, out List<string>)` helper into `Application/Newsletter/` if duplication itches — acceptable either way); on parse failure after one retry, throw.

- [ ] **Step 1: Write the failing integration tests**

`tests/ContentAutomatorX.IntegrationTests/PostServiceTests.cs`:

```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.IntegrationTests;

public class FakeLlm(string reply) : ILlmBackend
{
    public string Name => "fake";
    public Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default) =>
        Task.FromResult(new LlmResult(reply, "fake-model"));
}

public class FakeDelivery : IDraftDelivery
{
    public Task<string> DeliverAsync(Tenant tenant, RecipeOutput output, Draft draft, CancellationToken ct = default) =>
        Task.FromResult(Path.Combine(Path.GetTempPath(), $"{draft.Id}.md"));
}

public class FakeMailerLite : IMailerLiteClient
{
    public List<(MailerLiteDraft Draft, string? ExistingId)> Pushes { get; } = [];
    public bool FailNextPush { get; set; }
    public MailerLiteCampaignStatus NextStatus { get; set; } = new("draft", null, null, null);

    public Task<bool> TestAsync(string apiKey, CancellationToken ct = default) => Task.FromResult(true);
    public Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(string apiKey, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MailerLiteGroup>>([new("g1", "Main")]);
    public Task<string> PushDraftAsync(string apiKey, MailerLiteDraft draft, string? existingCampaignId, CancellationToken ct = default)
    {
        if (FailNextPush) { FailNextPush = false; throw new InvalidOperationException("MailerLite POST /campaigns failed: 422"); }
        Pushes.Add((draft, existingCampaignId));
        return Task.FromResult(existingCampaignId ?? "c-100");
    }
    public Task<MailerLiteCampaignStatus> GetStatusAsync(string apiKey, string campaignId, CancellationToken ct = default) =>
        Task.FromResult(NextStatus);
}

public class InMemoryCredentials : ICredentialStore
{
    private readonly Dictionary<string, string> _map = [];
    public Task SetAsync(string name, string secret, CancellationToken ct = default) { _map[name] = secret; return Task.CompletedTask; }
    public Task<string?> GetAsync(string name, CancellationToken ct = default) => Task.FromResult(_map.GetValueOrDefault(name));
    public Task DeleteAsync(string name, CancellationToken ct = default) { _map.Remove(name); return Task.CompletedTask; }
}

public class PostServiceTests
{
    private sealed record World(TestDb Test, PostService Posts, PlatformService Platforms,
        FakeMailerLite MailerLite, Tenant Tenant, Recipe Recipe, Source SourceA, Source SourceB);

    private static async Task<World> BuildAsync(string llmReply = "# Composed Issue\n\nbody")
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-posts", OutputFolderPath = Path.GetTempPath() };
        var sourceA = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "A" };
        var sourceB = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "B" };
        var template = new PromptTemplate { TenantId = null, Kind = DraftKinds.Newsletter, Template = "{voice_profile}{tone_modifiers}{items}{extra_instructions}" };
        var recipe = new Recipe
        {
            TenantId = tenant.Id, Name = "AI Weekly", Kind = DraftKinds.Newsletter,
            PromptTemplateId = template.Id,
            SourceIdsJson = JsonSerializer.Serialize(new[] { sourceA.Id, sourceB.Id })
        };
        test.Db.AddRange(tenant, sourceA, sourceB, template, recipe);
        foreach (var (source, n) in new[] { (sourceA, 1), (sourceB, 2) })
            test.Db.ContentItems.Add(new ContentItem
            {
                TenantId = tenant.Id, SourceId = source.Id, ExternalId = $"e{n}",
                Title = $"Item {n}", Body = "b", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        await test.Db.SaveChangesAsync();

        var ml = new FakeMailerLite();
        var creds = new InMemoryCredentials();
        var platforms = new PlatformService(test.Db, creds, ml);
        var generation = new GenerationPipeline(test.Db, new FakeLlm(llmReply), new FakeDelivery());
        var posts = new PostService(test.Db, generation, new FakeLlm("[\"s1\",\"s2\",\"s3\",\"s4\",\"s5\"]"), platforms, ml);
        return new World(test, posts, platforms, ml, tenant, recipe, sourceA, sourceB);
    }

    [Fact]
    public async Task Create_issue_numbers_titles_and_binds_platform()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        Assert.Equal("AI Weekly #1", await w.Posts.SuggestTitleAsync(w.Recipe.Id));

        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "AI Weekly #1");

        Assert.Equal(PostStatus.Draft, post.Status);
        Assert.Equal(w.Recipe.Id, post.RecipeId);
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        Assert.Equal(platform.Id, post.PlatformId);
        Assert.Equal("AI Weekly #2", await w.Posts.SuggestTitleAsync(w.Recipe.Id));
    }

    [Fact]
    public async Task Candidates_respect_the_per_issue_source_subset()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, [w.SourceA.Id], "t");

        var candidates = await w.Posts.GetCandidatesAsync(post);

        var item = Assert.Single(candidates);           // sourceB's item excluded
        Assert.Equal(w.SourceA.Id, item.SourceId);
    }

    [Fact]
    public async Task Compose_links_draft_and_prefills_subject()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var items = await w.Posts.GetCandidatesAsync(post);

        var (run, updated) = await w.Posts.ComposeAsync(post.Id, items.Select(i => i.Id).ToList(), null);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.NotNull(updated.DraftId);
        Assert.Equal("Composed Issue", updated.Title);
        Assert.Equal("Composed Issue", updated.Subject);
    }

    [Fact]
    public async Task Push_renders_html_creates_campaign_and_repush_reuses_id()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        await w.Platforms.SetApiKeyAsync(platform, "KEY");
        await w.Platforms.SaveConfigAsync(platform, new MailerLiteConfig("g1", "Main", "AIVisions", "n@x.com"));
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var items = await w.Posts.GetCandidatesAsync(post);
        await w.Posts.ComposeAsync(post.Id, items.Select(i => i.Id).ToList(), null);

        var pushed = await w.Posts.PushAsync(post.Id);

        Assert.Equal(PostStatus.Pushed, pushed.Status);
        Assert.Equal("c-100", pushed.ExternalId);
        Assert.Contains("Composed Issue", w.MailerLite.Pushes.Single().Draft.Html);

        await w.Posts.PushAsync(post.Id); // re-push
        Assert.Equal("c-100", w.MailerLite.Pushes[1].ExistingId);
    }

    [Fact]
    public async Task Push_failure_marks_failed_and_rethrows()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        await w.Platforms.SetApiKeyAsync(platform, "KEY");
        await w.Platforms.SaveConfigAsync(platform, new MailerLiteConfig("g1", "Main", "A", "n@x.com"));
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var items = await w.Posts.GetCandidatesAsync(post);
        await w.Posts.ComposeAsync(post.Id, items.Select(i => i.Id).ToList(), null);
        w.MailerLite.FailNextPush = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Posts.PushAsync(post.Id));
        Assert.Equal(PostStatus.Failed, (await w.Posts.GetAsync(post.Id))!.Status);
    }

    [Fact]
    public async Task Push_without_config_throws_actionable_message()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        await w.Posts.SaveIssueAsync(post.Id, "t", "hand-written body", "s", null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => w.Posts.PushAsync(post.Id));
        Assert.Contains("Platforms", ex.Message);
    }

    [Fact]
    public async Task SaveIssue_creates_a_draft_for_a_hand_written_issue()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");

        await w.Posts.SaveIssueAsync(post.Id, "My title", "typed by hand", "subj", "pv");

        var reloaded = await w.Posts.GetAsync(post.Id);
        Assert.NotNull(reloaded!.DraftId);
        Assert.Equal("My title", reloaded.Title);
        var draft = await w.Test.Db.Drafts.FindAsync(reloaded.DraftId);
        Assert.Equal("typed by hand", draft!.Body);
    }

    [Fact]
    public async Task Subject_ideas_parses_five_strings()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        await w.Posts.SaveIssueAsync(post.Id, "t", "body", null, null);

        var ideas = await w.Posts.SubjectIdeasAsync(post.Id);

        Assert.Equal(5, ideas.Count);
        Assert.Equal("s1", ideas[0]);
    }

    [Fact]
    public async Task Review_queue_lists_needs_review_and_pushed()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        w.Test.Db.Posts.AddRange(
            new Post { TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "r", NeedsReview = true },
            new Post { TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "p", Status = PostStatus.Pushed },
            new Post { TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "done", Status = PostStatus.Published });
        await w.Test.Db.SaveChangesAsync();

        var queue = await w.Posts.ReviewQueueAsync(w.Tenant.Id);

        Assert.Equal(2, queue.Count);
        Assert.DoesNotContain(queue, p => p.Title == "done");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests -c Release --filter FullyQualifiedName~PostServiceTests --nologo`
Expected: COMPILE ERROR — services don't exist.

- [ ] **Step 3: Implement `MailerLiteConfig`, `PlatformService`, `PostService`**

`src/ContentAutomatorX.Application/Newsletter/MailerLiteConfig.cs`:

```csharp
namespace ContentAutomatorX.Application.Newsletter;

public record MailerLiteConfig(string? GroupId, string? GroupName, string? FromName, string? FromEmail);
```

`src/ContentAutomatorX.Application/Services/PlatformService.cs`:

```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class PlatformService(IAppDbContext db, ICredentialStore credentials, IMailerLiteClient mailerLite)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<Platform> GetOrCreateMailerLiteAsync(Guid tenantId, CancellationToken ct = default)
    {
        var existing = await db.Platforms
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Type == PlatformTypes.MailerLite, ct);
        if (existing is not null) return existing;

        var platform = new Platform { TenantId = tenantId, Type = PlatformTypes.MailerLite, DisplayName = "MailerLite" };
        platform.CredentialRef = $"mailerlite:{platform.Id}";
        db.Platforms.Add(platform);
        await db.SaveChangesAsync(ct);
        return platform;
    }

    public MailerLiteConfig GetConfig(Platform platform) =>
        JsonSerializer.Deserialize<MailerLiteConfig>(platform.ConfigJson, JsonOpts)
            ?? new MailerLiteConfig(null, null, null, null);

    public async Task SaveConfigAsync(Platform platform, MailerLiteConfig config, string? colorHex = null,
        CancellationToken ct = default)
    {
        platform.ConfigJson = JsonSerializer.Serialize(config, JsonOpts);
        if (!string.IsNullOrWhiteSpace(colorHex)) platform.ColorHex = colorHex;
        await db.SaveChangesAsync(ct);
    }

    public Task SetApiKeyAsync(Platform platform, string apiKey, CancellationToken ct = default) =>
        credentials.SetAsync(platform.CredentialRef ?? $"mailerlite:{platform.Id}", apiKey, ct);

    public Task<string?> GetApiKeyAsync(Platform platform, CancellationToken ct = default) =>
        credentials.GetAsync(platform.CredentialRef ?? $"mailerlite:{platform.Id}", ct);

    public async Task<bool> TestAsync(Platform platform, CancellationToken ct = default)
    {
        var key = await GetApiKeyAsync(platform, ct);
        return key is not null && await mailerLite.TestAsync(key, ct);
    }

    public async Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(Platform platform, CancellationToken ct = default)
    {
        var key = await GetApiKeyAsync(platform, ct)
            ?? throw new InvalidOperationException("No API key stored — set it on the Platforms page.");
        return await mailerLite.ListGroupsAsync(key, ct);
    }
}
```

`src/ContentAutomatorX.Application/Services/PostService.cs`:

```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class PostService(IAppDbContext db, GenerationPipeline generation, ILlmBackend llm,
    PlatformService platforms, IMailerLiteClient mailerLite)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<string> SuggestTitleAsync(Guid recipeId, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.SingleAsync(r => r.Id == recipeId, ct);
        var n = await db.Posts.CountAsync(p => p.RecipeId == recipeId, ct) + 1;
        return $"{recipe.Name} #{n}";
    }

    public async Task<Post> CreateIssueAsync(Guid tenantId, Guid recipeId, int windowDays,
        IReadOnlyList<Guid>? sourceIds, string title, CancellationToken ct = default)
    {
        var platform = await platforms.GetOrCreateMailerLiteAsync(tenantId, ct);
        var post = new Post
        {
            TenantId = tenantId, PlatformId = platform.Id, RecipeId = recipeId,
            Kind = DraftKinds.Newsletter, Title = title, WindowDays = windowDays,
            SourceIdsJson = sourceIds is null ? null : JsonSerializer.Serialize(sourceIds)
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);
        return post;
    }

    public Task<Post?> GetAsync(Guid postId, CancellationToken ct = default) =>
        db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);

    public Task<List<Post>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Posts.Where(p => p.TenantId == tenantId).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    public Task<List<Post>> ReviewQueueAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Posts.Where(p => p.TenantId == tenantId &&
                (p.NeedsReview || p.Status == PostStatus.Pushed) && p.Status != PostStatus.Published)
            .OrderBy(p => p.CreatedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetIssueSourceIdsAsync(Post post, CancellationToken ct = default)
    {
        if (post.SourceIdsJson is not null)
            return JsonSerializer.Deserialize<Guid[]>(post.SourceIdsJson) ?? [];
        var recipe = await db.Recipes.SingleAsync(r => r.Id == post.RecipeId, ct);
        var fromRecipe = JsonSerializer.Deserialize<Guid[]>(recipe.SourceIdsJson) ?? [];
        if (fromRecipe.Length > 0) return fromRecipe;
        return await db.Sources.Where(s => s.TenantId == post.TenantId).Select(s => s.Id).ToArrayAsync(ct);
    }

    public async Task SetIssueSourcesAsync(Post post, IReadOnlyList<Guid> sourceIds, CancellationToken ct = default)
    {
        post.SourceIdsJson = JsonSerializer.Serialize(sourceIds);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ContentItem>> GetCandidatesAsync(Post post, CancellationToken ct = default)
    {
        var recipe = await db.Recipes.SingleAsync(r => r.Id == post.RecipeId, ct);
        var sourceIds = (await GetIssueSourceIdsAsync(post, ct)).ToHashSet();

        var candidates = await db.ContentItems
            .Where(i => i.TenantId == post.TenantId && sourceIds.Contains(i.SourceId))
            .ToListAsync(ct);

        var priorDrafts = await db.Drafts.Where(d => d.RecipeId == recipe.Id)
            .Select(d => d.SourceItemIdsJson).ToListAsync(ct);
        var used = priorDrafts.SelectMany(j => JsonSerializer.Deserialize<string[]>(j) ?? [])
            .Select(Guid.Parse).ToHashSet();

        var rules = JsonSerializer.Deserialize<SelectionRules>(recipe.SelectionJson, JsonOpts) ?? new SelectionRules();
        rules.TimeWindowDays = post.WindowDays;
        rules.MaxItems = 50; // generous pool; the human checks what compose actually gets
        return ItemSelector.Select(candidates, rules, used, DateTimeOffset.UtcNow);
    }

    public async Task<(PipelineRun Run, Post Post)> ComposeAsync(Guid postId, IReadOnlyList<Guid> itemIds,
        string? extraInstructions, CancellationToken ct = default)
    {
        if (itemIds.Count == 0) throw new InvalidOperationException("Pick at least one item to compose from.");
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var recipeId = post.RecipeId ?? throw new InvalidOperationException("Issue has no automation to compose with.");

        var (run, draft) = await generation.RunAsync(recipeId, itemIds, extraInstructions, RunTriggers.Manual, ct);
        if (draft is not null)
        {
            post.DraftId = draft.Id;
            post.Title = draft.Title;
            post.Subject ??= draft.Title;
            await db.SaveChangesAsync(ct);
        }
        return (run, post);
    }

    public async Task SaveIssueAsync(Guid postId, string title, string body, string? subject,
        string? previewText, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        Draft draft;
        if (post.DraftId is Guid draftId)
        {
            draft = await db.Drafts.SingleAsync(d => d.Id == draftId, ct);
        }
        else
        {
            draft = new Draft
            {
                TenantId = post.TenantId, RecipeId = post.RecipeId ?? Guid.Empty,
                Kind = post.Kind, Title = title
            };
            db.Drafts.Add(draft);
            post.DraftId = draft.Id;
        }
        draft.Title = title;
        draft.Body = body;
        post.Title = title;
        post.Subject = subject;
        post.PreviewText = previewText;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<string>> SubjectIdeasAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var draft = post.DraftId is Guid id ? await db.Drafts.SingleAsync(d => d.Id == id, ct)
            : throw new InvalidOperationException("Nothing to write subjects for yet.");
        var excerpt = draft.Body.Length <= 4000 ? draft.Body : draft.Body[..4000];
        var prompt = $"""
            Write 5 email subject lines for this newsletter issue. Punchy, concrete, <60 chars, no clickbait.
            Respond with ONLY a JSON array of 5 strings, no prose, no markdown fences.

            Issue body:
            {excerpt}
            """;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var reply = await llm.GenerateAsync(attempt == 1 ? prompt
                : prompt + "\nYour previous reply was not valid JSON. ONLY the JSON array.", ct);
            if (TryParseStringArray(reply.Text, out var subjects)) return subjects!;
        }
        throw new InvalidOperationException("Model did not return subject lines as JSON.");
    }

    public async Task<Post> PushAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var draft = post.DraftId is Guid id ? await db.Drafts.SingleAsync(d => d.Id == id, ct)
            : throw new InvalidOperationException("Compose or write the issue first.");
        var platform = await db.Platforms.SingleAsync(p => p.Id == post.PlatformId, ct);
        var config = platforms.GetConfig(platform);
        var apiKey = await platforms.GetApiKeyAsync(platform, ct);
        if (apiKey is null || config.GroupId is null || config.FromName is null || config.FromEmail is null)
            throw new InvalidOperationException("MailerLite is not fully configured — finish setup on the Platforms page.");

        var html = EmailHtmlRenderer.Render(draft.Body, post.Title);
        try
        {
            var campaignId = await mailerLite.PushDraftAsync(apiKey, new MailerLiteDraft(
                Name: post.Title, Subject: post.Subject ?? post.Title, PreviewText: post.PreviewText,
                FromName: config.FromName, FromEmail: config.FromEmail, GroupId: config.GroupId, Html: html),
                post.ExternalId, ct);
            post.ExternalId = campaignId;
            post.ExternalUrl = $"https://dashboard.mailerlite.com/campaigns/{campaignId}";
            post.Status = PostStatus.Pushed;
            post.NeedsReview = false;
            await db.SaveChangesAsync(ct);
            return post;
        }
        catch
        {
            post.Status = PostStatus.Failed;
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task MarkReviewedAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        post.NeedsReview = false;
        await db.SaveChangesAsync(ct);
    }

    internal static bool TryParseStringArray(string text, out List<string>? values)
    {
        values = null;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }
        try
        {
            values = JsonSerializer.Deserialize<List<string>>(trimmed);
            return values is { Count: > 0 };
        }
        catch (JsonException) { return false; }
    }
}
```

DI in `Program.cs` next to the other scoped services:

```csharp
builder.Services.AddScoped<PlatformService>();
builder.Services.AddScoped<PostService>();
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests -c Release --filter FullyQualifiedName~PostServiceTests --nologo`
Expected: 9 PASS. (If `ItemSelector.Select` filters differently than assumed — e.g. MinScore default — read `src/ContentAutomatorX.Application/Generation/ItemSelector.cs` and adjust seeded item metadata, not the service contract.)

- [ ] **Step 5: Full suite + commit**

Run: `dotnet test ContentAutomatorX.slnx -c Release --nologo -v q`
Expected: all green.

```bash
git add -A
git commit -m "feat: PlatformService and PostService (issue lifecycle: create/candidates/compose/push)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Recipe.TargetPlatformId → automation creates review-queue posts

**Files:**
- Modify: `src/ContentAutomatorX.Domain/Entities/Recipe.cs` (one property)
- Modify: `src/ContentAutomatorX.Application/Pipelines/GenerationPipeline.cs:56-80` (post creation after draft save)
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor` (platform picker in the edit form)
- Test: `tests/ContentAutomatorX.IntegrationTests/GenerationPipelineTests.cs` (append)
- Generated: `src/ContentAutomatorX.Infrastructure/Migrations/*_RecipeTargetPlatform.cs`

**Interfaces:**
- Consumes: Task 1 entities.
- Produces: `Recipe.TargetPlatformId` (`Guid?`); pipeline behavior: when set, every successful generation also creates `Post { Status = Draft, NeedsReview = true, DraftId, RecipeId, PlatformId, Kind, Title = draft.Title, Subject = draft.Title }`. Scheduled weekly runs thereby land in Today's review queue (Task 14) — this is Path A of the walkthrough.

- [ ] **Step 1: Write the failing test**

Append to `tests/ContentAutomatorX.IntegrationTests/GenerationPipelineTests.cs` (reuse the file's existing seed helpers/fakes — it already fakes `ILlmBackend`/`IDraftDelivery`; mirror its arrange style):

```csharp
[Fact]
public async Task Recipe_with_target_platform_creates_a_needs_review_post()
{
    // Arrange: seed tenant + source + one fresh ContentItem + newsletter recipe (as the file's
    // other tests do), plus:
    //   var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
    //   recipe.TargetPlatformId = platform.Id;
    //   test.Db.Platforms.Add(platform);
    // Act: run the pipeline (fake LLM returning "# Weekly\n\nbody").
    // Assert:
    var post = await test.Db.Posts.SingleAsync();
    Assert.True(post.NeedsReview);
    Assert.Equal(PostStatus.Draft, post.Status);
    Assert.Equal(draft!.Id, post.DraftId);
    Assert.Equal(recipe.Id, post.RecipeId);
    Assert.Equal("Weekly", post.Title);
}

[Fact]
public async Task Recipe_without_target_platform_creates_no_post()
{
    // Same arrange minus TargetPlatformId; after a successful run:
    Assert.Empty(test.Db.Posts.ToList());
}
```

(The comments describe arrange steps to copy from the file's existing tests; the assertions are the contract.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests -c Release --filter FullyQualifiedName~GenerationPipelineTests --nologo`
Expected: COMPILE ERROR (`TargetPlatformId`).

- [ ] **Step 3: Implement**

`Recipe.cs` — add after `ScheduleCron`:

```csharp
public Guid? TargetPlatformId { get; set; } // set → each run also creates a review-queue Post
```

`GenerationPipeline.cs` — in `RunCoreAsync`, immediately after the `db.Drafts.Add(draft); foreach (...) item.Status = Used; await db.SaveChangesAsync(ct);` block (before delivery), insert:

```csharp
if (recipe.TargetPlatformId is Guid platformId)
{
    db.Posts.Add(new Post
    {
        TenantId = recipe.TenantId, PlatformId = platformId, RecipeId = recipe.Id,
        DraftId = draft.Id, Kind = recipe.Kind, Title = draft.Title,
        Subject = draft.Title, Status = PostStatus.Draft, NeedsReview = true
    });
    await db.SaveChangesAsync(ct);
    log.Add("post created (review queue)");
}
```

Migration: `dotnet ef migrations add RecipeTargetPlatform --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web`

`Recipes.razor` — in the New/Edit form panel, after the cron `MudTextField`, add:

```razor
<MudSelect T="Guid?" @bind-Value="_targetPlatformId"
           Label="Create post for platform (optional — parks in Today's review queue)">
    <MudSelectItem T="Guid?" Value="@((Guid?)null)">none (file only)</MudSelectItem>
    @foreach (var p in _platforms)
    {
        <MudSelectItem T="Guid?" Value="@((Guid?)p.Id)">@p.DisplayName</MudSelectItem>
    }
</MudSelect>
```

with `@code` additions: `private List<Platform> _platforms = []; private Guid? _targetPlatformId;`, load in `ReloadAsync` via `PlatformService.GetOrCreateMailerLiteAsync(Ctx.Active.Id)` wrapped in a list (inject `PlatformService PlatformSvc`), map `_targetPlatformId` in its `Edit`/`Reset`/`Save` methods exactly like the existing fields (read the file, mirror the pattern used for `_cron`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests -c Release --filter FullyQualifiedName~GenerationPipelineTests --nologo`
Expected: PASS (new + existing).

- [ ] **Step 5: Full suite + commit**

```bash
git add -A
git commit -m "feat: automations can create review-queue posts (Recipe.TargetPlatformId)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: PostSyncService (Sent detection + stats snapshots)

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/PostSyncService.cs`
- Create: `src/ContentAutomatorX.Web/Jobs/PlatformSyncJob.cs`
- Modify: `src/ContentAutomatorX.Web/Program.cs` (hosted service)
- Test: `tests/ContentAutomatorX.IntegrationTests/PostSyncServiceTests.cs`

**Interfaces:**
- Consumes: `IMailerLiteClient.GetStatusAsync`, `PlatformService.GetApiKeyAsync`, Task 1 entities.
- Produces: `PostSyncService(IAppDbContext db, PlatformService platforms, IMailerLiteClient mailerLite)` with `Task<int> TickAsync(DateTimeOffset now, CancellationToken ct = default)` returning the number of posts touched. StatsJson shape: `{"refreshedAt":"<ISO8601>","sent":1204,"opens":577,"clicks":89}`.
- Rules: Pushed post whose campaign status is `sent` → `Status = Published`, `PublishedAt = now`, stats written. Published post with `PublishedAt > now-30d` and stats older than 24h → stats refreshed. Per-post try/catch: one failing post never blocks others (idempotent next tick).

- [ ] **Step 1: Write the failing tests**

`tests/ContentAutomatorX.IntegrationTests/PostSyncServiceTests.cs`:

```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.IntegrationTests;

public class PostSyncServiceTests
{
    private static async Task<(TestDb Test, PostSyncService Sync, FakeMailerLite Ml, Post Post)> BuildAsync(
        PostStatus status, string? statsJson = null, DateTimeOffset? publishedAt = null)
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-sync" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML", CredentialRef = "k" };
        var post = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "t", Status = status, ExternalId = "c-1",
            StatsJson = statsJson ?? "{}", PublishedAt = publishedAt
        };
        test.Db.AddRange(tenant, platform, post);
        await test.Db.SaveChangesAsync();

        var creds = new InMemoryCredentials();
        await creds.SetAsync("k", "KEY");
        var ml = new FakeMailerLite();
        var sync = new PostSyncService(test.Db, new PlatformService(test.Db, creds, ml), ml);
        return (test, sync, ml, post);
    }

    [Fact]
    public async Task Pushed_post_becomes_published_when_campaign_is_sent()
    {
        var (test, sync, ml, post) = await BuildAsync(PostStatus.Pushed);
        using var _ = test;
        ml.NextStatus = new("sent", 1204, 577, 89);
        var now = DateTimeOffset.UtcNow;

        var touched = await sync.TickAsync(now);

        Assert.Equal(1, touched);
        var reloaded = await test.Db.Posts.FindAsync(post.Id);
        Assert.Equal(PostStatus.Published, reloaded!.Status);
        Assert.Equal(now, reloaded.PublishedAt);
        using var stats = JsonDocument.Parse(reloaded.StatsJson);
        Assert.Equal(1204, stats.RootElement.GetProperty("sent").GetInt32());
    }

    [Fact]
    public async Task Pushed_post_still_draft_in_mailerlite_is_untouched()
    {
        var (test, sync, ml, post) = await BuildAsync(PostStatus.Pushed);
        using var _ = test;
        ml.NextStatus = new("draft", null, null, null);

        Assert.Equal(0, await sync.TickAsync(DateTimeOffset.UtcNow));
        Assert.Equal(PostStatus.Pushed, (await test.Db.Posts.FindAsync(post.Id))!.Status);
    }

    [Fact]
    public async Task Recent_published_post_with_stale_stats_gets_refreshed()
    {
        var staleStats = $$"""{"refreshedAt":"{{DateTimeOffset.UtcNow.AddHours(-25):O}}","sent":1204,"opens":100,"clicks":5}""";
        var (test, sync, ml, post) = await BuildAsync(PostStatus.Published, staleStats,
            publishedAt: DateTimeOffset.UtcNow.AddDays(-3));
        using var _ = test;
        ml.NextStatus = new("sent", 1204, 601, 92);

        Assert.Equal(1, await sync.TickAsync(DateTimeOffset.UtcNow));
        using var stats = JsonDocument.Parse((await test.Db.Posts.FindAsync(post.Id))!.StatsJson);
        Assert.Equal(601, stats.RootElement.GetProperty("opens").GetInt32());
    }

    [Fact]
    public async Task Old_published_post_is_left_alone()
    {
        var (test, sync, ml, _) = await BuildAsync(PostStatus.Published,
            $$"""{"refreshedAt":"{{DateTimeOffset.UtcNow.AddDays(-2):O}}","sent":1,"opens":1,"clicks":0}""",
            publishedAt: DateTimeOffset.UtcNow.AddDays(-45));
        using var _1 = test;
        Assert.Equal(0, await sync.TickAsync(DateTimeOffset.UtcNow));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests -c Release --filter FullyQualifiedName~PostSyncServiceTests --nologo`
Expected: COMPILE ERROR.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Application/Services/PostSyncService.cs`:

```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class PostSyncService(IAppDbContext db, PlatformService platforms, IMailerLiteClient mailerLite)
{
    private record StatsSnapshot(DateTimeOffset RefreshedAt, int? Sent, int? Opens, int? Clicks);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<int> TickAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var candidates = await db.Posts
            .Where(p => p.ExternalId != null &&
                (p.Status == PostStatus.Pushed ||
                 (p.Status == PostStatus.Published && p.PublishedAt > now.AddDays(-30))))
            .ToListAsync(ct);

        var touched = 0;
        foreach (var post in candidates)
        {
            try
            {
                if (post.Status == PostStatus.Published && !StatsStale(post, now)) continue;

                var platform = await db.Platforms.SingleAsync(p => p.Id == post.PlatformId, ct);
                var key = await platforms.GetApiKeyAsync(platform, ct);
                if (key is null) continue;

                var status = await mailerLite.GetStatusAsync(key, post.ExternalId!, ct);
                if (post.Status == PostStatus.Pushed && status.Status != "sent") continue;

                if (post.Status == PostStatus.Pushed)
                {
                    post.Status = PostStatus.Published;
                    post.PublishedAt = now;
                }
                post.StatsJson = JsonSerializer.Serialize(
                    new StatsSnapshot(now, status.Sent, status.OpensCount, status.ClicksCount), JsonOpts);
                await db.SaveChangesAsync(ct);
                touched++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                // one failing post never blocks the rest; next tick retries (idempotent)
            }
        }
        return touched;
    }

    private static bool StatsStale(Post post, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(post.StatsJson);
            return !doc.RootElement.TryGetProperty("refreshedAt", out var r)
                || r.GetDateTimeOffset() < now.AddHours(-24);
        }
        catch (JsonException) { return true; }
    }
}
```

`src/ContentAutomatorX.Web/Jobs/PlatformSyncJob.cs` (mirror `SchedulerService`'s scope/log pattern):

```csharp
using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.Web.Jobs;

public class PlatformSyncJob(IServiceScopeFactory scopeFactory, ILogger<PlatformSyncJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<PostSyncService>();
                var touched = await sync.TickAsync(DateTimeOffset.UtcNow, ct);
                if (touched > 0) logger.LogInformation("platform sync touched {Count} posts", touched);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "platform sync tick failed"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
```

`Program.cs`: `builder.Services.AddScoped<PostSyncService>();` next to the services and `builder.Services.AddHostedService<PlatformSyncJob>();` next to `SchedulerService`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests -c Release --filter FullyQualifiedName~PostSyncServiceTests --nologo`
Expected: 4 PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: hourly Sent detection and 30-day stats snapshots for posts

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## UI tasks (10–14) — testing note

There is no bUnit/Playwright test project. The established pattern: keep logic in Application services (already test-covered above), keep `.razor` files thin, verify UI by build + the end-to-end drive in Task 16 (publish-based, per `.claude/skills/verify` — never `dotnet run`). Each UI task ends with `dotnet build ContentAutomatorX.slnx -c Release` green + a focused manual checklist that Task 16 automates.

All pages follow the house pattern (see `Sources.razor`): `@inject TenantContext Ctx`, guard `!Ctx.Initialized` → `MudProgressLinear`, `Ctx.Active is null` → `<NoTenantHint />`, subscribe `Ctx.Changed` in `OnInitializedAsync`, detach in `Dispose`.

### Task 10: Platforms page — real MailerLite configuration

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Platforms.razor` (replace the static MailerLite row with a config card; keep the other planned rows)

**Interfaces:**
- Consumes: `PlatformService` (Task 7): `GetOrCreateMailerLiteAsync`, `GetConfig`, `SaveConfigAsync`, `SetApiKeyAsync`, `GetApiKeyAsync` (presence check only), `TestAsync`, `ListGroupsAsync`.

- [ ] **Step 1: Implement the config card**

Replace the MailerLite `<tr>` in the static table with a note "configured below" and add above the planned-platforms paper:

```razor
@implements IDisposable
@inject TenantContext Ctx
@inject PlatformService PlatformSvc
@inject ISnackbar Snackbar

@* after the ComingSoonBanner, tenant-guarded like Sources.razor: *@
<MudPaper Class="pa-4 mb-4" Outlined="true">
    <MudText Typo="Typo.h6" Class="mb-2">● MailerLite — ⚡ Auto</MudText>
    <MudTextField @bind-Value="_apiKey" Label="API key" InputType="InputType.Password"
                  HelperText="@(_hasKey ? "A key is stored — enter a new one to replace it." : "Paste your MailerLite API token.")" />
    <div class="mt-2 mb-2">
        <MudButton OnClick="SaveKey" Variant="Variant.Outlined" Class="mr-2"
                   Disabled="@string.IsNullOrWhiteSpace(_apiKey)">Save key</MudButton>
        <MudButton OnClick="Test" Variant="Variant.Outlined" Disabled="@(!_hasKey)">Test & load groups</MudButton>
    </div>
    <MudSelect T="string" @bind-Value="_groupId" Label="Audience group" Disabled="@(_groups.Count == 0)">
        @foreach (var g in _groups)
        {
            <MudSelectItem T="string" Value="@g.Id">@g.Name</MudSelectItem>
        }
    </MudSelect>
    <MudTextField @bind-Value="_fromName" Label="From name" />
    <MudTextField @bind-Value="_fromEmail" Label="From email" />
    <MudTextField @bind-Value="_colorHex" Label="Platform color (hex)" />
    <MudButton OnClick="SaveConfig" Variant="Variant.Filled" Color="Color.Primary" Class="mt-2">Save</MudButton>
</MudPaper>
```

`@code`: `_platform` loaded in `ReloadAsync` via `PlatformSvc.GetOrCreateMailerLiteAsync(Ctx.Active.Id)`; `_hasKey = await PlatformSvc.GetApiKeyAsync(_platform) is not null`; fields prefilled from `PlatformSvc.GetConfig(_platform)` + `_platform.ColorHex`. `SaveKey` → `SetApiKeyAsync` + clear `_apiKey` + snackbar; `Test` → `TestAsync` snackbar ✓/✗, on success `_groups = await PlatformSvc.ListGroupsAsync(_platform)`; `SaveConfig` → `SaveConfigAsync(_platform, new MailerLiteConfig(_groupId, _groups.FirstOrDefault(g => g.Id == _groupId)?.Name, _fromName, _fromEmail), _colorHex)` + snackbar. Wrap service calls in try/catch → `Snackbar.Add(ex.Message, Severity.Error)`.

- [ ] **Step 2: Build**

Run: `dotnet build ContentAutomatorX.slnx -c Release -v q`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat: Platforms page configures MailerLite (key, group, from, color)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

Manual checklist (automated in Task 16): key save → Test shows ✓ against a stub? (No — real key or skip; Task 16 asserts the form renders and validation disables buttons correctly.)

---

### Task 11: Sources page — Website & LLM-research types

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Sources.razor:22-36,104-125` (type select + config fields + Edit/BuildConfig branches)

**Interfaces:**
- Consumes: `SourceTypes.Website` / `SourceTypes.LlmResearch` (Tasks 3–4) and their config JSON shapes.

- [ ] **Step 1: Extend the form**

Type select — add:

```razor
<MudSelectItem T="string" Value="@SourceTypes.Website">Website (page watch)</MudSelectItem>
<MudSelectItem T="string" Value="@SourceTypes.LlmResearch">LLM research (AI web sweep)</MudSelectItem>
```

Replace the Reddit/else `@if` chain with a four-branch chain; new branches:

```razor
else if (_type == SourceTypes.Website)
{
    <MudTextField @bind-Value="_siteUrl" Label="Page URL (listing/blog index)" />
    <MudSelect T="string" @bind-Value="_siteMode" Label="Extraction">
        <MudSelectItem T="string" Value="@("auto")">Auto (articles & main links)</MudSelectItem>
        <MudSelectItem T="string" Value="@("selector")">CSS selector</MudSelectItem>
    </MudSelect>
    @if (_siteMode == "selector")
    {
        <MudTextField @bind-Value="_siteSelector" Label="Item selector (e.g. .post-list a)" />
    }
}
else if (_type == SourceTypes.LlmResearch)
{
    <MudTextField @bind-Value="_researchPrompt" Label="Research prompt" Lines="3"
                  HelperText="E.g. 'top AI image-generation news of the past 7 days'. Runs via the LLM with web search (Claude ExtraArgs)." />
}
```

`@code` additions: `private string _siteUrl = "", _siteMode = "auto", _siteSelector = "", _researchPrompt = "";` — extend `Reset()` (include them in the tuple assignment), `Edit(Source s)` (parse `url`/`mode`/`itemSelector` resp. `prompt` from `ConfigJson`, same `JsonDocument` pattern as the Reddit branch), and `BuildConfig()`:

```csharp
private string BuildConfig() => _type switch
{
    var t when t == SourceTypes.Reddit =>
        JsonSerializer.Serialize(new { subreddit = _subreddit, sort = _sort, timeframe = _timeframe }),
    var t when t == SourceTypes.Website =>
        JsonSerializer.Serialize(new { url = _siteUrl, mode = _siteMode,
            itemSelector = _siteMode == "selector" ? _siteSelector : null }),
    var t when t == SourceTypes.LlmResearch =>
        JsonSerializer.Serialize(new { prompt = _researchPrompt }),
    _ => JsonSerializer.Serialize(new { feedUrl = _feedUrl })
};
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build ContentAutomatorX.slnx -c Release -v q` → 0 errors.

```bash
git add -A
git commit -m "feat: Sources page supports Website and LLM-research source types

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 12: New-issue dialog + quick source creation + NewMenu wiring

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Shared/NewIssueDialog.razor`
- Create: `src/ContentAutomatorX.Web/Components/Shared/QuickSourceDialog.razor`
- Modify: `src/ContentAutomatorX.Web/Components/Layout/NewMenu.razor` (Newsletter item opens the dialog)

**Interfaces:**
- Consumes: `RecipeService.ListAsync(tenantId)`, `SourceService.ListAsync/CreateAsync`, `PostService.SuggestTitleAsync/CreateIssueAsync`.
- Produces: navigation to `/issue/{postId}?gather=1` (gather) or `/issue/{postId}` (start empty). Task 13's editor honors `gather=1`.

- [ ] **Step 1: QuickSourceDialog**

`QuickSourceDialog.razor` — a trimmed copy of the Sources form (all four types, no cron field — an unscheduled source is naturally a one-off, per spec):

```razor
@inject SourceService SourceSvc

<MudDialog>
    <DialogContent>
        <MudSelect T="string" @bind-Value="_type" Label="Type">
            <MudSelectItem T="string" Value="@SourceTypes.Reddit">Reddit</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Rss">RSS/Atom feed</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Website">Website (page watch)</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.LlmResearch">LLM research</MudSelectItem>
        </MudSelect>
        <MudTextField @bind-Value="_displayName" Label="Display name" />
        @* same four config branches as Sources.razor, minus cron *@
        <MudText Typo="Typo.caption" Class="mt-2">
            No schedule — this source only runs when an issue gathers it. Add a schedule later on the Sources page.
        </MudText>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => Dialog!.Cancel())">Cancel</MudButton>
        <MudButton Color="Color.Primary" Disabled="@string.IsNullOrWhiteSpace(_displayName)"
                   OnClick="Create">Create source</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance? Dialog { get; set; }
    [Parameter] public Guid TenantId { get; set; }
    // fields + BuildConfig() copied from Sources.razor's pattern (all four branches)

    private async Task Create()
    {
        var source = await SourceSvc.CreateAsync(new Source
        {
            TenantId = TenantId, Type = _type, DisplayName = _displayName,
            ConfigJson = BuildConfig(), ScheduleCron = null, IsEnabled = true
        });
        Dialog!.Close(DialogResult.Ok(source));
    }
}
```

(If `IMudDialogInstance` doesn't resolve in this MudBlazor version, use the same cascading dialog type `CreateTenantDialog.razor` uses — read that file and mirror it exactly.)

- [ ] **Step 2: NewIssueDialog**

`NewIssueDialog.razor`:

```razor
@inject RecipeService RecipeSvc
@inject SourceService SourceSvc
@inject PostService PostSvc
@inject IDialogService DialogService
@inject NavigationManager Nav

<MudDialog>
    <DialogContent>
        @if (_recipes.Count == 0)
        {
            <MudAlert Severity="Severity.Warning">
                No newsletter automation yet — create one under Automations first
                (it holds sources, selection rules and the prompt).
            </MudAlert>
        }
        else
        {
            <MudSelect T="Guid" Value="_recipeId" ValueChanged="OnRecipeChanged" Label="Based on">
                @foreach (var r in _recipes)
                {
                    <MudSelectItem T="Guid" Value="@r.Id">@r.Name</MudSelectItem>
                }
            </MudSelect>
            <MudSelect T="int" @bind-Value="_windowDays" Label="Material window">
                <MudSelectItem T="int" Value="3">Last 3 days</MudSelectItem>
                <MudSelectItem T="int" Value="7">Last 7 days</MudSelectItem>
                <MudSelectItem T="int" Value="14">Last 14 days</MudSelectItem>
                <MudSelectItem T="int" Value="30">Last 30 days</MudSelectItem>
            </MudSelect>
            <MudTextField @bind-Value="_title" Label="Title" />

            <MudText Typo="Typo.subtitle2" Class="mt-4">
                Sources for this issue (@_checked.Count of @_sources.Count)
            </MudText>
            @foreach (var s in _sources)
            {
                <MudCheckBox T="bool" Value="@_checked.Contains(s.Id)"
                             ValueChanged="@(v => Toggle(s.Id, v))"
                             Label="@($"{s.DisplayName} ({s.Type})")" Dense="true" />
            }
            <div class="mt-1">
                <MudButton Size="Size.Small" StartIcon="@Icons.Material.Filled.Add"
                           OnClick="AddSource">New source…</MudButton>
                <MudCheckBox T="bool" @bind-Value="_saveAsDefault" Dense="true"
                             Label="Save this set as the automation's new default" />
            </div>
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => Dialog!.Cancel())">Cancel</MudButton>
        <MudButton OnClick="@(() => Create(gather: false))" Disabled="@(_recipes.Count == 0)">Start empty</MudButton>
        <MudButton Color="Color.Primary" Variant="Variant.Filled" Disabled="@(_recipes.Count == 0)"
                   OnClick="@(() => Create(gather: true))">Create &amp; gather</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance? Dialog { get; set; }
    [Parameter] public Guid TenantId { get; set; }

    private List<Recipe> _recipes = [];
    private List<Source> _sources = [];
    private HashSet<Guid> _checked = [];
    private Guid _recipeId;
    private int _windowDays = 7;
    private string _title = "";
    private bool _saveAsDefault;

    protected override async Task OnInitializedAsync()
    {
        _recipes = (await RecipeSvc.ListAsync(TenantId)).Where(r => r.Kind == DraftKinds.Newsletter).ToList();
        _sources = await SourceSvc.ListAsync(TenantId);
        if (_recipes.Count > 0) await OnRecipeChanged(_recipes[0].Id);
    }

    private async Task OnRecipeChanged(Guid id)
    {
        _recipeId = id;
        var recipe = _recipes.Single(r => r.Id == id);
        var ids = JsonSerializer.Deserialize<Guid[]>(recipe.SourceIdsJson) ?? [];
        _checked = ids.Length == 0 ? _sources.Select(s => s.Id).ToHashSet() : ids.ToHashSet();
        _title = await PostSvc.SuggestTitleAsync(id);
    }

    private void Toggle(Guid id, bool on) { if (on) _checked.Add(id); else _checked.Remove(id); }

    private async Task AddSource()
    {
        var dialog = await DialogService.ShowAsync<QuickSourceDialog>("New source",
            new DialogParameters<QuickSourceDialog> { { d => d.TenantId, TenantId } });
        if ((await dialog.Result)?.Data is Source created)
        {
            _sources = await SourceSvc.ListAsync(TenantId);
            _checked.Add(created.Id);
        }
    }

    private async Task Create(bool gather)
    {
        var post = await PostSvc.CreateIssueAsync(TenantId, _recipeId, _windowDays,
            _checked.ToList(), string.IsNullOrWhiteSpace(_title) ? "Untitled issue" : _title);
        if (_saveAsDefault)
        {
            var recipe = _recipes.Single(r => r.Id == _recipeId);
            recipe.SourceIdsJson = JsonSerializer.Serialize(_checked.ToList());
            await RecipeSvc.UpdateAsync();
        }
        Dialog!.Close();
        Nav.NavigateTo(gather ? $"/issue/{post.Id}?gather=1" : $"/issue/{post.Id}");
    }
}
```

(Uses `RecipeService.UpdateAsync()` — confirm the method exists in `RecipeService.cs`; it follows `SourceService`'s pattern. If it's named differently, use the actual save method.)

- [ ] **Step 3: Wire NewMenu**

In `NewMenu.razor`: inject `TenantContext Ctx` and `IDialogService DialogService`; replace the Newsletter menu item's `OnClick` with:

```razor
<MudMenuItem Icon="@Icons.Material.Filled.Email" OnClick="NewIssue">Newsletter issue…</MudMenuItem>
```

```csharp
private async Task NewIssue()
{
    if (!Ctx.Initialized || Ctx.Active is null)
    {
        Snackbar.Add("Create a tenant first (top-right).", Severity.Info);
        return;
    }
    await DialogService.ShowAsync<NewIssueDialog>("New newsletter issue",
        new DialogParameters<NewIssueDialog> { { d => d.TenantId, Ctx.Active.Id } });
}
```

- [ ] **Step 4: Build + commit**

Run: `dotnet build ContentAutomatorX.slnx -c Release -v q` → 0 errors.

```bash
git add -A
git commit -m "feat: new-issue dialog with per-issue source checklist + quick source creation

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 13: Issue editor page

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`

**Interfaces:**
- Consumes: `PostService` (Get/GetIssueSourceIdsAsync/SetIssueSourcesAsync/GetCandidatesAsync/ComposeAsync/SaveIssueAsync/SubjectIdeasAsync/PushAsync), `IngestionPipeline.RunAsync(tenantId, sourceId, trigger)`, `SourceService.ListAsync`, `EmailHtmlRenderer.Render`.
- Produces: route `/issue/{PostId:guid}` (+ `?gather=1` auto-gathers once on load). Task 12 navigates here; Task 14 links here.

- [ ] **Step 1: Implement the page**

`IssueEditor.razor` — structure (route + house tenant guard; long-running work via `IServiceScopeFactory` like `Sources.razor.FetchNow`):

```razor
@page "/issue/{PostId:guid}"
@implements IDisposable
@inject TenantContext Ctx
@inject PostService PostSvc
@inject SourceService SourceSvc
@inject IServiceScopeFactory ScopeFactory
@inject ISnackbar Snackbar
@inject NavigationManager Nav

@* header: title field · status chip · [Save] [Push ⚡ / Re-push] [Open in MailerLite ↗ when ExternalUrl != null] *@
@* subject row: subject + preview-text fields · [✨ subjects] → chips, click = adopt into subject field *@
@* two panes (MudGrid 6/6):
   left  MATERIAL: source chips (MudChipSet of issue sources, click toggles → SetIssueSourcesAsync + [Re-gather])
         [Gather/Re-gather] button → per checked source: IngestionPipeline.RunAsync(post.TenantId, sourceId)
         then reload candidates; progress via _gathering flag + MudProgressLinear
         candidates table: checkbox | title (link) | source | published — prechecked = all
         [Compose ✨ (n items)] + extra-instructions field; if DraftId != null confirm overwrite
         via a MudMessageBox ("Regenerate replaces the current body. Continue?")
   right BODY: MudTextField @bind-Value="_body" Lines="24" (markdown)
         toggle [Edit|Preview]; Preview renders ((MarkupString)EmailHtmlRenderer.Render(_body, _title))
         inside a bordered div — renderer escapes raw HTML, so this is script-free *@
```

`@code` contract:

```csharp
[Parameter] public Guid PostId { get; set; }
private Post? _post;
private string _title = "", _body = "", _subject = "", _preview = "", _extraInstructions = "";
private List<Source> _allSources = [];
private HashSet<Guid> _issueSources = [];
private List<ContentItem> _candidates = [];
private HashSet<Guid> _checkedItems = [];
private IReadOnlyList<string> _subjectIdeas = [];
private bool _gathering, _composing, _pushing;

protected override async Task OnInitializedAsync()
{
    _post = await PostSvc.GetAsync(PostId);
    if (_post is null) { Nav.NavigateTo("/posts"); return; }
    _title = _post.Title; _subject = _post.Subject ?? ""; _preview = _post.PreviewText ?? "";
    if (_post.DraftId is not null) /* load draft body via PostSvc-added helper or db-backed DraftService.GetAsync */ ;
    _allSources = await SourceSvc.ListAsync(_post.TenantId);
    _issueSources = (await PostSvc.GetIssueSourceIdsAsync(_post)).ToHashSet();
    await ReloadCandidatesAsync();
    var uri = new Uri(Nav.Uri);
    if (System.Web.HttpUtility.ParseQueryString(uri.Query)["gather"] == "1") await GatherAsync();
}
```

Add the one missing read: extend `PostService` with `public Task<Draft?> GetDraftAsync(Guid draftId, CancellationToken ct = default) => db.Drafts.FirstOrDefaultAsync(d => d.Id == draftId, ct);` (one line; keeps the page off `IAppDbContext`).

Actions (each wraps in try/catch → error snackbar, sets its busy flag, `StateHasChanged`):
- `GatherAsync`: scope → `IngestionPipeline`; `foreach (var id in _issueSources) await pipeline.RunAsync(_post.TenantId, id)`; then `ReloadCandidatesAsync()`; snackbar summary.
- `ReloadCandidatesAsync`: `_candidates = await PostSvc.GetCandidatesAsync(_post); _checkedItems = _candidates.Select(c => c.Id).ToHashSet();`
- `ComposeAsync`: guard `_checkedItems.Count > 0`; overwrite-confirm when `_post.DraftId != null`; scope → `PostService` (fresh scope so the CLI call doesn't block the circuit's DbContext); on success reload post + body; snackbar with run status.
- `SaveAsync`: `PostSvc.SaveIssueAsync(PostId, _title, _body, _subject, _preview)`.
- `SubjectsAsync`: `_subjectIdeas = await PostSvc.SubjectIdeasAsync(PostId)` (save first so the body is current).
- `PushAsync`: save first, then `PostSvc.PushAsync(PostId)`; success snackbar with `Open in MailerLite` action; reload `_post`.
- Source chip toggle: update `_issueSources`, `PostSvc.SetIssueSourcesAsync`, then `ReloadCandidatesAsync()`.

- [ ] **Step 2: Build + commit**

Run: `dotnet build ContentAutomatorX.slnx -c Release -v q` → 0 errors.

```bash
git add -A
git commit -m "feat: issue editor (gather, compose, preview, subjects, push)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 14: Posts page real section + Today review queue

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Drafts.razor` (posts table above the drafts list; page already answers `/posts`)
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Home.razor` (real Review queue card replacing part of the coming-soon strip)

**Interfaces:**
- Consumes: `PostService.ListAsync/ReviewQueueAsync`, post fields from Task 1.

- [ ] **Step 1: Posts table in Drafts.razor**

Above the existing `MudExpansionPanels`, add (inject `PostService PostSvc`; load `_posts = await PostSvc.ListAsync(id)` in its reload path):

```razor
<MudText Typo="Typo.h6" Class="mb-2">Newsletter issues</MudText>
<MudTable T="Post" Items="_posts" Hover="true" Class="mb-6" Dense="true">
    <HeaderContent>
        <MudTh>Created</MudTh><MudTh>Title</MudTh><MudTh>Status</MudTh><MudTh></MudTh>
    </HeaderContent>
    <RowTemplate>
        <MudTd>@context.CreatedAt.ToLocalTime().ToString("g")</MudTd>
        <MudTd>@context.Title</MudTd>
        <MudTd>
            <MudChip T="string" Size="Size.Small" Color="@StatusColor(context.Status)">
                @context.Status@(context.NeedsReview ? " · review" : "")
            </MudChip>
        </MudTd>
        <MudTd>
            <MudButton Size="Size.Small" Href="@($"/issue/{context.Id}")">Edit</MudButton>
            @if (context.ExternalUrl is not null)
            {
                <MudButton Size="Size.Small" Href="@context.ExternalUrl" Target="_blank">Open in MailerLite</MudButton>
            }
        </MudTd>
    </RowTemplate>
</MudTable>
<MudText Typo="Typo.h6" Class="mb-2">File drafts (Phase 1)</MudText>
```

```csharp
private static Color StatusColor(PostStatus s) => s switch
{
    PostStatus.Published => Color.Success,
    PostStatus.Pushed => Color.Info,
    PostStatus.Failed => Color.Error,
    _ => Color.Default
};
```

- [ ] **Step 2: Today review queue in Home.razor**

Inject `PostService PostSvc`; in `ReloadAsync` add `_reviewQueue = await PostSvc.ReviewQueueAsync(id);`. Insert a new grid item ABOVE the coming-soon paper:

```razor
@if (_reviewQueue.Count > 0)
{
    <MudItem xs="12">
        <MudPaper Class="pa-4">
            <MudText Typo="Typo.subtitle2" Class="mb-2">Review queue (@_reviewQueue.Count)</MudText>
            @foreach (var post in _reviewQueue)
            {
                <div class="d-flex align-center mb-1" style="gap:12px">
                    <MudChip T="string" Size="Size.Small"
                             Color="@(post.Status == PostStatus.Pushed ? Color.Info : Color.Warning)">
                        @(post.Status == PostStatus.Pushed ? "waiting: hit Send in MailerLite" : "review draft")
                    </MudChip>
                    <MudText>@post.Title</MudText>
                    <MudSpacer />
                    <MudButton Size="Size.Small" Href="@($"/issue/{post.Id}")">Open</MudButton>
                    @if (post.ExternalUrl is not null)
                    {
                        <MudButton Size="Size.Small" Href="@post.ExternalUrl" Target="_blank">MailerLite ↗</MudButton>
                    }
                </div>
            }
        </MudPaper>
    </MudItem>
}
```

Trim the coming-soon paper's text to drop "Review queue for automation drafts" (it's real now).

- [ ] **Step 3: Build + commit**

Run: `dotnet build ContentAutomatorX.slnx -c Release -v q` → 0 errors.

```bash
git add -A
git commit -m "feat: posts list on Posts page + real review queue on Today

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 15: MCP tools — list_posts, push_post

**Files:**
- Modify: `src/ContentAutomatorX.Web/Mcp/ContentXTools.cs` (two tools, same static-method pattern)
- Test: `tests/ContentAutomatorX.IntegrationTests/PostServiceTests.cs` (service layer already covers behavior; MCP layer stays thin per Phase 1 rule 3 — no extra test unless `McpToolsTests.cs` has per-tool coverage; if it does, mirror one entry)

**Interfaces:**
- Consumes: `PostService.ListAsync/PushAsync`.

- [ ] **Step 1: Add the tools**

```csharp
[McpServerTool(Name = "list_posts"), Description("List a tenant's posts (newsletter issues): status, needsReview, externalUrl.")]
public static async Task<string> ListPosts(PostService posts, [Description("Tenant id (GUID)")] string tenantId) =>
    ToJson((await posts.ListAsync(Guid.Parse(tenantId)))
        .Select(p => new { p.Id, p.Title, Status = p.Status.ToString(), p.NeedsReview, p.ExternalUrl, p.CreatedAt }));

[McpServerTool(Name = "push_post"), Description("Push a composed newsletter issue to MailerLite as a DRAFT campaign (sending stays human).")]
public static async Task<string> PushPost(PostService posts, [Description("Post id (GUID)")] string postId)
{
    var post = await posts.PushAsync(Guid.Parse(postId));
    return ToJson(new { post.Id, Status = post.Status.ToString(), post.ExternalUrl });
}
```

- [ ] **Step 2: Build, run full suite, commit**

Run: `dotnet test ContentAutomatorX.slnx -c Release --nologo -v q` → all green.

```bash
git add -A
git commit -m "feat: MCP tools list_posts and push_post

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 16: Docs sync + end-to-end verification

**Files:**
- Modify: `docs/mockups/08a-newsletter-issue-walkthrough.md` (implementation deviations)
- Modify: `docs/superpowers/specs/2026-07-18-newsletter-first-design.md` (status line)
- Create (scratchpad, not committed): drive script per `.claude/skills/verify`

- [ ] **Step 1: Document the two deviations in 08a**

Append under "Recommendations awaiting confirmation":

```markdown
## Implementation notes (2026-07-18 plan)

- Subjects do NOT ride along in the compose call: compose returns the body
  (keeps file drafts clean); the subject field prefills with the title and
  the [✨ subjects] button runs a small dedicated call on demand. Same UX,
  two cheaper calls instead of one fragile structured one.
- The dialog's "since last issue" window option ships as fixed day windows
  (3/7/14/30) in v1.
```

- [ ] **Step 2: Flip the spec status**

Change the spec's `**Status:**` line to `Implemented 2026-07-18 (plan: docs/superpowers/plans/2026-07-18-newsletter-first.md)` — only after Step 4 passes.

- [ ] **Step 3: Full suite**

Run: `dotnet test ContentAutomatorX.slnx -c Release --nologo -v q`
Expected: all green (≈75 pre-existing + ≈30 new).

- [ ] **Step 4: E2E drive (publish-based, per `.claude/skills/verify`)**

1. `dotnet publish src/ContentAutomatorX.Web -c Release -o <scratch>/publish`
2. Run `dotnet ContentAutomatorX.Web.dll --urls http://localhost:5091` from the publish dir (background; fresh throwaway DB).
3. Playwright-core + system Chrome script asserting, in order:
   - create tenant via top-right menu (existing drive recipe)
   - Sources: create an RSS source pointing at a local fixture URL is not possible against the real app — instead create a `Website` source with any URL (it may fail to fetch; that's fine) and assert the form's new type options render
   - Automations: create a newsletter automation (kind Newsletter), assert the new platform select renders
   - `+ New → Newsletter issue…` opens the dialog; source checklist lists the created sources; `Start empty` navigates to `/issue/{id}`
   - editor: type body, Save, assert Preview toggle renders the HTML (contains the typed text)
   - Push without MailerLite config → error snackbar mentioning "Platforms"
   - Platforms page: form renders; Save key button disabled while empty
   - Posts page shows the issue row; Today shows nothing in review (NeedsReview false) — then set nothing further
4. Kill only the 5091 instance (match `--urls http://localhost:5091` in the command line).
Expected: every assertion passes; screenshots for the user.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "docs: newsletter walkthrough deviations + spec status; E2E verified

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Spec-coverage map (self-review)

| Spec item | Task |
|---|---|
| Website source | 3, 11 |
| LlmResearch source (+ web search args) | 4, 11 |
| Platform/Post model, migrations | 1, 8 |
| Credential store (DPAPI) | 2 |
| Markdown → email HTML (fixed template) | 5 |
| MailerLite connector (test/groups/push-draft/status) | 6 |
| Issue lifecycle services (create/candidates/compose/save/subjects/push) | 7 |
| Per-issue source checklist semantics (fetch + eligibility) | 7 (GetCandidatesAsync), 12, 13 |
| Automation → review-queue post (`TargetPlatformId`) | 8 |
| Sent detection + 30-day stats | 9 |
| Platforms page config | 10 |
| New-issue dialog (+ quick source, save-as-default) | 12 |
| Issue editor (gather/re-gather, compose, preview, subjects, push) | 13 |
| Posts page + Today review queue | 14 |
| MCP `list_posts` / `push_post` | 15 |
| Docs + manual E2E | 16 |

Out-of-scope confirmations honored: no auto-send anywhere (connector cannot send); no search-API connector; no section-outline composer; Calendar/Library/AI-Studio untouched.
