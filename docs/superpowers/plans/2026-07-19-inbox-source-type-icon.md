# Inbox Source-Type Icon Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the per-type source icon (same icons as the Recipes/automation source picker) next to the source name in the Inbox table.

**Architecture:** Extract the icon mapping that lives as a private helper in `Recipes.razor` into a shared static class `SourceTypeDisplay` (icon + friendly label), switch Recipes to it, and render icon-with-tooltip before the source name in the Inbox's Source column.

**Tech Stack:** .NET 10 Blazor Server, MudBlazor. Spec: `docs/superpowers/specs/2026-07-19-inbox-source-type-icon-design.md`.

## Global Constraints

- Icon mapping copied verbatim from `Recipes.razor:217-224`: Reddit → `Icons.Custom.Brands.Reddit`, Rss → `Icons.Material.Filled.RssFeed`, Website → `Icons.Material.Filled.Language`, LlmResearch → `Icons.Material.Filled.AutoAwesome`, fallback → `Icons.Material.Filled.Source`.
- Labels match the existing filter dropdowns: "Reddit", "RSS/Atom feed", "Website", "LLM research"; fallback: the raw type string.
- No new table column; no changes to filters, sorting, or selection. Recipes page must look unchanged.
- Unknown/deleted source degrades to fallback icon + existing "?" name.
- The test projects cannot render Razor components — verification is `dotnet build` + full suite green + UI walkthrough; no new unit tests.

---

### Task 1: Shared SourceTypeDisplay + Inbox icon

**Files:**
- Create: `src/ContentAutomatorX.Web/SourceTypeDisplay.cs`
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor:40` and `:217-224`
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Content.razor:82` (Source cell) and the `@code` helpers around `:203`

**Interfaces:**
- Consumes: `SourceTypes` constants (`ContentAutomatorX.Domain`), `Icons` (MudBlazor), `_sourcesById` dictionary already on the Content page.
- Produces: `static string SourceTypeDisplay.Icon(string type)` and `static string SourceTypeDisplay.Label(string type)` in namespace `ContentAutomatorX.Web`.

- [ ] **Step 1: Set up the branch**

```bash
git checkout main && git pull
git checkout -b feature/inbox-source-type-icon
git cherry-pick 9f929ba   # design spec commit from the old feature branch
```

Expected: clean cherry-pick of "docs: design spec for Inbox source-type icon".

- [ ] **Step 2: Create the shared helper**

Create `src/ContentAutomatorX.Web/SourceTypeDisplay.cs`:

```csharp
using ContentAutomatorX.Domain;
using MudBlazor;

namespace ContentAutomatorX.Web;

/// <summary>
/// Single home for how a source type is shown in the UI (icon + friendly name),
/// shared by the Recipes source picker and the Inbox table.
/// </summary>
public static class SourceTypeDisplay
{
    public static string Icon(string type) => type switch
    {
        SourceTypes.Reddit => Icons.Custom.Brands.Reddit,
        SourceTypes.Rss => Icons.Material.Filled.RssFeed,
        SourceTypes.Website => Icons.Material.Filled.Language,
        SourceTypes.LlmResearch => Icons.Material.Filled.AutoAwesome,
        _ => Icons.Material.Filled.Source
    };

    public static string Label(string type) => type switch
    {
        SourceTypes.Reddit => "Reddit",
        SourceTypes.Rss => "RSS/Atom feed",
        SourceTypes.Website => "Website",
        SourceTypes.LlmResearch => "LLM research",
        _ => type
    };
}
```

- [ ] **Step 3: Switch Recipes.razor to the shared helper**

In `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor`:

1. Line 40: change

```razor
                        <MudIcon Icon="@SourceIcon(s.Type)" Size="Size.Small" />
```

to

```razor
                        <MudIcon Icon="@SourceTypeDisplay.Icon(s.Type)" Size="Size.Small" />
```

2. Delete the now-unused private helper (lines 217-224):

```csharp
    private static string SourceIcon(string type) => type switch
    {
        SourceTypes.Reddit => Icons.Custom.Brands.Reddit,
        SourceTypes.Rss => Icons.Material.Filled.RssFeed,
        SourceTypes.Website => Icons.Material.Filled.Language,
        SourceTypes.LlmResearch => Icons.Material.Filled.AutoAwesome,
        _ => Icons.Material.Filled.Source
    };
```

If `SourceTypeDisplay` doesn't resolve in the .razor file, add `@using ContentAutomatorX.Web` to `src/ContentAutomatorX.Web/Components/_Imports.razor` (check first — it is likely already there as the root namespace).

- [ ] **Step 4: Render the icon in the Inbox's Source cell**

In `src/ContentAutomatorX.Web/Components/Pages/Content.razor`, replace the Source cell (line 82):

```razor
            <MudTd>@(SourceNameOf(context.SourceId))</MudTd>
```

with:

```razor
            <MudTd>
                <div class="d-flex align-center" style="gap:6px">
                    <MudTooltip Text="@SourceTypeDisplay.Label(TypeOf(context.SourceId))">
                        <MudIcon Icon="@SourceTypeDisplay.Icon(TypeOf(context.SourceId))" Size="Size.Small" />
                    </MudTooltip>
                    @(SourceNameOf(context.SourceId))
                </div>
            </MudTd>
```

And in the `@code` block, next to `SourceNameOf` (around line 203), add:

```csharp
    private string TypeOf(Guid sourceId) => _sourcesById.GetValueOrDefault(sourceId)?.Type ?? "";
```

(For an unknown source this yields `""` → fallback icon `Icons.Material.Filled.Source` and label `""`, alongside the existing "?" name — matching the spec's degradation rule.)

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build src/ContentAutomatorX.Web && dotnet test`
Expected: build succeeds with 0 warnings introduced; all tests pass (no behavior changed — this is markup + a static class).

- [ ] **Step 6: Commit**

```bash
git add src/ContentAutomatorX.Web/SourceTypeDisplay.cs src/ContentAutomatorX.Web/Components/Pages/Recipes.razor src/ContentAutomatorX.Web/Components/Pages/Content.razor
git commit -m "feat: show source-type icon in Inbox source column"
```

(Include `src/ContentAutomatorX.Web/Components/_Imports.razor` in the `git add` if Step 3's fallback was needed.)

- [ ] **Step 7: UI verification**

Launch the app (controller runs the project's `/verify` walkthrough) and confirm:
- Inbox rows show the type icon before the source name (Reddit brand icon for Reddit sources, RSS icon for feeds).
- Hovering the icon shows the friendly type name.
- Recipes page source picker looks unchanged.
