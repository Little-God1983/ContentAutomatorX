# Newsletter Image Upload & Local Staging — Implementation Plan (PR 1 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the composer set a section image by uploading a file *or* importing from a URL; both are staged on local disk under `IssueSection.ImageKey` and shown in the preview. (R2 hosting + send-time promotion is PR 2; video compositing is PR 3.)

**Architecture:** A Web-layer disk store (`NewsletterImageStagingStore`, twin of the existing `TenantAvatarStore`) writes bytes under `data/newsletter-images` and serves them at `/newsletter-images/{file}`. `IssueSection` gains a nullable `ImageKey`. The static HTML renderers gain an optional `imageSrc` resolver delegate so the composer *preview* points at the local endpoint while the *send* path (unchanged default) omits staged images. `ImageUrl` is retained only as the legacy/auto-metadata hotlink fallback.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, EF Core (SQLite), xUnit.

## Global Constraints

- **Branch:** `feature/newsletter-image-upload` (already created off `main`; the design spec commit is already on it). Do not commit to `main`.
- **Layering:** `Application` must not reference Web or do image-byte file I/O. `Domain` gets only the `ImageKey` column. No new Infrastructure dependency (no AWS SDK / ImageSharp in PR 1).
- **Allow-list (both upload and URL import):** `image/png`→`.png`, `image/jpeg`→`.jpg`, `image/webp`→`.webp`, `image/gif`→`.gif`. Reject anything else. **Max 5 MB.** These match `TenantAvatarStore` exactly.
- **URL import hard-fails** on any download/validation error — no raw URL is stored (spec D4).
- **Spec:** `docs/superpowers/specs/2026-07-23-newsletter-image-upload-staging-design.md`.
- **Build/test:** `dotnet build ContentAutomatorX.sln` and `dotnet test tests/ContentAutomatorX.UnitTests` / `dotnet test tests/ContentAutomatorX.IntegrationTests`.

---

### Task 1: `IssueSection.ImageKey` — column, migration, history

**Files:**
- Modify: `src/ContentAutomatorX.Domain/Entities/IssueSection.cs`
- Modify: `src/ContentAutomatorX.Application/Services/IssueHistoryService.cs` (SectionSnapshot record + `CaptureAsync` ~L92 + `RestoreAsync` ~L130)
- Create: EF migration `NewsletterImageKey` under `src/ContentAutomatorX.Infrastructure/Migrations/`
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs` (undo-preserves-ImageKey test)

**Interfaces:**
- Produces: `IssueSection.ImageKey` (`string?`); `SectionSnapshot` gains an `ImageKey` positional field.

- [ ] **Step 1:** Add to `IssueSection.cs` after `ImageUrl`:
```csharp
public string? ImageKey { get; set; }      // staging file name under data/newsletter-images; null = none
```

- [ ] **Step 2:** In `IssueHistoryService.cs`, locate the `SectionSnapshot` record (positional). Add `string? ImageKey` immediately after `ImageUrl`. Update `CaptureAsync` (the `new SectionSnapshot(...)` at ~L92) to pass `s.ImageKey` after `s.ImageUrl`, and `RestoreAsync` (~L130) to add `section.ImageKey = want.ImageKey;` after the `ImageUrl` line.

- [ ] **Step 3:** Create the migration:
```
dotnet ef migrations add NewsletterImageKey --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web
```
Confirm the generated `Up` adds a nullable `ImageKey TEXT` column to `IssueSections` and nothing else.

- [ ] **Step 4:** Write the failing undo test in `IssueComposerServiceTests.cs` (follow the existing history-test style in that file / `ComposerHistoryTests.cs`):
```csharp
[Fact]
public async Task Undo_restores_ImageKey()
{
    using var test = await TestDb.CreateAsync();
    var composer = /* build IssueComposerService as other tests here do */;
    var post = /* create post + EnsureSectionsAsync + a topic section */;
    var sections = await composer.GetSectionsAsync(post.Id);
    var topic = sections.First(s => s.Type == SectionTypes.Topic);

    await composer.SetSectionImageKeyAsync(topic.Id, "abc.png");     // Task 6 method
    await composer.UpdateSectionAsync(topic.Id, "t", "b", topic.ImageUrl, null, null, null); // a later mutation to undo back past
    await history.UndoAsync(post.Id);                                 // undo the update
    var after = await composer.GetSectionsAsync(post.Id);
    Assert.Equal("abc.png", after.First(s => s.Id == topic.Id).ImageKey);
}
```
> If Task 6's `SetSectionImageKeyAsync` isn't implemented yet when running tasks in order, keep this test but mark it skipped until Task 6, or set `topic.ImageKey` directly via the db context to prove the snapshot round-trips. Prefer the direct-db version so Task 1 is self-contained:
```csharp
topic.ImageKey = "abc.png"; await test.Db.SaveChangesAsync();
await composer.UpdateSectionAsync(topic.Id, "t", "b", topic.ImageUrl, null, null, null);
await history.UndoAsync(post.Id);
Assert.Equal("abc.png", (await composer.GetSectionsAsync(post.Id)).First(s => s.Id == topic.Id).ImageKey);
```

- [ ] **Step 5:** Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter Undo_restores_ImageKey` — expect PASS (snapshot now carries ImageKey). Then `dotnet build` the whole solution to confirm the migration + snapshot changes compile.

