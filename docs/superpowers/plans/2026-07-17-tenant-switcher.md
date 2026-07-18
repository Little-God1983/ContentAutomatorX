# Tenant Switcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace per-page tenant dropdowns with a single global "active tenant", switched from an account-style menu in the upper-right of the app bar, persisted across refresh via browser storage.

**Architecture:** A new scoped `TenantContext` service (one per Blazor circuit) is the single source of truth for the active tenant, restoring the last-used tenant from `ProtectedLocalStorage` through a testable `ITenantIdStore` seam. A `TenantSwitcher` component in the app bar switches/creates tenants; all six pages read `TenantContext.Active` and react to its `Changed` event. Web layer only — no domain, application-service, or DB changes; no migration.

**Tech Stack:** .NET 10, Blazor Server (interactive server render mode, already global), MudBlazor 9.7.0, xUnit (hand-rolled fakes, no mocking library).

**Spec:** `docs/superpowers/specs/2026-07-17-tenant-switcher-design.md`

## Global Constraints

- Repo root: `E:\Repos\ContentAutomatorX`. All commands run from repo root.
- **The app may be running** (`ContentAutomatorX.Web`) and locks build output DLLs. Before every `dotnet build`/`dotnet test`, make sure it is stopped: `Get-Process ContentAutomatorX.Web -ErrorAction SilentlyContinue | Stop-Process` (PowerShell) — otherwise builds fail with MSB3026/MSB3027 file-lock errors.
- No new NuGet packages. `ProtectedLocalStorage` ships with `AddInteractiveServerComponents()` (already registered).
- Web project only (`src/ContentAutomatorX.Web`) plus integration tests. Do not touch Domain, Application, Infrastructure, or MCP tools.
- Tests: xUnit, hand-rolled fakes only. New tests go in `tests/ContentAutomatorX.IntegrationTests` (it already references the Web project; the unit-test project does not).
- Blazor components are NOT unit-tested in this repo — UI wiring is verified by running the app (final task).
- Conventional commits after every task. Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Existing test count: 52 (23 unit + 29 integration) — all must stay green.

## File Structure (end state)

```
src/ContentAutomatorX.Web/
  Services/ITenantIdStore.cs                        # 2-method persistence seam        (Task 1)
  Services/TenantContext.cs                         # scoped active-tenant state       (Task 1)
  Services/ProtectedLocalStorageTenantIdStore.cs    # browser-storage implementation   (Task 2)
  Services/TenantSlug.cs                            # name → slug derivation           (Task 3)
  Components/Shared/CreateTenantDialog.razor        # quick-create dialog              (Task 3)
  Components/Shared/NoTenantHint.razor              # empty-state hint                 (Task 4)
  Components/Layout/TenantSwitcher.razor            # app-bar menu                     (Task 4)
  Components/Layout/MainLayout.razor                # modified: spacer+switcher, drawer (Task 4)
  Components/Pages/{Home,Drafts,Runs,Sources,Recipes,Content}.razor  # modified (Tasks 5-8)
  Components/Pages/Tenants.razor                    # modified: RefreshAsync           (Task 9)
  Components/_Imports.razor                         # modified: new @usings            (Tasks 2-3)
  Program.cs                                        # modified: DI registrations       (Task 2)
tests/ContentAutomatorX.IntegrationTests/
  TenantContextTests.cs                             # incl. FakeTenantIdStore          (Task 1)
  TenantSlugTests.cs                                                                   (Task 3)
```

---

### Task 1: `ITenantIdStore` + `TenantContext` with tests

**Files:**
- Create: `src/ContentAutomatorX.Web/Services/ITenantIdStore.cs`
- Create: `src/ContentAutomatorX.Web/Services/TenantContext.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/TenantContextTests.cs`

**Interfaces:**
- Consumes: existing `TenantService(IAppDbContext)` with `Task<List<Tenant>> ListAsync()` (ordered by Name); `TestDb.Create()` test helper; `Tenant` entity (`Id`, `Name`, `Slug`, `IsActive`).
- Produces: `ITenantIdStore { Task<Guid?> GetAsync(); Task SetAsync(Guid id); }` and `TenantContext(TenantService, ITenantIdStore)` with `Tenant? Active`, `IReadOnlyList<Tenant> ActiveTenants`, `bool Initialized`, `event Action? Changed`, `Task InitializeAsync()`, `Task SwitchAsync(Guid)`, `Task RefreshAsync()`. Every later task depends on these exact names. Namespace: `ContentAutomatorX.Web.Services`.

- [ ] **Step 1: Write the failing tests**

`tests/ContentAutomatorX.IntegrationTests/TenantContextTests.cs`:

```csharp
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Web.Services;

namespace ContentAutomatorX.IntegrationTests;

public class FakeTenantIdStore : ITenantIdStore
{
    public Guid? Stored;
    public Task<Guid?> GetAsync() => Task.FromResult(Stored);
    public Task SetAsync(Guid id) { Stored = id; return Task.CompletedTask; }
}

public class TenantContextTests
{
    private static Tenant AddTenant(TestDb test, string name, bool active = true)
    {
        var tenant = new Tenant { Name = name, Slug = name.ToLowerInvariant(), IsActive = active };
        test.Db.Tenants.Add(tenant);
        test.Db.SaveChanges();
        return tenant;
    }

    [Fact]
    public async Task Initialize_restores_stored_tenant_and_raises_Changed()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var store = new FakeTenantIdStore { Stored = beta.Id };
        var ctx = new TenantContext(new TenantService(test.Db), store);
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.InitializeAsync();

        Assert.True(ctx.Initialized);
        Assert.Equal(beta.Id, ctx.Active!.Id);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Initialize_with_stale_stored_id_falls_back_to_first_active_by_name_and_persists_it()
    {
        using var test = TestDb.Create();
        var alpha = AddTenant(test, "Alpha");
        AddTenant(test, "Beta");
        var store = new FakeTenantIdStore { Stored = Guid.NewGuid() };
        var ctx = new TenantContext(new TenantService(test.Db), store);

        await ctx.InitializeAsync();

        Assert.Equal(alpha.Id, ctx.Active!.Id);
        Assert.Equal(alpha.Id, store.Stored);
    }

    [Fact]
    public async Task Initialize_with_no_active_tenants_leaves_Active_null()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Dormant", active: false);
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());

        await ctx.InitializeAsync();

        Assert.True(ctx.Initialized);
        Assert.Null(ctx.Active);
        Assert.Empty(ctx.ActiveTenants);
    }

    [Fact]
    public async Task Inactive_tenants_are_excluded_from_ActiveTenants()
    {
        using var test = TestDb.Create();
        var alpha = AddTenant(test, "Alpha");
        AddTenant(test, "Dormant", active: false);
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());

        await ctx.InitializeAsync();

        Assert.Equal([alpha.Id], ctx.ActiveTenants.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Switch_sets_active_persists_and_raises_Changed_but_ignores_unknown_ids()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var store = new FakeTenantIdStore();
        var ctx = new TenantContext(new TenantService(test.Db), store);
        await ctx.InitializeAsync();
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.SwitchAsync(beta.Id);
        Assert.Equal(beta.Id, ctx.Active!.Id);
        Assert.Equal(beta.Id, store.Stored);
        Assert.Equal(1, changed);

        await ctx.SwitchAsync(Guid.NewGuid());   // unknown id → no-op
        Assert.Equal(beta.Id, ctx.Active!.Id);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Refresh_after_deactivating_active_tenant_falls_back_and_raises_Changed()
    {
        using var test = TestDb.Create();
        var alpha = AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());
        await ctx.InitializeAsync();
        await ctx.SwitchAsync(beta.Id);
        beta.IsActive = false;
        test.Db.SaveChanges();
        var changed = 0;
        ctx.Changed += () => changed++;

        await ctx.RefreshAsync();

        Assert.Equal(alpha.Id, ctx.Active!.Id);
        Assert.Equal(1, changed);
        Assert.DoesNotContain(ctx.ActiveTenants, t => t.Id == beta.Id);
    }

    [Fact]
    public async Task Refresh_keeps_current_active_tenant_when_still_valid()
    {
        using var test = TestDb.Create();
        AddTenant(test, "Alpha");
        var beta = AddTenant(test, "Beta");
        var ctx = new TenantContext(new TenantService(test.Db), new FakeTenantIdStore());
        await ctx.InitializeAsync();
        await ctx.SwitchAsync(beta.Id);

        await ctx.RefreshAsync();

        Assert.Equal(beta.Id, ctx.Active!.Id);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter TenantContextTests`
Expected: FAIL — compile error, `ITenantIdStore`/`TenantContext` do not exist.

- [ ] **Step 3: Implement**

`src/ContentAutomatorX.Web/Services/ITenantIdStore.cs`:

```csharp
namespace ContentAutomatorX.Web.Services;

/// <summary>Persistence seam for the last-used tenant id (browser storage in production).</summary>
public interface ITenantIdStore
{
    Task<Guid?> GetAsync();
    Task SetAsync(Guid id);
}
```

`src/ContentAutomatorX.Web/Services/TenantContext.cs`:

```csharp
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Web.Services;

/// <summary>
/// Scoped (per-circuit) single source of truth for the active tenant.
/// Restores the last-used tenant from the store; falls back to the first
/// active tenant (TenantService.ListAsync order = by name), else null.
/// </summary>
public class TenantContext(TenantService tenantSvc, ITenantIdStore store)
{
    public Tenant? Active { get; private set; }
    public IReadOnlyList<Tenant> ActiveTenants { get; private set; } = [];
    public bool Initialized { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (Initialized) return;
        Guid? stored;
        try { stored = await store.GetAsync(); }
        catch { stored = null; }   // unreadable browser storage = no stored id
        await ResolveAsync(stored);
        Initialized = true;
        Changed?.Invoke();
    }

    public async Task SwitchAsync(Guid tenantId)
    {
        var tenant = ActiveTenants.FirstOrDefault(t => t.Id == tenantId);
        if (tenant is null || tenant.Id == Active?.Id) return;
        Active = tenant;
        await PersistAsync(tenantId);
        Changed?.Invoke();
    }

    public async Task RefreshAsync()
    {
        await ResolveAsync(Active?.Id);
        Changed?.Invoke();
    }

    private async Task ResolveAsync(Guid? preferredId)
    {
        ActiveTenants = (await tenantSvc.ListAsync()).Where(t => t.IsActive).ToList();
        Active = ActiveTenants.FirstOrDefault(t => t.Id == preferredId) ?? ActiveTenants.FirstOrDefault();
        if (Active is not null) await PersistAsync(Active.Id);
    }

    private async Task PersistAsync(Guid id)
    {
        try { await store.SetAsync(id); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter TenantContextTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Run the full suite (no regressions)**

Run: `dotnet test`
Expected: 59 passed (23 unit + 36 integration), 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/ContentAutomatorX.Web/Services tests/ContentAutomatorX.IntegrationTests/TenantContextTests.cs
git commit -m "feat: TenantContext active-tenant state with store seam and tests"
```

---

### Task 2: Production store + DI wiring

