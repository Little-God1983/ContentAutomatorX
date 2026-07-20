# Per-Tenant LLM Model & Effort Selector Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each tenant choose which Claude model runs its ✨ actions and how hard it thinks, editable in AI Studio with no app restart.

**Architecture:** A new `TenantLlmSetting` row per tenant stores Model + Effort. `LlmSettingsService` (Application) reads it, falling back to `appsettings` then to "omit the flag". `ILlmBackend.GenerateAsync` gains a **required** `LlmSettings` parameter, so each of the six call sites resolves its own tenant's settings and passes them down — the backend never learns what a tenant is.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, EF Core 10 + SQLite, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-20-llm-model-selector-design.md`

## Global Constraints

- **Model validation regex:** `^[A-Za-z0-9._\-\[\]]+$`, max length **100**. Must accept `opus`, `claude-opus-4-8`, `claude-opus-4-8[1m]`; must reject spaces, quotes, semicolons, and `opus --dangerously-skip-permissions`.
- **Validation lives in the service**, not only the UI. `SaveAsync` throws `ArgumentException` on a rejected model.
- **Effort storage strings** are exactly: `""` (Default), `low`, `medium`, `high`, `xhigh`, `max`. Unrecognized strings parse to `Default`, never throw.
- **Default behavior is byte-identical to today:** when settings resolve to `LlmSettings.Inherit`, the CLI args string must be exactly `-p --output-format json` plus any `ExtraArgs`.
- **`ExtraArgs` is always appended last**, after `--model` and `--effort`.
- **`GenerateAsync`'s `settings` parameter has no default value.** A missed call site must fail to compile.
- **No foreign key** from `TenantLlmSetting` to `Tenant` — no tenant-owned entity in this codebase declares one.
- **Branch:** work on `feature/issue-composer`. Do not create a new branch.
- **Naming:** the entity is `TenantLlmSetting` (singular, tenant-prefixed); the value record is `LlmSettings` (plural). Do not collapse these names — the one-letter difference is why the entity carries the `Tenant` prefix.

## Environment Notes

- **There is no `.sln`.** Run each test project by path (commands given per task).
- **Stop the running app before building.** If `ContentAutomatorX.Web` is running (or Visual Studio has it loaded), builds fail with MSB3021/MSB3027 file-lock errors. This is an environment lock, not a code regression.
- EF migrations: `dotnet ef migrations add <Name> --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web` (a design-time factory exists). If `dotnet ef` is missing: `dotnet tool install --global dotnet-ef`.
- A stale `.superpowers/sdd/progress.md` ledger may exist from the previous (issue-composer) feature. It does **not** describe this plan — ignore or clear it before starting.

## File Structure

| File | Responsibility |
|---|---|
| `src/ContentAutomatorX.Domain/Models/LlmSettings.cs` | `LlmEffort` enum, `LlmSettings` record, storage-string parse/format |
| `src/ContentAutomatorX.Domain/Models/LlmModelName.cs` | The one validation rule, shared by service and UI |
| `src/ContentAutomatorX.Domain/Entities/TenantLlmSetting.cs` | Persisted row, one per tenant |
| `src/ContentAutomatorX.Domain/Abstractions/ILlmSettingsProvider.cs` | Read/save port |
| `src/ContentAutomatorX.Domain/Abstractions/ILlmBackend.cs` | *Modified* — `GenerateAsync` takes `LlmSettings` |
| `src/ContentAutomatorX.Application/Services/LlmSettingsService.cs` | Implements the port; owns the fallback chain and validation |
| `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs` | *Modified* — adds the DbSet |
| `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs` | *Modified* — DbSet + unique index |
| `src/ContentAutomatorX.Infrastructure/Llm/ClaudeCliBackend.cs` | *Modified* — `Effort` option, `--model`/`--effort` composition |
| `src/ContentAutomatorX.Infrastructure/Sources/LlmResearchConnector.cs` | *Modified* — resolves settings from `source.TenantId` |
| `src/ContentAutomatorX.Application/Services/IssueComposerService.cs` | *Modified* — 2 call sites |
| `src/ContentAutomatorX.Application/Services/PostService.cs` | *Modified* — 1 call site |
| `src/ContentAutomatorX.Application/Pipelines/GenerationPipeline.cs` | *Modified* — 1 call site |
| `src/ContentAutomatorX.Web/Program.cs` | *Modified* — DI registration |
| `src/ContentAutomatorX.Web/Components/Pages/AiStudio.razor` | *Modified* — the settings card |

---

### Task 1: Domain contracts and model-name validation

Pure Domain types with no dependencies. Compiles and tests standalone; nothing consumes them yet.

**Files:**
- Create: `src/ContentAutomatorX.Domain/Models/LlmSettings.cs`
- Create: `src/ContentAutomatorX.Domain/Models/LlmModelName.cs`
- Create: `src/ContentAutomatorX.Domain/Entities/TenantLlmSetting.cs`
- Create: `src/ContentAutomatorX.Domain/Abstractions/ILlmSettingsProvider.cs`
- Test: `tests/ContentAutomatorX.UnitTests/LlmSettingsDomainTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum LlmEffort { Default, Low, Medium, High, XHigh, Max }`
  - `record LlmSettings(string Model, LlmEffort Effort)` with `static readonly LlmSettings Inherit`, `static LlmSettings From(string? model, string? effort)`, `static string ToStorage(LlmEffort)`, `static LlmEffort ParseEffort(string?)`
  - `static class LlmModelName` with `const int MaxLength = 100` and `bool IsValid(string? model)`
  - `class TenantLlmSetting { Guid Id; Guid TenantId; string Model; string Effort; }`
  - `interface ILlmSettingsProvider { Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default); Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default); }`

- [ ] **Step 1: Write the failing tests**

Create `tests/ContentAutomatorX.UnitTests/LlmSettingsDomainTests.cs`:

