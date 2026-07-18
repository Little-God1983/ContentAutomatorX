# 02 — Today (attention-first dashboard)

Replaces the current stat-tile Dashboard. Principle: **Today shows only what
needs a human**, in order of urgency — never a museum of numbers. After 5 years
this page looks exactly as empty or full as your actual workload.

## Mockup

```
◉ Today — Saturday, Jul 18 2026                                 [AIVisions ▾]

┌ NEEDS YOU (3) ───────────────────────────────────────────────────────────────┐
│ 🖐 ● civitai   Neon Samurai Set — prefilled, waiting for final click         │
│                                        [Reopen browser]  [I posted it ✓]     │
│ 🖐 ● patreon   Flux Tutorial early access — kit ready       [Open kit]       │
│ ⚠ ● youtube   API token expires in 3 days — renew before Sat 18:00 [Renew]  │
└──────────────────────────────────────────────────────────────────────────────┘

┌ SCHEDULED TODAY & TOMORROW ──────────────────────────────────────────────────┐
│ Today 18:00  ● youtube   Flux Workflow Tutorial #12  (⚡ auto — no action)   │
│ Sun   09:00  ● newsletter AI Weekly #42 — planned send (draft in review ↓)   │
│                                             [Preview draft]  [Edit schedule] │
└──────────────────────────────────────────────────────────────────────────────┘

┌ REVIEW QUEUE (2) ────────────────────────┐ ┌ THIS WEEK ──────────────────────┐
│ ✏️ AI Weekly #42 draft generated 07:00   │ │ Mo   Tu   We   Th   Fr  SA   Su │
│    by automation "Weekly AI news"        │ │  ·   ●C  🖐P   ·   🖐C  ○Y  ○N  │
│    [Review & edit] [Regenerate ✨]       │ │ (mini strip — click → Calendar) │
│ ✏️ YT description for Tutorial #12       │ └─────────────────────────────────┘
│    [Review] [Approve ✓]                  │ ┌ LAST 10 RUNS ───────────────────┐
└──────────────────────────────────────────┘ │ ✅ ingest ✅ gen ⚠ partial …    │
                                             │ 2 sources failing 3 days: [view]│
┌ RECENTLY PUBLISHED (7 days) ────────────┐  └─────────────────────────────────┘
│ ● civitai  Cyber Alley Set   Jul 14 → 231 👍  12 💬                          │
│ ● youtube  ComfyUI Speedrun  Jul 12 → 4.1k views · CTR 6.2%                  │
│ ● patreon  June Prompt Vault Jul 10 → (manual — no stats)                    │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Section rules

| Section | Contents | Empty state |
|---|---|---|
| **Needs you** | `Waiting for you 🖐` posts, `Failed ⚠` posts/runs, expired credentials | Hidden entirely — best case is an empty Today |
| **Scheduled** | next 48h of ⚡ auto posts + automation runs, so surprises are visible *before* they happen | "Nothing scheduled — plan on the Calendar →" |
| **Review queue** | AI-generated drafts awaiting approval (newsletter issues, descriptions) | Hidden |
| **This week** | 7-day mini calendar strip, links to Calendar | Always shown |
| **Recently published** | last 7 days, with headline stat per platform (pulled by analytics sync where an API exists; "manual — no stats" otherwise) | Hidden after 7 quiet days |

## Behavior notes

- `I posted it ✓` on an Assisted/Manual item opens a tiny dialog: *paste the
  post URL (optional) → confirm* → post becomes Published, dot on the calendar
  fills in, Library record created.
- Everything here is a *view* over Posts/Runs — no state of its own. Deleting
  this page loses nothing (that's what makes it safe to keep minimal).
- Old items never accumulate here: published → Library, failed → resolved or
  snoozed. This page cannot grow with the years. The current stat tiles
  (new items, drafts, failed runs) fold into the sidebar badges + Runs card.

## Open questions

1. Is "Scheduled today & tomorrow" the right pre-flight window — or 7 days?
2. Should the Review queue block auto-publish? Proposal: an automation can be
   set to `auto-send` (⚡ publishes without review — you only hit Send in
   MailerLite) or `review-first` (parks here). Default: review-first.
3. Do you want a morning push notification/email digest of this page? (The
   MCP server + a cron could do it — zero UI needed.)