- [ ] **Step 6:** Commit:
```bash
git add -A && git commit -m "feat: add IssueSection.ImageKey column + history snapshot"
```

---

### Task 2: `NewsletterImageStaging` — request-path constant + resolvers (Application)

**Files:**
- Create: `src/ContentAutomatorX.Application/Newsletter/NewsletterImageStaging.cs`
- Modify: `src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs` (make `IsHttpUrl` `internal`)
- Test: `tests/ContentAutomatorX.UnitTests/NewsletterImageStagingTests.cs`

**Interfaces:**
- Produces:
  - `const string NewsletterImageStaging.RequestPath = "/newsletter-images"`
  - `static string? NewsletterImageStaging.PreviewSrc(IssueSection s)` — staged key → `"/newsletter-images/{key}"`; else pasted hotlink; else null
  - `static string? NewsletterImageStaging.PushSrc(IssueSection s)` — pasted hotlink only (staged key omitted); equals the renderer default

- [ ] **Step 1:** Change `SectionHtmlRenderer.IsHttpUrl` from `private` to `internal`.

- [ ] **Step 2:** Write failing test `NewsletterImageStagingTests.cs`:
```csharp
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Xunit;

namespace ContentAutomatorX.UnitTests;

public class NewsletterImageStagingTests
{
    private static IssueSection Sec(string? key = null, string? url = null) =>
        new() { Type = SectionTypes.Topic, ImageKey = key, ImageUrl = url };

    [Fact] public void PreviewSrc_prefers_staged_key() =>
        Assert.Equal("/newsletter-images/a.png", NewsletterImageStaging.PreviewSrc(Sec(key: "a.png", url: "https://x/y.png")));

    [Fact] public void PreviewSrc_falls_back_to_hotlink() =>
        Assert.Equal("https://x/y.png", NewsletterImageStaging.PreviewSrc(Sec(url: "https://x/y.png")));

    [Fact] public void PreviewSrc_null_when_nothing() =>
        Assert.Null(NewsletterImageStaging.PreviewSrc(Sec()));

    [Fact] public void PushSrc_omits_staged_key() =>
        Assert.Null(NewsletterImageStaging.PushSrc(Sec(key: "a.png")));

    [Fact] public void PushSrc_keeps_hotlink() =>
        Assert.Equal("https://x/y.png", NewsletterImageStaging.PushSrc(Sec(url: "https://x/y.png")));
}
```

- [ ] **Step 3:** Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter NewsletterImageStaging` — expect FAIL (type missing).

- [ ] **Step 4:** Create `NewsletterImageStaging.cs`:
```csharp
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Staging-store request path + the two image-source resolvers the renderers take.
/// Preview points at the local staging endpoint; push (PR 1) omits staged images and keeps
/// only pasted hotlinks — PR 2 replaces the push resolver with the R2 one.</summary>
public static class NewsletterImageStaging
{
    public const string RequestPath = "/newsletter-images";

    public static string? PreviewSrc(IssueSection s) =>
        !string.IsNullOrEmpty(s.ImageKey) ? $"{RequestPath}/{s.ImageKey}"
        : SectionHtmlRenderer.IsHttpUrl(s.ImageUrl) ? s.ImageUrl
        : null;