**Files:**
- Create: `src/ContentAutomatorX.Web/Services/ProtectedLocalStorageTenantIdStore.cs`
- Modify: `src/ContentAutomatorX.Web/Program.cs` (after line 59, the `RunService` registration)
- Modify: `src/ContentAutomatorX.Web/Components/_Imports.razor`

**Interfaces:**
- Consumes: `ITenantIdStore`, `TenantContext` (Task 1); `ProtectedLocalStorage` from `Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage` (registered by the existing `AddInteractiveServerComponents()`).
- Produces: DI registrations so any component can `@inject TenantContext Ctx`; `@using ContentAutomatorX.Web.Services` available in all components.

- [ ] **Step 1: Implement the store**

`src/ContentAutomatorX.Web/Services/ProtectedLocalStorageTenantIdStore.cs`:

```csharp
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace ContentAutomatorX.Web.Services;

/// <summary>Persists the active tenant id in encrypted browser localStorage (survives refresh/new tabs).</summary>
public class ProtectedLocalStorageTenantIdStore(ProtectedLocalStorage storage) : ITenantIdStore
{
    private const string Key = "contentx-active-tenant";

    public async Task<Guid?> GetAsync()
    {
        // Data-protection key rotation or a tampered payload throws — treat as "no stored id".
        try
        {
            var result = await storage.GetAsync<Guid>(Key);
            return result.Success ? result.Value : null;
        }
        catch { return null; }
    }

    public async Task SetAsync(Guid id)
    {
        try { await storage.SetAsync(Key, id); } catch { /* best effort */ }
    }
}
```

- [ ] **Step 2: Register in `Program.cs`**

In `src/ContentAutomatorX.Web/Program.cs`, directly after `builder.Services.AddScoped<RunService>();` (line 59), add:

```csharp
builder.Services.AddScoped<ContentAutomatorX.Web.Services.ITenantIdStore,
    ContentAutomatorX.Web.Services.ProtectedLocalStorageTenantIdStore>();
builder.Services.AddScoped<ContentAutomatorX.Web.Services.TenantContext>();
```

- [ ] **Step 3: Add the @using**

In `src/ContentAutomatorX.Web/Components/_Imports.razor`, after the line `@using ContentAutomatorX.Web.Components.Layout`, add:

```razor
@using ContentAutomatorX.Web.Services
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors (NU1903 SQLitePCLRaw advisory warnings are pre-existing and OK).

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Web/Services/ProtectedLocalStorageTenantIdStore.cs src/ContentAutomatorX.Web/Program.cs src/ContentAutomatorX.Web/Components/_Imports.razor
git commit -m "feat: persist active tenant in ProtectedLocalStorage, DI wiring"
```

---

### Task 3: `TenantSlug` + quick-create dialog

**Files:**
- Create: `src/ContentAutomatorX.Web/Services/TenantSlug.cs`
- Create: `src/ContentAutomatorX.Web/Components/Shared/CreateTenantDialog.razor`
- Modify: `src/ContentAutomatorX.Web/Components/_Imports.razor`
- Test: `tests/ContentAutomatorX.IntegrationTests/TenantSlugTests.cs`

**Interfaces:**
- Consumes: `TenantContext.RefreshAsync()/SwitchAsync(Guid)` (Task 1), existing `TenantService.CreateAsync(Tenant)/ListAsync()`, MudBlazor dialog infrastructure (`IMudDialogInstance`, `DialogResult` — MudBlazor 9 API).
- Produces: `static string TenantSlug.Derive(string name)` (lowercase; ASCII letters/digits kept; space/`-`/`_` → single hyphen; everything else dropped; no leading/trailing hyphens). `CreateTenantDialog` component (namespace `ContentAutomatorX.Web.Components.Shared`), opened by Task 4 via `IDialogService.ShowAsync<CreateTenantDialog>("New tenant")` — on success it creates the tenant, switches to it, and closes itself; the caller does not need the dialog result.

- [ ] **Step 1: Write the failing tests**

`tests/ContentAutomatorX.IntegrationTests/TenantSlugTests.cs`:

```csharp
using ContentAutomatorX.Web.Services;

namespace ContentAutomatorX.IntegrationTests;

public class TenantSlugTests
{
    [Theory]
    [InlineData("Alpha Tenant", "alpha-tenant")]
    [InlineData("  My_Cool Channel  ", "my-cool-channel")]
    [InlineData("Über Cool!!", "ber-cool")]      // non-ASCII and punctuation dropped
    [InlineData("A -- B", "a-b")]                // separator runs collapse to one hyphen
    [InlineData("-lead and trail-", "lead-and-trail")]
    [InlineData("!!!", "")]                      // nothing usable → empty
    [InlineData("", "")]
    public void Derive_produces_expected_slug(string name, string expected) =>
        Assert.Equal(expected, TenantSlug.Derive(name));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter TenantSlugTests`
Expected: FAIL — compile error, `TenantSlug` does not exist.

- [ ] **Step 3: Implement `TenantSlug`**

`src/ContentAutomatorX.Web/Services/TenantSlug.cs`:

```csharp
using System.Text;

namespace ContentAutomatorX.Web.Services;

public static class TenantSlug
{
    /// <summary>Derives a slug: lowercase, ASCII letters/digits kept, space/-/_ become one hyphen, rest dropped.</summary>
    public static string Derive(string name)
    {
        var sb = new StringBuilder(name.Length);
        var lastWasHyphen = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasHyphen = false;
            }
            else if (ch is ' ' or '-' or '_' && sb.Length > 0 && !lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }
        return sb.ToString().TrimEnd('-');
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter TenantSlugTests`
Expected: PASS (7 cases).

- [ ] **Step 5: Create the dialog**

`src/ContentAutomatorX.Web/Components/Shared/CreateTenantDialog.razor`:

