# Follow-up Issues #4–#8 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close GitHub issues #4 (credential-store hardening), #5 (email renderer ol/table styles), #6 (Today/newsletter UX polish), #7 (test-coverage gaps), #8 (SQLitePCLRaw NU1903 bump). Issue #9 is explicitly out of scope (manual Reddit application by the user).

**Architecture:** Clean Architecture .NET 10 solution — `Domain` ← `Application`/`Infrastructure` ← `Web` (Blazor Server + MudBlazor, MCP tools in `Web/Mcp`). Tests: `tests/ContentAutomatorX.UnitTests` (xUnit + hand-rolled fakes + `StubHttpHandler`) and `tests/ContentAutomatorX.IntegrationTests` (xUnit + real SQLite temp-file DB via `TestDb.Create()` + `Database.Migrate()`).

**Tech Stack:** .NET 10 (`net10.0`), EF Core 10.0.9 (SQLite), Markdig 0.37.0, MudBlazor 9.7.0, xUnit 2.9.3. **No mocking libraries** — write hand-rolled fakes matching existing style.

## Global Constraints

- Branch: `feature/follow-up-taks` (already checked out, even with origin/main). Commit per task, conventional prefixes (`feat:`/`fix:`/`test:`) matching repo history.
- Every commit message ends with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Build/test commands (repo root): `dotnet build ContentAutomatorX.slnx`, `dotnet test ContentAutomatorX.slnx`. Final verification uses `-c Release` (issue #8 requires Release clean).
- No new NuGet dependencies except the one named in Task 1. No mocking libs. No `Directory.Build.props` introduction.
- Match existing test style: AAA with blank lines, raw-string-literal JSON (`"""..."""`), `Assert.Single`, world-builder `BuildAsync(...)`/`Seed(...)` privates, `using var test = TestDb.Create();` for DB tests.
- Verbatim code in this plan was captured from the current files; if an anchor line moved, adapt the edit to the real file — the intent column governs.

---

### Task 1: Bump SQLitePCLRaw to clear NU1903 (issue #8)

**Files:**
- Modify: `src/ContentAutomatorX.Infrastructure/ContentAutomatorX.Infrastructure.csproj`
- Modify (only if restore still warns): `tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj`

**Interfaces:** none (package graph only).

Background: `Microsoft.EntityFrameworkCore.Sqlite 10.0.9` pulls `SQLitePCLRaw.lib.e_sqlite3 2.1.11` transitively, which trips advisory GHSA-2m69-gcr7-jv3q (affected ≤ 2.1.11). `SQLitePCLRaw.bundle_e_sqlite3 2.1.12` is on nuget.org and stays on the 2.1.x line EF Core expects. Do NOT jump to the 3.x line.

- [ ] **Step 1: Add the direct bundle reference**

In `src/ContentAutomatorX.Infrastructure/ContentAutomatorX.Infrastructure.csproj`, inside the existing `<ItemGroup>` with package references, next to the `Microsoft.EntityFrameworkCore.Sqlite` line, add:

```xml
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.12" />
```

- [ ] **Step 2: Restore and check for NU1903**

Run: `dotnet restore ContentAutomatorX.slnx` (from repo root)
Expected: restore succeeds. Then run `dotnet build ContentAutomatorX.slnx -c Release 2>&1 | grep -i NU1903` — expected: **no output** (warning gone). If NU1903 still appears for `ContentAutomatorX.IntegrationTests` (it references EF Sqlite directly), add the same `<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.12" />` to `tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj` and re-run.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test ContentAutomatorX.slnx -c Release`
Expected: all existing tests PASS (SQLite native lib swap must not break the temp-file DB tests).

- [ ] **Step 4: Commit**

```bash
git add src/ContentAutomatorX.Infrastructure/ContentAutomatorX.Infrastructure.csproj tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj
git commit -m "fix: pin SQLitePCLRaw.bundle_e_sqlite3 2.1.12 to clear NU1903 (GHSA-2m69-gcr7-jv3q) (#8)"
```

---

### Task 2: Harden DpapiCredentialStore + Windows-only test skip (issue #4 + issue #7 DPAPI item)

**Files:**
- Modify: `src/ContentAutomatorX.Infrastructure/Security/DpapiCredentialStore.cs`
- Create: `tests/ContentAutomatorX.UnitTests/WindowsOnlyFactAttribute.cs`
- Test: `tests/ContentAutomatorX.UnitTests/DpapiCredentialStoreTests.cs`

**Interfaces:**
- Consumes: `ICredentialStore` (Domain/Abstractions) — unchanged.
- Produces: `GetAsync` returns `null` for missing file, missing directory, AND corrupted/foreign-user blob (`CryptographicException`). `DeleteAsync` never throws for absent file/dir. `WindowsOnlyFactAttribute` (namespace `ContentAutomatorX.UnitTests`) — reusable for any Windows-only test.

- [ ] **Step 1: Create the skip attribute**

`tests/ContentAutomatorX.UnitTests/WindowsOnlyFactAttribute.cs`:

```csharp
namespace ContentAutomatorX.UnitTests;

/// <summary>DPAPI-backed tests run only on Windows; elsewhere they skip instead of failing.</summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requires Windows (DPAPI)";
    }
}
```

- [ ] **Step 2: Switch existing facts + write the failing tests**

In `DpapiCredentialStoreTests.cs`: replace every `[Fact]` with `[WindowsOnlyFact]` (4 existing tests), then append these tests to the class:

```csharp
[WindowsOnlyFact]
public async Task Empty_secret_round_trips()
{
    var store = new DpapiCredentialStore(_dir);
    await store.SetAsync("empty", "");

    Assert.Equal("", await store.GetAsync("empty"));
}