    public static string? PushSrc(IssueSection s) =>
        SectionHtmlRenderer.IsHttpUrl(s.ImageUrl) ? s.ImageUrl : null;
}
```

- [ ] **Step 5:** Run the same filter — expect PASS.

- [ ] **Step 6:** Commit:
```bash
git add -A && git commit -m "feat: newsletter image staging path + preview/push resolvers"
```

---

### Task 3: Renderer `imageSrc` resolver parameter

**Files:**
- Modify: `src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs`
- Modify: `src/ContentAutomatorX.Application/Newsletter/TemplateHtmlRenderer.cs`
- Test: `tests/ContentAutomatorX.UnitTests/SectionHtmlRendererTests.cs`

**Interfaces:**
- Consumes: `NewsletterImageStaging.PreviewSrc/PushSrc` (Task 2).
- Produces: renderer overloads accepting `Func<IssueSection,string?>? imageSrc = null`; default behaves exactly as today (pasted-URL only, YouTube fallback preserved).

- [ ] **Step 1:** Write failing test in `SectionHtmlRendererTests.cs`:
```csharp
[Fact]
public void Render_uses_imageSrc_resolver_for_topic_image()
{
    var sections = new List<IssueSection>
    { new() { Position = 0, Type = SectionTypes.Topic, Title = "T", ImageKey = "k.png" } };
    var html = SectionHtmlRenderer.Render(sections, TestTenant(), "t",
        s => s.ImageKey is { } k ? $"/newsletter-images/{k}" : null);
    Assert.Contains("/newsletter-images/k.png", html);
}

[Fact]
public void Render_default_omits_ImageKey()   // push behavior
{
    var sections = new List<IssueSection>
    { new() { Position = 0, Type = SectionTypes.Topic, Title = "T", ImageKey = "k.png" } };
    var html = SectionHtmlRenderer.Render(sections, TestTenant(), "t");
    Assert.DoesNotContain("newsletter-images", html);
}
```

- [ ] **Step 2:** Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter imageSrc` — expect FAIL (overload missing).

- [ ] **Step 3:** In `SectionHtmlRenderer.cs`:
  - Add a private default: `internal static string? DefaultImageSrc(IssueSection s) => IsHttpUrl(s.ImageUrl) ? s.ImageUrl : null;`
  - `Render(...)`: add trailing param `Func<IssueSection,string?>? imageSrc = null`; inside, `var resolve = imageSrc ?? DefaultImageSrc;` pass `resolve` to `AppendSection`.
  - `RenderSection(...)`: add same optional param, thread to `AppendSection`.
  - `AppendSection(StringBuilder sb, IssueSection s, string accent, Func<IssueSection,string?> imageSrc)`: replace the two `IsHttpUrl(s.ImageUrl)` image blocks (Topic ~L77, Sponsor ~L103) with:
```csharp
var src = imageSrc(s);
if (src is not null)
    sb.AppendLine($"""<img src="{WebUtility.HtmlEncode(src)}" alt="{title}" style="..." />""");   // keep each block's existing style string
```
  - `VideoThumbnail`: change to `internal static string? VideoThumbnail(IssueSection s, Func<IssueSection,string?> imageSrc) => imageSrc(s) ?? (YouTubeUrl.TryGetVideoId(s.LinkUrl, out var id) ? YouTubeUrl.FallbackThumbnail(id) : null);` and update its caller in the Video case to pass `imageSrc`.

- [ ] **Step 4:** In `TemplateHtmlRenderer.cs`:
  - `Render(...)`: add trailing `Func<IssueSection,string?>? imageSrc = null`; `var resolve = imageSrc ?? SectionHtmlRenderer.DefaultImageSrc;`
  - Pass `resolve` into the `SectionHtmlRenderer.Render(...)` (L29) and `SectionHtmlRenderer.RenderSection(...)` (L51) fallback calls.
  - Replace `values["thumbnail_url"] = SafeUrl(SectionHtmlRenderer.VideoThumbnail(section), ...)` (L181) with `SectionHtmlRenderer.VideoThumbnail(section, resolve)`.
  - Replace `values["image_url"] = SafeUrl(section.ImageUrl, ...)` (L185) with `SafeUrl(resolve(section), ...)`.

- [ ] **Step 5:** Run the full unit suite: `dotnet test tests/ContentAutomatorX.UnitTests`. Expect the two new tests PASS and **all existing renderer tests still pass** (default resolver preserves behavior).

- [ ] **Step 6:** Commit:
```bash
git add -A && git commit -m "feat: renderers take an imageSrc resolver (default preserves current behavior)"
```

---

### Task 4: `NewsletterImageStagingStore` — upload + serve (Web)

**Files:**
- Create: `src/ContentAutomatorX.Web/Services/NewsletterImageStagingStore.cs`
- Test: `tests/ContentAutomatorX.UnitTests/NewsletterImageStagingStoreTests.cs`