```csharp
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.UnitTests;

public class LlmModelNameTests
{
    [Theory]
    [InlineData("opus")]
    [InlineData("sonnet")]
    [InlineData("haiku")]
    [InlineData("fable")]
    [InlineData("claude-opus-4-8")]
    [InlineData("claude-opus-4-8[1m]")]
    [InlineData("some.model_v2")]
    public void Accepts_real_aliases_and_full_ids(string model) =>
        Assert.True(LlmModelName.IsValid(model));

    [Theory]
    [InlineData("opus --dangerously-skip-permissions")]  // the attack this rule exists for
    [InlineData("opus sonnet")]
    [InlineData("\"opus\"")]
    [InlineData("opus;rm -rf /")]
    [InlineData("opus&whoami")]
    [InlineData("opus|tee x")]
    [InlineData("opus>out.txt")]
    [InlineData("opus%PATH%")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_anything_that_could_inject_an_argument(string? model) =>
        Assert.False(LlmModelName.IsValid(model));

    [Fact]
    public void Rejects_strings_over_the_length_cap()
    {
        Assert.True(LlmModelName.IsValid(new string('a', 100)));
        Assert.False(LlmModelName.IsValid(new string('a', 101)));
    }
}

public class LlmSettingsTests
{
    [Fact]
    public void Inherit_means_both_flags_omitted()
    {
        Assert.Equal("", LlmSettings.Inherit.Model);
        Assert.Equal(LlmEffort.Default, LlmSettings.Inherit.Effort);
    }

    [Theory]
    [InlineData("low", LlmEffort.Low)]
    [InlineData("medium", LlmEffort.Medium)]
    [InlineData("high", LlmEffort.High)]
    [InlineData("xhigh", LlmEffort.XHigh)]
    [InlineData("max", LlmEffort.Max)]
    [InlineData("HIGH", LlmEffort.High)]     // case-insensitive
    [InlineData("  high  ", LlmEffort.High)] // tolerates stored whitespace
    public void Parses_every_stored_effort_string(string stored, LlmEffort expected) =>
        Assert.Equal(expected, LlmSettings.ParseEffort(stored));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ludicrous")]   // hand-edited or written by a future version
    public void Unrecognized_effort_degrades_to_Default_rather_than_throwing(string? stored) =>
        Assert.Equal(LlmEffort.Default, LlmSettings.ParseEffort(stored));

    [Theory]
    [InlineData(LlmEffort.Default, "")]
    [InlineData(LlmEffort.Low, "low")]
    [InlineData(LlmEffort.XHigh, "xhigh")]
    [InlineData(LlmEffort.Max, "max")]
    public void Formats_effort_back_to_its_storage_string(LlmEffort effort, string expected) =>
        Assert.Equal(expected, LlmSettings.ToStorage(effort));

    [Fact]
    public void Storage_round_trips_for_every_enum_value()
    {
        foreach (var effort in Enum.GetValues<LlmEffort>())
            Assert.Equal(effort, LlmSettings.ParseEffort(LlmSettings.ToStorage(effort)));
    }

    [Fact]
    public void From_trims_the_model_and_parses_the_effort()
    {
        var settings = LlmSettings.From("  sonnet  ", "low");
        Assert.Equal("sonnet", settings.Model);
        Assert.Equal(LlmEffort.Low, settings.Effort);
    }

    [Fact]
    public void From_treats_null_or_blank_model_as_unset()
    {
        Assert.Equal("", LlmSettings.From(null, null).Model);
        Assert.Equal("", LlmSettings.From("   ", null).Model);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter "FullyQualifiedName~LlmModelNameTests|FullyQualifiedName~LlmSettingsTests"`

Expected: build FAILS with `CS0246: The type or namespace name 'LlmModelName' could not be found` (and the same for `LlmSettings`/`LlmEffort`).

- [ ] **Step 3: Create the settings value types**

Create `src/ContentAutomatorX.Domain/Models/LlmSettings.cs`:

```csharp
namespace ContentAutomatorX.Domain.Models;

/// <summary>How hard the model should think. Provider-neutral: each backend
/// translates this to its own vocabulary (Claude CLI --effort, an
/// OpenAI-compatible reasoning_effort, or nothing at all).</summary>
public enum LlmEffort { Default, Low, Medium, High, XHigh, Max }

/// <summary>Resolved per-tenant LLM choice, passed into every ILlmBackend call.
/// Names nothing Claude-specific.</summary>
/// <param name="Model">Alias or full model ID. "" means "omit the flag".</param>
public record LlmSettings(string Model, LlmEffort Effort)
{
    /// <summary>Change nothing — let the backend's own default stand.</summary>
    public static readonly LlmSettings Inherit = new("", LlmEffort.Default);

    public static LlmSettings From(string? model, string? effort) =>
        new(model?.Trim() ?? "", ParseEffort(effort));

    /// <summary>Canonical persisted form. Deliberately a string, not the enum's
    /// int, so the column stays readable and survives the enum being reordered.</summary>
    public static string ToStorage(LlmEffort effort) => effort switch
    {
        LlmEffort.Low => "low",
        LlmEffort.Medium => "medium",
        LlmEffort.High => "high",
        LlmEffort.XHigh => "xhigh",
        LlmEffort.Max => "max",
        _ => "",
    };

    /// <summary>Never throws. A row hand-edited to garbage, or written by a
    /// future version that knows more levels, degrades to Default (flag omitted)
    /// rather than bricking generation for that tenant.</summary>
    public static LlmEffort ParseEffort(string? stored) => stored?.Trim().ToLowerInvariant() switch
    {
        "low" => LlmEffort.Low,
        "medium" => LlmEffort.Medium,
        "high" => LlmEffort.High,
        "xhigh" => LlmEffort.XHigh,
        "max" => LlmEffort.Max,
        _ => LlmEffort.Default,
    };
}
```

- [ ] **Step 4: Create the model-name validator**

Create `src/ContentAutomatorX.Domain/Models/LlmModelName.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ContentAutomatorX.Domain.Models;

/// <summary>The single rule for what may be used as a model name.
///
/// This exists because the model string becomes an argument on a process the app
/// spawns: "opus --dangerously-skip-permissions" would otherwise inject a flag
/// into the CLI call. On Windows ProcessRunner may additionally route through
/// cmd.exe /c for npm shims (see WindowsCommandResolver), putting a second
/// parser on the path — so shell metacharacters are rejected too.
///
/// Lives in Domain so the service and the UI enforce the same rule; the service
/// is the authority (the UI can be bypassed, the service cannot).</summary>
public static partial class LlmModelName
{
    public const int MaxLength = 100;

    [GeneratedRegex(@"^[A-Za-z0-9._\-\[\]]+$")]
    private static partial Regex Allowed();

    /// <summary>True for a usable model name. Blank is NOT valid here — callers
    /// treat blank as "unset" and skip validation entirely.</summary>
    public static bool IsValid(string? model) =>
        !string.IsNullOrEmpty(model) && model.Length <= MaxLength && Allowed().IsMatch(model);
}
```

- [ ] **Step 5: Create the entity and the port**

Create `src/ContentAutomatorX.Domain/Entities/TenantLlmSetting.cs`:

```csharp
namespace ContentAutomatorX.Domain.Entities;

/// <summary>One row per tenant holding that tenant's LLM choice. Absent row =
/// tenant never configured = fall back to appsettings, then to the CLI default.
/// Named TenantLlmSetting rather than LlmSetting so it does not read as a typo
/// of the LlmSettings value record.</summary>
public class TenantLlmSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Model { get; set; } = "";    // "" = omit --model
    public string Effort { get; set; } = "";   // "" = omit --effort
}
```

Create `src/ContentAutomatorX.Domain/Abstractions/ILlmSettingsProvider.cs`:

```csharp
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ILlmSettingsProvider
{
    /// <summary>Never throws for a missing row — returns the fallback chain's result.</summary>
    Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Upserts this tenant's row.</summary>
    /// <exception cref="ArgumentException">The model name fails validation.</exception>
    Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter "FullyQualifiedName~LlmModelNameTests|FullyQualifiedName~LlmSettingsTests"`

Expected: PASS, 0 failed. (Roughly 30 test cases from the `[Theory]` rows.)

- [ ] **Step 7: Commit**

