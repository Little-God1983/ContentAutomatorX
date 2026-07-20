# ContentAutomatorX — LLM Model & Effort Selector Design

**Date:** 2026-07-20
**Status:** Approved 2026-07-20 (revised same day — scope changed to per-tenant)
**Depends on:** Phase 2b (issue composer, shipped 2026-07-19)
**Partially realizes:** `docs/mockups/10-platforms-ai-settings.md` (AI Studio)

## 1. Goal

Today every AI action in the app runs on whatever model the Claude CLI
defaults to. The only lever is `Claude:Model` in `appsettings.json`, which is
bound once at startup into a singleton and therefore cannot be changed without
editing a file and restarting the app. There is no way to influence reasoning
depth at all, and no way to differ per tenant.

Give the operator one place in the UI to choose **which model** and **how hard
it thinks**, **per tenant**, applying to every ✨ action for that tenant,
without building the full AI Studio provider/job matrix.

Success test: with tenant AIVisions active, open AI Studio → set Model =
Sonnet, Effort = low → press Generate ✨ in the issue composer → the CLI is
invoked with `--model sonnet --effort low`, with no app restart. Switch to
another tenant and its own (unset) settings apply instead.

## 2. Decisions (brainstormed 2026-07-20)

| # | Question | Decision |
|---|---------|----------|
| 1 | Scope | **Per tenant.** Each tenant owns its Model + Effort, like it owns its Platforms, Recipes and Sources. No global row and no cross-tenant default. |
| 2 | Where it's edited | **AI Studio page**, scoped to the active tenant, above the existing "coming soon" mockup content. |
| 3 | Default values | **"Default (CLI decides)" for both.** The flag is omitted entirely, so behavior is byte-identical to today until a tenant is actively configured. |
| 4 | Model input | **Preset dropdown** (opus, sonnet, haiku, fable) **plus a Custom… escape hatch** for pinned full IDs (`claude-opus-4-8`). |
| 5 | Effort representation | **Provider-neutral enum** (`Default/Low/Medium/High/XHigh/Max`) in Domain, translated per backend. Not a raw CLI string. |
| 6 | Read timing | **Per call, no cache.** Saves take effect immediately; no stale-value class of bug. |
| 7 | How settings reach the backend | **`ILlmBackend.GenerateAsync` gains a required `LlmSettings` parameter.** The caller resolves; the backend stays tenancy-ignorant. See §6.3. |
| 8 | Backend lifetime | **`ILlmBackend` stays a singleton.** `LlmSettingsService` is also a singleton and opens a DB scope per read via `IServiceScopeFactory`. |
| 9 | Injection safety | **Whitelist-validated custom model strings.** These become process arguments; see §7. |

### 2.1 Why per-tenant, and how this differs from the mockup

`docs/mockups/10-platforms-ai-settings.md` describes AI Studio as a global
job-binding table with a per-tenant *override* column, and states the chain
*this-run choice → tenant override → job default*. This design deliberately
takes the simpler branch: **per-tenant only, no global row.**

Consequences accepted:

- A newly created tenant starts unconfigured and inherits the `appsettings`
  value (then the CLI default). That is the same "starts empty" behavior as
  its Recipes and Sources.
- Changing the model for every tenant means editing each tenant. With a
  handful of tenants this is cheaper than maintaining a resolution chain that
  nothing yet needs.
- If a global default is wanted later, it is an added nullable-`TenantId` row
  plus one fallback step in `LlmSettingsService.GetAsync` — the pattern
  `PromptTemplate` already uses (`Guid? TenantId  // null = system default`).
  The storage shape and the UI do not change.

## 3. Claude CLI facts (verified 2026-07-20 against v2.1.207)

- `--model <model>` — accepts an alias (`opus`, `sonnet`, `haiku`, `fable`) or
  a full name (`claude-opus-4-8`).
- `--effort <level>` — accepts exactly `low`, `medium`, `high`, `xhigh`, `max`.
- Both are per-invocation flags on the same `claude -p --output-format json`
  call the app already makes. Omitting a flag leaves the CLI's own default in
  place.
- Not used (deliberately out of scope): `--max-budget-usd`, `--fallback-model`.

## 4. Scope

### In (v1)

1. **`LlmSetting` table** — at most one row per tenant, holding Model + Effort.
2. **`LlmSettings` domain record + `LlmEffort` enum** — provider-neutral.
3. **`ILlmSettingsProvider` / `LlmSettingsService`** — read and save a tenant's
   values; fall back to `appsettings`, then to "omit the flag".
4. **`ILlmBackend.GenerateAsync` signature change** — takes `LlmSettings`.
5. **`ClaudeCliBackend`** appends `--model` / `--effort` from the passed settings.
6. **Six call sites resolve their tenant's settings** — each already has a
   tenant in scope (§6.4).
7. **AI Studio settings card**, scoped to the active tenant, reacting to the
   tenant switcher.
8. **Validation** of custom model strings against an argument-injection
   whitelist, surfaced inline in the UI.

### Out (deliberately)

- A global cross-tenant default row (see §2.1).
- Per-job overrides (newsletter-topics vs subject-ideas vs llm-research). The
  data shape leaves room for this as an added column/table, not a rewrite.
