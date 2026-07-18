# ContentAutomatorX

Multi-tenant content automation: pulls material from Reddit and RSS feeds,
generates drafts (newsletters, social posts, YouTube scripts) per configurable
**recipe** using the `claude` CLI, and delivers Markdown files into per-tenant
sync folders (OneDrive, Mega). Exposes an MCP server so Claude Code / LM Studio
can drive the whole system.

Docs: `docs/superpowers/specs/` (design) and `docs/superpowers/plans/` (implementation plan).

## Requirements

- .NET 10 SDK
- [Claude Code CLI](https://claude.com/claude-code) installed and logged in (`claude --version`)
- A local sync client folder (OneDrive / Mega) per tenant for delivered drafts

## Run

    dotnet run --project src/ContentAutomatorX.Web

Open http://localhost:5090. Data lives in `src/ContentAutomatorX.Web/data/contentx.db`;
logs in `src/ContentAutomatorX.Web/logs/`.

## Configure (appsettings.json)

| Key | Meaning | Default |
|---|---|---|
| `Urls` | listen address | `http://localhost:5090` |
| `Database:Path` | SQLite file path | `data/contentx.db` under the Web project |
| `Claude:Command` | claude executable | `claude` (set full path if not on PATH) |
| `Claude:Model` | model override | empty = CLI default |
| `Claude:TimeoutSeconds` | per-generation timeout | `300` |

## Quick start

1. **Tenant menu (top-right)** → create a tenant, then **Manage tenants** to set its voice profile and output folder (Verify folder). The menu also switches the whole app between tenants; the choice persists per browser.
2. **Sources** → add a subreddit and/or RSS feed; **Fetch now**.
3. **Recipes** → create a recipe (kind, selection rules, optional schedule); **Run now**.
4. The draft lands as Markdown in the tenant's output folder and under **Drafts**.

Cron schedules are UTC (Cronos syntax, e.g. `0 8 * * MON` = Mondays 08:00 UTC).
A scheduled recipe ingests its sources first, then generates — full auto.
A recipe or source with no prior run fires on the first scheduler tick after startup.
A failed scheduled run is not retried early — it waits for the next cron occurrence (check **Runs** for failures).

## MCP

The app exposes MCP (streamable HTTP) at `http://localhost:5090/mcp` with tools:
`list_tenants`, `get_tenant`, `list_sources`, `trigger_ingestion`, `list_content_items`,
`mark_item`, `list_recipes`, `get_recipe`, `run_recipe`, `list_drafts`, `get_draft`,
`get_pipeline_runs`.

Connect Claude Code:

    claude mcp add --transport http contentx http://localhost:5090/mcp

## Tests

    dotnet test
