# 05 — Project Workspace

The container for one piece of work. This mockup shows the exact case you
raised: **a YouTube video that also gets a Patreon post** (early access), and
below it the patron-only variant. Same page, no special casing.

## Header + Posts tab (the crux)

```
🗂 Flux Workflow Tutorial #12                                    🎬 video · Active
📁 D:\Content\AIVisions\projects\2026\2026-07-10_flux-workflow-tutorial-12
   [Open folder]                                       [Archive] [⋯]

[ Overview | Assets 7 | Script ✓ | Voiceover ✓ | Posts 2 | Activity ]
──────────────────────────────────────────────────────────────────────────────

┌ ●P PATREON — early access                    ✋ Manual ──────────────────────┐
│ Status: 🖐 Kit ready          Planned: Wed Jul 15 (overdue)                  │
│ Audience: 🔒 Patrons — tiers: [✓ Supporter] [✓ Pro]                          │
│ Title:  Flux Workflow Tutorial #12 — 2 days early                            │
│ Body:   early-access.md (generated from script ✨, edited by you)            │
│ Files:  video link (unlisted YT) + 2 attachments (workflow, wallpapers)      │
│                                            [Open kit ✋]  [I posted it ✓]    │
└──────────────────────────────────────────────────────────────────────────────┘

┌ ●Y YOUTUBE — public release                  ⚡ Auto ────────────────────────┐
│ Status: ○ Scheduled Sat Jul 18, 18:00        Visibility: Unlisted → Public   │
│ Title:  This Flux Workflow Changed My Renders (Tutorial #12)     [✨ ideas]  │
│ Descr:  description.md ✓ approved            Tags: flux, comfyui… [✨]       │
│ Thumb:  thumb_v3.png (from Assets)           Playlist: Tutorials             │
│ Video:  render_final.mp4 · 14:32 · 2.1 GB    Chapters: from script ✓ [✨]    │
│                          [Edit metadata]  [Unschedule]  [Publish now ⚡]     │
└──────────────────────────────────────────────────────────────────────────────┘

[+ Add destination ▾]   (Civitai · Newsletter mention · Ko-fi · …)
```

Each card is one **Post**: own status, own schedule, own audience — posts of
one project are staggered on the calendar (Patreon Wed ●P, YouTube Sat ●Y).
`+ Add destination` shows every channel that accepts this project's type —
"Newsletter mention" queues a teaser block into the next issue (see 08).

## Other tabs

```
Overview  — status strip (what's done/next), notes, linked inbox items that
            inspired this, mini-timeline of both posts
Assets    — file grid mirroring the project folder: raw/, renders/, thumbs/;
            drag-drop adds files; [Open folder] everywhere. The DB stores
            paths + hashes; files live on disk, synced by OneDrive/Mega as today.
Script    — markdown editor; [✨ Generate] runs AI task "video-script" (same
            pipeline as today's VideoScript recipes); versions kept
Voiceover — see 07: Record myself / AI voice / None
Posts     — above
Activity  — audit log: generated, edited, scheduled, published, by whom/what
            (you, automation, MCP agent)
```

Tab set adapts to type: image projects get `Images` instead of
Script/Voiceover; newsletter issues use the dedicated composer (08).

## Patron-only variant — no ceremony

```
🗂 Prompt Vault July (patrons)                                  🎬 video · Active
[ Overview | Assets 3 | Script — | Voiceover — | Posts 1 | Activity ]

┌ ●P PATREON — patron exclusive                ✋ Manual ──────────────────────┐
│ Audience: 🔒 Patrons — tiers: [✓ Pro only]                                   │
│ Status: ✏️ Draft            Planned: Fri Jul 31                              │
│ Files:  vault-july.mp4 + prompts.zip                                         │
└──────────────────────────────────────────────────────────────────────────────┘
[+ Add destination ▾]
```

**Audience is a property of the post, not of the project or folder.** The
patron-only project sits in the same `projects/2026/…` folder scheme
(see [11-folder-structure.md](11-folder-structure.md)); it's exclusive simply
because its only post targets Patreon. If you later make it public, you add a
YouTube post to the same project — nothing moves on disk.

## Open questions

1. Should a post's text fields live as files in the project folder (mockup:
   yes — `posts/youtube/description.md`, editable in any editor, watched by
   the app) or DB-only with export? Files match your sync-folder workflow;
   DB stays source of truth for status/schedule either way.
2. "Newsletter mention" as a destination: pushes a teaser block into the next
   open issue. Right model, or should the newsletter side *pull* from
   recently-published posts instead? (08 proposes: both — push queues, pull
   offers.)
3. Cross-post links: Patreon early-access wants the unlisted YouTube URL before
   the public one exists. Publish YT as unlisted first, flip to public on
   schedule (YouTube API supports this) — acceptable dependency to model?
