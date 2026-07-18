# 10 — Channels & AI Studio (the modularity core)

Two settings pages carry your whole modularity requirement: **Channels**
(where posts go) and **AI Studio** (which model does which job). Both are
"add a row, not a feature" by design.

## 🔌 Channels

```
🔌 Channels — AIVisions                                        [+ Add channel ▾]
┌──────────────────────────────────────────────────────────────────────────────┐
│ CHANNEL        MODE         ACCOUNT/TARGET        COLOR  STATUS        DEFAULTS│
│ ●Y YouTube     ⚡ Auto      @AIVisions            🔴    token ✓ 89d   [edit] │
│ ●N MailerLite  ⚡ Auto      Main list (1.2k)      🔵    key ✓         [edit] │
│ ●C Civitai     🤝 Assisted  browser profile ✓     🟢    recipe v3 ✓   [edit] │
│ ●P Patreon     ✋ Manual    (kit only)            🟠    —             [edit] │
├──────────────────────────────────────────────────────────────────────────────┤
│ per row: [Test] · [Disable] · capability mode is fixed by connector,         │
│ color/defaults/account are yours                                             │
└──────────────────────────────────────────────────────────────────────────────┘

[+ Add channel ▾]
   ⚡ YouTube · ⚡ MailerLite · 🤝 Civitai · ✋ Patreon kit · ⚡ Ko-fi
   ✋ Generic kit channel…            ← any future API-less platform, today
   🤝 Custom assisted site… (beta)   ← your unknown site #4:
      name, color, start URL, then a field-mapping step:
      "click into the site's Title field → app records selector" per field.
      Saved as a site recipe (versioned; re-record when the site changes).
```

- **Defaults per channel** = what prefills every new post: YouTube category/
  playlist/description footer; Civitai default tags; Patreon default tiers;
  MailerLite audience group.
- **Credentials** live in the OS keychain/DPAPI (per the Phase 1 decision),
  status column shows expiry *before* a 3 a.m. failure does.
- Channel color feeds every dot, chip and analytics line in the app.

## 🤖 AI Studio — providers × tasks

Your requirement: swap LLMs per job, keep manual always possible.
Two tables, one binding.

```
PROVIDER PROFILES                                            [+ Add provider ▾]
┌──────────────────────────────────────────────────────────────────────────────┐
│ NAME             KIND            CONNECTION              STATUS              │
│ Claude CLI       text (CLI)      `claude -p` (subscr.)   ✓ v2.4 found        │
│ LM Studio local  text (OpenAI-c) http://localhost:1234   ✓ model: qwen3-32b  │
│ OpenAI API       text (API)      key ****7a               ✓                  │
│ ElevenLabs       voice/TTS       key ****k2               ✓ 71k chars left   │
│ (disabled) Ollama text           http://localhost:11434  — offline           │
└──────────────────────────────────────────────────────────────────────────────┘

TASK BINDINGS                                  (what runs when you press ✨)
┌──────────────────────────────────────────────────────────────────────────────┐
│ AI TASK                DEFAULT PROVIDER   TEMPLATE               OVERRIDES    │
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
**this-run choice (dropdown in the dialog) → tenant override → task default.**

Three consequences worth stating:

1. **Swapping your newsletter LLM is editing one cell.** No screen that uses
   ✨ knows which provider is behind it.
2. **"Manual" is not a mode — it's the absence of pressing ✨.** Every
   AI-fillable field is an ordinary editable field; AI only ever proposes
   text into it. Voiceover's "I record it myself" is the same idea for audio.
3. New provider kinds (image gen via ComfyUI in Phase 4) join as
   `kind: image` profiles + new tasks ("thumbnail-gen") — same two tables.

This maps 1:1 onto the existing architecture: profiles are `ILlmBackend`
implementations, tasks are today's `PromptTemplate` kinds grown up, and the
binding table is the new piece.

## Open questions

1. Custom assisted-site recorder (site #4): build the field-mapping UI, or is
   "I'll write a small C# recipe class per site" fine for a developer-owner?
   The recorder is the biggest optional build in these mockups.
2. Task templates: keep the current clone-and-edit prompt templates per tenant,
   or introduce versioning with rollback (AI Studio shows `Newsletter v4`)?
3. Cost/usage tracking per provider (tokens, TTS chars) — nice-to-have panel
   here, or skip while Claude CLI is subscription-flat?