**Interfaces:**
- Consumes: `NewsletterImageStaging.RequestPath` (Task 2).
- Produces:
  - `Task<string> SaveAsync(IBrowserFile file, CancellationToken ct = default)` → new file name; throws `InvalidOperationException` on bad type/size
  - `void Delete(string? key)` — traversal-guarded, best-effort
  - `static string? UrlFor(string? key)` → `null` or `"/newsletter-images/{key}"`
  - `string DirectoryPath`
  - test constructor `NewsletterImageStagingStore(string dir, HttpClient http)`

- [ ] **Step 1:** Write failing test `NewsletterImageStagingStoreTests.cs`:
```csharp
using System.Net.Http;
using ContentAutomatorX.Web.Services;
using Xunit;

namespace ContentAutomatorX.UnitTests;

public class NewsletterImageStagingStoreTests
{
    private static NewsletterImageStagingStore Make(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "cx-stage-" + Guid.NewGuid().ToString("N"));
        return new NewsletterImageStagingStore(dir, new HttpClient(new StubHttpHandler(_ => new(System.Net.HttpStatusCode.OK))));
    }

    [Fact]
    public void UrlFor_formats_or_nulls()
    {
        Assert.Null(NewsletterImageStagingStore.UrlFor(null));
        Assert.Equal("/newsletter-images/x.png", NewsletterImageStagingStore.UrlFor("x.png"));
    }

    [Fact]
    public void Delete_ignores_traversal_and_missing()
    {
        var store = Make(out _);
        store.Delete("../secret");   // must not throw, must not escape
        store.Delete("nope.png");    // missing, must not throw
    }
}
```

- [ ] **Step 2:** Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter NewsletterImageStagingStore` — expect FAIL (type missing).

- [ ] **Step 3:** Create the store (model on `TenantAvatarStore`; the `SaveFromUrlAsync` body comes in Task 5 — add the method stub `throw new NotImplementedException();` for now so the file compiles, or omit it until Task 5):
```csharp
using ContentAutomatorX.Application.Newsletter;
using Microsoft.AspNetCore.Components.Forms;

namespace ContentAutomatorX.Web.Services;