```razor
@using Microsoft.EntityFrameworkCore
@inject TenantService TenantSvc
@inject TenantContext Ctx
@inject ISnackbar Snackbar

<MudDialog>
    <DialogContent>
        <MudTextField T="string" Value="_name" ValueChanged="OnNameChanged" Immediate="true"
                      Label="Name" AutoFocus="true" />
        <MudTextField T="string" Value="_slug" ValueChanged="OnSlugEdited" Immediate="true"
                      Label="Slug (short id, used in draft front-matter)" />
        <MudText Typo="Typo.caption" Class="mt-2">
            Voice profile and output folder can be set afterwards under Manage tenants.
        </MudText>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Color="Color.Primary" Variant="Variant.Filled" Disabled="_busy"
                   OnClick="CreateAsync">Create &amp; switch</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    private string _name = "", _slug = "";
    private bool _slugEdited;
    private bool _busy;

    private void OnNameChanged(string value)
    {
        _name = value;
        if (!_slugEdited) _slug = TenantSlug.Derive(value);
    }

    private void OnSlugEdited(string value)
    {
        _slug = value;
        _slugEdited = true;   // user took over — stop auto-deriving
    }

    private void Cancel() => MudDialog.Cancel();

    private async Task CreateAsync()
    {
        var name = _name.Trim();
        var slug = _slug.Trim();
        if (name.Length == 0 || slug.Length == 0)
        {
            Snackbar.Add("Name and slug are required", Severity.Warning);
            return;
        }
        var existing = await TenantSvc.ListAsync();
        if (existing.Any(t => t.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)))
        {
            Snackbar.Add($"Slug '{slug}' is already in use", Severity.Error);
            return;
        }

        _busy = true;
        try
        {
            var tenant = await TenantSvc.CreateAsync(new Tenant { Name = name, Slug = slug });
            await Ctx.RefreshAsync();
            await Ctx.SwitchAsync(tenant.Id);
            Snackbar.Add($"Switched to {tenant.Name}", Severity.Success);
            MudDialog.Close(DialogResult.Ok(tenant.Id));
        }
        catch (DbUpdateException)   // unique-index backstop (concurrent create)
        {
            Snackbar.Add("A tenant with this slug already exists", Severity.Error);
        }
        finally { _busy = false; }
    }
}
```

- [ ] **Step 6: Add the @using**

In `src/ContentAutomatorX.Web/Components/_Imports.razor`, after `@using ContentAutomatorX.Web.Services`, add:

```razor
@using ContentAutomatorX.Web.Components.Shared
```

- [ ] **Step 7: Build and run full tests**

Run: `dotnet build` then `dotnet test`
Expected: Build succeeded; 66 tests passed (23 unit + 43 integration), 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/ContentAutomatorX.Web/Services/TenantSlug.cs "src/ContentAutomatorX.Web/Components/Shared" src/ContentAutomatorX.Web/Components/_Imports.razor tests/ContentAutomatorX.IntegrationTests/TenantSlugTests.cs
git commit -m "feat: tenant slug derivation and quick-create dialog"
```

---

### Task 4: App-bar switcher, drawer cleanup, no-tenant hint

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Layout/TenantSwitcher.razor`
- Create: `src/ContentAutomatorX.Web/Components/Shared/NoTenantHint.razor`
- Modify: `src/ContentAutomatorX.Web/Components/Layout/MainLayout.razor`

**Interfaces:**
- Consumes: `TenantContext` (`Active`, `ActiveTenants`, `Initialized`, `Changed`, `InitializeAsync`, `SwitchAsync`), `CreateTenantDialog` (Task 3).
- Produces: `<TenantSwitcher />` (self-contained app-bar menu) and `<NoTenantHint />` (empty-state panel used by Tasks 5-8). `MainLayout` calls `TenantContext.InitializeAsync()` on first after-render — pages rely on this being the ONLY initializer.

- [ ] **Step 1: Create `NoTenantHint`**

`src/ContentAutomatorX.Web/Components/Shared/NoTenantHint.razor`:

```razor
<MudPaper Class="pa-8 mt-8 mx-auto" Style="max-width: 480px; text-align: center;">
    <MudIcon Icon="@Icons.Material.Filled.PersonOff" Size="Size.Large" Class="mb-2" />
    <MudText Typo="Typo.h6">No tenant yet</MudText>
    <MudText Typo="Typo.body2">Create one from the menu in the top-right corner.</MudText>
</MudPaper>
```

- [ ] **Step 2: Create `TenantSwitcher`**

`src/ContentAutomatorX.Web/Components/Layout/TenantSwitcher.razor`:

```razor
@implements IDisposable
@inject TenantContext Ctx
@inject IDialogService DialogService
@inject NavigationManager Nav

@if (!Ctx.Initialized)
{
    @* state not loaded yet — render nothing *@
}
else if (Ctx.Active is null)
{
    <MudButton Color="Color.Inherit" StartIcon="@Icons.Material.Filled.PersonAdd"
               OnClick="OpenCreateDialog">No tenant — create one</MudButton>
}
else
{
    <MudMenu AnchorOrigin="Origin.BottomRight" TransformOrigin="Origin.TopRight">
        <ActivatorContent>
            <MudButton Color="Color.Inherit" EndIcon="@Icons.Material.Filled.ArrowDropDown">
                <MudAvatar Size="Size.Small" Color="Color.Secondary" Class="mr-2">@Initial(Ctx.Active.Name)</MudAvatar>
                @Ctx.Active.Name
            </MudButton>
        </ActivatorContent>
        <ChildContent>
            @foreach (var tenant in Ctx.ActiveTenants)
            {
                <MudMenuItem Icon="@(tenant.Id == Ctx.Active.Id ? Icons.Material.Filled.Check : null)"
                             OnClick="@(() => Ctx.SwitchAsync(tenant.Id))">@tenant.Name</MudMenuItem>
            }
            <MudDivider />
            <MudMenuItem Icon="@Icons.Material.Filled.Add" OnClick="OpenCreateDialog">New tenant</MudMenuItem>
            <MudMenuItem Icon="@Icons.Material.Filled.Settings"
                         OnClick="@(() => Nav.NavigateTo("/tenants"))">Manage tenants</MudMenuItem>
        </ChildContent>
    </MudMenu>
}

@code {
    protected override void OnInitialized() => Ctx.Changed += OnChanged;

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    private static string Initial(string name) =>
        string.IsNullOrEmpty(name) ? "?" : name[..1].ToUpperInvariant();

    private Task OpenCreateDialog() => DialogService.ShowAsync<CreateTenantDialog>("New tenant");

    public void Dispose() => Ctx.Changed -= OnChanged;
}
```

