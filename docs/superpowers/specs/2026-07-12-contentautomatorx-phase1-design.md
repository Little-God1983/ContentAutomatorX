# ContentAutomatorX — Phase 1 Design

**Date:** 2026-07-12
**Status:** Approved design, pending implementation plan
**Repo:** https://github.com/Little-God1983/ContentAutomatorX

## 1. Purpose

ContentAutomatorX is an automated content creation and distribution system. It gathers material from predefined sources (Reddit, RSS feeds; later websites and more), uses an LLM to prepare content drafts (newsletters, social media posts, YouTube video scripts) per tenant, and delivers them as files into local sync folders (OneDrive, Mega). Later phases add publishing to platforms (YouTube, Patreon, Civitai, Ko-fi), more LLM backends, and ComfyUI media generation.

It runs locally on the owner's Windows machine but is architected so the same deployable can later run on a server unchanged.

### Phase 1 scope (this spec)

- Multi-tenant core: tenants as brand/channel profiles
- Ingestion from Reddit (public JSON endpoints) and RSS/Atom feeds
- Draft generation via Claude (`claude` CLI, subscription billing — no per-token API)
- Draft kinds: Newsletter, SocialPost, VideoScript
- Delivery as Markdown files into per-tenant sync folders
- Blazor Server web UI (localhost)
- Exposed MCP server (streamable HTTP) so Claude Code / LM Studio can drive the system

### Explicitly out of scope for Phase 1

- Publishing to any platform (Phase 2: YouTube, Patreon, Civitai, Ko-fi + credential store + approval flow)
- Generic website scraping and other source types (Phase 3)
- LM Studio / other LLM backends (Phase 3)
- ComfyUI audio/image generation (Phase 4)
- Authentication on the MCP endpoint or UI (localhost-only in Phase 1)

## 2. Architecture

