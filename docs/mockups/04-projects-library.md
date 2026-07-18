# 04 — Projects (active work) & Library (the 5-year answer)

Two pages, one boundary: **Projects = things you're still working on.
Library = the permanent record of everything published.** Work flows left to
right and never comes back — that's what keeps the working UI clean in 2031.

```
Idea/plan ──► Project (active) ──► Posts published ──► Library record
                                   └─► project auto-archives after 14 days done
```

## Projects page

```
🗂 Projects                                  [Active ▾]  [🔍 filter]  [+ New ▾]
             (Active | Done | Archived | All)

┌──────────────────────────────────────────────────────────────────────────────┐
│ TITLE                        TYPE      POSTS                NEXT      UPDATED │
├──────────────────────────────────────────────────────────────────────────────┤
│ Flux Workflow Tutorial #12   🎬 video  [●Y ○ sched 18:00]   today     2h ago  │
│                                        [●P 🖐 kit ready]                      │
│ Neon Samurai Set             🖼 images [●C 🖐 in browser]   overdue   1d ago  │
│                                        [●P ✏️ draft]                          │
│ AI Weekly #42                ✉️ issue  [●N ✏️ review]       Sun 09:00 7h ago  │
│ Prompt Vault July (patrons)  🎬 video  [●P ✏️ draft]        Jul 31    3d ago  │
└──────────────────────────────────────────────────────────────────────────────┘
                                                          4 active projects
```

- The **POSTS column is the heart**: one chip per destination post, each with
  platform color + status glyph. The video-with-attached-Patreon-post case reads
  at a glance: `[●Y sched] [●P kit ready]`.
- A patron-only project simply has only a `●P` chip. No special casing.
- **Default filter: Active.** Done = all posts published (auto-set). Archived =
  Done + 14 days, or manual. Archived projects vanish from here but stay
  searchable and linked from Library records.

## Posts page (the flat cross-platform list)

Same data as project chips, flattened — for platform-centric days ("what's in
flight on Civitai?"). Default: open posts + next 30 days.

```
📤 Posts                [Open ▾] [All platforms ▾] [Next 30 days ▾]  [🔍]
┌──────────────────────────────────────────────────────────────────────────────┐
│ WHEN        PLATFORM  TITLE                        STATUS         PROJECT     │
│ today 18:00 ●Y        Flux Workflow Tutorial #12   ○ Scheduled ⚡  Flux #12   │
│ overdue     ●C        Neon Samurai Set             🖐 Waiting 🤝   Neon Samurai│
│ overdue     ●P        Flux Tutorial early access   🖐 Kit ready ✋ Flux #12   │
│ Sun 09:00   ●N        AI Weekly #42                ✏️ Review       AI Weekly  │
│ Jul 24      ●Y        (idea — no content yet)      💡 Idea         —          │
│ Jul 28      ●C        (idea)                       💡 Idea         —          │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Library page

Append-only publication log. Post-centric (one row per published post), because
in 2031 you'll ask "what did I put on Civitai in March 2028?", not "which
project folder was that in" — though every row links back to its project.

```
🏛 Library                                              [🔍 search everything…]

 YEAR        PLATFORMS                   TYPE            ┌ 312 records ────────┐
 ▸ 2026 (54) [✓●Y 18][✓●C 21][✓●N 28]    [✓🎬][✓🖼][✓✉️] │ exports: [CSV] [JSON]│
 ▸ 2025 (131)[✓●P 12][ ●K  2]                            └──────────────────────┘
 ▸ 2024 (127)

── July 2026 ──────────────────────────────────────────────────────────────────
│ Jul 14  ●C  Cyber Alley Set              → civitai.com/posts/98… 231👍 12💬  │
│ Jul 12  ●Y  ComfyUI Speedrun             → youtu.be/x7…  4.1k views · 6.2% CTR│
│ Jul 12  ●N  AI Weekly #41                → 1,204 sent · 48% open              │
│ Jul 10  ●P  June Prompt Vault            → patreon.com/posts/… (manual)      │
│ Jul 07  ●C  Neon Alley Set               → civitai.com/posts/97… 890👍       │
│ Jul 05  ●N  AI Weekly #40                → 1,198 sent · 51% open             │
│ Jul 03  ●Y  Flux LoRA in 10 min          → youtu.be/a2… 11.9k views          │
── June 2026 ──────────────────────────────────────────────────────────────────
│ …                                                                            │
```

- **Facets, not pages**: year ▸ expands; platform and type are toggles. Adding
  platform #9 in 2029 = one more toggle chip.
- Stats columns refresh via analytics sync where an API exists (YouTube,
  MailerLite); manual platforms show the URL you recorded and nothing pretends
  otherwise.
- A Library record is created the moment a post hits `Published` — including
  Assisted/Manual posts confirmed via "I posted it ✓ (+URL)".
- **Never deletable in bulk** (single-record fix-ups allowed). This is your
  provenance: which items/prompt/model produced it, when, where.

## Why this beats status-quo Drafts/Content pages long-term

The current Drafts page is a flat list that only grows. Splitting *open work*
(Posts, capped by your real workload) from *history* (Library, grows forever
but is faceted/searchable) means no page ever contains both — the first is
always small, the second is built to be searched, not scrolled.

## Open questions

1. Auto-archive delay for Done projects: 14 days OK? (Configurable, but what
   default feels right?)
2. Library grouping: by month (shown) vs continuous infinite scroll with a
   year/month jump-rail on the right?
3. Should Library rows for YouTube embed a mini sparkline (first-30-days
   views) once analytics exist, or keep it to a single number + Analytics page?
