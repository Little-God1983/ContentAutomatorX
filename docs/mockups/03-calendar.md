# 03 — Calendar (planning surface)

The calendar is a **view over Posts** — it owns no data. Dots are posts; colors
are channels; filled = published, hollow = planned. Drag a hollow dot to
reschedule. This is your green-dot/red-dot idea, generalized to every channel.

## Month view

```
📅 Calendar — July 2026            [◀] [Today] [▶]        [Month|Week|List]

Channels: [✓ ●Y YouTube] [✓ ●C Civitai] [✓ ●N Newsletter] [✓ ●P Patreon]   [+ ICS feed]

┌─────────┬─────────┬─────────┬─────────┬─────────┬─────────┬─────────┐
│ Mon     │ Tue     │ Wed     │ Thu     │ Fri     │ Sat     │ Sun     │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│ 29      │ 30      │ 1       │ 2       │ 3       │ 4       │ 5       │
│         │         │         │         │ ●Y      │         │ ●N      │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│ 6       │ 7       │ 8       │ 9       │ 10      │ 11      │ 12      │
│         │ ●C      │         │         │ ●P      │         │ ●N ●Y   │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│ 13      │ 14      │ 15      │ 16      │ 17      │ 18 TODAY│ 19      │
│         │ ●C      │ 🖐P     │         │ 🖐C     │ ○Y      │ ○N      │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│ 20      │ 21      │ 22      │ 23      │ 24      │ 25      │ 26      │
│ ○P      │         │         │         │ ○Y      │         │ ○N      │
├─────────┼─────────┼─────────┼─────────┼─────────┼─────────┼─────────┤
│ 27      │ 28      │ 29      │ 30      │ 31      │ 1       │ 2       │
│         │ ○C      │         │         │ ○Y ○P   │         │         │
└─────────┴─────────┴─────────┴─────────┴─────────┴─────────┴─────────┘

Legend:  ● published   ○ scheduled/planned   🖐 waiting for you (overdue if past)
         ⚠ failed      colors = channel (set per channel in 🔌 Channels)
```

## Day panel (click a day — e.g. Sat 18)

```
┌ Saturday, Jul 18 ────────────────────────────────────────────┐
│ ○ ●Y 18:00  Flux Workflow Tutorial #12                       │
│      ⚡ auto-publishes via YouTube API      [Open post]      │
│                                                              │
│ Overdue from this week:                                      │
│ 🖐 ●P Flux Tutorial early access (planned Wed 15) [Open kit] │
│ 🖐 ●C Neon Samurai Set (planned Fri 17)   [Reopen browser]   │
│                                                              │
│ [+ Plan something on Jul 18 ▾]  (Image post / Video / Issue) │
└──────────────────────────────────────────────────────────────┘
```

## Interactions

- **Drag** hollow dot → new day: updates the post's scheduled date (and the
  YouTube API schedule if already handed off). Filled dots don't drag.
- **Hover** dot → tooltip: title, channel, status, time.
- **`+ Plan` on any day** → same menu as global `+ New`, date prefilled. This
  enables plan-first workflow: sketch empty slots ("image post here, video
  there"), fill them with content later. A planned-but-empty post = status
  `Idea` and renders as a dimmed hollow dot.
- **Overdue** 🖐/○ in the past get a red ring and also appear on Today.
- **Filters** persist per user. Unchecking `●C Civitai` hides its dots — with
  5+ channels in 2031 you can look at one channel's rhythm alone.

## Week view (one line per channel — spotting collisions & gaps)

```
            Mon 13   Tue 14   Wed 15   Thu 16   Fri 17   Sat 18   Sun 19
●Y YouTube    ·        ·        ·        ·        ·      ○ 18:00    ·
●C Civitai    ·      ● 20:15    ·        ·      🖐 —       ·        ·
●N Newsletter ·        ·        ·        ·        ·        ·      ○ 09:00
●P Patreon    ·        ·      🖐 —       ·        ·        ·        ·
```

## External calendar (Part 3 of your list)

**Phase A (cheap, recommended first):** read-only **ICS feed** per tenant
(`/calendar/{tenant}.ics`, secret token URL). Subscribe from Google/Outlook/
Proton/phone — your plan appears everywhere, always current, zero sync bugs.

**Phase B (only if A isn't enough):** two-way sync with one provider (Google
Calendar API): events created there with a `#cax` tag become `Idea` posts here.
Deliberately later — two-way sync is a swamp (conflicts, deletions, auth).

The built-in calendar stays the **source of truth** either way; your planning
suite is this page, external calendars are mirrors.

## Open questions

1. Dot-per-post vs bar-per-project (multi-day video productions could render as
   a spanning bar with milestone dots)? Dots keep v1 simple; bars help "what am
   I *working on* this week" — want both (toggle)?
2. Should automation runs (ingestion, scheduled newsletter generation) show on
   the calendar too (tiny gray ticks), or is that noise better left in Runs?
3. Time-of-day: does scheduling need minute precision in the UI (YouTube needs
   it for the API) or is "morning/evening + exact time only for YouTube" enough?