```bash
git add src/ContentAutomatorX.Domain/Models/LlmSettings.cs \
        src/ContentAutomatorX.Domain/Models/LlmModelName.cs \
        src/ContentAutomatorX.Domain/Entities/TenantLlmSetting.cs \
        src/ContentAutomatorX.Domain/Abstractions/ILlmSettingsProvider.cs \
        tests/ContentAutomatorX.UnitTests/LlmSettingsDomainTests.cs
git commit -m "feat: per-tenant LLM settings domain contracts + model-name validation (#aistudio)"
```

---

### Task 2: Persistence and `LlmSettingsService`

Adds the table and the service that owns the fallback chain and enforces validation. Verified against a real migrated SQLite file, because the fallback and upsert behavior is exactly what an in-memory fake would paper over.

**Files:**
- Modify: `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`
- Modify: `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/ContentAutomatorX.Application/Services/LlmSettingsService.cs`
- Create: migration `TenantLlmSettings` (generated)
- Test: `tests/ContentAutomatorX.IntegrationTests/LlmSettingsServiceTests.cs`

**Interfaces:**
- Consumes: `LlmSettings`, `LlmEffort`, `LlmModelName`, `TenantLlmSetting`, `ILlmSettingsProvider` (Task 1).
- Produces: `LlmSettingsService(IAppDbContext db, LlmSettings fallback) : ILlmSettingsProvider`. The `fallback` is a plain `LlmSettings` value, **not** `ClaudeCliOptions` — Application cannot reference Infrastructure. Task 3 registers it from appsettings.

- [ ] **Step 1: Add the DbSet to the Application-side interface**

In `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`, add this line after the `IssueSections` property (line 17):

```csharp
    DbSet<TenantLlmSetting> TenantLlmSettings { get; }
```

- [ ] **Step 2: Add the DbSet and unique index to AppDbContext**

In `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`, add after the `IssueSections` property (line 18):

```csharp
    public DbSet<TenantLlmSetting> TenantLlmSettings => Set<TenantLlmSetting>();
```

and inside `OnModelCreating`, after the `IssueSection` FK line (line 34):

```csharp
        // Unique so "at most one row per tenant" is enforced by the database, not
        // by hoping every writer goes through SaveAsync's upsert. No FK to Tenant:
        // no tenant-owned entity here declares one (see Platform, Recipe, Source).
        b.Entity<TenantLlmSetting>().HasIndex(s => s.TenantId).IsUnique();
```

- [ ] **Step 3: Generate the migration**

Run:

```bash
dotnet ef migrations add TenantLlmSettings --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web
```

Expected: creates `src/ContentAutomatorX.Infrastructure/Migrations/<timestamp>_TenantLlmSettings.cs` (+ `.Designer.cs`) and updates `AppDbContextModelSnapshot.cs`.

Open the generated `.cs` and confirm it contains `migrationBuilder.CreateTable(name: "TenantLlmSettings"` and a `CreateIndex` with `unique: true` on `TenantId`. If `dotnet ef` is not found: `dotnet tool install --global dotnet-ef`, then re-run.

- [ ] **Step 4: Write the failing tests**

Create `tests/ContentAutomatorX.IntegrationTests/LlmSettingsServiceTests.cs`:

```csharp
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class LlmSettingsServiceTests
{
    private static async Task<Tenant> SeedTenant(TestDb test, string slug)
    {
        var tenant = new Tenant { Name = slug, Slug = slug };
        test.Db.Tenants.Add(tenant);
        await test.Db.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task Unconfigured_tenant_falls_back_to_appsettings()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, new LlmSettings("haiku", LlmEffort.Low));

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("haiku", settings.Model);
        Assert.Equal(LlmEffort.Low, settings.Effort);
    }

    [Fact]
    public async Task Unconfigured_tenant_with_blank_appsettings_inherits()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        Assert.Equal(LlmSettings.Inherit, await svc.GetAsync(tenant.Id));
    }

    [Fact]
    public async Task Saved_values_round_trip()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        await svc.SaveAsync(tenant.Id, new LlmSettings("sonnet", LlmEffort.XHigh));
        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("sonnet", settings.Model);
        Assert.Equal(LlmEffort.XHigh, settings.Effort);
    }

    [Fact]
    public async Task Saving_twice_upserts_rather_than_adding_a_second_row()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        await svc.SaveAsync(tenant.Id, new LlmSettings("opus", LlmEffort.High));
        await svc.SaveAsync(tenant.Id, new LlmSettings("haiku", LlmEffort.Low));

        Assert.Equal(1, await test.Db.TenantLlmSettings.CountAsync(s => s.TenantId == tenant.Id));
        Assert.Equal("haiku", (await svc.GetAsync(tenant.Id)).Model);
    }

    [Fact]
    public async Task Tenants_do_not_see_each_others_settings()
    {
        using var test = TestDb.Create();
        var a = await SeedTenant(test, "a");
        var b = await SeedTenant(test, "b");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        await svc.SaveAsync(a.Id, new LlmSettings("opus", LlmEffort.Max));

        Assert.Equal("opus", (await svc.GetAsync(a.Id)).Model);
        Assert.Equal("", (await svc.GetAsync(b.Id)).Model);
        Assert.Equal(LlmEffort.Default, (await svc.GetAsync(b.Id)).Effort);
    }

    [Fact]
    public async Task Save_rejects_an_injected_flag_and_writes_nothing()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SaveAsync(tenant.Id, new LlmSettings("opus --dangerously-skip-permissions", LlmEffort.Default)));

        Assert.Equal(0, await test.Db.TenantLlmSettings.CountAsync());
    }

    [Fact]
    public async Task Save_accepts_a_blank_model_as_meaning_unset()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.High));
        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("", settings.Model);
        Assert.Equal(LlmEffort.High, settings.Effort);
    }

    [Fact]
    public async Task A_garbage_effort_string_in_the_row_degrades_to_Default()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        test.Db.TenantLlmSettings.Add(new TenantLlmSetting
        {
            TenantId = tenant.Id, Model = "opus", Effort = "ludicrous"
        });
        await test.Db.SaveChangesAsync();
        var svc = new LlmSettingsService(test.Db, LlmSettings.Inherit);

        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("opus", settings.Model);
        Assert.Equal(LlmEffort.Default, settings.Effort);
    }

    [Fact]
    public async Task A_row_with_a_blank_model_still_falls_back_for_the_model_only()
    {
        using var test = TestDb.Create();
        var tenant = await SeedTenant(test, "a");
        var svc = new LlmSettingsService(test.Db, new LlmSettings("haiku", LlmEffort.Low));

        await svc.SaveAsync(tenant.Id, new LlmSettings("", LlmEffort.Max));
        var settings = await svc.GetAsync(tenant.Id);

        Assert.Equal("haiku", settings.Model);      // blank model -> appsettings
        Assert.Equal(LlmEffort.Max, settings.Effort); // explicit effort wins
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter "FullyQualifiedName~LlmSettingsServiceTests"`

Expected: build FAILS with `CS0246: The type or namespace name 'LlmSettingsService' could not be found`.

