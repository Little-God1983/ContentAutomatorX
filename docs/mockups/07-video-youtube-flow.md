# 07 — Video Pipeline, Voiceover Choices & YouTube

Video projects have the longest life of anything in the app: idea → script →
voiceover → edit/render (outside the app) → publish → analytics. The app owns
the bookends and stays out of your editor's way in the middle.

## Voiceover tab — "sometimes AI, sometimes me"

```
[ Overview | Assets | Script ✓ | Voiceover | Posts | Activity ]

Voiceover for: Flux Workflow Tutorial #12
┌──────────────────────────────────────────────────────────────────────────────┐
│ (•) 🎙 I record it myself                                                    │
│      → script exported as teleprompter.md (large print, chunked)             │
│      → drop your recording into assets\audio\ when done   [Open folder]      │
│                                                                              │
│ ( ) 🤖 AI voiceover        job: "voiceover-tts"                              │
│      Provider: [ElevenLabs ▾]  Voice: [Aria ▾]  Speed: [1.0]                 │
│      Source: Script tab (auto-split into takes)                              │
│      [Generate 14 takes ✨]  → lands in assets\audio\tts\take_01.mp3 …       │
│      cost estimate: ~31k chars                                               │
│                                                                              │
│ ( ) ∅ No voiceover (music only / talking-head)                               │
└──────────────────────────────────────────────────────────────────────────────┘
```

The choice is **per project**, defaults from the tenant. "AI voiceover" is just
another job binding — swap ElevenLabs for a local TTS in AI Studio without
touching this screen (see 10).

## YouTube post detail (⚡ Auto via YouTube Data API — or your own MCP)

```
● YOUTUBE POST — Flux Workflow Tutorial #12                       ⚡ Auto
┌──────────────────────────────────────────────────────────────────────────────┐
│ Video file:  renders\render_final.mp4  (14:32 · 2.1 GB · 4K)   [pick other]  │
│ Title:       [This Flux Workflow Changed My Renders (Tutorial #12)]          │
│              [✨ 5 title ideas]  ← job "yt-title", A/B list to pick from     │
│ Description: [✨ from script]   preview: "In this tutorial we build…"        │
│              auto-appended: chapter list ✓ · link block (Patreon, Civitai) ✓ │
│ Tags [✨] · Category [Education ▾] · Playlist [Tutorials ▾] · Lang [EN ▾]    │
│ Thumbnail:   thumb_v3.png  [A/B pool: v1 v2 v3]                              │
│ Visibility:  ( ) Public now                                                  │
│              (•) Unlisted now → Public at [Sat Jul 18, 18:00]  ← enables     │
│                  early-access links for Patreon before public release        │
│ Kids/made-for-kids: [No ▾]   Monetization: (manual on YT Studio — noted)     │
│                                                                              │
│ [Upload & schedule ⚡]     upload progress shows here; resumable; retried    │
└──────────────────────────────────────────────────────────────────────────────┘
```

Architecture note: per the Phase 1 seams, this is one `IPlatformConnector`.
It can call the YouTube Data API directly **or** be an MCP client proxy to a
standalone YouTube MCP server you write — orchestration can't tell the
difference. Building the MCP server first also gives Claude Code the same
powers for free.

## Analytics page

```
📊 Analytics                       [ YouTube | Newsletter | Civitai* | All ]
                                   *manual platforms: only what you logged

YouTube — channel                                    range: [Last 90 days ▾]
┌ Subscribers ─────────┐ ┌ Views ───────────┐ ┌ Watch time ──────────────────┐
│ 12,480 (+312)        │ │ 148k             │ │ 9.1k h                       │
│ ▁▂▂▃▃▄▅▅▆▇           │ │ ▂▃▂▄▅▄▆▇▆█       │ │ ▂▃▃▄▄▅▅▆▆▇                   │
└──────────────────────┘ └──────────────────┘ └──────────────────────────────┘

Per video (sortable)                                  [compare year-over-year]
│ TITLE                        PUBLISHED   VIEWS   CTR    AVG VIEW   SUBS+     │
│ ComfyUI Speedrun             Jul 12      4.1k    6.2%   52%        +38       │
│ Flux LoRA in 10 min          Jul 03      11.9k   7.1%   48%        +102      │
│ …older years collapsed under [2025 ▸] [2024 ▸]…                              │
```

Data pulled by a daily background job (existing scheduler) into local tables →
works offline, supports 5-year trends YouTube's own UI makes painful. Same
pattern later for MailerLite (opens/clicks) — one tab per platform that has an
API, and **no fake numbers for platforms that don't**.

## Open questions

1. Analytics depth: are channel totals + per-video table enough, or do you
   want retention curves/traffic sources mirrored too? (Each is another API
   scope + storage; I'd start shallow.)
2. Thumbnail A/B: YouTube's native "Test & compare" isn't in the public API —
   keep a local pool + manual swap, or skip pools entirely?
3. Should uploading big files go through the app at all, or should the app
   register an already-uploaded (unlisted) video by ID for metadata-only
   management? Both paths shown are possible; upload-through-app is one less
   manual step but needs resumable-upload plumbing first.