(Design-spec note: the spec sketches avatar initials on every menu row; the check icon on the current row + plain names is the sanctioned simplification — MudMenuItem's icon slot is used for the checkmark.)

- [ ] **Step 3: Rewrite `MainLayout.razor`**

Replace the entire content of `src/ContentAutomatorX.Web/Components/Layout/MainLayout.razor` with:

```razor
@inherits LayoutComponentBase
@inject TenantContext Ctx

<MudThemeProvider />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <MudAppBar Elevation="1">
        <MudIconButton Icon="@Icons.Material.Filled.Menu" Color="Color.Inherit" Edge="Edge.Start"
                       OnClick="@(() => _drawerOpen = !_drawerOpen)" />
        <MudText Typo="Typo.h6">ContentAutomatorX</MudText>
        <MudSpacer />
        <TenantSwitcher />
    </MudAppBar>
    <MudDrawer @bind-Open="_drawerOpen" Elevation="2">
        <MudNavMenu>
            <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">Dashboard</MudNavLink>
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // ProtectedLocalStorage needs JS interop — unavailable before first render.
        if (firstRender) await Ctx.InitializeAsync();
    }
}
```

Note the "Tenants" nav link is intentionally gone — `/tenants` stays routable via the switcher's "Manage tenants".

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Web/Components/Layout src/ContentAutomatorX.Web/Components/Shared/NoTenantHint.razor
git commit -m "feat: app-bar tenant switcher, drawer cleanup, no-tenant hint"
```

---

### Task 5: Dashboard scoped to the active tenant

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Home.razor` (full rewrite)

**Interfaces:**
- Consumes: `TenantContext`, `NoTenantHint`, existing `ContentService.ListAsync(Guid, ContentItemStatus?)`, `DraftService.ListAsync(Guid)`, `RunService.ListAsync(Guid, int)`.
- Produces: nothing consumed by later tasks. Establishes the page pattern Tasks 6-8 repeat: subscribe `Ctx.Changed` in `OnInitializedAsync`, handler = `_ = InvokeAsync(async () => { <reset>; await ReloadAsync(); StateHasChanged(); })`, guard `if (!Ctx.Initialized || Ctx.Active is null)` before querying, unsubscribe in `Dispose`.

- [ ] **Step 1: Rewrite the page**

Replace the entire content of `src/ContentAutomatorX.Web/Components/Pages/Home.razor` with:

```razor
@page "/"
@implements IDisposable
@inject TenantContext Ctx
@inject ContentService ContentSvc
@inject DraftService DraftSvc
@inject RunService RunSvc

<MudText Typo="Typo.h4" Class="mb-4">Dashboard</MudText>

@if (!Ctx.Initialized)
{
    <MudProgressLinear Indeterminate="true" />
}
else if (Ctx.Active is null)
{
    <NoTenantHint />
}
else
{
    <MudGrid>
        <MudItem xs="12" sm="4">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.subtitle2">New items</MudText>
                <MudText Typo="Typo.h4">@_newItems</MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12" sm="4">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.subtitle2">Drafts</MudText>
                <MudText Typo="Typo.h4">@_draftCount</MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12" sm="4">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.subtitle2">Failed/partial (last 10 runs)</MudText>
                <MudText Typo="Typo.h4" Color="@(_recentErrors > 0 ? Color.Error : Color.Default)">@_recentErrors</MudText>
            </MudPaper>
        </MudItem>
        <MudItem xs="12">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.subtitle2">Last run</MudText>
                @if (_lastRun is null)
                {
                    <MudText>none yet</MudText>
                }
                else
                {
                    <MudText Color="@StatusColor(_lastRun.Status)">
                        @_lastRun.Kind @_lastRun.Status (@_lastRun.StartedAt.ToLocalTime().ToString("g"))
                    </MudText>
                }
            </MudPaper>
        </MudItem>
    </MudGrid>
}

@code {
    private int _newItems, _draftCount, _recentErrors;
    private PipelineRun? _lastRun;

    protected override async Task OnInitializedAsync()
    {
        Ctx.Changed += OnTenantChanged;
        await ReloadAsync();
    }

    private void OnTenantChanged() => _ = InvokeAsync(async () =>
    {
        await ReloadAsync();
        StateHasChanged();
    });

    private async Task ReloadAsync()
    {
        if (!Ctx.Initialized || Ctx.Active is null) return;
        var id = Ctx.Active.Id;
        _newItems = (await ContentSvc.ListAsync(id, ContentItemStatus.New)).Count;
        _draftCount = (await DraftSvc.ListAsync(id)).Count;
        var runs = await RunSvc.ListAsync(id, 10);
        _lastRun = runs.FirstOrDefault();
        _recentErrors = runs.Count(r => r.Status is RunStatus.Failed or RunStatus.Partial);
    }

    private static Color StatusColor(RunStatus status) => status switch
    {
        RunStatus.Succeeded => Color.Success,
        RunStatus.Partial => Color.Warning,
        RunStatus.Failed => Color.Error,
        _ => Color.Info
    };

    public void Dispose() => Ctx.Changed -= OnTenantChanged;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ContentAutomatorX.Web/Components/Pages/Home.razor
git commit -m "feat: dashboard scoped to active tenant"
```

---

### Task 6: Drafts and Runs pages use the global tenant

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Drafts.razor` (full rewrite)
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Runs.razor` (full rewrite)

**Interfaces:**
- Consumes: `TenantContext`, `NoTenantHint`; existing `DraftService.ListAsync(Guid)/RetryDeliveryAsync(Guid)`, `RunService.ListAsync(Guid, int = 50)`.
- Produces: nothing consumed later.

- [ ] **Step 1: Rewrite `Drafts.razor`**

Replace the entire content of `src/ContentAutomatorX.Web/Components/Pages/Drafts.razor` with:

```razor
@page "/drafts"
@implements IDisposable
@inject TenantContext Ctx
@inject DraftService DraftSvc
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Drafts</MudText>

@if (!Ctx.Initialized)
{
    <MudProgressLinear Indeterminate="true" />
}
else if (Ctx.Active is null)
{
    <NoTenantHint />
}
else
{
    <MudExpansionPanels MultiExpansion="false">
        @foreach (var draft in _drafts)
        {
            <MudExpansionPanel @key="draft.Id">
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
    private List<Draft> _drafts = [];

    protected override async Task OnInitializedAsync()
    {
        Ctx.Changed += OnTenantChanged;
        await ReloadAsync();
    }

    private void OnTenantChanged() => _ = InvokeAsync(async () =>
    {
        await ReloadAsync();
        StateHasChanged();
    });

    private async Task ReloadAsync()
    {
        _drafts = Ctx.Initialized && Ctx.Active is not null ? await DraftSvc.ListAsync(Ctx.Active.Id) : [];
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
        await ReloadAsync();
    }

    public void Dispose() => Ctx.Changed -= OnTenantChanged;
}
```

- [ ] **Step 2: Rewrite `Runs.razor`**

Replace the entire content of `src/ContentAutomatorX.Web/Components/Pages/Runs.razor` with:

```razor
@page "/runs"
@implements IDisposable
@inject TenantContext Ctx
@inject RunService RunSvc

<MudText Typo="Typo.h4" Class="mb-4">Pipeline runs</MudText>

@if (!Ctx.Initialized)
{
    <MudProgressLinear Indeterminate="true" />
}
else if (Ctx.Active is null)
{
    <NoTenantHint />
}
else
{
    <MudExpansionPanels>
        @foreach (var run in _runs)
        {
            <MudExpansionPanel @key="run.Id">
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
    private List<PipelineRun> _runs = [];

    protected override async Task OnInitializedAsync()
    {
        Ctx.Changed += OnTenantChanged;
        await ReloadAsync();
    }

    private void OnTenantChanged() => _ = InvokeAsync(async () =>
    {
        await ReloadAsync();
        StateHasChanged();
    });

    private async Task ReloadAsync()
    {
        _runs = Ctx.Initialized && Ctx.Active is not null ? await RunSvc.ListAsync(Ctx.Active.Id) : [];
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

    public void Dispose() => Ctx.Changed -= OnTenantChanged;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ContentAutomatorX.Web/Components/Pages/Drafts.razor src/ContentAutomatorX.Web/Components/Pages/Runs.razor
git commit -m "feat: drafts and runs pages use global tenant"
```

---

### Task 7: Sources page uses the global tenant

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Sources.razor` (full rewrite)

**Interfaces:**
- Consumes: `TenantContext`, `NoTenantHint`; existing `SourceService`, `IngestionPipeline` (via scope factory).
- Produces: nothing consumed later.

- [ ] **Step 1: Rewrite the page**

Replace the entire content of `src/ContentAutomatorX.Web/Components/Pages/Sources.razor` with:

```razor
@page "/sources"
@implements IDisposable
@inject TenantContext Ctx
@inject SourceService SourceSvc
@inject IServiceScopeFactory ScopeFactory
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Sources</MudText>

@if (!Ctx.Initialized)
{
    <MudProgressLinear Indeterminate="true" />
}
else if (Ctx.Active is null)
{
    <NoTenantHint />
}
else
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
    private List<Source> _sources = [];
    private Source? _editing;
    private string _type = SourceTypes.Reddit, _displayName = "", _subreddit = "", _sort = "hot",
        _timeframe = "week", _feedUrl = "", _cron = "";
    private bool _enabled = true;

    protected override async Task OnInitializedAsync()
    {
        Ctx.Changed += OnTenantChanged;
        await ReloadAsync();
    }

    private void OnTenantChanged() => _ = InvokeAsync(async () =>
    {
        Reset();
        await ReloadAsync();
        StateHasChanged();
    });

    private async Task ReloadAsync()
    {
        _sources = Ctx.Initialized && Ctx.Active is not null ? await SourceSvc.ListAsync(Ctx.Active.Id) : [];
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
        if (Ctx.Active is null || string.IsNullOrWhiteSpace(_displayName)) return;
        if (_editing is null)
        {
            await SourceSvc.CreateAsync(new Source
            {
                TenantId = Ctx.Active.Id, Type = _type, DisplayName = _displayName,
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
        await ReloadAsync();
        Snackbar.Add("Saved", Severity.Success);
    }

    private async Task Delete(Source s)
    {
        await SourceSvc.DeleteAsync(s.Id);
        await ReloadAsync();
    }

    private async Task FetchNow(Source s)
    {
        Snackbar.Add($"Fetching {s.DisplayName}...", Severity.Info);
        using var scope = ScopeFactory.CreateScope();
        var ingestion = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();
        var run = await ingestion.RunAsync(s.TenantId, s.Id);
        Snackbar.Add($"Fetch {run.Status}: {run.LogJson}",
            run.Status == RunStatus.Succeeded ? Severity.Success : Severity.Error);
        await ReloadAsync();
    }

    public void Dispose() => Ctx.Changed -= OnTenantChanged;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/ContentAutomatorX.Web/Components/Pages/Sources.razor
git commit -m "feat: sources page uses global tenant"
```

---

### Task 8: Recipes and Content pages use the global tenant

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor` (full rewrite)
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Content.razor` (full rewrite)

**Interfaces:**
- Consumes: `TenantContext`, `NoTenantHint`; existing `RecipeService`, `SourceService`, `ContentService`, `GenerationPipeline` (via scope factory).
- Produces: nothing consumed later. Tenant-switch resets carry over the behavior added in commit `84b46df` (recipe selection reset on tenant switch; item selection cleared on reload).

- [ ] **Step 1: Rewrite `Recipes.razor`**

Replace the entire content of `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor` with:

```razor
@page "/recipes"
@implements IDisposable
@inject TenantContext Ctx
@inject SourceService SourceSvc
@inject RecipeService RecipeSvc
@inject IServiceScopeFactory ScopeFactory
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Recipes</MudText>

@if (!Ctx.Initialized)
{
    <MudProgressLinear Indeterminate="true" />
}
else if (Ctx.Active is null)
{
    <NoTenantHint />
}
else
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
    private List<Source> _sources = [];
    private List<Recipe> _recipes = [];
    private Recipe? _editing;
    private PromptTemplate? _template;
    private bool _running;

    private string _name = "", _kind = DraftKinds.Newsletter, _includeKeywords = "", _excludeKeywords = "",
        _tone = "", _length = "", _language = "", _subfolder = "", _filenamePattern = "", _targetPlatform = "", _cron = "";
    private int? _windowDays = 7, _minScore;
    private int _maxItems = 10;
    private bool _enabled = true;
    private IReadOnlyCollection<Guid> _selectedSourceIds = [];

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    protected override async Task OnInitializedAsync()
    {
        Ctx.Changed += OnTenantChanged;
        await ReloadAsync();
    }

    private void OnTenantChanged() => _ = InvokeAsync(async () =>
    {
        Reset();
        await ReloadAsync();
        StateHasChanged();
    });

    private async Task ReloadAsync()
    {
        if (!Ctx.Initialized || Ctx.Active is null)
        {
            _sources = [];
            _recipes = [];
            return;
        }
        _sources = await SourceSvc.ListAsync(Ctx.Active.Id);
        _recipes = await RecipeSvc.ListAsync(Ctx.Active.Id);
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
        if (Ctx.Active is null || string.IsNullOrWhiteSpace(_name)) return;
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
                TenantId = Ctx.Active.Id, Name = _name, Kind = _kind, SourceIdsJson = sourceIds,
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
        await ReloadAsync();
        Snackbar.Add("Saved", Severity.Success);
    }

    private async Task Delete(Recipe r)
    {
        await RecipeSvc.DeleteAsync(r.Id);
        await ReloadAsync();
    }

    private async Task RunNow(Recipe r)
    {
        _running = true;
        Snackbar.Add($"Running recipe '{r.Name}' — this calls the LLM and can take a few minutes...", Severity.Info);
        try
        {
            using var scope = ScopeFactory.CreateScope();
            var generation = scope.ServiceProvider.GetRequiredService<GenerationPipeline>();
            var (run, draft) = await generation.RunAsync(r.Id);
            if (run.Status == RunStatus.Succeeded)
                Snackbar.Add($"Draft delivered: {draft!.FilePath}", Severity.Success);
            else
                Snackbar.Add($"Run {run.Status}: {run.LogJson}", Severity.Error);
        }
        finally
        {
            _running = false;
            await ReloadAsync();
        }
    }

    public void Dispose() => Ctx.Changed -= OnTenantChanged;
}
```

- [ ] **Step 2: Rewrite `Content.razor`**

Replace the entire content of `src/ContentAutomatorX.Web/Components/Pages/Content.razor` with:

```razor
@page "/content"
@implements IDisposable
@inject TenantContext Ctx
@inject ContentService ContentSvc
@inject RecipeService RecipeSvc
@inject IServiceScopeFactory ScopeFactory
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">Content items</MudText>

@if (!Ctx.Initialized)
{
    <MudProgressLinear Indeterminate="true" />
}
else if (Ctx.Active is null)
{
    <NoTenantHint />
}
else
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
    private List<ContentItem> _items = [];
    private List<Recipe> _recipes = [];
    private HashSet<ContentItem> _selectedItems = [];
    private Guid? _recipeId;
    private string _statusFilter = "all";
    private string _extraInstructions = "";
    private bool _running;

    protected override async Task OnInitializedAsync()
    {
        Ctx.Changed += OnTenantChanged;
        await ReloadAllAsync();
    }

    private void OnTenantChanged() => _ = InvokeAsync(async () =>
    {
        _recipeId = null;
        _extraInstructions = "";
        await ReloadAllAsync();
        StateHasChanged();
    });

    private async Task ReloadAllAsync()
    {
        _recipes = Ctx.Initialized && Ctx.Active is not null ? await RecipeSvc.ListAsync(Ctx.Active.Id) : [];
        await Reload();
    }

    private async Task OnFilterChanged(string filter)
    {
        _statusFilter = filter;
        await Reload();
    }

    private async Task Reload()
    {
        _selectedItems = [];
        if (!Ctx.Initialized || Ctx.Active is null) { _items = []; return; }
        ContentItemStatus? status = _statusFilter == "all" ? null : Enum.Parse<ContentItemStatus>(_statusFilter);
        _items = await ContentSvc.ListAsync(Ctx.Active.Id, status);
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
            using var scope = ScopeFactory.CreateScope();
            var generation = scope.ServiceProvider.GetRequiredService<GenerationPipeline>();
            var (run, draft) = await generation.RunAsync(_recipeId!.Value, ids,
                string.IsNullOrWhiteSpace(_extraInstructions) ? null : _extraInstructions);
            if (run.Status == RunStatus.Succeeded)
                Snackbar.Add($"Draft delivered: {draft!.FilePath}", Severity.Success);
            else
                Snackbar.Add($"Run {run.Status}: {run.LogJson}", Severity.Error);
        }
        finally
        {
            _running = false;
            await Reload();
        }
    }

    public void Dispose() => Ctx.Changed -= OnTenantChanged;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ContentAutomatorX.Web/Components/Pages/Recipes.razor src/ContentAutomatorX.Web/Components/Pages/Content.razor
git commit -m "feat: recipes and content pages use global tenant"
```

---

### Task 9: Tenants page refreshes the switcher; README update

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Tenants.razor`
- Modify: `README.md`

**Interfaces:**
- Consumes: `TenantContext.RefreshAsync()`.
- Produces: switcher stays in sync after create/rename/deactivate/delete on the management page (including fallback when the active tenant is removed — handled inside `RefreshAsync`).

- [ ] **Step 1: Wire `RefreshAsync` into `Tenants.razor`**

Three edits to `src/ContentAutomatorX.Web/Components/Pages/Tenants.razor`:

(a) Add the inject after the existing `@inject` lines (after `@inject ISnackbar Snackbar`):

```razor
@inject TenantContext Ctx
```

(b) In `Save()`, replace the tail

```csharp
        Reset();
        _tenants = await TenantSvc.ListAsync();
        Snackbar.Add("Saved", Severity.Success);
```

with:

```csharp
        Reset();
        _tenants = await TenantSvc.ListAsync();
        await Ctx.RefreshAsync();
        Snackbar.Add("Saved", Severity.Success);
```

(c) In `Delete(Tenant t)`, replace

```csharp
        await TenantSvc.DeleteAsync(t.Id);
        _tenants = await TenantSvc.ListAsync();
```

with:

```csharp
        await TenantSvc.DeleteAsync(t.Id);
        _tenants = await TenantSvc.ListAsync();
        await Ctx.RefreshAsync();
```

- [ ] **Step 2: Update the README quick start**

In `README.md`, replace:

```markdown
1. **Tenants** → create a tenant; set its voice profile and output folder (Verify folder).
```

with:

```markdown
1. **Tenant menu (top-right)** → create a tenant, then **Manage tenants** to set its voice profile and output folder (Verify folder). The menu also switches the whole app between tenants; the choice persists per browser.
```

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build` then `dotnet test`
Expected: Build succeeded; 66 tests passed (23 unit + 43 integration), 0 failed.

- [ ] **Step 4: Commit**

```bash
git add src/ContentAutomatorX.Web/Components/Pages/Tenants.razor README.md
git commit -m "feat: tenants page refreshes switcher; README quick-start update"
```

---

### Task 10: End-to-end verification

**Files:** none (verification only).

**Interfaces:**
- Consumes: everything above.
- Produces: confirmation the feature works in the real app.

- [ ] **Step 1: Full test suite**

Run: `dotnet test`
Expected: 66 passed (23 unit + 43 integration), 0 failed.

- [ ] **Step 2: Start the app**

Run: `dotnet run --project src/ContentAutomatorX.Web` (background) and open `http://localhost:5090`.

- [ ] **Step 3: Manual walkthrough**

Verify each of these; note any failure and fix before proceeding:

1. **Switcher visible** top-right; drawer has no "Tenants" link. If the DB already has tenants, the alphabetically-first (or previously stored) one shows as active.
2. **Fresh-state path** (only if DB has no tenants): every page shows the "No tenant yet" hint; the app bar shows "No tenant — create one".
3. **Quick-create**: menu → New tenant → type a name, watch the slug derive; edit the slug, confirm it stops auto-deriving. Create → app switches to the new tenant, snackbar confirms.
4. **Duplicate slug**: open the dialog again, enter the same slug → error snackbar, dialog stays open. Cancel.
5. **Switching**: with ≥2 tenants, switch via the menu on each page (Dashboard, Sources, Recipes, Content, Drafts, Runs) — data changes to the selected tenant, edit forms/selections reset, check icon moves.
6. **Persistence**: switch to a non-first tenant, hard-refresh (F5) → same tenant is active. Open a second tab → same tenant.
7. **Manage tenants**: menu → Manage tenants lands on `/tenants`; rename the active tenant → app-bar name updates after save. Deactivate the active tenant → switcher falls back to another tenant and the deactivated one is gone from the menu. Reactivate it.
8. **Console/log check**: no circuit errors in the browser console or `src/ContentAutomatorX.Web/logs/`.

- [ ] **Step 4: Stop the app**

Stop the `dotnet run` process (it locks build outputs otherwise).

- [ ] **Step 5: Mark plan complete**

No commit needed for this task unless fixes were required (commit those as `fix:` with a description of what the walkthrough caught).
