# 09 — Patreon Flow (the ✋ prepare-kit pattern)

Patreon has no posting API, and its editor is a fussy rich-text SPA — so the
honest baseline is a **kit**: everything assembled, ordered, and one click per
field to paste. This pattern is generic; any future API-less platform gets it
for free.

## Post kit

```
●P PATREON KIT — Flux Workflow Tutorial #12 (early access)        ✋ Manual
┌──────────────────────────────────────────────────────────────────────────────┐
│ 1 │ Title                                                    [📋 Copy]      │
│   │ "Flux Workflow Tutorial #12 — watch it 2 days early"                    │
│ 2 │ Body (rich-text-safe markdown → HTML)                    [📋 Copy]      │
│   │ ┌──────────────────────────────────────────────────────┐               │
│   │ │ Patrons — the new tutorial is up early for you:      │  ✨ generated │
│   │ │ ▸ unlisted link: youtu.be/q9…                        │  from script, │
│   │ │ ▸ workflow.json + 8 wallpaper renders attached       │  edited by you│
│   │ └──────────────────────────────────────────────────────┘               │
│ 3 │ Attachments (2)                              [Open folder 📁]           │
│   │ workflow.json · wallpapers.zip     ← staged in posts\patreon\attachments│
│ 4 │ Audience: tiers [✓ Supporter] [✓ Pro]   ·   ( ) paid post              │
│ 5 │ Checklist                                                               │
│   │ [ ] patreon.com/posts/new opened          [Open Patreon ↗]             │
│   │ [ ] title pasted   [ ] body pasted   [ ] files attached                 │
│   │ [ ] tiers set      [ ] published                                        │
│ 6 │ [I posted it ✓]  → paste URL: [https://patreon.com/posts/____] [Save]  │
└──────────────────────────────────────────────────────────────────────────────┘
```

- Every `Copy` click ticks its checklist row — the kit tracks progress so an
  interruption doesn't lose your place (status stays 🖐 until step 6).
- Body copies as **rich-text/HTML clipboard**, not raw markdown, so headings,
  bold and links survive Patreon's editor.
- Scheduled Patreon post = ○P dot on the calendar; on the day it flips to
  🖐 "kit ready" on Today. The app can't post it — but it can make sure you
  never forget it and never retype it.
- After step 6: ✔ Published, Library row with your URL (no stats — Patreon
  gives us none; the row says "manual" instead of pretending).

## Why not browser-assist Patreon like Civitai?

Their editor breaks simple form-filling (contenteditable, uploads behind XHR).
Assisted mode may come later as an experiment — the kit is the fallback that
always works, and per 06 every Assisted channel degrades to exactly this kit
when its recipe breaks. Same UI muscle memory either way.

## Open questions

1. Is checklist-per-field too much ceremony, or right? (It's optional sugar —
   `Copy all + Open Patreon` could be one button for the fearless.)
2. Early-access mechanics: unlisted-YouTube-link-in-post (shown) vs uploading
   the video file natively to Patreon (better for patrons, manual upload of
   GBs)? Kit supports either — which is your default?
3. Do you want a recurring "monthly patron recap" automation that pre-fills a
   kit from the month's Library records? (Cheap once kits exist.)
