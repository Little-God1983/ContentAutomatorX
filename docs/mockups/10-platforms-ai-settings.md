# 10 — Platforms & AI Studio (the modularity core)

Two settings pages carry the whole modularity requirement: **Platforms**
(where posts go) and **AI Studio** (which provider does which job). Both are
"add a row, not a feature" by design.

## 🔌 Platforms

```
🔌 Platforms — AIVisions                                      [+ Add platform ▾]
┌──────────────────────────────────────────────────────────────────────────────┐
│ PLATFORM       MODE         ACCOUNT/TARGET        COLOR  STATUS        DEFAULTS│
│ ●Y YouTube     ⚡ Auto      @AIVisions            🔴    token ✓ 89d   [edit] │
│ ●N MailerLite  ⚡ Auto      Main list (1.2k)      🔵    key ✓         [edit] │
│ ●C Civitai     🤝 Assisted  browser profile ✓     🟢    recipe v3 ✓   [edit] │
│ ●P Patreon     ✋ Manual    (kit only)            🟠    —             [edit] │
├──────────────────────────────────────────────────────────────────────────────┤
│ per row: [Test] · [Disable] · capability mode is fixed by connector,         │
│ color/defaults/account are yours                                             │
└──────────────────────────────────────────────────────────────────────────────┘

[+ Add platform ▾]
   ⚡ YouTube · ⚡ MailerLite · 🤝 Civitai · ✋ Patreon kit · ⚡ Ko-fi
   ✋ Generic kit platform…           ← any future API-less platform, today
   🤝 Custom assisted site… (beta)   ← the unknown image site #4:
      name, color, start URL, then a field-mapping step:
      "click into the site's Title field → app records selector" per field.
      Saved as a site recipe (versioned; re-record when the site changes).
```

- **Defaults per platform** = what prefills every new post: YouTube category/
  playlist/description footer; Civitai default tags; Patreon default tiers;
  MailerLite audience group.
- **Credentials** live in the OS keychain/DPAPI (per the Phase 1 decision),
  status column shows expiry *before* a 3 a.m. failure does.
- Platform color feeds every dot, chip and analytics line in the app.

## 🤖 AI Studio — providers × jobs

> **Partially real as of 2026-07-20.** A global **Model + Effort** selector
> ships as a settings card on this page — one model and one reasoning-depth
> setting for every ✨ action, no provider table and no per-job bindings yet.
> The two tables below remain the target shape; the global setting is the
> `newsletter-compose` row's default, generalized to all jobs.
> Spec: `docs/superpowers/specs/2026-07-20-llm-model-selector-design.md`
>
> ```
> ┌─ AI Studio ── Model (real) ────────────────────────────┐
> │  Provider  [ Claude CLI ▾ ]   (only provider today)    │
> │  Model     [ Opus ▾ ]   Default·Opus·Sonnet·Haiku·     │
> │                         Fable·Custom…                  │
> │  Effort    [ High ▾ ]   Default·low·medium·high·       │
> │                         xhigh·max                      │
> │  ⓘ Applies to every ✨ action.        [ Save ]         │
> └────────────────────────────────────────────────────────┘
> ```

The requirement: swap LLMs per job, keep manual always possible, and stay open
to **local** solutions — LM Studio, Ollama, any OpenAI-compatible endpoint —
as first-class peers of Claude CLI, hosted APIs, and MCP-proxied backends.
Two tables, one binding.

```
PROVIDER PROFILES                                            [+ Add provider ▾]
┌──────────────────────────────────────────────────────────────────────────────┐
│ NAME             KIND            CONNECTION              STATUS              │
│ Claude CLI       text (CLI)      `claude -p` (subscr.)   ✓ v2.4 found        │
│ LM Studio local  text (OpenAI-c) http://localhost:1234   ✓ model: qwen3-32b  │
│ Ollama local     text (OpenAI-c) http://localhost:11434  ✓ model: llama4     │
│ OpenAI API       text (API)      key ****7a               ✓                  │
│ ElevenLabs       voice/TTS       key ****k2               ✓ 71k chars left   │
└──────────────────────────────────────────────────────────────────────────────┘
   kinds: CLI · OpenAI-compatible HTTP (covers LM Studio/Ollama/vLLM) ·
          vendor API · MCP server · voice/TTS · (Phase 4: image/ComfyUI)

JOB BINDINGS                                   (what runs when you press ✨)
┌──────────────────────────────────────────────────────────────────────────────┐
│ JOB                    DEFAULT PROVIDER   TEMPLATE               OVERRIDES    │
│ newsletter-compose     Claude CLI ▾       Newsletter v4 [edit]   —            │
│ yt-title               LM Studio ▾        Title ideas   [edit]   —            │
│ yt-description         Claude CLI ▾       YT descr v2   [edit]   tenant B: LM │
│ image-post-descr       LM Studio ▾        Img post      [edit]   —            │
│ patreon-teaser         Claude CLI ▾       Teaser        [edit]   —            │
│ video-script           Claude CLI ▾       Script beats  [edit]   —            │
│ voiceover-tts          ElevenLabs ▾       (voice: Aria) [edit]   —            │
└──────────────────────────────────────────────────────────────────────────────┘
```

Resolution order when you press ✨ anywhere:
**this-run choice (dropdown in the dialog) → tenant override → job default.**

Three consequences worth stating:

1. **Swapping the newsletter LLM is editing one cell.** No screen that uses
   ✨ knows which provider is behind it.
2. **"Manual" is not a mode — it's the absence of pressing ✨.** Every
   AI-fillable field is an ordinary editable field; AI only ever proposes
   text into it. Voiceover's "I record it myself" is the same idea for audio.
3. New provider kinds (image gen via ComfyUI in Phase 4) join as
   `kind: image` profiles + new jobs ("thumbnail-gen") — same two tables.

This maps 1:1 onto the existing architecture: profiles are `ILlmBackend`
implementations, jobs are today's `PromptTemplate` kinds grown up, and the
binding table is the new piece.

## Open questions

1. Custom assisted-site recorder (site #4): build the field-mapping UI, or is
   "I'll write a small C# recipe class per site" fine for a developer-owner?
   The recorder is the biggest optional build in these mockups.
   *(Status 2026-07-18: still open — "not sure".)*
2. Job templates: keep the current clone-and-edit prompt templates per tenant,
   or introduce versioning with rollback (AI Studio shows `Newsletter v4`)?
3. Cost/usage tracking per provider (tokens, TTS chars) — nice-to-have panel
   here, or skip while Claude CLI is subscription-flat?
