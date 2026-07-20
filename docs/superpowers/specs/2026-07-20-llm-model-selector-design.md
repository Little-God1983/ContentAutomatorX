# ContentAutomatorX — LLM Model & Effort Selector Design

**Date:** 2026-07-20
**Status:** Approved 2026-07-20
**Depends on:** Phase 2b (issue composer, shipped 2026-07-19)
**Partially realizes:** `docs/mockups/10-platforms-ai-settings.md` (AI Studio)

## 1. Goal

Today every AI action in the app runs on whatever model the Claude CLI
defaults to. The only lever is `Claude:Model` in `appsettings.json`, which is
bound once at startup into a singleton and therefore cannot be changed without
editing a file and restarting the app. There is no way to influence reasoning
depth at all.

Give the operator one place in the UI to choose **which model** and **how hard
it thinks**, applying to every ✨ action, without building the full AI Studio
provider/job matrix.

Success test: open AI Studio → set Model = Sonnet, Effort = low → press
Generate ✨ in the issue composer → the CLI is invoked with
`--model sonnet --effort low`, with no app restart.

## 2. Decisions (brainstormed 2026-07-20)

| # | Question | Decision |
|---|---|---|
| 1 | Scope | **One global default.** Not per-tenant, per-recipe, or per-run. Applies to every LLM call in the app. |
| 2 | Where it's edited | **AI Studio page** — a real settings card above the existing "coming soon" mockup content. |
| 3 | Default values | **"Default (CLI decides)" for both.** The flag is omitted entirely, so behavior is byte-identical to today until the operator actively chooses. |
| 4 | Model input | **Preset dropdown** (opus, sonnet, haiku, fable) **plus a Custom… escape hatch** for pinned full IDs (`claude-opus-4-8`). |
| 5 | Effort representation | **Provider-neutral enum** (`Default/Low/Medium/High/XHigh/Max`) in Domain, translated per backend. Not a raw CLI string. |
| 6 | Read timing | **Per call, no cache.** Saves take effect immediately; no stale-value class of bug. |
| 7 | Backend lifetime | **`ILlmBackend` stays a singleton.** The settings provider opens a DB scope per read via `IServiceScopeFactory`. |
| 8 | Injection safety | **Whitelist-validated custom model strings.** These become process arguments; see §7. |

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

1. **`LlmSetting` single-row table** holding the global Model + Effort.
2. **`LlmSettings` domain record + `LlmEffort` enum** — provider-neutral.
3. **`ILlmSettingsProvider` / `LlmSettingsService`** — read current values,
   save new ones, fall back to `appsettings` then to "omit the flag".
4. **`ClaudeCliBackend` reads settings per call** and appends
   `--model` / `--effort` when set.
5. **AI Studio settings card** — provider (read-only, one entry today), model
   select + custom field, effort select, Save.
6. **Validation** of custom model strings against an argument-injection
   whitelist, surfaced inline in the UI.

### Out (deliberately)

- Per-job overrides (newsletter-topics vs subject-ideas vs llm-research). The
  data shape leaves room for this as an added table, not a rewrite.
- Per-run pickers in the composer, per-tenant models, provider profiles table.
- A second provider implementation. The `LlmSettings` seam is where one would
  attach; no second backend is written here.
- Cost/budget caps and fallback models.
- Displaying which model actually answered (`LlmResult.Model` already carries
  it; no UI is added for it).

## 5. Data model

```
LlmSetting  (new table, exactly one row)
  Id      Guid PK   — fixed SingletonId 00000000-0000-0000-0000-000000000001
  Model   string ("")  — "" means "omit --model"; else alias or full ID
  Effort  string ("")  — "" means "omit --effort"; else low|medium|high|xhigh|max

Tenant, Recipe, Post, IssueSection  — unchanged.
```

