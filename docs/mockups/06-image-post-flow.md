# 06 — Image Post Flow (drag-drop → Civitai & friends)

Your requirement: *drag images in, click post, browser opens with the Civitai
form filled, you click the final button.* The wizard below creates a project +
posts in under a minute; the browser-assist engine does the tedious part.

## `+ New → Image post…` wizard

```
Step 1/4 — Images
┌──────────────────────────────────────────────────────────────────────────────┐
│   ╔═ drop zone ═══════════════════════════════════════════════════════════╗ │
│   ║  Drag images here, or [Browse] — PNG metadata is read automatically   ║ │
│   ╚════════════════════════════════════════════════════════════════════════╝ │
│  [img1★][img2][img3][img4][img5][img6][img7][img8]   ★ = cover, drag to sort │
│  ✓ 8/8 have generation metadata (prompt, model, seed — from PNG chunks)      │
│  Per image: [🔞 mature] toggle                                               │
└──────────────────────────────────────────────────────────────────────────────┘

Step 2/4 — Destinations (any combination)
┌──────────────────────────────────────────────────────────────────────────────┐
│ [✓] ●C Civitai      🤝 Assisted — prefills post form, you click Post         │
│ [✓] ●P Patreon      ✋ Manual — builds a copy-paste kit                      │
│ [ ] ●? <NextSite>   🤝 Assisted — added via Platforms, no new UI needed      │
└──────────────────────────────────────────────────────────────────────────────┘

Step 3/4 — Content                                    shared + per-destination
┌──────────────────────────────────────────────────────────────────────────────┐
│ Title: [ Neon Samurai Set                    ] [✨]                          │
│ Description (shared):                          [✨ job: image-post-descr]    │
│ ┌──────────────────────────────────────────┐                                 │
│ │ 8 neon-soaked samurai renders — Flux +   │  ✨ uses: Claude CLI ▾ (change)│
│ │ my CyberGlow LoRA. Workflow attached.    │                                 │
│ └──────────────────────────────────────────┘                                 │
│ [ Civitai overrides ] [ Patreon overrides ]                                  │
│   Civitai: tags [samurai][neon][flux]  ·  attach workflow.json [✓]           │
│            per-image prompts: from PNG metadata ✓ (editable)                 │
│   Patreon: tiers [✓ all patrons] · teaser = first 2 images                   │
└──────────────────────────────────────────────────────────────────────────────┘

Step 4/4 — Schedule
┌──────────────────────────────────────────────────────────────────────────────┐
│ ●C Civitai:  (•) Now   ( ) Pick: [____]                                      │
│ ●P Patreon:  ( ) Now   (•) Pick: [Mon Jul 20, 10:00]  (staggering is normal) │
│                                    [Create project & 2 posts]                │
└──────────────────────────────────────────────────────────────────────────────┘
```

Result: project `Neon Samurai Set` with images copied into its folder, a
Civitai post (starts the assisted flow now) and a Patreon post (kit ready on
the calendar for Monday ○P).

## The browser-assist handshake (Civitai — no API)

```
 you                      app (Playwright, your real browser profile)   civitai
  │  [Post now]                │                                           │
  │ ───────────────────────►   │  launch Chrome/Edge with your profile     │
  │                            │ ────────────────────────────────────────► │
  │                            │  open /posts/create, wait for login ✓     │
  │                            │  upload 8 images (in your order)          │
  │                            │  fill title, description, tags            │
  │                            │  attach per-image generation data         │
  │                            │  …then STOPS. Nothing is submitted.       │
  │  app shows: 🖐 "Review in the browser and click Post yourself"         │
  │  (browser stays open — post status: Waiting for you)                   │
  │                                                                        │
  │  you review, maybe tweak, click [Post] on Civitai ────────────────────►│
  │  back in app: [I posted it ✓] → paste URL (optional)                   │
  │  post → Published ● · Library record · green dot fills on calendar     │
```

Design commitments:

- **The app never clicks the final submit button.** That's both your
  requirement and the ToS-friendly posture: it types faster than you, it
  doesn't post for you.
- **Your real browser profile** (persistent Playwright context) — you're
  already logged in; no credential handling for Civitai at all.
- **Site recipes, not hardcoded sites.** Each Assisted platform = a small
  scripted recipe: `open URL → map fields → upload files → stop`. Civitai
  ships built-in; the unnamed site #4 is a new recipe, ideally configurable
  (field-mapping UI in Platforms), worst-case one small class implementing
  the same interface (`IPlatformConnector` from the Phase 1 seams).
- **When a site redesign breaks a recipe:** the post flips to
  `⚠ Assist failed — form changed`, with `[Fill manually]` (opens browser +
  copies the kit like a ✋ Manual platform) as the always-working fallback.
  Manual mode is the degraded state of Assisted mode — you're never stuck.

## Open questions

1. Should the wizard exist at all, or is `blank project → add images → add
   destinations` enough? (I say keep it: the 80% case deserves a fast path.)
2. Per-image mature flags + per-image prompt editing: in the wizard (shown) or
   only later in the project's Images tab? Wizard keeps momentum but grows.
3. After [Create project & posts] with "Now": jump straight into the browser
   handshake, or land on the project page first? Mockup assumes straight in.
4. Watch folder (e.g. `_drop\`): auto-suggest a new image post when N new
   renders appear? Cheap to add later via the existing job scheduler.