[WindowsOnlyFact]
public async Task Corrupted_blob_reads_as_absent_instead_of_throwing()
{
    var store = new DpapiCredentialStore(_dir);
    await store.SetAsync("mailerlite:abc", "s3cret");
    var file = Directory.GetFiles(_dir).Single();
    await File.WriteAllBytesAsync(file, [1, 2, 3, 4, 5]); // garbage — Unprotect will fail

    Assert.Null(await store.GetAsync("mailerlite:abc"));
}

[WindowsOnlyFact]
public async Task Get_treats_file_vanishing_underneath_it_as_absent()
{
    var store = new DpapiCredentialStore(_dir);
    await store.SetAsync("gone", "v");
    File.Delete(Directory.GetFiles(_dir).Single()); // simulates the TOCTOU loser side

    Assert.Null(await store.GetAsync("gone"));
}

[WindowsOnlyFact]
public async Task Delete_without_store_directory_does_not_throw()
{
    var store = new DpapiCredentialStore(Path.Combine(_dir, "never-created"));

    await store.DeleteAsync("nope");
}
```

- [ ] **Step 3: Run to verify the red test fails**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter DpapiCredentialStoreTests`
Expected: `Corrupted_blob_reads_as_absent_instead_of_throwing` FAILS with `CryptographicException`. (The other new tests may already pass — they pin behavior.)

- [ ] **Step 4: Implement**

Replace `GetAsync` and `DeleteAsync` in `DpapiCredentialStore.cs` (keep `SetAsync`/`PathFor` untouched):

```csharp
public async Task<string?> GetAsync(string name, CancellationToken ct = default)
{
    byte[] blob;
    try
    {
        blob = await File.ReadAllBytesAsync(PathFor(name), ct);
    }
    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
    {
        return null; // absent — no Exists pre-check, so a file removed mid-flight is just "not found"
    }

    try
    {
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser));
    }
    catch (CryptographicException)
    {
        // Corrupted blob or written by a different Windows user: treat as absent so the UI's
        // "no key stored" path prompts for re-entry instead of crashing the page.
        return null;
    }
}

public Task DeleteAsync(string name, CancellationToken ct = default)
{
    try
    {
        File.Delete(PathFor(name)); // no-op when the file is already gone
    }
    catch (DirectoryNotFoundException)
    {
    }
    return Task.CompletedTask;
}
```

- [ ] **Step 5: Run tests to verify green**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter DpapiCredentialStoreTests`
Expected: all 8 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ContentAutomatorX.Infrastructure/Security/DpapiCredentialStore.cs tests/ContentAutomatorX.UnitTests/WindowsOnlyFactAttribute.cs tests/ContentAutomatorX.UnitTests/DpapiCredentialStoreTests.cs
git commit -m "fix: credential store tolerates vanished files and corrupted blobs; DPAPI tests skip off-Windows (#4)"
```

---

### Task 3: Email renderer — style ordered lists and tables (issue #5)

**Files:**
- Modify: `src/ContentAutomatorX.Application/Newsletter/EmailHtmlRenderer.cs` (method `InlineStyles`)
- Test: `tests/ContentAutomatorX.UnitTests/EmailHtmlRendererTests.cs`

**Interfaces:** `EmailHtmlRenderer.Render(string markdown, string title)` — unchanged signature.

- [ ] **Step 1: Write the failing tests** (append to `EmailHtmlRendererTests`):

```csharp
[Fact]
public void Styles_ordered_lists_like_unordered_ones()
{
    var html = EmailHtmlRenderer.Render("1. first\n2. second", "t");

    Assert.Contains("<ol style=\"margin:0 0 14px;padding-left:24px;\">", html);
    Assert.Contains("<li style=\"margin:0 0 6px;\">", html);
}

[Fact]
public void Styles_tables_with_borders_and_padding()
{
    var html = EmailHtmlRenderer.Render("| A | B |\n| --- | --- |\n| 1 | 2 |", "t");

    Assert.Contains("<table style=\"border-collapse:collapse;width:100%;margin:0 0 14px;\">", html);
    Assert.Contains("<th style=\"border:1px solid #dddddd;padding:6px 10px;text-align:left;background:#f7f7f7;\">", html);
    Assert.Contains("<td style=\"border:1px solid #dddddd;padding:6px 10px;\">", html);
}

[Fact]
public void Styles_horizontal_rules()
{
    var html = EmailHtmlRenderer.Render("above\n\n---\n\nbelow", "t");

    Assert.Contains("<hr style=\"border:none;border-top:1px solid #dddddd;margin:20px 0;\" />", html);
}

[Fact]
public void Null_and_empty_markdown_render_the_shell_without_crashing()
{
    foreach (var md in new[] { null, "", "   " })
    {
        var html = EmailHtmlRenderer.Render(md!, "Empty");

        Assert.Contains("<html", html);
        Assert.Contains("Empty", html);
    }
}
```

- [ ] **Step 2: Run to verify the ol/table tests fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter EmailHtmlRendererTests`
Expected: `Styles_ordered_lists...` and `Styles_tables...` FAIL (`<ol>`/`<table>` pass through unstyled today); `Styles_horizontal_rules` and the null test should already PASS (they pin untested behavior).

- [ ] **Step 3: Implement** — in `InlineStyles`, extend the replace chain (after the `<ul>`/`<li>` lines, before `<blockquote>`):

```csharp
.Replace("<ol>", "<ol style=\"margin:0 0 14px;padding-left:24px;\">")
.Replace("<table>", "<table style=\"border-collapse:collapse;width:100%;margin:0 0 14px;\">")
.Replace("<th>", "<th style=\"border:1px solid #dddddd;padding:6px 10px;text-align:left;background:#f7f7f7;\">")
.Replace("<td>", "<td style=\"border:1px solid #dddddd;padding:6px 10px;\">")
```

(Cell borders + `border-collapse` on the table is the email-safe treatment; `width:100%` keeps single-column layouts intact. Alignment-annotated cells (`<th style="text-align: center;">` from Markdig) keep their Markdig style — same known naive-replace limitation as the existing tags; do not fix that here.)

- [ ] **Step 4: Run tests to verify green**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter EmailHtmlRendererTests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Application/Newsletter/EmailHtmlRenderer.cs tests/ContentAutomatorX.UnitTests/EmailHtmlRendererTests.cs
git commit -m "feat: email-safe inline styles for ordered lists and tables; pin hr + empty-markdown rendering (#5)"
```