/// <summary>Stages newsletter images on disk under <c>data/newsletter-images</c>, served at
/// <c>/newsletter-images/{file}</c> (static-file mapping in Program.cs). Twin of
/// <see cref="TenantAvatarStore"/>; unlike avatars these are promoted to R2 at send (PR 2).</summary>
public sealed class NewsletterImageStagingStore
{
    public const string RequestPath = NewsletterImageStaging.RequestPath;
    private const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png", ["image/jpeg"] = ".jpg", ["image/webp"] = ".webp", ["image/gif"] = ".gif",
    };

    private readonly string _dir;
    private readonly HttpClient _http;

    public NewsletterImageStagingStore(IWebHostEnvironment env, IHttpClientFactory httpFactory)
        : this(Path.Combine(env.ContentRootPath, "data", "newsletter-images"),
               httpFactory.CreateClient(nameof(NewsletterImageStagingStore))) { }

    // Test-friendly.
    public NewsletterImageStagingStore(string dir, HttpClient http)
    {
        _dir = dir; _http = http;
        Directory.CreateDirectory(_dir);
    }

    public string DirectoryPath => _dir;

    public static string? UrlFor(string? key) =>
        string.IsNullOrEmpty(key) ? null : $"{RequestPath}/{key}";

    public async Task<string> SaveAsync(IBrowserFile file, CancellationToken ct = default)
    {
        if (!AllowedTypes.TryGetValue(file.ContentType, out var ext))
            throw new InvalidOperationException("Unsupported image type — use PNG, JPEG, WEBP or GIF.");
        if (file.Size > MaxBytes)
            throw new InvalidOperationException($"Image is too large (max {MaxBytes / (1024 * 1024)} MB).");
        await using var src = file.OpenReadStream(MaxBytes, ct);
        return await SaveStreamAsync(src, ext, ct);
    }

    internal async Task<string> SaveStreamAsync(Stream src, string ext, CancellationToken ct)
    {
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var full = Path.Combine(_dir, fileName);
        await using var dest = File.Create(full);
        await src.CopyToAsync(dest, ct);
        return fileName;
    }

    public void Delete(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (Path.GetFileName(key) != key) return;         // never escape the folder
        try { var full = Path.Combine(_dir, key); if (File.Exists(full)) File.Delete(full); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 4:** Run the filter — expect PASS.

- [ ] **Step 5:** Commit:
```bash
git add -A && git commit -m "feat: NewsletterImageStagingStore (upload + serve + delete)"
```

---

### Task 5: `SaveFromUrlAsync` — URL import with validation (hard-fail)

**Files:**
- Modify: `src/ContentAutomatorX.Web/Services/NewsletterImageStagingStore.cs`
- Test: `tests/ContentAutomatorX.UnitTests/NewsletterImageStagingStoreTests.cs`

**Interfaces:**
- Produces: `Task<string> SaveFromUrlAsync(string url, CancellationToken ct = default)` → new file name; throws `InvalidOperationException` on non-absolute URL, download failure, disallowed content-type, magic-byte mismatch, or oversize.

- [ ] **Step 1:** Add failing tests (binary responses via `StubHttpHandler`):
```csharp
private static byte[] PngBytes() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };

private static NewsletterImageStagingStore WithResponse(byte[] body, string contentType, out string dir)
{
    dir = Path.Combine(Path.GetTempPath(), "cx-stage-" + Guid.NewGuid().ToString("N"));
    var handler = new StubHttpHandler(_ =>
    {
        var msg = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return msg;
    });
    return new NewsletterImageStagingStore(dir, new HttpClient(handler));
}

[Fact]
public async Task SaveFromUrl_stages_a_valid_png()
{
    var store = WithResponse(PngBytes(), "image/png", out var dir);
    var key = await store.SaveFromUrlAsync("https://host/img.png");
    Assert.EndsWith(".png", key);
    Assert.True(File.Exists(Path.Combine(dir, key)));
}

[Fact]
public async Task SaveFromUrl_rejects_html_masquerading_as_png()
{
    var store = WithResponse(System.Text.Encoding.UTF8.GetBytes("<html>nope</html>"), "image/png", out _);
    await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveFromUrlAsync("https://host/x"));
}

[Fact]
public async Task SaveFromUrl_rejects_disallowed_content_type()
{
    var store = WithResponse(PngBytes(), "text/html", out _);
    await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveFromUrlAsync("https://host/x"));
}

[Fact]
public async Task SaveFromUrl_rejects_relative_url()
{
    var store = Make(out _);
    await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveFromUrlAsync("/not/absolute"));
}
```

- [ ] **Step 2:** Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter SaveFromUrl` — expect FAIL.

- [ ] **Step 3:** Implement in the store:
```csharp
public async Task<string> SaveFromUrlAsync(string url, CancellationToken ct = default)
{
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        throw new InvalidOperationException("Enter an absolute http(s) image URL.");

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(10));
    byte[] bytes;
    try
    {
        using var resp = await _http.GetAsync(uri, cts.Token);
        resp.EnsureSuccessStatusCode();
        bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
    {
        throw new InvalidOperationException("Couldn't download that image — check the URL.");
    }

    if (bytes.Length > MaxBytes)
        throw new InvalidOperationException($"Image is too large (max {MaxBytes / (1024 * 1024)} MB).");

    var ext = SniffImageExtension(bytes)
        ?? throw new InvalidOperationException("That URL is not a PNG, JPEG, WEBP or GIF image.");

    await using var src = new MemoryStream(bytes);
    return await SaveStreamAsync(src, ext, ct);
}

// Trust magic bytes, not the server's Content-Type header.
private static string? SniffImageExtension(byte[] b)
{
    if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
    if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
    if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38) return ".gif";
    if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
        && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) return ".webp";
    return null;
}
```
> Note: validation is by **magic bytes**, so the `text/html` test fails at the sniff step (its body is `<html>`, no image signature) — the header is not trusted. That is intentional and covers both bad-content-type and lying-server cases.

- [ ] **Step 4:** Run the filter — expect all four PASS. Then run the whole `NewsletterImageStagingStore` filter to confirm Task 4 tests still pass.

- [ ] **Step 5:** Commit:
```bash
git add -A && git commit -m "feat: import newsletter image from URL with magic-byte validation (hard-fail)"
```

---

### Task 6: Composer service image methods + wire preview resolver

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs`

**Interfaces:**
- Consumes: `NewsletterImageStaging.PreviewSrc` (Task 2), history `SnapshotAsync`.
- Produces:
  - `Task<string?> SetSectionImageKeyAsync(Guid sectionId, string key, CancellationToken ct = default)` — snapshot, set `ImageKey=key`, clear `ImageUrl`, save; returns the **previous** `ImageKey` (for the caller to delete its file)
  - `Task<string?> ClearSectionImageAsync(Guid sectionId, CancellationToken ct = default)` — snapshot, clear `ImageKey`+`ImageUrl`, save; returns previous `ImageKey`
  - `RenderPreviewAsync` now resolves images via `NewsletterImageStaging.PreviewSrc`

