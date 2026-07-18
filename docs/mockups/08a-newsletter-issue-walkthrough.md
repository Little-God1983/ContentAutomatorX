# 08a — Newsletter Issue: Step-by-Step Walkthrough

Companion to [08-newsletter-flow.md](08-newsletter-flow.md). Answers precisely:
*"I press + New → Newsletter issue… — then what? Which steps are AI?"*

## The two births of an issue

```
Path A (steady state — no button pressed):
  Sat 07:00 automation: gather → select → compose ✨ → Post(needs review)
  → waits on Today → YOU enter at step 5 (review)

Path B (the button): + New → Newsletter issue…
  → dialog (step 1) → same pipeline, run interactively → editor
```

Same machinery, different trigger. The button is for issue #1, special
editions, or weeks when the automation was paused.

## Step 0 — one-time setup (never per issue)

MailerLite platform configured (API key, group, from-address) · sources exist
(RSS / Reddit / Website / LLM-research) · one newsletter **automation** holds
the reusable settings: which sources, selection rules, prompt template, tone,
schedule. *This* is why later steps can be one click. **No AI.**

## Step 1 — the dialog (Path B only) — [you]

```
┌ New newsletter issue ─────────────────────────────────────────┐
│ Based on:   [Weekly AI news ▾]        ← automation = settings │
│ Material:   [Since last issue (Jul 12) ▾ | last 7 days | …]   │
│ Title:      [AI Weekly #43]          ← auto-numbered, editable │
│                                                               │
│ Sources for this issue (5 of 6)                    [collapse ▴]│
│   [✓] r/StableDiffusion        [✓] Ars Technica RSS           │
│   [✓] HN AI feed               [✓] comfy blog (site watch)    │
│   [✓] 🤖 Weekly sweep (LLM)    [ ] r/LocalLLaMA               │
│   [+ New source…]                                             │
│   [ ] Save this set as the automation's new default           │
│                                                               │
│                         [Start empty]   [Create & gather]     │
└───────────────────────────────────────────────────────────────┘
```

**Nothing gathers until you've seen this list** (decided 2026-07-18). The
checklist starts from the automation's configured sources; the tenant's other
sources sit below, unchecked. Semantics of unchecking a source — two effects:

1. it is **not fetched** for this run, and
2. its already-gathered Inbox items are **ineligible** for this issue's
   selection (otherwise last night's scheduled ingest would smuggle them in).

`+ New source…` opens the normal source form (Reddit / RSS / Website /
LLM-research) inline; the new source is saved to the tenant and comes back
checked. A source created without a schedule is effectively a **one-off**: it
only runs when an issue gathers it. The per-issue set is a **transient
override** — the automation's defaults stay untouched unless you tick
*Save as the automation's new default*.

Creates `Post(kind=Newsletter, status=Draft)` and opens the issue editor.
`Start empty` skips all AI forever — blank editor, you write. **No AI.**

## Step 2 — gather material — [automatic; one AI source]

Runs **the sources you checked in step 1** (Path A scheduled runs use the
automation's configured set — nobody is around to ask). Fetch is skipped per
source when the scheduler already pulled it recently:

| Source type | How | AI? |
|---|---|---|
| RSS / Reddit | existing connectors, dedup by external id | no |
| Website watch | fetch page, extract new article links + text | no |
| **LLM research** | prompt → LLM **with web search** → JSON items (title, url, summary) | **✨ AI #1** |

Everything lands in the Inbox as items. Progress line in the editor:
`gathering… 38 items (7 new)`. The LLM-research sweep is "AI as a source" —
it *finds* material, it never writes the newsletter.

## Step 3 — select items — [rules; your triage optional]

Deterministic, from the automation's rules: time window, min score, max
items, keyword include/exclude, exclude already-used. Your Inbox triage
bends it: **Kept** items are guaranteed in, **Skipped** are out — but triage
is optional; zero-triage weeks work on rules alone. **No AI in v1**
(a "rank by newsletter-worthiness" ✨ job is a possible later upgrade).

## Step 4 — the editor, pre-compose — [you decide: AI or not]

```
✉️ AI Weekly #43 · Draft                      [Push to MailerLite ⚡] (disabled)
Subject: [_______________________] [✨]   Preview: [______________] [✨]
┌ MATERIAL — rules picked 14 of 38 (Jul 12–18) ─────┐ ┌ BODY ────────────────┐
│ Sources: [✓r/SD][✓Ars][✓HN][✓site][✓🤖][+]        │ │      (empty)         │
│          (changed? → [Re-gather])                 │ │                      │
│ [✓] Flux 2.0 rumors thread        r/SD   ↑2.1k    │ │   [Compose ✨]       │
│ [✓] EU AI Act enforcement begins  Ars             │ │   …or just type      │
│ [✓] ComfyUI 2.0 release notes     site watch      │ │                      │
│ [✓] Weekly sweep: 6 links         🤖 research     │ │                      │
│ [ ] 24 more below threshold  [show]               │ │                      │
│ [Run research sweep now 🤖]  last: Sat 07:00      │ └──────────────────────┘
└───────────────────────────────────────────────────┘
```

The source chips mirror the dialog's checklist — the same per-issue set,
adjustable mid-flight: results feel thin, so you tick `r/LocalLLaMA` (or add
a brand-new source with `+`) and hit **Re-gather**; the item list refreshes,
nothing composed yet, nothing lost.

You can kick items out / pull items in before any AI runs. Then either press
**Compose ✨** or write by hand — AI is a button, never a gate.

## Step 5 — compose — **✨ AI #2 (the big one)**

One structured LLM call (job `newsletter-compose`, default Claude CLI,
swappable in AI Studio):

- **Prompt assembly (deterministic):** template (news-first structure: intro,
  top stories, quick links + empty Sponsor/Plug placeholder headings) +
  tenant voice profile + tone/length/language + the checked items
  (title/url/excerpt).
- **One call returns:** body markdown + **3–5 subject-line candidates** +
  preview text (structured JSON). Subject chips appear above the editor —
  pick one, edit it, or hit the subject [✨] to re-roll just subjects
  (**✨ AI #3**, cheap).
- Guardrail in the template: *only claims supported by the given items; keep
  every source link.* Your review is the hallucination gate — that's a big
  part of why Send stays human.
- Items used are marked (provenance saved; next week's selection excludes them).
- Takes ~2–5 min via CLI; editor shows progress, you can keep triaging.

In Path A this step already ran at 07:00 — you never wait for it.

## Step 6 — review & polish — [you]

Markdown left, **email-HTML preview right** (rendered through the same
template MailerLite will get — WYSIWYG). You:

- edit text directly (most weeks: trim, reorder, tweak the intro)
- fill **Sponsor / Plug** headings by hand — paste the sponsor line, link a
  video/Patreon post if you feel like it (decided: never automatic)
- optionally **Regenerate ✨** with an instruction box ("more skeptical,
  drop story 3") — re-runs AI #2 with your note appended; overwrite asks
  for confirmation first (open question: or keep both versions to diff?)

## Step 7 — push — [you, one click; no AI]

`Push to MailerLite ⚡`: markdown → email-safe HTML (deterministic Markdig +
fixed template) → **draft campaign** created via API (subject, preview,
group, from) → status `Pushed`, buttons become `Open in MailerLite ↗` and
`Re-push` (editing after push is fine while it's still a draft there).

## Step 8 — send — [YOU, in MailerLite; never AI]

You look at MailerLite's own preview/spam checks and hit **Send** (or
schedule it there — both are detected).

## Step 9 — aftermath — [automatic; no AI]

Hourly job sees the campaign sent → post `Published`, leaves Today, blue dot
fills on the calendar (once it exists). Daily stats snapshots (sent / opens /
clicks) for 30 days show on the post row.

## Who does what — the whole flow at a glance

| # | Step | Actor | AI |
|---|---|---|---|
| 0 | Setup (platform, sources, automation) | you, once | — |
| 1 | Trigger | scheduler (A) or you (B) | — |
| 2 | Gather feeds/sites | connectors | — |
| 2b | Research sweep | job `research-sweep` | **✨ #1** |
| 3 | Select items | rules (+ optional triage) | — |
| 5 | Compose body + subjects + preview | job `newsletter-compose` | **✨ #2** |
| 5b | Subject re-roll | job `subject-variants` | ✨ #3 (on demand) |
| 6 | Review, Sponsor/Plug, polish | **you** | (✨ rewrite-selection: later) |
| 7 | Push draft campaign | you (1 click), deterministic | — |
| 8 | **Send** | **you, in MailerLite** | **never** |
| 9 | Sent detection + stats | hourly/daily jobs | — |

Design rule the table encodes: **AI only where judgment scales (finding and
drafting); deterministic code everywhere else (so failures are debuggable);
human gates exactly twice (review, send).**

Saturday time budget in steady state: review 10–15 min + push 5 s + send in
MailerLite 1–2 min ≈ **~15 minutes**, zero waiting on AI (it ran at 07:00).

## Recommendations awaiting confirmation

1. **Pool-first, not auto-compose:** after `Create & gather`, land in the
   editor with the material list and a big Compose button (trust-building,
   you see what goes in) — rather than auto-running compose. Steady state is
   Path A anyway, where compose already happened.
2. **Subjects ride along:** compose returns subject candidates + preview in
   the same call; dedicated ✨ re-rolls subjects only.
3. **Research sweep is a scheduled source** (like RSS), with a `Run sweep
   now` button in the editor when stale — not an inline step of compose
   (keeps compose fast and its cost predictable).
4. **Regenerate = overwrite with confirm** in v1 (diff-view of two versions
   is a later nicety).

## Implementation notes (2026-07-18 plan)

- Subjects do NOT ride along in the compose call: compose returns the body
  (keeps file drafts clean); the subject field prefills with the title and
  the [✨ subjects] button runs a small dedicated call on demand. Same UX,
  two cheaper calls instead of one fragile structured one.
- The dialog's "since last issue" window option ships as fixed day windows
  (3/7/14/30) in v1.