---

### Task 4: Failed posts appear in Today's review queue with Retry (issue #6, item 1)

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/PostService.cs` (`ReviewQueueAsync`, lines ~69-75)
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Home.razor` (review-queue block, lines ~41-64, and `@code`)
- Test: `tests/ContentAutomatorX.IntegrationTests/PostServiceTests.cs`

**Interfaces:**
- Produces: `ReviewQueueAsync` now also returns posts with `Status == PostStatus.Failed`.
- Consumes: `PostService.PushAsync(Guid postId)` for the Retry button (already exists; sets Failed → re-push).

- [ ] **Step 1: Write the failing integration test** (append to `PostServiceTests`, reusing its existing `BuildAsync` world-builder — read the file first and match how other review-queue tests seed posts):

```csharp
[Fact]
public async Task Review_queue_includes_failed_posts()
{
    using var test = TestDb.Create();
    var world = await BuildAsync(test); // adapt to the file's actual builder signature
    var failed = new Post
    {
        TenantId = world.Tenant.Id, PlatformId = world.Platform.Id, Kind = DraftKinds.Newsletter,
        Title = "Broken push", Status = PostStatus.Failed, NeedsReview = false
    };
    test.Db.Posts.Add(failed);
    await test.Db.SaveChangesAsync();

    var queue = await world.Posts.ReviewQueueAsync(world.Tenant.Id);

    Assert.Contains(queue, p => p.Id == failed.Id);
}
```

