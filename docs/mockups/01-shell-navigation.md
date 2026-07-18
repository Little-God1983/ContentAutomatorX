# 01 — App Shell & Navigation

The shell is the part that must survive 5 years unchanged. It stays MudBlazor
(app bar + drawer), extends the current layout rather than replacing it.

## Full shell

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ ☰  ContentAutomatorX   [ 🔍 Search everything…        ]   [+ New ▾]  [AIVisions ▾] │
├───────────────┬──────────────────────────────────────────────────────────────┤
│ PLAN          │                                                              │
│  ◉ Today    3 │                                                              │
│  📅 Calendar  │                                                              │
│               │                                                              │
│ WORK          │                                                              │
│  📥 Inbox  12 │                      (page body)                             │
│  🗂 Projects 4│                                                              │
│  📤 Posts   6 │                                                              │
│               │                                                              │
│ ARCHIVE       │                                                              │
│  🏛 Library   │                                                              │
│  📊 Analytics │                                                              │
│               │                                                              │
│ SYSTEM        │                                                              │
│  📡 Sources   │                                                              │
│  ⚙ Automations│                                                              │
│  🔌 Platforms │                                                              │
│  🤖 AI Studio │                                                              │
│  🧾 Runs      │                                                              │
└───────────────┴──────────────────────────────────────────────────────────────┘
```

Badge numbers = items needing attention (Today: manual steps waiting; Inbox:
untriaged; Posts: open). They are *attention* counts, not totals — totals live
in Library/Analytics.

## `+ New ▾` — the "do a post" button

One global entry point for everything you start, from anywhere in the app:

```
┌───────────────────────────────┐
│ 🖼  Image post…               │  → quick wizard (06) — project auto-created
│ 🎬  Video project…            │  → project workspace, video template (07)
│ ✉️  Newsletter issue…         │  → issue composer (08)
│ 🧡  Patreon post…             │  → standalone Patreon post + kit (09)
│ 📣  YT community post…        │  → text/image post (no public API — assisted)
│ ───────────────────────────── │
│ 📁  Blank project…            │  → empty container, decide posts later
└───────────────────────────────┘
```

Design rule: the menu lists **work shapes, not platforms**. "Image post" then
asks *which* image platforms (Civitai, next site, Patreon gallery…) inside the
wizard — so a new image platform never adds a menu entry here.

## Global search (the 2031 escape hatch)

```
┌ 🔍 "samurai" ────────────────────────────────────────────────┐
│ PROJECTS                                                     │
│   🗂 Neon Samurai Set (8 images)              Jul 2026       │
│ POSTS                                                        │
│   ● civitai  Neon Samurai Set                Published Jul 14│
│   ● patreon  Samurai pack early access       Published Jul 12│
│ LIBRARY (2024–2026)                                          │
│   ● youtube  Samurai LoRA training guide     Published Nov 2024 · 12.4k views │
│ INBOX                                                        │
│   📥 r/StableDiffusion: "New samurai LoRA…"  New             │
└──────────────────────────────────────────────────────────────┘
```

Searches titles, tags, descriptions/bodies, and platform names across all years.
Grouped by entity type; Library results show publish date + headline stat.

## Behavior notes

- **Tenant switcher** stays top-right exactly as today; every page below is
  tenant-scoped (existing `TenantContext` pattern).
- Sidebar sections are fixed. New platforms **never** add nav
  entries — they appear as chips, colors, and rows in Platforms.
- Section order = frequency of use: plan today → do work → look things up →
  configure machinery. SYSTEM is collapsed by default once configured.
- Current pages map: Dashboard→Today, Content→Inbox, Drafts→merged into Posts,
  Recipes→Automations (rename), Sources/Runs unchanged.

## Open questions

1. Sidebar badge counts: useful or noise? (They can be turned off per section.)
2. Should SYSTEM live behind a single "Settings" entry instead of 5 items?
   I kept 5 because Sources/Automations/Runs are used weekly, not set-and-forget.
3. Keyboard palette (`Ctrl+K` → search + actions like "new image post"): worth
   it early? Cheap with MudBlazor autocomplete, big daily-driver win.