- [ ] **Step 1:** Write failing tests:
```csharp
[Fact]
public async Task SetSectionImageKey_sets_key_clears_url_returns_old()
{
    // build composer + a topic section with ImageUrl set (as other tests here do)
    // ...
    var first = await composer.SetSectionImageKeyAsync(topic.Id, "one.png");
    Assert.Null(first);                                   // no prior key
    var s1 = (await composer.GetSectionsAsync(post.Id)).First(s => s.Id == topic.Id);
    Assert.Equal("one.png", s1.ImageKey);
    Assert.Null(s1.ImageUrl);                             // hotlink cleared

    var second = await composer.SetSectionImageKeyAsync(topic.Id, "two.png");
    Assert.Equal("one.png", second);                      // returns prior key
}

[Fact]
public async Task RenderPreview_points_staged_image_at_local_endpoint()
{
    // topic with ImageKey = "pic.png"
    await composer.SetSectionImageKeyAsync(topic.Id, "pic.png");
    var html = await composer.RenderPreviewAsync(post.Id, "My issue");
    Assert.Contains("/newsletter-images/pic.png", html);
}
```

- [ ] **Step 2:** Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "SetSectionImageKey|RenderPreview_points"` — expect FAIL.

- [ ] **Step 3:** Add to `IssueComposerService.cs`:
```csharp
public async Task<string?> SetSectionImageKeyAsync(Guid sectionId, string key, CancellationToken ct = default)
{
    var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
    await history.SnapshotAsync(section.PostId, "Set image", ct);
    var previous = section.ImageKey;
    section.ImageKey = key;
    section.ImageUrl = null;                              // uploaded/imported image replaces any hotlink
    await db.SaveChangesAsync(ct);
    return previous;
}

public async Task<string?> ClearSectionImageAsync(Guid sectionId, CancellationToken ct = default)
{
    var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
    await history.SnapshotAsync(section.PostId, "Remove image", ct);
    var previous = section.ImageKey;
    section.ImageKey = null;
    section.ImageUrl = null;
    await db.SaveChangesAsync(ct);
    return previous;
}
```
And change `RenderPreviewAsync`'s two render calls to pass the resolver:
```csharp
var html = template is null
    ? SectionHtmlRenderer.Render(sections, tenant, title, NewsletterImageStaging.PreviewSrc)
    : TemplateHtmlRenderer.Render(sections, tenant, title, template.Html, post.CreatedAt, NewsletterImageStaging.PreviewSrc);
```
> `PostService.cs:233-234` (the send path) is **left unchanged** — its default resolver already omits staged images, which is the intended PR-1 behavior.

- [ ] **Step 4:** Run the filter — expect PASS. Also run `ComposerHistoryTests` to confirm the new mutating methods snapshot correctly if that test enumerates by reflection (add `SetSectionImageKeyAsync`/`ClearSectionImageAsync` to its expected-name list if it maintains one).

- [ ] **Step 5:** Commit:
```bash
git add -A && git commit -m "feat: composer set/clear section image + preview resolves staged images"
```

---

### Task 7: DI registration + static-file mapping (Program.cs)

**Files:**
- Modify: `src/ContentAutomatorX.Web/Program.cs` (~L102 registration; ~L156 static-file mapping)

**Interfaces:**
- Consumes: `NewsletterImageStagingStore` (Task 4).
- Produces: the store registered as a singleton with a named `HttpClient`; `/newsletter-images` served from its directory.

- [ ] **Step 1:** After the `TenantAvatarStore` registration (L102) add:
```csharp
builder.Services.AddHttpClient(nameof(ContentAutomatorX.Web.Services.NewsletterImageStagingStore));
builder.Services.AddSingleton<ContentAutomatorX.Web.Services.NewsletterImageStagingStore>();
```

- [ ] **Step 2:** After the existing `/avatars` `UseStaticFiles` block (~L161) add:
```csharp
var newsletterImages = app.Services.GetRequiredService<ContentAutomatorX.Web.Services.NewsletterImageStagingStore>();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(newsletterImages.DirectoryPath),
    RequestPath = ContentAutomatorX.Web.Services.NewsletterImageStagingStore.RequestPath,
});
```

- [ ] **Step 3:** `dotnet build src/ContentAutomatorX.Web` — expect success.

- [ ] **Step 4:** Commit:
```bash
git add -A && git commit -m "chore: register staging store + serve /newsletter-images"
```

---

### Task 8: UI — upload / import / remove on `SectionCard`

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`
- Modify: `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`

