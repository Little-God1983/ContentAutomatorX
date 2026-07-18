# 08 — Newsletter Flow (sources → AI draft → MailerLite → you hit Send)

Your target: *automatic enough that only the final Send is yours.* A weekly,
**news-first, independent** product — sponsor/Patreon/YouTube references are
optional manual blocks, never machinery (decided 2026-07-18). The chain:

```
Sources (RSS / Reddit / websites / search / LLM research) ─► Inbox triage
      ─► Issue composer (✨ AI) ─► Push to MailerLite as draft ⚡
      ─► YOU hit Send in MailerLite ─► app detects Sent → Library + stats
```

Steps 1–3 already exist in spirit (Sources, Content page, Newsletter recipes) —
this mockup evolves them; the MailerLite connector is the new part.

## Inbox (today's "Content" page, sharpened for triage)

```
📥 Inbox — 12 new                       [New ▾] [All sources ▾] [This week ▾]
┌──────────────────────────────────────────────────────────────────────────────┐
│ ▸ r/StableDiffusion  "Flux 2.0 rumors thread" ↑2.1k    [👍 Keep] [👎 Skip]   │
│ ▸ ArsTechnica RSS    "EU AI Act enforcement begins"    [👍 Keep] [👎 Skip]   │
│ ▸ 🌐 comfy blog page "2.0 release notes" (site watch)  [👍 Keep] [👎 Skip]   │
│ ▸ 🤖 LLM research    "Weekly sweep: 6 links found"     [expand ▾]            │
│   … j/k keys to move, 1 to keep, 0 to skip — triage 30 items in 2 minutes    │
└──────────────────────────────────────────────────────────────────────────────┘
Kept items land in the pool automations & issues draw from (status Selected).
```

## Issue composer (a specialized project workspace)

```
✉️ AI Weekly #42                                    ●N MailerLite · ✏️ Review
[ Outline ]                    [ Editor + Preview ]
┌────────────────────────────┐ ┌────────────────────────────────────────────┐
│ ✓ Intro (✨ generated)     │ │ ## Flux 2.0 — what we actually know        │
│ ✓ Top stories (5 items)    │ │ The rumor thread everyone linked this week │
│ ✓ Quick links (8 items)    │ │ claims… [source](reddit.com/…)             │
│ ▸ Sponsor block (optional) │ │                                            │
│ ▸ Plug: Patreon / YouTube  │ │ (live preview in MailerLite template →)    │
│   (optional, added by hand)│ │                                            │
│ + Add section              │ │                                            │
├────────────────────────────┤ │                                            │
│ ITEM POOL (Selected: 13)   │ │                                            │
│ drag items into sections → │ │                                            │
└────────────────────────────┘ └────────────────────────────────────────────┘
Subject: [Flux 2.0 rumors, EU AI Act, my new tutorial]  [✨ 5 variants]
Preview text: [What the leak actually says…]            [✨]
[Regenerate all ✨ — job "newsletter-compose" via Claude CLI ▾]
[Push to MailerLite ⚡]
```

- **The newsletter is independent and news-first (decided).** No automatic
  cross-pollination: `+ Add section` offers reusable section types — Stories,
  Quick links, **Sponsor**, **Plug (Patreon / YouTube / a project)** — and you
  fill Sponsor/Plug blocks by hand (paste a link, pick a project) only in the
  weeks you want them.
- The whole issue is still a **Post** (`●N`, platform MailerLite) inside a
  project — same statuses, same calendar dot, nothing newsletter-special in
  the model.

## MailerLite handoff (⚡ API, but Send stays human)

```
[Push to MailerLite ⚡]
   → creates a real MailerLite campaign (draft): subject, HTML body from
     markdown via their template, audience group [Main list ▾]
   → post status: 🖐 "Draft in MailerLite — review & hit Send there"
                                   [Open in MailerLite ↗]  [Re-push (overwrite)]
   → hourly job sees campaign status = sent
   → post → ✔ Published · Library row: "1,204 sent · 48% open" (updates daily)
```

Deliberate choice: even though the API *could* send, **Send is left in
MailerLite** — you wanted the final word, and their UI shows the last-mile
stuff (spam score, list health) better than we should rebuild.

## Full-auto weekly automation (existing Recipes, one step further)

```
⚙ Automation "Weekly AI news"                                    [Enabled ✓]
schedule: Sat 07:00 · sources: r/StableDiffusion + 4 feeds · window: 7 days
steps: ingest → select (rules) → compose issue ✨ → ( ) auto-push to MailerLite
                                                    (•) park in Review queue
```

Saturday 07:00: draft waits on Today's Review queue → you polish 10 minutes →
`Push` → hit Send in MailerLite. Total human time: ~12 minutes.

## Open questions

1. MailerLite template mapping: one fixed template with a markdown body block
   (simple, shown), or a section-to-block designer (lots of UI)? I'd start
   fixed.
2. Keyboard triage in Inbox — worth it, or is click-only fine at your volume?
3. Should `auto-push` mode ever be allowed to also auto-*send* for a fully
   trusted automation someday, or is human-Send a permanent principle?