- [ ] **Step 6: Implement the service**

Create `src/ContentAutomatorX.Application/Services/LlmSettingsService.cs`:

```csharp
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

/// <summary>Resolves a tenant's LLM choice: its own row, else appsettings, else
/// "omit both flags". Read fresh on every call — one indexed SQLite row is
/// microseconds against a multi-second CLI invocation, so there is no cache and
/// therefore no cache-invalidation bug when a save lands mid-session.</summary>
/// <param name="fallback">Values from appsettings. A plain LlmSettings rather
/// than ClaudeCliOptions because Application must not reference Infrastructure.</param>
public class LlmSettingsService(IAppDbContext db, LlmSettings fallback) : ILlmSettingsProvider
{
    public async Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await db.TenantLlmSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (row is null) return fallback;

        // Each field falls back independently: a tenant may pin a model while
        // leaving effort alone, or the reverse.
        var model = string.IsNullOrWhiteSpace(row.Model) ? fallback.Model : row.Model.Trim();
        var effort = LlmSettings.ParseEffort(row.Effort);
        if (effort == LlmEffort.Default) effort = fallback.Effort;
        return new LlmSettings(model, effort);
    }

    public async Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default)
    {
        var model = settings.Model?.Trim() ?? "";
        // Blank means "unset"; anything else must survive becoming a process argument.
        // Enforced here and not only in the UI, so it holds for any caller.
        if (model.Length > 0 && !LlmModelName.IsValid(model))
            throw new ArgumentException(
                $"'{model}' is not a valid model name. Use letters, digits, dot, underscore, " +
                $"hyphen or square brackets, up to {LlmModelName.MaxLength} characters.",
                nameof(settings));

        var row = await db.TenantLlmSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (row is null)
        {
            row = new TenantLlmSetting { TenantId = tenantId };
            db.TenantLlmSettings.Add(row);
        }
        row.Model = model;
        row.Effort = LlmSettings.ToStorage(settings.Effort);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter "FullyQualifiedName~LlmSettingsServiceTests"`

Expected: PASS, 9 passed, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs \
        src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs \
        src/ContentAutomatorX.Infrastructure/Migrations/ \
        src/ContentAutomatorX.Application/Services/LlmSettingsService.cs \
        tests/ContentAutomatorX.IntegrationTests/LlmSettingsServiceTests.cs
git commit -m "feat: TenantLlmSetting table and LlmSettingsService fallback chain (#aistudio)"
```

---

### Task 3: Thread settings through the backend and all six call sites

The `ILlmBackend` signature change is compile-breaking by design, so the interface, the backend, all six call sites, all four test fakes, and the DI wiring move together. Nothing compiles until they all do — do not try to split this task.

**Files:**
- Modify: `src/ContentAutomatorX.Domain/Abstractions/ILlmBackend.cs`
- Modify: `src/ContentAutomatorX.Infrastructure/Llm/ClaudeCliBackend.cs`
- Modify: `src/ContentAutomatorX.Application/Services/IssueComposerService.cs` (2 sites)
- Modify: `src/ContentAutomatorX.Application/Services/PostService.cs` (1 site)
- Modify: `src/ContentAutomatorX.Application/Pipelines/GenerationPipeline.cs` (1 site)
- Modify: `src/ContentAutomatorX.Infrastructure/Sources/LlmResearchConnector.cs` (2 sites)
- Modify: `src/ContentAutomatorX.Web/Program.cs`
- Modify: `src/ContentAutomatorX.Web/appsettings.json`
- Modify: `tests/ContentAutomatorX.UnitTests/LlmResearchConnectorTests.cs`
- Modify: `tests/ContentAutomatorX.IntegrationTests/GenerationPipelineTests.cs`
- Modify: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs`
- Modify: `tests/ContentAutomatorX.IntegrationTests/McpToolsTests.cs`
- Test: `tests/ContentAutomatorX.UnitTests/ClaudeCliBackendArgsTests.cs`

**Interfaces:**
- Consumes: `LlmSettings`, `LlmEffort` (Task 1); `LlmSettingsService` (Task 2).
- Produces: `Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default)` on `ILlmBackend`. All consumers pass settings explicitly.

- [ ] **Step 1: Write the failing argument-composition tests**

Create `tests/ContentAutomatorX.UnitTests/ClaudeCliBackendArgsTests.cs`:

```csharp
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Llm;

namespace ContentAutomatorX.UnitTests;

/// <summary>Captures what the backend would launch, so the args string can be
/// asserted exactly without spawning a process.</summary>
public class RecordingRunner(string stdout = """{"result":"ok"}""") : IProcessRunner
{
    public string? LastArguments { get; private set; }
    public string? LastFileName { get; private set; }

    public Task<ProcessResult> RunAsync(string fileName, string arguments, string? stdin,
        TimeSpan timeout, CancellationToken ct = default)
    {
        LastFileName = fileName;
        LastArguments = arguments;
        return Task.FromResult(new ProcessResult(0, stdout, ""));
    }
}

public class ClaudeCliBackendArgsTests
{
    private static async Task<string> ArgsFor(LlmSettings settings, ClaudeCliOptions? options = null)
    {
        var runner = new RecordingRunner();
        var backend = new ClaudeCliBackend(runner, options ?? new ClaudeCliOptions());
        await backend.GenerateAsync("hello", settings);
        return runner.LastArguments!;
    }

    [Fact]
    public async Task Inherit_produces_exactly_todays_arguments() =>
        Assert.Equal("-p --output-format json", await ArgsFor(LlmSettings.Inherit));

    [Fact]
    public async Task Model_only_adds_the_model_flag() =>
        Assert.Equal("-p --output-format json --model sonnet",
            await ArgsFor(new LlmSettings("sonnet", LlmEffort.Default)));

    [Fact]
    public async Task Effort_only_adds_the_effort_flag() =>
        Assert.Equal("-p --output-format json --effort xhigh",
            await ArgsFor(new LlmSettings("", LlmEffort.XHigh)));

    [Fact]
    public async Task Both_set_adds_both_flags_model_first() =>
        Assert.Equal("-p --output-format json --model claude-opus-4-8 --effort max",
            await ArgsFor(new LlmSettings("claude-opus-4-8", LlmEffort.Max)));

    [Fact]
    public async Task ExtraArgs_is_appended_after_the_flags()
    {
        var options = new ClaudeCliOptions { ExtraArgs = "--allowedTools WebSearch" };
        Assert.Equal("-p --output-format json --model haiku --effort low --allowedTools WebSearch",
            await ArgsFor(new LlmSettings("haiku", LlmEffort.Low), options));
    }

    [Fact]
    public async Task Passed_settings_beat_the_appsettings_model()
    {
        // The service already applied the fallback chain; whatever arrives here wins.
        var options = new ClaudeCliOptions { Model = "opus" };
        Assert.Equal("-p --output-format json --model sonnet",
            await ArgsFor(new LlmSettings("sonnet", LlmEffort.Default), options));
    }

    [Theory]
    [InlineData(LlmEffort.Low, "low")]
    [InlineData(LlmEffort.Medium, "medium")]
    [InlineData(LlmEffort.High, "high")]
    [InlineData(LlmEffort.XHigh, "xhigh")]
    [InlineData(LlmEffort.Max, "max")]
    public async Task Every_effort_level_maps_to_the_CLI_vocabulary(LlmEffort effort, string expected) =>
        Assert.Contains($"--effort {expected}", await ArgsFor(new LlmSettings("", effort)));

    [Fact]
    public async Task Reports_the_model_the_CLI_actually_used()
    {
        var runner = new RecordingRunner("""{"result":"ok","modelUsage":{"claude-sonnet-5":{}}}""");
        var backend = new ClaudeCliBackend(runner, new ClaudeCliOptions());

        var result = await backend.GenerateAsync("hi", new LlmSettings("sonnet", LlmEffort.Default));

        Assert.Equal("claude-sonnet-5", result.Model);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter "FullyQualifiedName~ClaudeCliBackendArgsTests"`

