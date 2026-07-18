# 11 — Folder Structure on Disk

Principles: **the DB is the source of truth, folders are the workbench** (kept
from Phase 1). One project = one folder, always the same shape, regardless of
where it ends up being published. Audience (public/patron-only) is metadata on
posts — it never moves files.

## Layout (per tenant, inside the existing `OutputFolderPath` sync root)

```
D:\Content\AIVisions\                      ← tenant root (OneDrive/Mega synced)
├─ _drop\                                  ← throw anything here; app offers
│                                            "promote to project" (see 06 Q4)
├─ projects\
│  ├─ 2026\                                ← year shard = 5-year rule on disk
│  │  ├─ 2026-07-10_flux-workflow-tutorial-12\
│  │  │  ├─ project.json                   ← manifest: stable ID + post index
│  │  │  ├─ notes.md
│  │  │  ├─ assets\
│  │  │  │  ├─ raw\        (footage, PSDs, source images)
│  │  │  │  └─ audio\      (your recording OR tts\take_01.mp3 …)
│  │  │  ├─ renders\       (render_final.mp4, thumb_v1..v3.png)
│  │  │  └─ posts\
│  │  │     ├─ youtube\    (description.md, tags.txt, chapters.txt)
│  │  │     └─ patreon\    (post.md, attachments\workflow.json, wallpapers.zip)
│  │  ├─ 2026-07-13_neon-samurai-set\
│  │  │  ├─ project.json
│  │  │  ├─ images\        (01_cover.png … 08.png — wizard order = filename order)
│  │  │  └─ posts\
│  │  │     ├─ civitai\    (description.md, tags.txt)
│  │  │     └─ patreon\    (post.md)
│  │  └─ 2026-07-08_prompt-vault-july\     ← patron-only: same shape,
│  │     └─ posts\patreon\                    just fewer post folders
│  └─ 2025\ …                              ← old years: untouched, synced, findable
├─ newsletter\
│  └─ 2026\
│     ├─ 2026-W29_ai-weekly-42\ (issue.md, pushed.html)
│     └─ …
└─ inbox-exports\                          ← optional: kept item dumps for automations
```

## The rules that make it survivable

1. **`YYYY\YYYY-MM-DD_slug\`** — sorts chronologically in Explorer, year folder
   keeps any single directory from holding 500 entries by 2031. The date is the
   *creation* date and never changes (publish dates live on posts).
2. **`posts\<platform>\` holds only platform-specific text/exports.** Media stays
   in `assets\`/`renders\`/`images\` and is *referenced* — no duplicate copies
   per destination (exception: Patreon `attachments\` staging, which is a real
   deliverable).
3. **`project.json` carries the stable project ID.** Rename the folder or the
   slug, move it between machines — the app re-links by scanning manifests, so
   Explorer-level reorganizing can't orphan anything. (Fallback if the manifest
   is deleted: the app flags an "unlinked folder" on Today instead of guessing.)
4. **Editable-anywhere:** `description.md`, `post.md`, `notes.md` are watched;
   edit them in the app, VS Code, or on another synced device — same result.
   Status/schedule/URLs live only in the DB (never in files, no edit races).
5. **Nothing is required to be on disk.** A metadata-only project (an `Idea`
   post on the calendar) has no folder yet; the folder is created on first
   asset. Deleting files never deletes Library history.

## How the UI touches it

- Every project header, asset grid, and kit shows **[Open folder 📁]** — the
  app is comfortable being left for Explorer and your editor.
- The wizard (06) copies dropped images *into* `images\` (originals untouched
  elsewhere) so the project folder is always self-contained and syncable.
- "Assets" tab = a thin mirror of the folder (with hash tracking so the
  YouTube post knows if `render_final.mp4` changed after approval ⚠).

## What stays in the DB only

Post status & schedule, published URLs, provenance (which inbox items, which
prompt, which model), analytics snapshots, platform credentials (DPAPI),
Library records. **Backup = SQLite file + the tenant folder — two things.**

## Open questions

1. Newsletter under its own `newsletter\` root (shown, matches current
   delivery) vs newsletters as regular `projects\` entries? Own root mirrors
   how MailerLite issues feel different from media projects; regular entries
   would be more uniform. Preference?
2. `_drop\` watch folder: promote-to-project suggestions on Today — want it in
   v1 of this structure, or later?
3. Should `renders\` final files be renamed on publish (`published_YYYY-MM-DD_`
   prefix) for Explorer-level clarity, or is touching artist files taboo?
