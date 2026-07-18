# ContentAutomatorX — Newsletter-First (Phase 2a) Design

**Date:** 2026-07-18
**Status:** Draft — awaiting owner review
**Depends on:** Phase 1 (shipped), UI shell mockup (shipped 2026-07-18, uncommitted)
**Mockups:** `docs/mockups/08-newsletter-flow.md` (flow), `10-platforms-ai-settings.md` (platforms), `README.md` §7–8 (decisions)

## 1. Goal

The weekly newsletter runs end-to-end with exactly one human step: **hitting
Send inside MailerLite.** Everything before that — gathering material from
feeds/sites/search, composing a news-first draft, creating the MailerLite
campaign — is automated or one-click. Success test: Saturday morning the draft
waits in the review queue; ≤15 minutes of polish later it's a MailerLite draft;
Send is clicked in MailerLite; the app records Sent + stats without being told.

Decisions honored (2026-07-18): newsletter is independent and news-first;
sponsor/Patreon/YouTube references are manual markdown, no cross-posting
machinery; Send stays human; local-only calendar unaffected.

## 2. Scope

### In (v1)

1. **Two new source types** behind the existing `ISourceConnector` seam:
   - `Website` — watch a page/listing, extract new article links + text
   - `LlmResearch` — a prompt run against an LLM backend with web access
     (Claude CLI `--tools websearch`); returns structured items (title, url,
     summary). Covers the "search engine" wish v1: the LLM does the searching.
2. **Minimal Platform + Post model** (the foundation later phases reuse):
   - `Platform` entity + Platforms page: real rows, but only the MailerLite
     connector is implemented; others stay the current "planned" table
   - `Post` entity: the publication intent (platform, status, schedule,
     external id/url, stats); a newsletter issue = one Post