**Interfaces:**
- Consumes: `NewsletterImageStagingStore`, `IssueComposerService.SetSectionImageKeyAsync/ClearSectionImageAsync`, `NewsletterImageStagingStore.UrlFor`.
- Produces: image slot UI; `SectionCard.SectionEdit` **drops `ImageUrl`**; three new card callbacks `OnUploadImage(IBrowserFile)`, `OnImportImageUrl(string)`, `OnRemoveImage()`.

- [ ] **Step 1 — SectionCard.razor:**
  - Change the record to `public record SectionEdit(string? Title, string? BodyMd, string? LinkUrl, string? LinkText, string? Category);` and drop `_image` usage from `ToggleExpand`/`Apply`.
  - Replace the `@if (HasImage())` text field (L73-76) with an image slot:
```razor
@if (HasImage())
{
    <div class="mt-2">
        @{ var current = !string.IsNullOrEmpty(Section.ImageKey)
                ? NewsletterImageStagingStore.UrlFor(Section.ImageKey)
                : (SectionHtmlRenderer.IsHttpUrl(Section.ImageUrl) ? Section.ImageUrl : null); }
        @if (current is not null)
        {
            <img src="@current" alt="" style="max-width:100%;max-height:160px;display:block;margin-bottom:6px;border:1px solid var(--mud-palette-lines-default);" />
            @if (!string.IsNullOrEmpty(Section.ImageKey))
            {
                <MudText Typo="Typo.caption" Class="mud-text-secondary">
                    Shown in preview — will be hosted when you post to MailerLite (coming soon).
                </MudText>
            }
        }
        <div class="d-flex align-center mt-1" style="gap:8px">
            <MudFileUpload T="IBrowserFile" Accept="image/*" FilesChanged="OnFilePicked" Disabled="@Busy">
                <ActivatorContent>
                    <MudButton Size="Size.Small" Variant="Variant.Outlined"
                               StartIcon="@Icons.Material.Filled.Upload" Disabled="@Busy">Upload</MudButton>
                </ActivatorContent>
            </MudFileUpload>
            @if (current is not null)
            {
                <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Error"
                           Disabled="@Busy" OnClick="@(() => OnRemoveImage.InvokeAsync())">Remove</MudButton>
            }
        </div>
        <div class="d-flex align-center mt-1" style="gap:8px">
            <MudTextField @bind-Value="_importUrl" Label="@ImageLabel()" Class="flex-grow-1" Immediate="false" />
            <MudButton Size="Size.Small" Variant="Variant.Outlined" Disabled="@(Busy || string.IsNullOrWhiteSpace(_importUrl))"
                       OnClick="ImportUrl">Import URL</MudButton>
        </div>
    </div>
}
```
  - Add fields/handlers in `@code`:
```csharp
private string _importUrl = "";
[Parameter] public EventCallback<IBrowserFile> OnUploadImage { get; set; }
[Parameter] public EventCallback<string> OnImportImageUrl { get; set; }
[Parameter] public EventCallback OnRemoveImage { get; set; }
private Task OnFilePicked(IBrowserFile? file) => file is null ? Task.CompletedTask : OnUploadImage.InvokeAsync(file);
private async Task ImportUrl()
{
    var url = _importUrl.Trim();
    if (url.Length == 0) return;
    _importUrl = "";
    await OnImportImageUrl.InvokeAsync(url);
}
```
  - Add `@using ContentAutomatorX.Web.Services`, `@using ContentAutomatorX.Application.Newsletter`, `@using Microsoft.AspNetCore.Components.Forms` at the top.

- [ ] **Step 2 — IssueEditor.razor:**
  - Inject the store: `@inject ContentAutomatorX.Web.Services.NewsletterImageStagingStore StagingStore` (near the other `@inject`s).
  - Add the three callbacks to the `<SectionCard>` usage (L104-112):
```razor
OnUploadImage="@(file => UploadImageAsync(section, file))"
OnImportImageUrl="@(url => ImportImageAsync(section, url))"
OnRemoveImage="@(() => RemoveImageAsync(section))"
```
  - Update `ApplyAsync` to preserve `ImageUrl` (the card no longer edits it):