Expected: build FAILS with `CS1501: No overload for method 'GenerateAsync' takes 2 arguments`.

- [ ] **Step 3: Change the ILlmBackend signature**

Replace the whole of `src/ContentAutomatorX.Domain/Abstractions/ILlmBackend.cs`:

```csharp
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Domain.Abstractions;

public interface ILlmBackend
{
    string Name { get; }

    /// <param name="settings">Which model and how hard it thinks, already resolved
    /// for the calling tenant. Required — deliberately NOT defaulted to null: a
    /// default would let a missed call site compile and silently run on another
    /// tenant's model, which is the exact bug per-tenant settings exist to prevent.</param>
    Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default);
}
```

- [ ] **Step 4: Add the Effort option and compose the flags in ClaudeCliBackend**

In `src/ContentAutomatorX.Infrastructure/Llm/ClaudeCliBackend.cs`, add to `ClaudeCliOptions` after the `Model` property (line 12):

```csharp
    /// <summary>Fallback reasoning depth for tenants that have not chosen one.
    /// Storage vocabulary: "", low, medium, high, xhigh, max. Configured via
    /// appsettings Claude:Effort.</summary>
    public string? Effort { get; set; }
```

Then replace the `GenerateAsync` method (lines 24-40) with:

```csharp
    public async Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings,
        CancellationToken ct = default)
    {
        var args = "-p --output-format json";
        if (!string.IsNullOrWhiteSpace(settings.Model)) args += $" --model {settings.Model}";
        if (EffortFlag(settings.Effort) is { } effort) args += $" --effort {effort}";
        if (!string.IsNullOrWhiteSpace(options.ExtraArgs)) args += $" {options.ExtraArgs}";
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        string lastError = "";
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var result = await runner.RunAsync(options.Command, args, prompt, timeout, ct);
            if (result.ExitCode == 0 && TryParse(result.StdOut, out var text, out var model))
                return new LlmResult(text, model ?? NonBlank(settings.Model) ?? "claude-default");
            lastError = $"exit={result.ExitCode} stderr={result.StdErr} stdout={Truncate(result.StdOut)}";
        }
        throw new InvalidOperationException($"claude CLI failed after 2 attempts: {lastError}");
    }

    /// <summary>Claude CLI's --effort vocabulary, verified against v2.1.207.
    /// Intentionally a separate switch from LlmSettings.ToStorage: that one is the
    /// persistence format, this one is one provider's argument vocabulary. They
    /// read identically today by coincidence — do not collapse them, or a future
    /// backend with a different vocabulary forces a database migration.</summary>
    private static string? EffortFlag(LlmEffort effort) => effort switch
    {
        LlmEffort.Low => "low",
        LlmEffort.Medium => "medium",
        LlmEffort.High => "high",
        LlmEffort.XHigh => "xhigh",
        LlmEffort.Max => "max",
        _ => null,
    };

    private static string? NonBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
```

Add to the usings at the top of the file (after line 3):

```csharp
using ContentAutomatorX.Domain.Models;
```

(`LlmResult` is already imported via `ContentAutomatorX.Domain.Models` — verify the using is present exactly once.)

- [ ] **Step 5: Run the backend tests to verify they pass**

Run: `dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter "FullyQualifiedName~ClaudeCliBackendArgsTests"`

Expected: the `ClaudeCliBackendArgsTests` cases PASS (12 passed). Other test files in the project may still fail to compile — that is expected until Step 7.

- [ ] **Step 6: Update the four Application/Infrastructure call sites**

**6a — `src/ContentAutomatorX.Application/Pipelines/GenerationPipeline.cs`**

Change the class declaration (line 12) to:

```csharp
public class GenerationPipeline(IAppDbContext db, ILlmBackend llm, IDraftDelivery delivery,
    ILlmSettingsProvider llmSettings)
```

After the `tenant` load (line 38), add:

```csharp
            var settings = await llmSettings.GetAsync(recipe.TenantId, ct);
```

Change the call (line 52) to:

```csharp
            try { result = await llm.GenerateAsync(prompt, settings, ct); }
```

**6b — `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`**

Change the class declaration (line 17) to:

```csharp
public class IssueComposerService(IAppDbContext db, ILlmBackend llm, PostService posts,
    ILlmSettingsProvider llmSettings)
```

In `GenerateTopicsAsync`, after the `prompt` is built (line 171), add:

```csharp
        var settings = await llmSettings.GetAsync(tenant.Id, ct);
```

and change the call (lines 176-177) to:

```csharp
            var reply = await llm.GenerateAsync(attempt == 1 ? prompt
                : prompt + "\nYour previous reply was not valid JSON. Respond with ONLY the JSON array.",
                settings, ct);
```

In `RegenerateSectionAsync`, after the `tenant` load (line 203), add:

```csharp
        var settings = await llmSettings.GetAsync(tenant.Id, ct);
```

and change the call (line 237) to:

```csharp
        var reply = await llm.GenerateAsync(prompt, settings, ct);
```

**6c — `src/ContentAutomatorX.Application/Services/PostService.cs`**

Change the class declaration (lines 14-15) to:

```csharp
public class PostService(IAppDbContext db, GenerationPipeline generation, ILlmBackend llm,
    PlatformService platforms, IMailerLiteClient mailerLite, ILlmSettingsProvider llmSettings)
```

In `SubjectIdeasAsync`, after the `excerpt` line (line 189), add:

```csharp
        var settings = await llmSettings.GetAsync(post.TenantId, ct);
```

and change the call (lines 199-200) to:

```csharp
            var reply = await llm.GenerateAsync(attempt == 1 ? prompt
                : prompt + "\nYour previous reply was not valid JSON. ONLY the JSON array.", settings, ct);
```

**6d — `src/ContentAutomatorX.Infrastructure/Sources/LlmResearchConnector.cs`**

Change the class declaration (line 12) to:

```csharp
public class LlmResearchConnector(ILlmBackend llm, ILlmSettingsProvider llmSettings) : ISourceConnector
```

In `FetchAsync`, after the `prompt` line (line 25), add:

