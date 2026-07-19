# Duplicate-Link Protection in Ingestion — Design

**Date:** 2026-07-19
**Status:** Approved

## Problem

The ingestion pipeline dedups per source by `(SourceId, ExternalId)` only. Two gaps:

1. **Cross-source duplicates:** the same article URL surfaced by two different sources (e.g. an RSS feed and a Reddit source) is imported twice.
2. **Unstable identifiers:** connectors that use the raw URL as `ExternalId` (Website, RSS listing mode, LLM research) are vulnerable to rotating tracking params (`utm_*` etc.) producing a new `ExternalId` for the same page on re-fetch.

Additionally, skipped duplicates are invisible today — the run log only reports counts of fetched/new.

This is preventive hardening; no live duplicate bug has been observed.

## Solution Overview

Dedup tenant-wide on a **normalized URL**, in addition to the existing per-source `ExternalId` check. Record every skipped duplicate in the existing `PipelineRun` log. A filtered unique DB index acts as a backstop.

## Components

### 1. `UrlNormalizer` (Application layer)

Static class, single method `string? Normalize(string? url)`:

- Returns `null` for null, whitespace, or unparseable input.
- Lowercases scheme and host.
- Drops the fragment.
- Removes tracking query params: `utm_*`, `fbclid`, `gclid`, `ref`, `ref_src`, `igshid`, `mc_cid`, `mc_eid`.
- Sorts remaining query params alphabetically for stable ordering.
- Trims a trailing slash from the path (root `/` is kept).
- Preserves everything else (port, path casing, remaining query values) — over-aggressive normalization risks merging genuinely different pages.

### 2. Schema: `ContentItem.NormalizedUrl`

- New nullable string property, set at item creation from `Url` via `UrlNormalizer`.
- EF migration adds the column.
- **Backfill:** one-time C# pass at migration time — read all existing items, normalize their `Url`, save. Dataset is small (local SQLite app), so this is cheap and makes old rows fully participate in dedup.
- **Backstop index:** filtered unique index on `(TenantId, NormalizedUrl) WHERE NormalizedUrl IS NOT NULL`. Note the backfill must run before the index is created; if the backfill discovers pre-existing collisions, keep the oldest row's `NormalizedUrl` and null out later duplicates' so index creation succeeds (rows themselves are not deleted).

### 3. Pipeline changes (`IngestionPipeline`)

Per source, after the existing `ExternalId` dedup:

1. Normalize each fresh item's URL.
2. In-batch dedup: `DistinctBy` normalized URL among non-null values (first occurrence wins).
3. Query `ContentItems` for the tenant where `NormalizedUrl` is in the batch's set; matches are skipped, not inserted.
4. Items with a null `NormalizedUrl` bypass the URL check entirely (per-source `ExternalId` dedup still applies).

Sources are processed sequentially inside the per-tenant lock and each source saves before the next runs, so source #2 in the same run sees source #1's items — no intra-run race.

### 4. Logging

The per-source log line becomes:

```
{source}: fetched N, new X, skipped Y duplicate link(s)
```

followed by one entry per skip:

```
  duplicate: {url} (already imported {date} via {source name})
```

Stored in `PipelineRun.LogJson` as today; viewable wherever run logs are viewed. No UI changes.

## Error handling

- Unparseable URLs: `Normalize` returns `null`; item is treated as URL-less (ExternalId dedup only). Never throws.
- Unique-index violation (should be unreachable given the app-level check + tenant lock): the existing per-source catch in `RunCoreAsync` already marks the source failed and rolls back its added items; no new handling needed.

## Testing

- **Unit — `UrlNormalizer`:** tracking-param stripping, fragment removal, scheme/host casing, trailing slash, query-param ordering, null/blank/garbage input, root path preserved.
- **Integration — `IngestionPipelineTests` style:**
  - Same URL from two different sources → one `ContentItem`, skip logged with originating source name.
  - Same URL differing only by `utm_*` params → skipped.
  - Items with no URL → unaffected by the new check.
  - Re-run of the same source → still no duplicates, metadata refresh still works.

## Out of scope

- UI changes (run log is already viewable).
- Retroactive cleanup/merging of pre-existing duplicate items.
- Cross-tenant dedup.