- Per-run pickers in the composer; the provider profiles table.
- A second provider implementation. The `LlmSettings` seam is where one would
  attach; no second backend is written here.
- Cost/budget caps and fallback models.
- Displaying which model actually answered (`LlmResult.Model` already carries
  it; no UI is added for it).

## 5. Data model

```
LlmSetting  (new table, at most one row per tenant)
  Id        Guid PK
  TenantId  Guid       — unique index; bare Guid, no FK navigation
  Model     string ("")  — "" means "omit --model"; else alias or full ID
  Effort    string ("")  — "" means "omit --effort"; else low|medium|high|xhigh|max

Tenant, Recipe, Post, IssueSection  — unchanged.
```

Shape follows `Platform`, the closest existing analogue: a `Guid Id` primary
key plus a plain `Guid TenantId` with a unique index
(`b.Entity<LlmSetting>().HasIndex(s => s.TenantId).IsUnique()`).

**No foreign key to `Tenant`.** No tenant-owned entity in this codebase
declares one — `AppDbContext` configures a relationship only for
`IssueSection → Post`. Deleting a tenant therefore leaves an orphan row, the
same as it does for that tenant's Recipes and Sources; this design does not
change that behavior.

Rows are created on first save (upsert), so no seed migration is required and
an empty table behaves exactly like "every tenant default". Note EF Core on
SQLite stores Guids as uppercase TEXT — relevant only if a row is ever seeded
by raw SQL.

Effort is persisted as a lowercase string rather than an int so the column
stays readable and stable if the enum is reordered.

## 6. Components

### 6.1 Domain

```csharp
public enum LlmEffort { Default, Low, Medium, High, XHigh, Max }

public record LlmSettings(string Model, LlmEffort Effort)
{
    public static readonly LlmSettings Inherit = new("", LlmEffort.Default);
}

public interface ILlmSettingsProvider
{
    Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default);
    Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default);
}
```

`LlmSettings` names nothing Claude-specific. A future OpenAI-compatible
backend maps `LlmEffort` onto `reasoning_effort` and `Model` onto its own
model field; the storage, the service, and the UI are untouched.

### 6.2 Application — `LlmSettingsService`

- `GetAsync(tenantId)` → reads that tenant's row; if absent or blank, falls
  back to `ClaudeCliOptions.Model` / `ClaudeCliOptions.Effort` from
  `appsettings`; if those are blank too, returns `LlmSettings.Inherit`.
- `SaveAsync(tenantId, settings)` → upserts the tenant's row. Validates
  before writing (§7); throws `ArgumentException` on a rejected model string.
- Registered **singleton**, resolving `IAppDbContext` through
  `IServiceScopeFactory` per call so it can be injected into singleton
  consumers without a lifetime mismatch.

Reading one indexed SQLite row per LLM call is microseconds against a
multi-second CLI invocation, so no caching is introduced and no invalidation
bug is possible.

### 6.3 Domain — `ILlmBackend` signature change

```csharp
Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default);
```

`settings` is **required — deliberately not defaulted to `null`.** A
defaulted parameter would let any call site that is missed compile silently
and run on the wrong tenant's model; a required parameter makes the compiler
enumerate every call site. This is the main cost of choosing per-tenant over
global: with a global setting the backend could read it internally and no
caller would change.

The caller resolves and passes settings, so `ILlmBackend` — a Domain
abstraction — never learns what a tenant is. That keeps the provider-neutral
seam clean: a future backend receives `LlmSettings` and knows nothing about
this app's tenancy model.

### 6.4 Call sites

All six already have a tenant in scope, so each adds one resolve line and
passes the result. No new plumbing, no context objects threaded through:

| Call site | Tenant obtained from |
|---|---|
| `IssueComposerService` (topics) | `tenant` (already loaded) |
| `IssueComposerService` (regenerate) | `tenant` (already loaded) |
| `PostService` (subject ideas) | `post.TenantId` |
| `LlmResearchConnector.FetchAsync` ×2 | `source.TenantId` |
| `GenerationPipeline` | `recipe.TenantId` |

Each of these types gains an `ILlmSettingsProvider` dependency.

### 6.5 Infrastructure — `ClaudeCliBackend`

Composes arguments per call from the passed settings:

```
-p --output-format json
   [--model <settings.Model>]     when non-blank
   [--effort <settings.Effort>]   when not Default
   [<ExtraArgs>]                  unchanged
```

`ClaudeCliOptions.Model` keeps working as the fallback (applied in the
service, §6.2), so a headless or freshly-provisioned install still honors
`appsettings`. `ExtraArgs` is appended last and is unchanged — it remains the
escape hatch for flags this design does not model (e.g.
`--allowedTools WebSearch` for research sources).

The existing retry loop, JSON parsing, and `modelUsage` extraction are
untouched.

### 6.6 Web — AI Studio page