```csharp
private Task ApplyAsync(IssueSection s, SectionCard.SectionEdit e) =>
    RunMutationAsync(c => c.UpdateSectionAsync(s.Id, e.Title, e.BodyMd, s.ImageUrl, e.LinkUrl, e.LinkText, e.Category), "Save");
```
  - Add handlers (mirror the `RunMutationAsync`/error-snackbar style already in the file; capture the old key from the in-memory section and delete its file after a successful DB write):
```csharp
private async Task UploadImageAsync(IssueSection s, IBrowserFile file)
{
    try
    {
        var key = await StagingStore.SaveAsync(file);
        await RunMutationAsync(c => c.SetSectionImageKeyAsync(s.Id, key), "Image");
        StagingStore.Delete(s.ImageKey);                 // old staged file, if any (s is pre-reload)
    }
    catch (InvalidOperationException ex) { Snackbar.Add(ex.Message, Severity.Error); }
}

private async Task ImportImageAsync(IssueSection s, string url)
{
    try
    {
        var key = await StagingStore.SaveFromUrlAsync(url);
        await RunMutationAsync(c => c.SetSectionImageKeyAsync(s.Id, key), "Image");
        StagingStore.Delete(s.ImageKey);
    }
    catch (InvalidOperationException ex) { Snackbar.Add(ex.Message, Severity.Error); }
}

private async Task RemoveImageAsync(IssueSection s)
{
    var oldKey = s.ImageKey;
    await RunMutationAsync(c => c.ClearSectionImageAsync(s.Id), "Image");
    StagingStore.Delete(oldKey);
}
```
> `RunMutationAsync` returns after `ReloadSectionsAsync`; `SetSectionImageKeyAsync`/`ClearSectionImageAsync` return the previous key too, but capturing `s.ImageKey` from the pre-reload section is equivalent and simpler here. Confirm the exact `RunMutationAsync` signature in the file (it wraps `WithComposerAsync` + reload + busy/snackbar) and match it; if it returns the op's value, prefer the returned previous-key for the delete.
  - In `DeleteAsync(section)` (the section-delete handler), capture `section.ImageKey` before removal and `StagingStore.Delete(...)` it after the mutation succeeds.

- [ ] **Step 3:** `dotnet build src/ContentAutomatorX.Web` — fix any Razor/type errors (esp. the `Snackbar` inject name — match what the page already uses; it may be `Snackbar` or `Sb`).

- [ ] **Step 4:** Commit:
```bash
git add -A && git commit -m "feat: image upload/import/remove UI on section cards"
```

---

### Task 9: End-to-end verification

**Files:** none (manual + full suite).

- [ ] **Step 1:** Full build + tests:
```
dotnet build ContentAutomatorX.sln
dotnet test tests/ContentAutomatorX.UnitTests
dotnet test tests/ContentAutomatorX.IntegrationTests
```
Expect all green.

- [ ] **Step 2:** Use the `verify` skill (or `dotnet run --project src/ContentAutomatorX.Web`) to drive the UI:
  - Open an issue in the composer, expand a Topic card.
  - **Upload** a PNG → it appears in the card slot and in the Preview tab (`<img src="/newsletter-images/...">`), with the "hosted when you post" caption.
  - **Import URL** with a real image URL → same result; with a bad URL (404 / HTML page) → red snackbar, slot unchanged.
  - **Remove** → image disappears from card + preview; the file is gone from `data/newsletter-images`.
  - Confirm a section with a **pasted metadata hotlink** (topic from inbox) still shows in preview and is unaffected.

- [ ] **Step 3:** Commit any fixups, then hand off (PR 2 = R2 publish + promotion).

## Self-Review notes (author)

- **Spec coverage:** §3 model→Task 1; §4 store→Tasks 4–5,7; §5 composer→Task 6; §6 renderers→Tasks 2–3; §7 UI→Task 8; §8 interim (push omits staged, hotlinks keep working)→Task 3 default + Task 6 leaving PostService untouched; §10 tests distributed per task; undo/redo of `ImageKey` (not in spec but required by the existing history system)→Task 1.
- **Type consistency:** `SetSectionImageKeyAsync`/`ClearSectionImageAsync` return `Task<string?>` (previous key) everywhere; `imageSrc` is `Func<IssueSection,string?>` in every renderer signature; `NewsletterImageStaging.RequestPath` is the single source of the `/newsletter-images` string, reused by the store and the static-file mapping.
- **Known interim regression (accepted):** URL import stages locally and is omitted from a pushed draft until PR 2 (spec §8) — surfaced by the card caption.