3. **Issue editor** (v1 composer): markdown body + live HTML preview, subject
   + preview-text fields, `Regenerate ✨` (re-runs the automation's prompt),
   `Push to MailerLite ⚡`, `Open in MailerLite ↗`
4. **MailerLite connector** (first `IPlatformConnector`):
   - API key stored via DPAPI (new `ICredentialStore`), never in SQLite
   - Push = create/update a **draft** campaign (markdown → email-safe HTML)
   - Audience = one MailerLite group chosen in platform config
   - Hourly job detects campaign Sent → Post becomes Published; daily stats
     snapshot (sent/opens/clicks) for 30 days
5. **Today review queue** (first real Today section): posts flagged
   `needs review` (automation output) with `Review & edit` / `Push` actions
6. **Weekly automation** wiring: existing Newsletter recipe machinery gains
   `CreatePost` output mode — compose → `Post(Draft, needs review)` instead of
   (or in addition to) the file drop

### Out (deliberately)

- Dedicated search-engine API connector (Brave/SerpAPI) — `LlmResearch`
  covers it; revisit only if its results disappoint
- Section-outline composer UI, structured sponsor blocks — v1 sections are
  just markdown headings the prompt template produces
- Projects, Calendar, Library data, other platform connectors, AI Studio
  binding table (the newsletter uses the existing `ILlmBackend` directly;
  the Jobs table arrives with the second consumer)
- Auto-send. Ever, per current decision.

## 3. Data model changes

```
Platform      Id, TenantId, Type ("MailerLite"; string, extensible),
              DisplayName, ColorHex, ConfigJson ({groupId, fromName, fromEmail}),
              CredentialRef (name of DPAPI blob), IsEnabled
Post          Id, TenantId, PlatformId, DraftId (content payload, FK Draft),
              ProjectId (nullable — always null in v1, reserved),
              Kind ("Newsletter"), Title, Subject, PreviewText,
              Status: Draft → Pushed → Published | Failed,
              NeedsReview (bool), ScheduledAt?, PublishedAt?,
              ExternalId (campaign id), ExternalUrl, StatsJson, timestamps
Source.Type   += "Website", "LlmResearch" (Config JSON per type:
              Website {url, itemSelector?, mode: auto|selector};
              LlmResearch {prompt, maxItems})
Draft         unchanged (remains the text artifact; Post references it)
```

Rationale: `Post` is the future cross-platform entity — the newsletter is
its first tenant, not a special case. `Draft` stays untouched so Phase 1
behavior (files into sync folders) keeps working in parallel.

## 4. Components

### 4.1 WebsiteConnector (Infrastructure/Sources)

- Fetch page (existing resilient HttpClient), extract candidate items:
  `auto` mode = heuristic `<article>/<a>` harvesting with title + absolute
  URL + best-effort text (readability-style); `selector` mode = CSS selector
  from Config for the link list. Item body = fetched target page text
  (truncated ~8k chars).
- Dedup via existing `(SourceId, ExternalId)` with ExternalId = canonical URL.
- Respect robots-friendly pacing: 1 req/s per host, `If-Modified-Since`.

### 4.2 LlmResearchConnector (Infrastructure/Sources)

- Runs the configured prompt through `ILlmBackend` (Claude CLI with web
  search enabled), demanding strict JSON: `[{title, url, summary, source}]`.
- Each array entry becomes a `ContentItem` (ExternalId = url; Body = summary;
  Metadata notes `via: llm-research`). One retry on malformed JSON.
- Runs on the source's cron like any connector; failures land in
  `PipelineRun.Log` like any connector.

### 4.3 MailerLiteConnector (Infrastructure/Platforms — first of its kind)

- `IPlatformConnector` v1 surface (kept minimal on purpose):
  `TestAsync()`, `ListGroupsAsync()`, `PushDraftAsync(post, html)` →
  external id, `GetStatusAsync(externalId)` → Draft|Sent + stats.
- MailerLite REST (`connect.mailerlite.com/api`, Bearer key): create
  campaign (type regular, group, subject, from, HTML content); PUT to update
  while still draft; GET for status/stats. Rate limits are generous;
  standard retry policy applies.
- Markdown → HTML via Markdig into one fixed, email-safe template (inline
  styles, single column). Template file lives in the repo; per-tenant
  templates are a later nicety.

### 4.4 CredentialStore (Infrastructure)

- `ICredentialStore` (Domain abstraction): `Set(name, secret)`, `Get(name)`,
  `Delete(name)`. Implementation: DPAPI user-scope
  (`ProtectedData.Protect`), blobs under `%LOCALAPPDATA%/ContentAutomatorX/
  secrets/`. Platforms page writes keys through it; nothing secret in SQLite
  (Phase 1 decision).

### 4.5 UI

- **Platforms page**: MailerLite row becomes editable — enter API key
  (write-only field), `Test`, pick group from `ListGroupsAsync`, from-name/
  email, color. Other rows stay static "planned".
- **Posts page**: top section lists `Post` rows (newsletter issues) with
  status chips + `Edit` / `Open in MailerLite`; the existing Drafts list
  moves below a divider labeled "File drafts (Phase 1)". No removal.
- **Issue editor** (`/issue/{postId}`): two-pane markdown editor + HTML
  preview (rendered with the same email template), subject + preview-text
  inputs, `Regenerate ✨` (rerun recipe prompt; asks before overwriting
  edits), `Push to MailerLite ⚡` (create/update campaign → status Pushed,
  snackbar with `Open in MailerLite ↗`), `Re-push` while draft.
- **Today**: "Review queue" card (real) — posts with NeedsReview, plus
  Pushed posts as "waiting for your Send in MailerLite". Placeholder strip
  shrinks accordingly.

### 4.6 Automation wiring

- Recipe gains optional `TargetPlatformId`. When set, generation ends with
  `Post(Draft, NeedsReview=true)` linked to the produced `Draft` (file
  delivery still happens if configured — both outputs coexist).
- MCP: existing `run_recipe` picks this up automatically; add
  `list_posts(tenantId, status?)` + `push_post(postId)` tools so Claude Code
  can drive the same flow.

## 5. Error handling

- Connector failures: per-source isolation as today (`Partial` runs, UI badges).
- Push failures: Post → `Failed` with API error in `PipelineRun.Log`;
  `Retry` button on the post; draft campaign never half-created (create
  returns id before content update; update failure keeps id + retries).
- Key invalid/expired: `Test` surfaces it; push errors set a platform-level
  warning shown on Platforms and Today.
- Sent-detection job: silent-fail with log; catch-up on next tick (idempotent).

## 6. Testing

- **Unit:** Website extraction (fixture HTML, both modes), LlmResearch JSON
  parsing (incl. malformed), markdown→email-HTML template, credential store
  round-trip, post status transitions.
- **Integration:** MailerLite connector against a stubbed HTTP handler
  (create/update/status/stats fixtures); recipe → Post pipeline end-to-end
  with fake LLM; migrations for Platform/Post.
- **Manual E2E:** real MailerLite account (test group!), one weekly automation
  live for a real Saturday run; verify Sent detection + stats appear.

## 7. Open questions (answer during review)

1. **MailerLite audience**: one fixed group for v1 — which one, and is
   from-name/from-email per tenant enough?
2. **LlmResearch cadence**: same weekly tick as the newsletter automation, or
   a mid-week sweep too?
3. **Regenerate semantics**: after you've hand-edited a draft, `Regenerate`
   overwrite-with-confirm is proposed — or should it always produce a second
   draft to diff?
4. **Issue naming**: auto `AI Weekly #<n>` numbering per tenant, or free-form
   titles?