(Adapt entity required members / builder record names to the real file — the assertion is the contract.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter Review_queue_includes_failed_posts`
Expected: FAIL (Failed posts are currently excluded).

- [ ] **Step 3: Implement service change** — in `ReviewQueueAsync` replace the `Where` clause:

```csharp
var list = await db.Posts.Where(p => p.TenantId == tenantId &&
        (p.NeedsReview || p.Status == PostStatus.Pushed || p.Status == PostStatus.Failed) &&
        p.Status != PostStatus.Published)
    .ToListAsync(ct);
```

- [ ] **Step 4: Run test to verify it passes**

Same filter as Step 2. Expected: PASS. Also run the whole `PostServiceTests` class to catch regressions in the other review-queue tests.

- [ ] **Step 5: Surface Failed posts on Home.razor with a Retry button**

In the review-queue `@foreach` block, replace the chip with a status-aware version and add Retry:

```razor
<MudChip T="string" Size="Size.Small" Color="@QueueChipColor(post)">@QueueChipLabel(post)</MudChip>
<MudText>@post.Title</MudText>
<MudSpacer />
@if (post.Status == PostStatus.Failed)
{
    <MudButton Size="Size.Small" Color="Color.Warning" Disabled="@_retrying"
               OnClick="@(() => RetryPush(post))">Retry</MudButton>
}
<MudButton Size="Size.Small" Href="@($"/issue/{post.Id}")">Open</MudButton>
```

In `@code`, add (verify `ISnackbar` is injected at the top of the file — add `@inject ISnackbar Snackbar` if missing):

```csharp
private bool _retrying;

private static Color QueueChipColor(Post p) => p.Status switch
{
    PostStatus.Failed => Color.Error,
    PostStatus.Pushed => Color.Info,
    _ => Color.Warning
};

private static string QueueChipLabel(Post p) => p.Status switch
{
    PostStatus.Failed => "push failed",
    PostStatus.Pushed => "waiting: hit Send in MailerLite",
    _ => "review draft"
};

private async Task RetryPush(Post post)
{
    _retrying = true;
    try
    {
        await PostSvc.PushAsync(post.Id);
        Snackbar.Add("Pushed to MailerLite ✓", Severity.Success);
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Retry failed: {ex.Message}", Severity.Error);
    }
    finally { _retrying = false; }
    await ReloadTodayAsync(); // call the page's existing load method (the one setting _reviewQueue) — use its real name
}
```

- [ ] **Step 6: Build**

Run: `dotnet build ContentAutomatorX.slnx`
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/ContentAutomatorX.Application/Services/PostService.cs src/ContentAutomatorX.Web/Components/Pages/Home.razor tests/ContentAutomatorX.IntegrationTests/PostServiceTests.cs
git commit -m "feat: failed pushes surface in Today's review queue with one-click Retry (#6)"
```

---

### Task 5: Dialog prefill, readable compose error, stored-group seeding (issue #6, items 2–5)

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Shared/NewIssueDialog.razor`
- Modify: `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor` (`ComposeAsync`, lines ~297-304)
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Platforms.razor` (`ReloadAsync` ~line 130, `Test()` ~line 157, select block ~lines 31-36)

**Interfaces:**
- Consumes: `SelectionRules.TimeWindowDays` (`ContentAutomatorX.Domain.Models`, `int?`), `Recipe.SelectionJson`, `MailerLiteGroup(string Id, string Name)` (`ContentAutomatorX.Domain.Models`), `PipelineRun.LogJson` (JSON string array; last element carries the failure message appended by `GenerationPipeline.Finish`).

- [ ] **Step 1: NewIssueDialog — prefill the material window from the automation**

Replace the static select items (lines 29-34) with:

```razor
<MudSelect T="int" @bind-Value="_windowDays" Label="Material window">
    @foreach (var d in WindowOptions())
    {
        <MudSelectItem T="int" Value="@d">Last @d days</MudSelectItem>
    }
</MudSelect>
```

In `@code`, add below `_windowDays`:

```csharp
private static readonly int[] DefaultWindows = [3, 7, 14, 30];

private IEnumerable<int> WindowOptions() =>
    DefaultWindows.Contains(_windowDays) ? DefaultWindows : DefaultWindows.Append(_windowDays).OrderBy(d => d);
```

In `OnRecipeChanged`, after `var recipe = _recipes.Single(r => r.Id == id);` (line 103), insert:

```csharp
int? configured = null;
try { configured = JsonSerializer.Deserialize<SelectionRules>(recipe.SelectionJson)?.TimeWindowDays; }
catch (JsonException) { /* malformed SelectionJson — keep the default */ }
_windowDays = configured.HasValue && configured.Value > 0 ? configured.Value : 7;
```

(`OnInitializedAsync` already routes the auto-selected first recipe through `OnRecipeChanged` (line 89), so the prefill applies on open too. Check `_Imports.razor` for `@using ContentAutomatorX.Domain.Models`; add the `@using` to this file if missing.)

- [ ] **Step 2: IssueEditor — readable one-line compose error**

Replace the snackbar line (~303):

```csharp
Snackbar.Add(run.Status == RunStatus.Succeeded ? "Composed."
    : $"Compose {run.Status}: {LastLogLine(run.LogJson)} — full log on the Runs page", severity);
```

Add to `@code`:

```csharp
private static string LastLogLine(string logJson)
{
    try
    {
        var lines = JsonSerializer.Deserialize<string[]>(logJson);
        return lines is { Length: > 0 } ? lines[^1] : "no details logged";
    }
    catch (JsonException) { return "no details logged"; }
}
```

(`GenerationPipeline.Finish` appends the error message as the final log entry, so the last line is the failure reason. The Runs page keeps the full log.)

- [ ] **Step 3: Platforms — seed the group select from stored config + "not verified" hint**

In `ReloadAsync`, after `_groupId`/`_storedGroupId`/`_storedGroupName` are populated (~line 130), add:

```csharp
_verified = false;
if (_groups.Count == 0 && !string.IsNullOrEmpty(_storedGroupId))
    _groups = [new MailerLiteGroup(_storedGroupId,
        string.IsNullOrWhiteSpace(_storedGroupName) ? _storedGroupId : _storedGroupName)];
```

Add field `private bool _verified;` and in `Test()` set `_verified = true;` inside the `if (ok)` branch (after `_groups = [.. await PlatformSvc.ListGroupsAsync(_platform)];`).

Under the `Audience group` `MudSelect` (after line 36), add:

```razor
@if (!_verified && _groups.Count > 0)
{
    <MudText Typo="Typo.caption" Class="mud-text-secondary">
        Showing the saved group from config — not verified yet. "Test &amp; load groups" checks the key and loads all groups.
    </MudText>
}
```

- [ ] **Step 4: Build**

Run: `dotnet build ContentAutomatorX.slnx`
Expected: 0 errors. (These are razor-only behaviors; the E2E "verify" skill covers them at the end.)

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Web/Components/Shared/NewIssueDialog.razor src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor src/ContentAutomatorX.Web/Components/Pages/Platforms.razor
git commit -m "feat: prefill issue window from automation, readable compose errors, seed stored MailerLite group (#6)"
```

---

### Task 6: MailerLiteClient test gaps (issue #7, item 3)

**Files:**
- Test: `tests/ContentAutomatorX.UnitTests/MailerLiteClientTests.cs` (tests only — no production change)

**Interfaces:** Consumes `MailerLiteClient(HttpClient)`, `StubHttpHandler`, the file's existing `Json(...)` helper, and `MailerLiteDraft` — **read `src/ContentAutomatorX.Domain/Models/MailerLiteModels.cs` first** for the record's exact positional parameters and copy construction style from the file's existing PushDraft tests.

- [ ] **Step 1: Add the tests** (append; adapt `MailerLiteDraft` construction to the real record):

```csharp
[Fact]
public async Task GetStatus_sends_get_with_bearer_to_the_campaign_path()
{
    var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"draft"}}"""));
    var client = new MailerLiteClient(new HttpClient(handler));

    await client.GetStatusAsync("KEY", "c42");

    var req = Assert.Single(handler.Requests);
    Assert.Equal(HttpMethod.Get, req.Method);
    Assert.EndsWith("/campaigns/c42", req.RequestUri!.AbsolutePath);
    Assert.Equal("Bearer KEY", req.Headers.Authorization!.ToString());
}

[Fact]
public async Task GetStatus_without_status_property_reports_unknown()
{
    var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42"}}"""));
    var client = new MailerLiteClient(new HttpClient(handler));

    var status = await client.GetStatusAsync("KEY", "c42");

    Assert.Equal("unknown", status.Status);
}

[Fact]
public async Task GetStatus_with_partial_stats_fills_what_it_can()
{
    var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c42","status":"sent","stats":{"sent":10}}}"""));
    var client = new MailerLiteClient(new HttpClient(handler));

    var status = await client.GetStatusAsync("KEY", "c42");

    Assert.Equal(10, status.Sent);
    Assert.Null(status.OpensCount);
    Assert.Null(status.ClicksCount);
}

[Fact]
public async Task GetStatus_error_response_throws_with_status_code()
{
    var handler = new StubHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
        { Content = new StringContent("""{"message":"nope"}""") });
    var client = new MailerLiteClient(new HttpClient(handler));

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetStatusAsync("KEY", "c42"));

    Assert.Contains("404", ex.Message);
}