A real settings card is added **above** the existing `ComingSoonBanner` and
mockup tables, which stay as-is to show where this is heading. The card is
tenant-scoped, so it follows the same pattern as `Recipes.razor`: inject
`TenantContext`, guard on `!Ctx.Initialized` and `Ctx.Active is null`,
subscribe to `Ctx.Changed` in `OnInitializedAsync`, and unsubscribe in
`Dispose`. Switching tenants reloads the card's values.

```
┌─ AI Studio ── Model — AIVisions ───────────────────────┐
│  Provider  [ Claude CLI ▾ ]   (only provider today)    │
│  Model     [ Opus ▾ ]                                  │
│              Default (CLI decides) · Opus · Sonnet     │
│              Haiku · Fable · Custom…                   │
│  Custom    [ claude-opus-4-8         ]  ← only when    │
│                                            Custom…     │
│  Effort    [ High ▾ ]                                  │
│              Default (CLI decides) · low · medium      │
│              high · xhigh · max                        │
│                                                        │
│  ⓘ Applies to every ✨ action for this tenant: topic   │
│    blurbs, subject ideas, regenerate, LLM research.    │
│    Other tenants keep their own settings.              │
│                                    [ Save ]            │
└────────────────────────────────────────────────────────┘
```

The active tenant's name appears in the card header, and the caption states
that other tenants are unaffected — without both, a per-tenant setting on a
global-looking nav page reads as global and invites the wrong edit.

**Provider is display-only** — a disabled field reading "Claude CLI", not a
live dropdown. There is exactly one backend and nothing to switch to; an
enabled control with one option would promise a choice that does not exist.
It is rendered at all only to mark where provider selection will land.

Selecting a preset hides the custom field and clears it. Saving shows a
success snackbar; a rejected custom string shows an inline validation error
and does not write.

### 6.7 What is NOT touched

`ILlmBackend`'s implementations beyond the signature change, the
`WindowsCommandResolver` launch path, `ProcessRunner`, MailerLite, and every
composer/renderer component. The six call sites change by one resolve line
each; no call site's own logic or signature changes.

## 7. Argument-injection safety

Model strings become arguments on a process the app spawns, so a value like
`opus --dangerously-skip-permissions` would inject a flag into the CLI call.

- **Effort** is an enum with a fixed translation table — safe by construction,
  no validation needed.
- **Model** is validated against `^[A-Za-z0-9._\-\[\]]+$` with a 100-character
  cap. This admits every real alias and full ID (`claude-opus-4-8`,
  `claude-opus-4-8[1m]`) and rejects whitespace, quotes, and shell/flag
  metacharacters.
- Validation lives in the **service**, not only the UI, so it holds regardless
  of caller. `SaveAsync` throws on a rejected value.
- Presets bypass user text entirely.

This matters more than usual here because `ProcessRunner` may route through
`cmd.exe /c` on Windows for npm shims (see `WindowsCommandResolver`), which
adds a second parser to the path.

## 8. Error handling

| Failure | Behavior |
|---|---|
| No `LlmSetting` row for this tenant | Fall back to `appsettings`, then to omitting both flags. Never an error. |
| Custom model fails validation | Inline field error in AI Studio; nothing written; service throws if called directly. |
| DB read fails mid-generation | Log and fall back to `appsettings` values so generation still runs rather than hard-failing on a settings lookup. |
| CLI rejects a model/effort value | Existing behavior: non-zero exit → one retry → `InvalidOperationException` surfaced as the current generation-failed banner, with stderr included. |
| Effort string in DB is unrecognized | Treated as `Default` (flag omitted) rather than throwing. |
| AI Studio opened with no active tenant | Card shows the same "no tenant" state the other tenant-scoped pages show; no save possible. |

## 9. Testing

- **Unit — argument composition:** model set / unset, effort set / unset, both
  set, `ExtraArgs` still appended last, and byte-identical args to today when
  passed `LlmSettings.Inherit`.
- **Unit — injection:** `SaveAsync` rejects `opus --dangerously-skip-permissions`,
  values with spaces, quotes, semicolons, and over-length strings; accepts
  `opus`, `claude-opus-4-8`, `claude-opus-4-8[1m]`.
- **Unit — fallback chain:** no row for tenant → `appsettings` value; blank
  `appsettings` → `Inherit`; unrecognized effort string → `Default`.
- **Unit — tenant isolation:** saving for tenant A does not change what
  `GetAsync(B)` returns; two tenants with different models each resolve their
  own.
- **Integration:** save via the service → a subsequent `GenerateAsync` (fake
  `IProcessRunner`) receives the expected flags; upsert does not create a
  second row for the same tenant.
- **Manual:** set Sonnet + low for one tenant in AI Studio, run Generate ✨ in
  the composer, confirm the run completes and `LlmResult.Model` reflects the
  chosen model; switch tenants and confirm the card shows the other tenant's
  values.

## 10. Open questions

1. Should the effective model be surfaced somewhere in the UI after a run
   (`LlmResult.Model` already carries it)? Deferred — no consumer asked yet.
2. A global cross-tenant default is the obvious addition once configuring each
   tenant individually becomes tedious. §2.1 records the one-column,
   one-fallback-step path to it. Revisit when it bites, not before.
3. Per-job overrides remain deferred until one job proves to want a different
   tier than the rest.
