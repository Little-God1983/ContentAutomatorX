# Source-Type Display Follow-Ups (Issues #13, #14) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the empty tooltip for deleted/unknown sources (#13) and make `SourceTypeDisplay` the single source of truth the three source-type dropdowns render from, with icons (#14).

**Architecture:** Extend `SourceTypeDisplay` with an "Unknown source" label fallback for empty type strings, an ordered `All` list, and a nullable `Hint(type)` for the longer create-flow wording. The Inbox filter, Sources create form, and QuickSourceDialog dropdowns iterate `All` and render icon + label (+ hint where applicable) instead of hardcoding items.

**Tech Stack:** .NET 10 Blazor Server, MudBlazor. Branch: `feature/inbox-source-type-icon` (append to open PR #12). Issues: #13, #14.

## Global Constraints

- #13: `Label("")` (null/empty type) must return `"Unknown source"`; unmapped-but-present type strings still return the raw type string. Mapped labels unchanged: "Reddit", "RSS/Atom feed", "Website", "LLM research".
- #14: dropdown order preserved: Reddit, Rss, Website, LlmResearch. Hints: Website → "page watch", LlmResearch → "AI web sweep", others → none. Rendered as `Label (hint)`.
- Decision (recorded per #14): QuickSourceDialog is harmonized to show the same hints as the Sources page (its LLM item gains "(AI web sweep)"); everything else keeps its current visible text.
- The Inbox filter keeps its leading "All" item (`Value="all"`) exactly as-is.
- Dropdown items gain the type icon (`Size.Small`, `Class="mr-2"`, vertically centered) matching the Inbox/Recipes visual language.
- Test projects cannot render Razor components — verification is `dotnet build` + full suite green + UI walkthrough; no new unit tests.

---

### Task 1: #13 — "Unknown source" tooltip fallback

**Files:**
- Modify: `src/ContentAutomatorX.Web/SourceTypeDisplay.cs` (the `Label` method's fallback arm)

**Interfaces:**
- Consumes: existing `SourceTypeDisplay.Label(string type)`.
- Produces: `Label("")` and `Label(null!)`-safe behavior → `"Unknown source"`; raw string for other unmapped types. Task 2 keeps using `Label` unchanged.

- [ ] **Step 1: Apply the change**

In `src/ContentAutomatorX.Web/SourceTypeDisplay.cs`, change the `Label` fallback arm from:

```csharp
        _ => type
```

to:

```csharp
        _ => string.IsNullOrEmpty(type) ? "Unknown source" : type
```

- [ ] **Step 2: Build and run the full suite**

Run: `dotnet build src/ContentAutomatorX.Web && dotnet test`
Expected: build clean, all tests pass (no behavior outside this static method changed).

- [ ] **Step 3: Commit**

```bash
git add src/ContentAutomatorX.Web/SourceTypeDisplay.cs
git commit -m "fix: show 'Unknown source' tooltip for deleted sources (#13)"
```

---

### Task 2: #14 — dropdowns render from SourceTypeDisplay

**Files:**
- Modify: `src/ContentAutomatorX.Web/SourceTypeDisplay.cs` (add `All`, `Hint`)
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Content.razor:31-37` (Source type filter)
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Sources.razor:27-32` (create-source type select)
- Modify: `src/ContentAutomatorX.Web/Components/Shared/QuickSourceDialog.razor:6-11` (dialog type select)

**Interfaces:**
- Consumes: `SourceTypeDisplay.Icon(string)`, `SourceTypeDisplay.Label(string)` (Task 1 state).
- Produces: `public static readonly IReadOnlyList<string> All` (order: Reddit, Rss, Website, LlmResearch) and `public static string? Hint(string type)` (Website → "page watch", LlmResearch → "AI web sweep", else null).

- [ ] **Step 1: Extend the helper**

In `src/ContentAutomatorX.Web/SourceTypeDisplay.cs`, add inside the class:

```csharp
    public static readonly IReadOnlyList<string> All =
        [SourceTypes.Reddit, SourceTypes.Rss, SourceTypes.Website, SourceTypes.LlmResearch];

    /// <summary>Extra wording for create flows, e.g. "Website (page watch)". Null when the label stands alone.</summary>
    public static string? Hint(string type) => type switch
    {
        SourceTypes.Website => "page watch",
        SourceTypes.LlmResearch => "AI web sweep",
        _ => null
    };
```

- [ ] **Step 2: Inbox filter iterates All**

In `src/ContentAutomatorX.Web/Components/Pages/Content.razor`, replace:

```razor
        <MudSelect T="string" Value="_typeFilter" ValueChanged="OnTypeFilterChanged" Label="Source type" Style="min-width:160px">
            <MudSelectItem T="string" Value="@("all")">All</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Reddit">Reddit</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Rss">RSS/Atom feed</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Website">Website</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.LlmResearch">LLM research</MudSelectItem>
        </MudSelect>
```

with:

```razor
        <MudSelect T="string" Value="_typeFilter" ValueChanged="OnTypeFilterChanged" Label="Source type" Style="min-width:160px">
            <MudSelectItem T="string" Value="@("all")">All</MudSelectItem>
            @foreach (var t in SourceTypeDisplay.All)
            {
                <MudSelectItem T="string" Value="@t">
                    <div class="d-flex align-center" style="gap:8px">
                        <MudIcon Icon="@SourceTypeDisplay.Icon(t)" Size="Size.Small" />@SourceTypeDisplay.Label(t)
                    </div>
                </MudSelectItem>
            }
        </MudSelect>
```

(The filter shows the plain label — no hint — as today.)

- [ ] **Step 3: Sources page select iterates All with hints**

In `src/ContentAutomatorX.Web/Components/Pages/Sources.razor`, replace:

```razor
        <MudSelect T="string" @bind-Value="_type" Label="Type">
            <MudSelectItem T="string" Value="@SourceTypes.Reddit">Reddit</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Rss">RSS/Atom feed</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Website">Website (page watch)</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.LlmResearch">LLM research (AI web sweep)</MudSelectItem>
        </MudSelect>
```

with:

```razor
        <MudSelect T="string" @bind-Value="_type" Label="Type">
            @foreach (var t in SourceTypeDisplay.All)
            {
                <MudSelectItem T="string" Value="@t">
                    <div class="d-flex align-center" style="gap:8px">
                        <MudIcon Icon="@SourceTypeDisplay.Icon(t)" Size="Size.Small" />@SourceTypeDisplay.Label(t)@(SourceTypeDisplay.Hint(t) is { } h ? $" ({h})" : "")
                    </div>
                </MudSelectItem>
            }
        </MudSelect>
```

- [ ] **Step 4: QuickSourceDialog select — same markup as Step 3**

In `src/ContentAutomatorX.Web/Components/Shared/QuickSourceDialog.razor`, replace:

```razor
        <MudSelect T="string" @bind-Value="_type" Label="Type">
            <MudSelectItem T="string" Value="@SourceTypes.Reddit">Reddit</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Rss">RSS/Atom feed</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.Website">Website (page watch)</MudSelectItem>
            <MudSelectItem T="string" Value="@SourceTypes.LlmResearch">LLM research</MudSelectItem>
        </MudSelect>
```

with the identical `@foreach` block from Step 3 (this intentionally changes the LLM item's visible text from "LLM research" to "LLM research (AI web sweep)" — the recorded harmonization decision).

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build src/ContentAutomatorX.Web && dotnet test`
Expected: build clean, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/ContentAutomatorX.Web/SourceTypeDisplay.cs src/ContentAutomatorX.Web/Components/Pages/Content.razor src/ContentAutomatorX.Web/Components/Pages/Sources.razor src/ContentAutomatorX.Web/Components/Shared/QuickSourceDialog.razor
git commit -m "refactor: render source-type dropdowns from SourceTypeDisplay (#14)"
```

- [ ] **Step 7: UI verification (controller)**

Publish + headless walkthrough: Inbox filter dropdown lists All/Reddit/RSS/Website/LLM with icons; Sources page select shows hints; deleted-source tooltip shows "Unknown source"; selecting a type filter still filters.