The row is created on first save (upsert), so no seed migration is required
and an empty table behaves exactly like "everything default". Note EF Core on
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
    Task<LlmSettings> GetAsync(CancellationToken ct = default);
}
```

`LlmSettings` names nothing Claude-specific. A future OpenAI-compatible
backend maps `LlmEffort` onto `reasoning_effort` and `Model` onto its own
model field; the storage, the service, and the UI are untouched.

### 6.2 Application — `LlmSettingsService`

- `GetAsync()` → reads the single row; if absent or blank, falls back to
  `ClaudeCliOptions.Model` / `ClaudeCliOptions.Effort` from `appsettings`;
  if those are blank too, returns `LlmSettings.Inherit`.
- `SaveAsync(LlmSettings)` → upserts the row. Validates before writing
  (§7); throws `ArgumentException` on a rejected model string.
- Registered **singleton**, resolving `IAppDbContext` through
  `IServiceScopeFactory` per call so it can be injected into the singleton
  backend without a lifetime mismatch.

Reading one SQLite row per LLM call is microseconds against a multi-second CLI
invocation, so no caching is introduced and no invalidation bug is possible.

### 6.3 Infrastructure — `ClaudeCliBackend`

Gains `ILlmSettingsProvider` and composes arguments per call:

```
-p --output-format json
   [--model <settings.Model>]     when non-blank
   [--effort <settings.Effort>]   when not Default
   [<ExtraArgs>]                  unchanged
```

`ClaudeCliOptions.Model` keeps working as the fallback, so a headless or
freshly-provisioned install still honors `appsettings`. `ExtraArgs` is
appended last and is unchanged — it remains the escape hatch for flags this
design does not model (e.g. `--allowedTools WebSearch` for research sources).

The existing retry loop, JSON parsing, and `modelUsage` extraction are
untouched.

### 6.4 Web — AI Studio page

A real settings card is added **above** the existing `ComingSoonBanner` and
mockup tables, which stay as-is to show where this is heading:

```
┌─ AI Studio ────────────────────────────────────────────┐
│  MODEL                                                 │
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
│  ⓘ Applies to every ✨ action: topic blurbs, subject   │
│    ideas, regenerate, and LLM research sources.        │
│                                    [ Save ]            │
└────────────────────────────────────────────────────────┘
```

Selecting a preset hides the custom field and clears it. Saving shows a
success snackbar; a rejected custom string shows an inline validation error
and does not write.

### 6.5 What is NOT touched

`ILlmBackend`'s interface, the six call sites (`IssueComposerService` ×2,
`PostService`, `LlmResearchConnector` ×2, `GenerationPipeline`), the
`WindowsCommandResolver` launch path, `ProcessRunner`, MailerLite, and every
composer/renderer component. Callers get the new behavior for free because the
change lives entirely inside the backend.

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
| No `LlmSetting` row yet | Fall back to `appsettings`, then to omitting both flags. Never an error. |
| Custom model fails validation | Inline field error in AI Studio; nothing written; service throws if called directly. |
| DB read fails mid-generation | Log and fall back to `appsettings` values so generation still runs rather than hard-failing on a settings lookup. |
| CLI rejects a model/effort value | Existing behavior: non-zero exit → one retry → `InvalidOperationException` surfaced as the current generation-failed banner, with stderr included. |
| Effort string in DB is unrecognized | Treated as `Default` (flag omitted) rather than throwing. |

## 9. Testing

- **Unit — argument composition:** model set / unset, effort set / unset, both
  set, `ExtraArgs` still appended last, and byte-identical args to today when
  both are `Default`.
- **Unit — injection:** `SaveAsync` rejects `opus --dangerously-skip-permissions`,
  values with spaces, quotes, semicolons, and over-length strings; accepts
  `opus`, `claude-opus-4-8`, `claude-opus-4-8[1m]`.
- **Unit — fallback chain:** empty DB → `appsettings` value; blank
  `appsettings` → `Inherit`; unrecognized effort string → `Default`.
- **Integration:** save via the service → a subsequent `GenerateAsync` (fake
  `IProcessRunner`) receives the expected flags; upsert does not create a
  second row.
- **Manual:** set Sonnet + low in AI Studio, run Generate ✨ in the composer,
  confirm the run completes and `LlmResult.Model` reflects the chosen model.

## 10. Open questions

1. Should the effective model be surfaced somewhere in the UI after a run
   (`LlmResult.Model` already carries it)? Deferred — no consumer asked yet.
2. Per-job overrides are the obvious next step once one job proves to want a
   different tier than the rest. Revisit when that happens, not before.
