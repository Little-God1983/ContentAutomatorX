# Inbox Source-Type Icon — Design

**Date:** 2026-07-19
**Status:** Approved

## Problem

The Inbox (`Content.razor`) shows which source an item came from by name only. The
app already has a per-type icon vocabulary (Recipes page: Reddit brand icon, RSS
feed, Language/globe, AutoAwesome), but the Inbox doesn't use it, so scanning the
list for "all the Reddit items" means reading names.

## Solution Overview

Show the existing source-type icon in the Inbox's **Source** column, before the
source name, with a tooltip naming the type. No new column. Extract the icon
mapping (currently a private helper in `Recipes.razor`) into a shared helper so
both pages use one definition.

## Components

### 1. `SourceTypeDisplay` (new, `src/ContentAutomatorX.Web/SourceTypeDisplay.cs`)

Static class, two methods:

- `string Icon(string type)` — the mapping now in `Recipes.razor:217-224`, moved
  verbatim: Reddit → `Icons.Custom.Brands.Reddit`, Rss → `Icons.Material.Filled.RssFeed`,
  Website → `Icons.Material.Filled.Language`, LlmResearch → `Icons.Material.Filled.AutoAwesome`,
  fallback → `Icons.Material.Filled.Source`.
- `string Label(string type)` — friendly names matching the filter dropdowns:
  "Reddit", "RSS/Atom feed", "Website", "LLM research"; fallback: the raw type string.

### 2. `Recipes.razor`

Delete the private `SourceIcon` helper; call `SourceTypeDisplay.Icon(...)` instead.
No visual change.

### 3. `Content.razor` (Inbox)

In the Source `<MudTd>`, render before the name:
`<MudTooltip Text="@SourceTypeDisplay.Label(type)"><MudIcon Icon="@SourceTypeDisplay.Icon(type)" Size="Size.Small" /></MudTooltip>`
followed by the existing source name, aligned in a flex row with a small gap.
The type comes from the page's existing `_sourcesById` lookup. If the source is
unknown (deleted), use the fallback icon and keep the existing "?" name.

## Error handling

Unknown/deleted source: fallback icon (`Icons.Material.Filled.Source`) + "?" name —
same degradation as today's name-only rendering.

## Testing

Pure markup plus a static mapping; the test projects don't render Razor components.
Verification is the project's UI walkthrough (`/verify` skill): Inbox rows show the
correct icon per source type, tooltip shows the friendly type name, Recipes page
icons unchanged.

## Out of scope

- No new table column; no changes to filters, sorting, or selection.
- No icon changes elsewhere (Sources page stays as-is).