Modular monolith. One solution, four projects, one deployable ASP.NET Core host (.NET 10 / C#).

```
ContentAutomatorX/
├─ src/
│  ├─ ContentAutomatorX.Domain/            # entities + abstractions, zero dependencies
│  │   ├─ Entities/        (Tenant, Source, ContentItem, Draft, PipelineRun, PromptTemplate)
│  │   └─ Abstractions/    (ISourceConnector, ILlmBackend,
│  │                        IPlatformConnector, IDraftDelivery)
│  ├─ ContentAutomatorX.Application/       # use cases & orchestration
│  │   ├─ Pipelines/       (IngestionPipeline, GenerationPipeline)
│  │   └─ Services/        (TenantService, SourceService, DraftService)
│  ├─ ContentAutomatorX.Infrastructure/    # all I/O implementations
│  │   ├─ Persistence/     (EF Core + SQLite, migrations)
│  │   ├─ Sources/         (RedditConnector, RssConnector)
│  │   ├─ Llm/             (ClaudeCliBackend; later LmStudioBackend)
│  │   ├─ Delivery/        (FileShareDraftDelivery → OneDrive/Mega sync folders)
│  │   └─ Platforms/       (empty in Phase 1; later one class per platform,
│  │                        native API or McpClientConnector proxy)
│  └─ ContentAutomatorX.Web/               # the single host
│      ├─ Components/      (Blazor Server UI, MudBlazor)
│      ├─ Mcp/             (exposed MCP server — streamable HTTP endpoint at /mcp)
│      └─ Jobs/            (hosted background service: per-tenant ingestion scheduler)
└─ tests/
   ├─ ContentAutomatorX.UnitTests/
   └─ ContentAutomatorX.IntegrationTests/
```

### Rules

1. **Dependencies point inward only.** Web → Application → Domain. Infrastructure implements Domain abstractions; DI wiring happens in Web. Application code has no knowledge of the hosting environment — porting to a server is a deployment decision, not a refactor.
2. **Every external system sits behind a Domain interface** — sources (`ISourceConnector`), LLMs (`ILlmBackend`), platforms (`IPlatformConnector`), file delivery (`IDraftDelivery`). Adding a platform later = one new class + DI registration. A platform connector implementation may internally be an MCP client proxy to an external MCP server; orchestration code cannot tell the difference. Python components (e.g. ComfyUI later) are wrapped as Infrastructure implementations that shell out or call a local HTTP API.
3. **The MCP server is a thin adapter.** MCP tools call the same Application services the Blazor UI calls; no business logic in the MCP layer.
4. **Scheduler runs in-process** as a hosted background service polling per-tenant cron schedules. No external cron; survives the server port unchanged.

### Tech stack

- .NET 10 / C#, ASP.NET Core, Blazor Server, MudBlazor
- EF Core + SQLite (code-first migrations)
- Official `ModelContextProtocol` C# SDK for the exposed MCP endpoint
- Serilog (rolling file logs)
- `HttpClientFactory` with resilience handlers (retry ×3, exponential backoff)

## 3. Data Model

SQLite via EF Core. Every tenant-owned row carries `TenantId`.

### Tenant
- `Id`, `Name`, `Slug`, `IsActive`
- `VoiceProfile` — freeform text (tone/style/audience), injected into every generation prompt
- `OutputFolderPath` — per-tenant sync folder (OneDrive or Mega client folder)

### Source
- `Id`, `TenantId`, `Type` (`Reddit` | `Rss`; stored as string, extensible), `DisplayName`
- `Config` — JSON column (Reddit: subreddit, sort, timeframe; RSS: feed URL)
- `ScheduleCron`, `IsEnabled`, `LastFetchedAt`

### ContentItem
- `Id`, `TenantId`, `SourceId`, `ExternalId` — unique index on `(SourceId, ExternalId)` for dedup
- `Title`, `Url`, `Author`, `Body` (extracted text), `Metadata` (JSON: score etc.), `PublishedAt`, `FetchedAt`
- `Status`: `New` → `Selected` | `Ignored` → `Used`

### Draft
- `Id`, `TenantId`, `Kind` (`Newsletter` | `SocialPost` | `VideoScript`; string, extensible)
- `Title`, `Body` (Markdown), `TargetPlatform` (nullable string, e.g. `Patreon`)
- `SourceItemIds` (JSON array of ContentItem ids — provenance)
- `FilePath` (delivered location), `Status`: `Generated` → `Delivered` (later: `Approved`, `Published`)
- `CreatedAt`, `ModelUsed`

### PipelineRun
- `Id`, `TenantId`, `Kind` (`Ingestion` | `Generation`), `Trigger` (`Scheduled` | `Manual` | `Mcp`)
- `StartedAt`, `FinishedAt`, `Status` (`Running` | `Succeeded` | `Failed` | `Partial`)
- `Log` — JSON: per-step messages, item counts, per-source errors

### PromptTemplate
- `Id`, `TenantId` (nullable → system default), `Kind`
- `Template` — text with placeholders: `{voice_profile}`, `{items}`, `{extra_instructions}`

### Decisions
- Dedup at ingest via the `(SourceId, ExternalId)` unique constraint; re-fetches never duplicate.
- Drafts are files **and** rows: the file is the reviewable artifact; the row carries provenance (items, prompt, model) so later publishing has everything it needs. The DB is the source of truth; files are projections.
- JSON config columns keep the schema stable as connector types grow — no migration per new platform.
- No platform credentials in Phase 1. Later they use a per-tenant credential store backed by DPAPI/OS keychain, never plaintext SQLite.

## 4. Pipelines

### Ingestion (per tenant, per source; triggers: scheduler, UI, MCP)

1. Scheduler finds due sources → `ISourceConnector.FetchAsync(source)` per source.
2. Reddit: public JSON endpoint `reddit.com/r/{sub}/{sort}.json` with a proper User-Agent and rate limiting. RSS: fetch + parse with `If-Modified-Since`.
3. New items (dedup via `ExternalId`) stored as `ContentItem(Status=New)`.
4. `PipelineRun` records counts and per-source errors; one failing source never blocks others (run status `Partial`).

### Generation (per tenant + draft kind; triggers: UI, MCP, optional schedule)

1. Input selection: default = top-N `New`/`Selected` items since the last generation of that kind (N configurable per tenant, default 10); or hand-picked item ids from UI/MCP.
2. Prompt built from the tenant's `PromptTemplate` for the kind: voice profile + item titles/bodies/links + kind-specific instructions (newsletter structure; Patreon-style post format; YouTube script beats: hook/intro/sections/outro/CTA).
3. `ILlmBackend.GenerateAsync(prompt)` → `ClaudeCliBackend` runs `claude -p` headlessly (non-interactive, JSON output mode, configurable timeout default 5 min, stderr captured, one retry on transient failure). Subscription billing; no API key.
4. Result saved as `Draft`, then `IDraftDelivery` writes `{date}-{kind}-{slug}.md` with YAML front-matter (tenant, kind, source items, model) into the tenant's output folder; the sync client uploads it.
5. Used items flip to `Used`; `PipelineRun` logs prompt size, duration, output path.

### Concurrency
- One pipeline run per tenant at a time (per-tenant semaphore). Ingestion and generation are separate runs; a slow LLM call never blocks fetching.

### Later-phase seams
- Publishing = a third pipeline: `Draft(Approved)` → `IPlatformConnector.PublishAsync`.
- ComfyUI = a fourth: `Draft(VideoScript)` → audio/image assets.
- Both reuse the same run/audit machinery.

## 5. Exposed MCP Server

Official `ModelContextProtocol` C# SDK; streamable HTTP at `/mcp` on the same host; binds to localhost in Phase 1 (auth added if/when it leaves the machine). Clients: Claude Code, LM Studio.

Tools (thin wrappers over Application services):

| Tool | Purpose |
|---|---|
| `list_tenants` / `get_tenant` | discover channels + voice profiles |
| `list_sources` | inspect a tenant's sources |
| `trigger_ingestion(tenantId, sourceId?)` | run fetches on demand |
| `list_content_items(tenantId, status?, since?)` | browse gathered material |
| `mark_item(itemId, status)` | curate (select/ignore) |
| `generate_draft(tenantId, kind, itemIds?, extraInstructions?)` | run generation; returns draft + file path |
| `list_drafts(tenantId, kind?, status?)` / `get_draft(draftId)` | read results |
| `get_pipeline_runs(tenantId, limit)` | audit/run history |

Notes:
- The MCP layer never touches the DB directly.
- Two brains, one machinery: the internal `ClaudeCliBackend` covers automated generation; an external agent (Claude Code via MCP) can drive interactive generation. Both produce identical `Draft` records.

## 6. UI (Blazor Server + MudBlazor)

1. **Dashboard** — per-tenant cards: new item counts, last runs, recent drafts, error badges
2. **Tenants** — CRUD, voice profile editor, output folder picker with write-access check
3. **Sources** — per tenant: add/edit Reddit/RSS sources, schedule, enable/disable, "fetch now", last-fetch status
4. **Content & Drafts** — item browser (select/ignore), "generate draft" dialog (kind + items + extra instructions), draft list with preview and open-file/folder actions
5. **Runs** — `PipelineRun` history with expandable logs (primary debugging view)

## 7. Error Handling

- **Connectors:** per-source try/catch; failures recorded in `PipelineRun.Log` and surfaced as UI badges; other sources continue. HTTP retry ×3 with exponential backoff; Reddit rate limits respected.
- **Claude CLI:** timeout, stderr capture, one retry; failed generation = `Failed` run with full error. Draft files written to temp then moved — never half-written.
- **Delivery:** unreachable output folder leaves the Draft `Generated` with the error logged; "retry delivery" action re-attempts.
- **Global:** every run auditable via `PipelineRun`; Serilog rolling file logs; recent-errors panel on the dashboard.

## 8. Testing

- **Unit:** prompt building, dedup, Reddit/RSS parsing against captured fixture payloads, scheduler due-date logic, file naming/front-matter. `ILlmBackend` faked.
- **Integration:** EF Core against temp SQLite files (real migrations); ingestion end-to-end with stubbed HTTP handlers; generation with fake LLM into temp folders; MCP tools invoked in-memory against the service layer.
- **Manual E2E:** one real tenant, one real subreddit + RSS feed, real `claude` CLI, drafts landing in the real OneDrive folder.

## 9. Roadmap

| Phase | Content |
|---|---|
| **1 (this spec)** | tenants, Reddit+RSS ingestion, Claude-CLI generation (newsletter / social post / video script), file delivery, Blazor UI, exposed MCP server |
| **2** | platform connectors: YouTube, Patreon, Civitai, Ko-fi (native API or MCP-proxied), credential store, publish pipeline, approval flow |
| **3** | more sources (generic website extraction, YouTube channels, news), LM Studio backend, per-tenant LLM backend choice |
| **4** | ComfyUI integration: AI audio for video scripts, thumbnails |

Each later phase gets its own brainstorm → spec → plan cycle.
