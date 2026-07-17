# Tenant Switcher — Design

**Date:** 2026-07-17
**Status:** Approved (brainstormed with user)
**Scope:** Web layer only — no domain, application-service, or DB changes; no migration.

## Goal

Replace the per-page tenant dropdowns with a single global "active tenant",
switched from an account-style menu in the upper-right corner of the app bar —
like being logged into one account and switching between them.

## Current state (what changes)

- `MainLayout.razor`: nav drawer has a "Tenants" link; app bar has only the
  hamburger + title.
- Five pages (Sources, Recipes, Content, Drafts, Runs) each carry their own
  independent `MudSelect` tenant dropdown; switching on one page does not
  affect the others.
- Dashboard (`Home.razor`) renders a card grid over **all** tenants.
- Tenants page (`/tenants`) is full CRUD (name, slug, voice profile, output
  folder, verify-folder probe, active toggle).

## Decisions (from brainstorming)

1. **Single global tenant** — the switcher is the single source of truth; all
   per-page dropdowns are removed.
2. **Dashboard scopes to the active tenant** (no more all-tenant grid).
3. **Selection persists across refresh/tabs** via browser storage.
4. **Menu contents:** tenant list (click = switch) + divider + "+ New tenant"
   + "Manage tenants".
5. **Inactive tenants are hidden** from the switcher; reactivation happens on
   the Tenants management page.
6. **Quick-create dialog:** Name + Slug only (slug auto-derived, editable);
   creates and switches immediately. Full details filled in later on
   `/tenants`.

## Design

### 1. `TenantContext` — scoped state service (one per circuit)

New file: `src/ContentAutomatorX.Web/Services/TenantContext.cs`, registered
`AddScoped` in `Program.cs`.

```csharp
public class TenantContext(TenantService tenantSvc, ITenantIdStore store)
{
    public Tenant? Active { get; private set; }
    public IReadOnlyList<Tenant> ActiveTenants { get; private set; } = [];
    public bool Initialized { get; private set; }
    public event Action? Changed;

    public Task InitializeAsync();          // idempotent; called from MainLayout
    public Task SwitchAsync(Guid tenantId); // set Active, persist, raise Changed
    public Task RefreshAsync();             // re-list tenants; revalidate Active
}
```

Semantics:

- `InitializeAsync` (first render, interactive): loads all `IsActive` tenants
  via the existing `TenantService`, reads the last-used tenant id from the
  store, then resolves `Active`:
  - stored id present **and** still in the active list → restore it;
  - stored id missing/stale → first active tenant (list order =
    `TenantService.ListAsync()` order);
  - no active tenants → `Active = null`.
- `SwitchAsync`: sets `Active`, persists the id, raises `Changed`. Unknown or
  inactive id → no-op (defensive; UI only offers valid ids).
- `RefreshAsync`: re-lists active tenants (called after create/edit/delete on
  the Tenants page and after quick-create). If `Active` is no longer in the
  list (deleted or deactivated), falls back exactly like `InitializeAsync`
  (first active, else `null`) and raises `Changed`. Always raises `Changed`
  so the switcher re-renders renamed tenants.

`ITenantIdStore` is a two-method seam so the restore logic is testable
without a browser:

```csharp
public interface ITenantIdStore
{
    Task<Guid?> GetAsync();
    Task SetAsync(Guid id);
}
```

Production implementation `ProtectedLocalStorageTenantIdStore` wraps
`ProtectedLocalStorage` (already available via
`AddInteractiveServerComponents()`; encrypted, per-browser, survives refresh
and new tabs). Storage read failures (e.g. data-protection key rotation)
are swallowed and treated as "no stored id". Both `TenantContext` and
`ITenantIdStore → ProtectedLocalStorageTenantIdStore` are registered
`AddScoped` in `Program.cs`.

### 2. App bar switcher (MainLayout)

`MainLayout.razor`:

- `MudAppBar` gains `<MudSpacer />` + a right-aligned `MudMenu`.
  - **Closed state:** `MudAvatar` with the tenant's first letter + tenant
    name + dropdown chevron. When `Active is null`: a muted
    "No tenant — create one" button that opens the quick-create dialog
    directly.
  - **Menu items:** one per active tenant (avatar initial + name, check icon
    on the current one; click → `SwitchAsync`), divider, "+ New tenant"
    (opens dialog), "Manage tenants" (navigates to `/tenants`).
- Nav drawer: **remove the "Tenants" link**; keep Dashboard, Sources,
  Recipes, Content, Drafts, Runs.
- `MainLayout` calls `TenantContext.InitializeAsync()` in
  `OnAfterRenderAsync(firstRender)` (ProtectedLocalStorage needs JS interop,
  which is unavailable during prerender) and subscribes to `Changed` for
  re-render; unsubscribes on dispose.

### 3. Quick-create dialog

New file: `Components/Shared/CreateTenantDialog.razor` (`MudDialog`):

- Fields: **Name**, **Slug**. Slug auto-derives from Name as the user types
  (lowercase, spaces→hyphens, strip non-alphanumerics) but stops auto-updating
  once the user edits the slug manually.
- Validation: both required (snackbar warning, same as Tenants page). Slug
  uniqueness violations from the DB unique index surface as an error snackbar;
  dialog stays open.
- On success: `TenantService.CreateAsync` → `TenantContext.RefreshAsync()` →
  `SwitchAsync(newId)` → close. Created tenant gets default
  `VoiceProfile = ""`, `OutputFolderPath = ""`, `IsActive = true` — the rest
  is filled in later on `/tenants`.

### 4. Page changes

All pages inject `TenantContext`, subscribe to `Changed` (reload data +
`StateHasChanged`), and unsubscribe in `Dispose`
(`@implements IDisposable`).

- **Sources, Recipes, Content, Drafts, Runs:** remove the top-of-page tenant
  `MudSelect` and local `_tenants`/`_tenantId` state; read
  `TenantContext.Active.Id` instead. On `Changed`: reload the page's data for
  the new tenant and reset in-progress edit/selection state — the same reset
  each page already performs on its dropdown's tenant-change today (including
  the Recipes page's recipe-selection reset and Content page's
  item-selection clear).
- **Dashboard (`Home.razor`):** single-tenant view — the active tenant's
  stats (new items, drafts, last run, recent errors) as full-width content
  instead of the per-tenant card grid.
- **Empty state (all pages):** when `Active is null`, render a centered
  hint: *"No tenant yet — create one from the menu in the top-right
  corner."*
- **Tenants page (`/tenants`):** unchanged CRUD, now reached via "Manage
  tenants". After any create/update/delete it calls
  `TenantContext.RefreshAsync()` so the switcher reflects renames,
  deactivations, and deletions immediately (including fallback when the
  active tenant is removed).

Timing note: pages render before `InitializeAsync` completes on a fresh
circuit. Pages treat "not initialized yet" like loading (render nothing or a
progress bar until the first `Changed`/initialized signal) rather than
flashing the no-tenant hint.

### 5. Testing

- **Integration tests** (existing `TestDb` + in-memory fake `ITenantIdStore`)
  for `TenantContext`:
  - stored id valid → restored;
  - stored id stale/absent → first active tenant;
  - no active tenants → `Active = null`;
  - inactive tenants excluded from `ActiveTenants`;
  - `RefreshAsync` after deleting/deactivating the active tenant → fallback
    + `Changed` raised;
  - `SwitchAsync` persists the id to the store.
- **UI wiring** (switcher menu, dialog, page reactions) verified by running
  the app — consistent with the project's existing approach of not
  unit-testing Blazor components.

## Out of scope

- Auth/real login (Phase 1 remains no-auth, localhost).
- Any change to MCP tools or application services (they stay explicitly
  tenant-parameterized).
- Cross-tenant "all accounts" overview (dropped with the dashboard change;
  can return later as a dedicated page if missed).