```csharp
        var settings = await llmSettings.GetAsync(source.TenantId, ct);
```

and change both calls (lines 26 and 29) to:

```csharp
        var reply = await llm.GenerateAsync(prompt, settings, ct);
```

```csharp
            reply = await llm.GenerateAsync(BuildPrompt(config, retry: true), settings, ct);
```

- [ ] **Step 7: Update the four test fakes and their construction sites**

**7a — `tests/ContentAutomatorX.UnitTests/LlmResearchConnectorTests.cs`**

Add `using ContentAutomatorX.Domain.Models;` to the usings, then replace `QueueLlm` (lines 9-21) with:

```csharp
public class QueueLlm(params string[] replies) : ILlmBackend
{
    private readonly Queue<string> _replies = new(replies);
    public List<string> Prompts { get; } = [];
    public LlmSettings? LastSettings { get; private set; }
    public int Calls { get; private set; }
    public string Name => "fake";
    public Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default)
    {
        Calls++;
        Prompts.Add(prompt);
        LastSettings = settings;
        return Task.FromResult(new LlmResult(_replies.Dequeue(), "fake-model"));
    }
}

/// <summary>Returns the same settings for every tenant.</summary>
public class StubLlmSettings(LlmSettings? settings = null) : ILlmSettingsProvider
{
    private readonly LlmSettings _settings = settings ?? LlmSettings.Inherit;
    public Guid? LastTenantId { get; private set; }
    public Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        LastTenantId = tenantId;
        return Task.FromResult(_settings);
    }
    public Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default) =>
        Task.CompletedTask;
}
```

Then update every `new LlmResearchConnector(...)` in the file to pass a settings stub. The six sites become:

```csharp
        var connector = new LlmResearchConnector(new QueueLlm(GoodJson), new StubLlmSettings());
```
```csharp
        var connector = new LlmResearchConnector(new QueueLlm(fenced), new StubLlmSettings());
```
```csharp
        var connector = new LlmResearchConnector(llm, new StubLlmSettings());
```
```csharp
        var connector = new LlmResearchConnector(new QueueLlm(many), new StubLlmSettings());
```
```csharp
        var connector = new LlmResearchConnector(llm, new StubLlmSettings());
```
```csharp
        var connector = new LlmResearchConnector(llm, new StubLlmSettings());
```

Add one new test at the end of `LlmResearchConnectorTests` proving the source's tenant is the one asked about:

```csharp
    [Fact]
    public async Task Resolves_settings_for_the_sources_own_tenant()
    {
        var tenantId = Guid.NewGuid();
        var source = Research();
        source.TenantId = tenantId;
        var llm = new QueueLlm(GoodJson);
        var settings = new StubLlmSettings(new LlmSettings("haiku", LlmEffort.Low));

        await new LlmResearchConnector(llm, settings).FetchAsync(source);

        Assert.Equal(tenantId, settings.LastTenantId);
        Assert.Equal("haiku", llm.LastSettings!.Model);
        Assert.Equal(LlmEffort.Low, llm.LastSettings!.Effort);
    }
```

**7b — `tests/ContentAutomatorX.IntegrationTests/GenerationPipelineTests.cs`**

Add `using ContentAutomatorX.Domain.Models;` if absent, then replace `FakeLlm` and `FailingLlm` (lines 11-27) with:

```csharp
public class FakeLlm(string reply = "# Generated Title\nGenerated body.") : ILlmBackend
{
    public string Name => "fake";
    public string? LastPrompt { get; private set; }
    public LlmSettings? LastSettings { get; private set; }
    public Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default)
    {
        LastPrompt = prompt;
        LastSettings = settings;
        return Task.FromResult(new LlmResult(reply, "fake-model"));
    }
}

public class FailingLlm : ILlmBackend
{
    public string Name => "failing";
    public Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default) =>
        throw new InvalidOperationException("llm down");
}

/// <summary>Returns the same settings for every tenant.</summary>
public class StubLlmSettings(LlmSettings? settings = null) : ILlmSettingsProvider
{
    private readonly LlmSettings _settings = settings ?? LlmSettings.Inherit;
    public Guid? LastTenantId { get; private set; }
    public Task<LlmSettings> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        LastTenantId = tenantId;
        return Task.FromResult(_settings);
    }
    public Task SaveAsync(Guid tenantId, LlmSettings settings, CancellationToken ct = default) =>
        Task.CompletedTask;
}
```

Then update every `new GenerationPipeline(...)` in this file to append `, new StubLlmSettings()` as the fourth argument, and every `new PostService(...)` to append `, new StubLlmSettings()` as the sixth argument.

Add one test proving the pipeline asks for the recipe's tenant. Place it inside `GenerationPipelineTests`, adapting `Seed`/`TestDb` usage to match the sibling tests in the file:

```csharp
    [Fact]
    public async Task Resolves_llm_settings_for_the_recipes_tenant()
    {
        using var test = TestDb.Create();
        var (tenant, _, recipe) = Seed(test);
        var llm = new FakeLlm();
        var settings = new StubLlmSettings(new LlmSettings("sonnet", LlmEffort.High));
        var pipeline = new GenerationPipeline(test.Db, llm, new FakeDelivery(), settings);

        await pipeline.RunAsync(recipe.Id);

        Assert.Equal(tenant.Id, settings.LastTenantId);
        Assert.Equal("sonnet", llm.LastSettings!.Model);
    }
```

**7c — `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs`**

Add `using ContentAutomatorX.Domain.Models;` if absent, then replace `SequenceLlm` (lines 13-24) with:

```csharp
public class SequenceLlm(params string[] replies) : ILlmBackend
{
    private int _n;
    public string Name => "seq";
    public List<string> Prompts { get; } = [];
    public LlmSettings? LastSettings { get; private set; }
    public Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default)
    {
        Prompts.Add(prompt);
        LastSettings = settings;
        var reply = replies[Math.Min(_n++, replies.Length - 1)];
        return Task.FromResult(new LlmResult(reply, "seq-model"));
    }
}
```

Update the `Composer` helper (lines 105-108) to:

```csharp
    private static IssueComposerService Composer(World w, ILlmBackend llm) =>
        new(w.Test.Db, llm,
            new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery(), new StubLlmSettings()),
                llm, w.Platforms, w.MailerLite, new StubLlmSettings()),
            new StubLlmSettings());
```

(`StubLlmSettings` comes from `GenerationPipelineTests.cs` — same assembly and namespace, so no extra using is needed.)

**7d — `tests/ContentAutomatorX.IntegrationTests/McpToolsTests.cs`**

Update both `new GenerationPipeline(test.Db, new FakeLlm(), new FakeDelivery())` occurrences (lines 90 and 291) to append `, new StubLlmSettings()`, and both `new PostService(test.Db, generation, new FakeLlm(), platforms, ml)` occurrences (lines 91 and 292) to append `, new StubLlmSettings()`.

- [ ] **Step 8: Wire up DI and appsettings**

In `src/ContentAutomatorX.Web/Program.cs`, replace the LLM backend block (lines 51-57) with:

```csharp
// --- LLM backend ---
var claudeOptions = new ClaudeCliOptions();
builder.Configuration.GetSection("Claude").Bind(claudeOptions);
if (string.IsNullOrWhiteSpace(claudeOptions.Model)) claudeOptions.Model = null;
builder.Services.AddSingleton(claudeOptions);
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<ILlmBackend, ClaudeCliBackend>();

// appsettings values become the fallback for tenants that have not chosen.
// Registered as a plain LlmSettings so Application never sees ClaudeCliOptions.
builder.Services.AddSingleton(LlmSettings.From(claudeOptions.Model, claudeOptions.Effort));
// Scoped, not singleton: every consumer (IssueComposerService, PostService,
// GenerationPipeline, LlmResearchConnector) is scoped or transient, so it can
// take IAppDbContext directly — no IServiceScopeFactory dance needed.
builder.Services.AddScoped<ILlmSettingsProvider, LlmSettingsService>();
```

Add to the usings at the top of `Program.cs`:

```csharp
using ContentAutomatorX.Domain.Models;
```

In `src/ContentAutomatorX.Web/appsettings.json`, add `"Effort": ""` to the `Claude` section so the option is discoverable:

```json
  "Claude": {
    "Command": "claude",
    "Model": "",
    "Effort": "",
    "TimeoutSeconds": 300,
    "ExtraArgs": ""
  }
```

- [ ] **Step 9: Run both test suites**

Stop the running `ContentAutomatorX.Web` app first, then run:

```bash
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj
dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj
```

Expected: both PASS, 0 failed. If you see MSB3021/MSB3027 file-lock errors, the app or Visual Studio still holds the DLLs — close them and re-run; it is not a code failure.

- [ ] **Step 10: Verify the app still starts**

Run: `dotnet build src/ContentAutomatorX.Web/ContentAutomatorX.Web.csproj`

Expected: `Build succeeded`, 0 errors. A DI misregistration will not surface here — it surfaces at first request — so also start the app (`dotnet run --project src/ContentAutomatorX.Web`), load any page, and confirm no `InvalidOperationException: Unable to resolve service` appears in the console. Stop the app afterwards.

- [ ] **Step 11: Commit**

```bash
git add src/ContentAutomatorX.Domain/Abstractions/ILlmBackend.cs \
        src/ContentAutomatorX.Infrastructure/Llm/ClaudeCliBackend.cs \
        src/ContentAutomatorX.Infrastructure/Sources/LlmResearchConnector.cs \
        src/ContentAutomatorX.Application/Services/IssueComposerService.cs \
        src/ContentAutomatorX.Application/Services/PostService.cs \
        src/ContentAutomatorX.Application/Pipelines/GenerationPipeline.cs \
        src/ContentAutomatorX.Web/Program.cs \
        src/ContentAutomatorX.Web/appsettings.json \
        tests/
git commit -m "feat: pass per-tenant LlmSettings into every LLM call (#aistudio)"
```

---

### Task 4: AI Studio settings card

The only user-facing task. Verification is a build plus a scripted manual pass — this codebase has no Blazor component test harness, and adding one for a single card is not worth it. Say so plainly in the task report rather than implying automated coverage exists.

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/AiStudio.razor`

**Interfaces:**
- Consumes: `ILlmSettingsProvider`, `LlmSettings`, `LlmEffort`, `LlmModelName` (Tasks 1-2); `TenantContext` (existing, at `src/ContentAutomatorX.Web/Services/TenantContext.cs`).
- Produces: nothing consumed elsewhere.

- [ ] **Step 1: Add the settings card to AiStudio.razor**

Replace the top of `src/ContentAutomatorX.Web/Components/Pages/AiStudio.razor` — everything from line 1 through the `<ComingSoonBanner ... />` element — with the following, leaving the existing `<MudGrid>` mockup tables and trailing caption untouched below it:

```razor
@page "/ai-studio"
@using ContentAutomatorX.Domain.Abstractions
@using ContentAutomatorX.Domain.Models
@using ContentAutomatorX.Web.Services
@implements IDisposable
@inject TenantContext Ctx
@inject ILlmSettingsProvider LlmSettings
@inject ISnackbar Snackbar

<MudText Typo="Typo.h4" Class="mb-4">AI Studio</MudText>

@if (!Ctx.Initialized)
{
    <MudProgressLinear Indeterminate="true" Class="mb-4" />
}
else if (Ctx.Active is null)
{
    <MudAlert Severity="Severity.Info" Class="mb-4">Create a tenant first — model settings are per tenant.</MudAlert>
}
else
{
    <MudPaper Class="pa-4 mb-6" Outlined="true">
        <MudText Typo="Typo.subtitle1" Class="mb-1">Model — @Ctx.Active.Name</MudText>
        <MudText Typo="Typo.caption" Class="mb-4 d-block">
            Applies to every ✨ action for this tenant: topic blurbs, subject ideas,
            regenerate, and LLM research sources. Other tenants keep their own settings.
        </MudText>

        <MudGrid>
            <MudItem xs="12" md="4">
                <MudTextField T="string" Value="@("Claude CLI")" Label="Provider" ReadOnly="true" Disabled="true"
                              Variant="Variant.Outlined" HelperText="Only provider today" />
            </MudItem>
            <MudItem xs="12" md="4">
                <MudSelect T="string" @bind-Value="_model" Label="Model" Variant="Variant.Outlined">
                    <MudSelectItem T="string" Value="@("")">Default (CLI decides)</MudSelectItem>
                    <MudSelectItem T="string" Value="@("opus")">Opus</MudSelectItem>
                    <MudSelectItem T="string" Value="@("sonnet")">Sonnet</MudSelectItem>
                    <MudSelectItem T="string" Value="@("haiku")">Haiku</MudSelectItem>
                    <MudSelectItem T="string" Value="@("fable")">Fable</MudSelectItem>
                    <MudSelectItem T="string" Value="@CustomOption">Custom…</MudSelectItem>
                </MudSelect>
            </MudItem>
            <MudItem xs="12" md="4">
                <MudSelect T="LlmEffort" @bind-Value="_effort" Label="Effort" Variant="Variant.Outlined">
                    <MudSelectItem T="LlmEffort" Value="LlmEffort.Default">Default (CLI decides)</MudSelectItem>
                    <MudSelectItem T="LlmEffort" Value="LlmEffort.Low">low</MudSelectItem>
                    <MudSelectItem T="LlmEffort" Value="LlmEffort.Medium">medium</MudSelectItem>
                    <MudSelectItem T="LlmEffort" Value="LlmEffort.High">high</MudSelectItem>
                    <MudSelectItem T="LlmEffort" Value="LlmEffort.XHigh">xhigh</MudSelectItem>
                    <MudSelectItem T="LlmEffort" Value="LlmEffort.Max">max</MudSelectItem>
                </MudSelect>
            </MudItem>
            @if (_model == CustomOption)
            {
                <MudItem xs="12" md="8">
                    <MudTextField @bind-Value="_custom" Label="Custom model ID" Variant="Variant.Outlined"
                                  Placeholder="claude-opus-4-8" Error="@(_error is not null)"
                                  ErrorText="@_error" Immediate="true" />
                </MudItem>
            }
        </MudGrid>

        <MudButton Class="mt-4" Variant="Variant.Filled" Color="Color.Primary"
                   Disabled="_saving" OnClick="SaveAsync">Save</MudButton>
    </MudPaper>
}