[Fact]
public async Task PushDraft_create_sends_every_campaign_field()
{
    var handler = new StubHttpHandler(_ => Json("""{"data":{"id":"c1"}}"""));
    var client = new MailerLiteClient(new HttpClient(handler));
    var draft = /* construct MailerLiteDraft with Name="Weekly #1", GroupId="g1", Subject="Subject A",
                   PreviewText="Preview B", FromName="Chris", FromEmail="chris@ex.com", Html="<p>x</p>"
                   — positional order per MailerLiteModels.cs */;

    var id = await client.PushDraftAsync("KEY", draft, existingCampaignId: null);

    Assert.Equal("c1", id);
    var req = Assert.Single(handler.Requests);
    Assert.Equal(HttpMethod.Post, req.Method);
    Assert.EndsWith("/campaigns", req.RequestUri!.AbsolutePath);
    using var body = System.Text.Json.JsonDocument.Parse(await req.Content!.ReadAsStringAsync());
    var root = body.RootElement;
    Assert.Equal("Weekly #1", root.GetProperty("name").GetString());
    Assert.Equal("regular", root.GetProperty("type").GetString());
    Assert.Equal("g1", root.GetProperty("groups")[0].GetString());
    var email = root.GetProperty("emails")[0];
    Assert.Equal("Subject A", email.GetProperty("subject").GetString());
    Assert.Equal("Preview B", email.GetProperty("preview_text").GetString());
    Assert.Equal("Chris", email.GetProperty("from_name").GetString());
    Assert.Equal("chris@ex.com", email.GetProperty("from").GetString());
    Assert.Equal("<p>x</p>", email.GetProperty("content").GetString());
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter MailerLiteClientTests`
Expected: all PASS (these pin existing behavior; a failure means a real client bug — investigate before touching the client).

- [ ] **Step 3: Commit**

```bash
git add tests/ContentAutomatorX.UnitTests/MailerLiteClientTests.cs
git commit -m "test: pin MailerLite GetStatus request shape, unknown/partial stats, and full create-campaign body (#7)"
```

---

### Task 7: WebsiteConnector — title normalization + single selector query (issue #7, item 5)

**Files:**
- Modify: `src/ContentAutomatorX.Infrastructure/Sources/WebsiteConnector.cs` (lines ~32-39 anchors block, line ~51 title)
- Test: `tests/ContentAutomatorX.UnitTests/WebsiteConnectorTests.cs`

**Interfaces:** `ISourceConnector.FetchAsync` — unchanged signature. Read the existing test file first for how a `Source` + config and `StubHttpHandler` are wired.

- [ ] **Step 1: Write the failing tests** (append; wire `Source`/config/handler exactly like the file's existing tests — inline HTML via `new StubHttpHandler(req => ...)` returning `text/html` content):

```csharp
[Fact]
public async Task Title_internal_whitespace_is_collapsed_to_single_spaces()
{
    // listing anchor text spans lines: "Big\n        AI   News"
    // build a listing page whose <article> contains:
    //   <a href="/story">Big
    //           AI   News</a>
    // expected item title: "Big AI News"
}

[Fact]
public async Task Selector_matching_both_container_and_anchor_yields_one_item_per_url()
{
    // config.Mode = "selector", config.ItemSelector = ".card, .card a"
    // page: <div class="card"><a href="/one">Story one headline</a></div>
    // the anchor is found via BOTH branches (as a match child and as a direct match) —
    // assert exactly one item for /one
}
```

Write these as real tests following the file's existing arrange helpers (fixtures vs inline HTML — match whichever pattern the neighboring tests use; assert `item.Title == "Big AI News"` and `Assert.Single(items)` respectively).

- [ ] **Step 2: Run to verify the title test fails**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter WebsiteConnectorTests`
Expected: title test FAILS (titles are only trimmed today, internal `\n`/runs of spaces survive). The dedup test should already PASS via the `seen` hash-set — it pins that contract while the query is restructured.

- [ ] **Step 3: Implement**

Title (line ~51) — replace `var title = a.TextContent.Trim();` with:

```csharp
var title = string.Join(' ', a.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
```

Selector block (lines ~32-39) — materialize the selector query once:

```csharp
var selectorMatches = config.Mode == "selector" && !string.IsNullOrWhiteSpace(config.ItemSelector)
    ? doc.QuerySelectorAll(config.ItemSelector).ToList()
    : null;
var anchors = (selectorMatches is not null
        ? selectorMatches.OfType<IHtmlAnchorElement>()
            .Concat(selectorMatches.SelectMany(e => e.QuerySelectorAll("a").OfType<IHtmlAnchorElement>()))
        : doc.QuerySelectorAll("article a[href], main a[href]").OfType<IHtmlAnchorElement>()
            .Where(a => (a.TextContent?.Trim().Length ?? 0) >= MinLinkTextLength))
    .Where(a => !string.IsNullOrWhiteSpace(a.GetAttribute("href")))
    .ToList();
```

- [ ] **Step 4: Run all WebsiteConnector tests**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter WebsiteConnectorTests`
Expected: all PASS (including the 6 pre-existing ones).

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Infrastructure/Sources/WebsiteConnector.cs tests/ContentAutomatorX.UnitTests/WebsiteConnectorTests.cs
git commit -m "fix: collapse whitespace in scraped titles; query item selector once and pin anchor dedup (#7)"
```

---

### Task 8: LlmResearchConnector — empty-array and retry-exhausted tests (issue #7, item 6)

**Files:**
- Test: `tests/ContentAutomatorX.UnitTests/LlmResearchConnectorTests.cs` (tests only)

**Interfaces:** Consumes the file's existing `QueueLlm(params string[] replies)` fake — read it first; if it lacks a call counter, add one (`public int Calls`) to the fake in the same file.

- [ ] **Step 1: Add the tests** (arrange a `Source` exactly like the file's existing tests do):

```csharp
[Fact]
public async Task Empty_array_reply_yields_no_items_without_retry()
{
    var llm = new QueueLlm("[]");
    // build connector + source per the file's existing pattern

    // act: FetchAsync

    // assert: result is empty AND only one LLM call was made (no retry)
    Assert.Empty(items);
    Assert.Equal(1, llm.Calls);
}

[Fact]
public async Task Two_malformed_replies_throw_an_actionable_error()
{
    var llm = new QueueLlm("not json", "still not json");
    // build connector + source per the file's existing pattern

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(/* act */);

    Assert.Contains("did not return valid JSON after retry", ex.Message);
}
```

Fill the arrange/act from the neighboring tests — they construct the connector and source inline; the assertions above are the contract to pin (`TryParse` accepts `[]` as valid on attempt 1; two non-JSON replies exhaust the single retry and throw).

- [ ] **Step 2: Run**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter LlmResearchConnectorTests`
Expected: all PASS (pinning existing behavior).

- [ ] **Step 3: Commit**

```bash
git add tests/ContentAutomatorX.UnitTests/LlmResearchConnectorTests.cs
git commit -m "test: pin LlmResearch empty-array reply and retry-exhausted failure (#7)"
```

---

### Task 9: PostSyncService — robust StatsStale + isolation/cancellation tests (issue #7, item 1)

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/PostSyncService.cs` (`StatsStale`, lines ~59-68)
- Test: `tests/ContentAutomatorX.IntegrationTests/PostSyncServiceTests.cs`

**Interfaces:** `PostSyncService.TickAsync(DateTimeOffset now, CancellationToken ct)` — unchanged. Reuse the file's `BuildAsync(PostStatus status, string? statsJson, DateTimeOffset? publishedAt)` builder and its `FakeMailerLite`; extend the fake if it can't throw per-campaign (add e.g. `public Func<string, MailerLiteCampaignStatus>? OnGetStatus` or a `ThrowForCampaignId` field — match its existing style).

- [ ] **Step 1: Write the failing test for non-date refreshedAt**

```csharp
[Fact]
public async Task Non_date_refreshedAt_counts_as_stale_and_gets_refreshed()
{
    // arrange: Published post, recent publishedAt, StatsJson = """{"refreshedAt":"not-a-date"}"""
    // act: TickAsync(now)
    // assert: the fake's GetStatus WAS called for the post (stats refreshed), StatsJson updated
}
```

Also add (these pin existing behavior, expected green from the start):

```csharp
[Fact]
public async Task Malformed_stats_json_counts_as_stale_and_gets_refreshed() { /* StatsJson = "{{{", same asserts */ }

[Fact]
public async Task Missing_api_key_skips_the_post_without_touching_it()
{
    // arrange: pushed post, but do NOT store an API key in the credentials fake
    // assert: post untouched, no GetStatus call, TickAsync completes without throwing
}

[Fact]
public async Task One_failing_post_does_not_block_the_next()
{
    // arrange: two Pushed posts; fake throws for the FIRST campaign id only
    // assert: second post still processed (status advanced / GetStatus called for it)
}

[Fact]
public async Task Cancellation_propagates_out_of_the_tick()
{
    // arrange: one Pushed post; a CancellationTokenSource cancelled before the call
    // assert: await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.TickAsync(now, cts.Token));
}
```

Write all five as full tests against the real builder — the comments above are the scenario contract, the arrange code comes from the file's existing four tests.

- [ ] **Step 2: Run to verify the red test**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter PostSyncServiceTests`
Expected: `Non_date_refreshedAt...` FAILS (the `FormatException`/`InvalidOperationException` from `GetDateTimeOffset()` escapes `StatsStale`, is swallowed by the per-post catch, and the post is silently skipped — no refresh happens). Others PASS.

- [ ] **Step 3: Implement** — replace `StatsStale`'s catch:

```csharp
catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
{
    // Unreadable stats (malformed JSON or a refreshedAt that isn't a date) count as stale:
    // the sync tick simply refreshes them.
    return true;
}
```

- [ ] **Step 4: Run tests to verify green**

Same filter. Expected: all PostSyncServiceTests PASS (9 total).

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Application/Services/PostSyncService.cs tests/ContentAutomatorX.IntegrationTests/PostSyncServiceTests.cs
git commit -m "fix: unreadable refreshedAt counts as stale; pin missing-key skip, per-post isolation, cancellation (#7)"
```

---

### Task 10: PostService — direct tests for untested methods (issue #7, item 2)

**Files:**
- Test: `tests/ContentAutomatorX.IntegrationTests/PostServiceTests.cs` (tests only)

**Interfaces:** Consumes `PostService.MarkReviewedAsync(Guid)`, `SetIssueSourcesAsync(Post, IReadOnlyList<Guid>)`, `ListAsync(Guid)`, `SubjectIdeasAsync(Guid)` and the file's `BuildAsync(string llmReply = ...)` builder (the `FakeLlm` returns the same reply on every call — two unparseable attempts happen automatically).

- [ ] **Step 1: Add the tests** (reuse `BuildAsync` + `CreateIssueAsync` arrange from neighbors):

```csharp
[Fact]
public async Task Mark_reviewed_clears_the_flag()
{
    // arrange: create issue (NeedsReview true or set it true + save)
    // act: await world.Posts.MarkReviewedAsync(post.Id);
    // assert: reload via fresh context (test.NewContext()) — NeedsReview is false
}

[Fact]
public async Task Set_issue_sources_persists_the_id_list()
{
    // arrange: created issue + two source ids
    // act: SetIssueSourcesAsync(post, [idA, idB])
    // assert: fresh-context reload — SourceIdsJson deserializes to exactly [idA, idB]
}

[Fact]
public async Task List_returns_newest_first()
{
    // arrange: two posts with CreatedAt an hour apart (set explicitly)
    // act: ListAsync(tenantId)
    // assert: first element is the newer post
}

[Fact]
public async Task Subject_ideas_throws_after_two_unparseable_replies()
{
    // arrange: BuildAsync(llmReply: "this is not a JSON array"); create + compose-or-save an issue body
    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => world.Posts.SubjectIdeasAsync(post.Id));
    Assert.Contains("did not return subject lines", ex.Message);
}
```

Model the arrange on `Subject_ideas_parses_five_strings` (same file) for the last test. Write all four in full.

- [ ] **Step 2: Run**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter PostServiceTests`
Expected: all PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/ContentAutomatorX.IntegrationTests/PostServiceTests.cs
git commit -m "test: cover MarkReviewed, SetIssueSources, List ordering, and subject-retry failure (#7)"
```

---

### Task 11: PlatformService — verify the unique-index race before recovering (issue #7, item 8)

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/PlatformService.cs` (`GetOrCreateMailerLiteAsync` catch, lines ~30-37)
- Test: `tests/ContentAutomatorX.IntegrationTests/PlatformServiceTests.cs`

**Interfaces:** `GetOrCreateMailerLiteAsync(Guid tenantId)` — unchanged signature; a `DbUpdateException` that is NOT a lost uniqueness race now rethrows instead of failing later inside `SingleAsync`. Also add coverage for `TestAsync` (null key → false) and `ListGroupsAsync` (null key → throws).

- [ ] **Step 1: Write the failing test**

The file already has `RacingPlatformDbContext` — add a sibling wrapper `FailingSaveDbContext` in the same file that implements the same interface/pattern but whose `SaveChangesAsync` throws `new DbUpdateException("disk I/O error")` once WITHOUT inserting a competing row. Then:

```csharp
[Fact]
public async Task Non_uniqueness_save_failure_is_rethrown_not_masked_as_a_race()
{
    // arrange: PlatformService over FailingSaveDbContext for a tenant with NO existing platform row
    await Assert.ThrowsAsync<DbUpdateException>(() => svc.GetOrCreateMailerLiteAsync(tenantId));
}
```

Also add (pin, likely green — arrange with the fakes the file/project already use; `InMemoryCredentials` and `FakeMailerLite` exist in the integration project):

```csharp
[Fact]
public async Task Test_returns_false_when_no_api_key_is_stored() { /* TestAsync → false, client not called */ }

[Fact]
public async Task List_groups_without_api_key_throws_actionable_message()
{
    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ListGroupsAsync(platform));
    Assert.Contains("No API key stored", ex.Message);
}
```

- [ ] **Step 2: Run to verify the red test**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter PlatformServiceTests`
Expected: `Non_uniqueness_save_failure...` FAILS — current code swallows the `DbUpdateException` and then `SingleAsync` throws `InvalidOperationException` ("Sequence contains no elements"), not `DbUpdateException`.

- [ ] **Step 3: Implement** — replace the catch block:

```csharp
catch (DbUpdateException)
{
    // Probably lost a race against another circuit that created the same tenant/type row first —
    // but verify: if no winning row exists, this wasn't the unique index. Surface the real failure.
    db.Platforms.Remove(platform);
    var winner = await db.Platforms.SingleOrDefaultAsync(
        p => p.TenantId == tenantId && p.Type == PlatformTypes.MailerLite, ct);
    if (winner is null) throw;
    return winner;
}
```

- [ ] **Step 4: Run tests to verify green**

Same filter. Expected: all PlatformServiceTests PASS, including the two pre-existing race tests.

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Application/Services/PlatformService.cs tests/ContentAutomatorX.IntegrationTests/PlatformServiceTests.cs
git commit -m "fix: rethrow non-uniqueness save failures in GetOrCreateMailerLite; cover TestAsync/ListGroups key guards (#7)"
```

---

### Task 12: MCP tools — consistent not-found contract + coverage for untested tools (issue #7, item 9)

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/ContentService.cs` (`MarkAsync`, lines 35-41)
- Modify: `src/ContentAutomatorX.Web/Mcp/ContentXTools.cs` (`MarkItem` lines 49-57, `PushPost` lines 112-117)
- Test: `tests/ContentAutomatorX.IntegrationTests/McpToolsTests.cs`

**Interfaces:**
- Produces: `ContentService.MarkAsync(Guid itemId, ContentItemStatus status)` now returns `Task<bool>` (false = item not found) instead of throwing. **Grep all `MarkAsync` call sites** (Inbox page and any others) — `await`-only callers keep compiling; if any caller displayed the old exception message, keep its UX by checking the bool.
- Produces: `mark_item` and `push_post` reply with the same JSON `"not found"` literal as the `get_*` tools.

- [ ] **Step 1: Write/strengthen the failing tests** in `McpToolsTests.cs`:

Strengthen `Mark_item_changes_status` — after the existing assert, verify persistence:

```csharp
using var fresh = test.NewContext();
Assert.Equal(ContentItemStatus.Selected, fresh.ContentItems.Single(i => i.Id == item.Id).Status);
```

Add not-found contract tests (these FAIL until Step 3):

```csharp
[Fact]
public async Task Mark_item_unknown_id_returns_not_found()
{
    using var test = TestDb.Create();

    var json = await ContentXTools.MarkItem(new ContentService(test.Db), Guid.NewGuid().ToString(), "Selected");

    Assert.Equal("not found", JsonDocument.Parse(json).RootElement.GetString());
}

[Fact]
public async Task Push_post_unknown_id_returns_not_found()
{
    // arrange a PostService per the project's existing pattern (world builder or direct construction)

    var json = await ContentXTools.PushPost(postService, Guid.NewGuid().ToString());

    Assert.Equal("not found", JsonDocument.Parse(json).RootElement.GetString());
}
```

Add coverage for the untested read tools (pin, expected green once written — arrange with seeded rows like the neighboring `List_drafts_returns_the_projected_shape`):

```csharp
[Fact] public async Task List_sources_returns_the_tenants_sources() { /* seed 1 source; assert id + type in JSON */ }
[Fact] public async Task List_content_items_filters_by_status() { /* seed New + Selected; call with status "Selected"; assert only that one */ }
[Fact] public async Task List_recipes_returns_the_tenants_recipes() { /* seed 1 recipe; assert name present */ }
[Fact] public async Task Get_recipe_returns_not_found_for_unknown_and_json_for_real() { /* both branches, like the get_tenant test */ }
[Fact] public async Task Get_draft_returns_not_found_for_unknown_id() { /* unknown GUID → "not found" */ }
[Fact] public async Task Trigger_ingestion_reports_run_status() { /* tenant with no sources → run completes; assert runStatus property exists in JSON */ }
```

Write all of these in full, modeling arrange on the file's existing tests (`IngestionPipeline` construction: copy however the integration project builds it elsewhere — check `IngestionPipelineTests` if present, else construct with an empty connector set per its ctor).

- [ ] **Step 2: Run to verify the red tests**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter McpToolsTests`
Expected: `Mark_item_unknown_id_returns_not_found` FAILS (throws `InvalidOperationException: Content item ... not found`), `Push_post_unknown_id_returns_not_found` FAILS (throws from `SingleAsync`).

- [ ] **Step 3: Implement**

`ContentService.MarkAsync`:

```csharp
/// <summary>Sets the item's curation status. Returns false when the item no longer exists.</summary>
public async Task<bool> MarkAsync(Guid itemId, ContentItemStatus status)
{
    var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == itemId);
    if (item is null) return false;
    item.Status = status;
    await db.SaveChangesAsync();
    return true;
}
```

`ContentXTools.MarkItem`:

```csharp
[McpServerTool(Name = "mark_item"), Description("Curate a content item: set status Selected or Ignored (or back to New).")]
public static async Task<string> MarkItem(ContentService content,
    [Description("Content item id (GUID)")] string itemId,
    [Description("New status: New|Selected|Ignored")] string status)
{
    var parsed = Enum.Parse<ContentItemStatus>(status, ignoreCase: true);
    return await content.MarkAsync(Guid.Parse(itemId), parsed)
        ? ToJson(new { itemId, status = parsed.ToString() })
        : ToJson("not found");
}
```

`ContentXTools.PushPost`:

```csharp
[McpServerTool(Name = "push_post"), Description("Push a composed newsletter issue to MailerLite as a DRAFT campaign (sending stays human).")]
public static async Task<string> PushPost(PostService posts, [Description("Post id (GUID)")] string postId)
{
    if (await posts.GetAsync(Guid.Parse(postId)) is null) return ToJson("not found");
    var post = await posts.PushAsync(Guid.Parse(postId));
    return ToJson(new { post.Id, Status = post.Status.ToString(), post.ExternalUrl });
}
```

Then grep `MarkAsync(` across `src/` — update any UI caller that relied on the exception (e.g. Inbox page): ignoring the bool is fine for bulk actions; keep behavior otherwise identical.

- [ ] **Step 4: Run tests to verify green**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter McpToolsTests` and `dotnet build ContentAutomatorX.slnx`
Expected: all McpToolsTests PASS; solution builds with 0 errors (catches missed `MarkAsync` callers).

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Application/Services/ContentService.cs src/ContentAutomatorX.Web/Mcp/ContentXTools.cs tests/ContentAutomatorX.IntegrationTests/McpToolsTests.cs src/ContentAutomatorX.Web/Components/Pages/*.razor
git commit -m "feat: uniform 'not found' MCP replies for mark_item/push_post; cover remaining MCP tools (#7)"
```

---

### Task 13: GenerationPipeline — delivery-failure keeps the review post (issue #7, item 4)

**Files:**
- Test: `tests/ContentAutomatorX.IntegrationTests/GenerationPipelineTests.cs` (tests only)

**Interfaces:** Consumes the file's world builder + `FakeDelivery` (make it throw — check how `Delivery_failure_keeps_draft_generated_and_run_partial` arranges the failing delivery) and a recipe WITH `TargetPlatformId` (copy arrange from `Recipe_with_target_platform_creates_a_needs_review_post`).

- [ ] **Step 1: Add the tests**

```csharp
[Fact]
public async Task Delivery_failure_with_target_platform_still_creates_the_review_post()
{
    // arrange: recipe WITH TargetPlatformId + failing delivery (combine the two existing arranges)

    // act: run the pipeline

    // assert: run.Status == RunStatus.Partial
    //         exactly one Post exists; it has NeedsReview == true
    //         post.PlatformId == the recipe's TargetPlatformId
    //         post.RecipeId == recipe.Id
}
```

Also extend the existing `Recipe_with_target_platform_creates_a_needs_review_post` with `PlatformId`/`RecipeId` value asserts if it lacks them.

- [ ] **Step 2: Run**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter GenerationPipelineTests`
Expected: all PASS — the post is created before delivery is attempted (lines 74-84 precede the delivery try/catch), so this pins the ordering. If it FAILS, that is a real regression risk — stop and investigate, do not change the pipeline to make the test pass without understanding.

- [ ] **Step 3: Commit**

```bash
git add tests/ContentAutomatorX.IntegrationTests/GenerationPipelineTests.cs
git commit -m "test: delivery failure with a target platform still yields the review post with platform/recipe ids (#7)"
```

---

### Task 14: Final verification + PR

**Files:** none (verification + git only).

- [ ] **Step 1: Full Release build + tests, NU1903 check**

```bash
dotnet build ContentAutomatorX.slnx -c Release 2>&1 | grep -iE "NU1903|error|warn" || echo CLEAN
dotnet test ContentAutomatorX.slnx -c Release
```
Expected: no NU1903, 0 errors, all tests pass.

- [ ] **Step 2: E2E sanity via the project's verify skill** — launch the app and click through: Today page renders (with a Failed post if seedable), new-issue dialog opens, Platforms page shows the seeded group hint. (Skill: `verify`.)

- [ ] **Step 3: Push and open PR**

```bash
git push -u origin feature/follow-up-taks
gh pr create --title "Follow-ups: credential-store hardening, email renderer, UX polish, test gaps, NU1903 (#4-#8)" --body "..."
```
PR body lists per-issue changes and ends with `Closes #4`, `Closes #5`, `Closes #6`, `Closes #7`, `Closes #8` plus the standard generated-with footer.