<ComingSoonBanner Phase="provider profiles land with the newsletter vertical; the full job-binding table grows with each phase"
                  Description="Provider profiles (Claude CLI, LM Studio, Ollama, hosted APIs, MCP servers, TTS — local solutions are first-class) get bound to named jobs like newsletter-compose or yt-description. Every ✨ button in the app runs a job; swapping the model behind it is editing one row here. Manual always works — AI only ever proposes text into ordinary fields." />
```

- [ ] **Step 2: Add the code block**

Append to the end of `src/ContentAutomatorX.Web/Components/Pages/AiStudio.razor`:

```razor
@code {
    // Sentinel for the "Custom…" dropdown entry. Contains a space, so
    // LlmModelName.IsValid rejects it — it can never be saved as a real model.
    private const string CustomOption = "custom model";

    private string _model = "";
    private string _custom = "";
    private LlmEffort _effort = LlmEffort.Default;
    private string? _error;
    private bool _saving;

    protected override async Task OnInitializedAsync()
    {
        Ctx.Changed += OnTenantChanged;
        await Ctx.InitializeAsync();
        await LoadAsync();
    }

    private void OnTenantChanged() => _ = InvokeAsync(async () =>
    {
        await LoadAsync();
        StateHasChanged();
    });

    private async Task LoadAsync()
    {
        if (!Ctx.Initialized || Ctx.Active is null) return;
        var settings = await LlmSettings.GetAsync(Ctx.Active.Id);
        _effort = settings.Effort;
        _error = null;
        if (settings.Model is "" or "opus" or "sonnet" or "haiku" or "fable")
        {
            _model = settings.Model;
            _custom = "";
        }
        else
        {
            _model = CustomOption;
            _custom = settings.Model;
        }
    }

    private async Task SaveAsync()
    {
        if (Ctx.Active is null) return;
        var model = _model == CustomOption ? _custom.Trim() : _model;

        // Mirror of the service's rule so the error lands on the field. The service
        // validates again on its own — this is convenience, not the enforcement point.
        if (model.Length > 0 && !LlmModelName.IsValid(model))
        {
            _error = $"Letters, digits, dot, underscore, hyphen or square brackets only, "
                   + $"up to {LlmModelName.MaxLength} characters.";
            return;
        }

        _error = null;
        _saving = true;
        try
        {
            await LlmSettings.SaveAsync(Ctx.Active.Id, new LlmSettings(model, _effort));
            Snackbar.Add($"Model settings saved for {Ctx.Active.Name}.", Severity.Success);
        }
        catch (ArgumentException ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose() => Ctx.Changed -= OnTenantChanged;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ContentAutomatorX.Web/ContentAutomatorX.Web.csproj`

Expected: `Build succeeded`, 0 errors, 0 warnings introduced by this file.

- [ ] **Step 4: Manual verification**

Start the app (`dotnet run --project src/ContentAutomatorX.Web`), then walk this exact list and record the result of each:

1. Open `/ai-studio`. The card shows "Model — <active tenant name>", Provider disabled and reading "Claude CLI", Model = "Default (CLI decides)", Effort = "Default (CLI decides)".
2. Set Model = Sonnet, Effort = low, press Save. A success snackbar naming the tenant appears.
3. Reload the page. Sonnet and low are still selected.
4. Open the issue composer and press Generate ✨ on a post with topic skeletons. The run completes without error.
5. Back on `/ai-studio`, choose Model = Custom…, type `opus --dangerously-skip-permissions`, press Save. An inline field error appears and nothing is saved (reload to confirm the previous value survives).
6. Type `claude-opus-4-8[1m]` in the same custom field and Save. It succeeds; reload shows Custom… selected with that value.
7. Switch tenants with the tenant switcher. The card header changes to the new tenant's name and its (unset) values load — Sonnet does **not** leak across.
8. Switch back. The first tenant's values return.

Stop the app when finished.

- [ ] **Step 5: Commit**

```bash
git add src/ContentAutomatorX.Web/Components/Pages/AiStudio.razor
git commit -m "feat: per-tenant model + effort card in AI Studio (#aistudio)"
```

---

## Plan Self-Review

Checked against `docs/superpowers/specs/2026-07-20-llm-model-selector-design.md`:

**Spec coverage** — every §4 "In (v1)" item maps to a task: `TenantLlmSetting` table → T2; `LlmSettings`/`LlmEffort` → T1; `ILlmSettingsProvider`/`LlmSettingsService` → T1/T2; signature change → T3; `ClaudeCliBackend` flags → T3; six call sites → T3; AI Studio card → T4; validation → T1 (rule) + T2 (service enforcement) + T4 (inline surfacing). Every §8 error-handling row has a test or an explicit manual step, except "DB read fails mid-generation" — see below.

**Deviations from the spec, deliberate:**

1. **Entity renamed** `LlmSetting` → `TenantLlmSetting`, so it does not read as a typo of the `LlmSettings` value record. The spec has been updated to match.
2. **`LlmSettingsService` is scoped, not a singleton with `IServiceScopeFactory`** (spec §2 decision 8). That decision existed because a *singleton* `ClaudeCliBackend` was going to read settings itself. Moving resolution to the callers removed that constraint — every consumer is scoped or transient — so the scope-factory indirection is now dead weight. The spec has been updated.
3. **A failed DB read propagates** rather than falling back to `appsettings`. The spec originally said to log and fall back; that was reversed while planning, because swallowing the exception would also swallow a genuinely broken database and run every generation on silently-wrong settings — harder to diagnose than a failed run, and the same read already happens on every page load, so a broken DB is not subtle. Spec §8 has been updated. If the reviewer disagrees, the fix is a `try`/`catch` returning `fallback` in `LlmSettingsService.GetAsync` — one method.

4. **Per-field fallback.** The spec originally said a tenant's row falls back when "absent or blank"; Model and Effort now resolve independently, so pinning a model without an effort still inherits the configured effort. Covered by `A_row_with_a_blank_model_still_falls_back_for_the_model_only`. Spec §6.2 has been updated.

**Placeholder scan:** no TBD/TODO; every code step carries complete code; every test step carries a runnable command and an expected result.

**Type consistency:** `GenerateAsync(string, LlmSettings, CancellationToken)` is used identically in T3's interface, backend, four call sites, and four fakes. `ILlmSettingsProvider.GetAsync(Guid, CancellationToken)` / `SaveAsync(Guid, LlmSettings, CancellationToken)` match across T1's interface, T2's service, T3's stubs, and T4's page. `LlmSettings.From` / `ToStorage` / `ParseEffort` / `Inherit` and `LlmModelName.IsValid` / `MaxLength` are defined in T1 and used with those exact names in T2, T3, and T4.
