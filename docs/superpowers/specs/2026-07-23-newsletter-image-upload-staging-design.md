# Newsletter Image Upload & Local Staging — Design (PR 1 of 3)

**Status:** Approved, implementing.
**Date:** 2026-07-23
**Relates to:** [#25](https://github.com/Little-God1983/ContentAutomatorX/issues/25) (image hosting) and [#26](https://github.com/Little-God1983/ContentAutomatorX/issues/26) (video-thumbnail compositing). Extends the newsletter-templates feature (`2026-07-21-newsletter-html-templates-design.md` §1.2, which deferred image hosting) and the captured brainstorm `2026-07-21-image-hosting-notes.md`.

## 1. Goal

Give the composer a real **image-upload path** and pull every user-chosen newsletter image **onto local disk** the moment it is chosen — whether it came from a file upload or a pasted URL. Today a section image is a free-text `https://` URL the user pastes ([`IssueSection.ImageUrl`](../../../src/ContentAutomatorX.Domain/Entities/IssueSection.cs#L11)), which hotlinks off other people's servers.

This is **the first of three PRs** that together deliver #25 + #26:

| PR | Scope | Shippable on its own? |
|----|-------|-----------------------|
| **PR 1 (this spec)** | Local staging store + upload/import UI + `ImageKey` model + preview | Reviewable & fully unit-testable. **Not** end-to-end sendable — a staged image is omitted from a pushed draft until PR 2 (see §8). |
| PR 2 (#25 core) | `IImageStore` over Cloudflare R2, per-tenant R2 creds (DPAPI), **promotion at `PushDraftAsync`** | Uploaded images actually ship. |
| PR 3 (#26) | `IThumbnailComposer` (ImageSharp), wire `IYouTubeThumbnailResolver`, YouTube still + play-button overlay → emits a staged image PR 2 promotes | Video thumbnails get baked-in play buttons. |

The elegant part of the roadmap: **PR 1's staging store is the foundation both later PRs sit on.** PR 3 needs no new storage plumbing — compositing just writes bytes into the same staging store, and PR 2's promotion carries them to R2.

### 1.1 The rule this preserves

The composer's existing guarantee is unchanged: **every write to a section is explicitly chosen, one section at a time.** Setting a section's image is one such explicit action. Nothing here writes to a section implicitly.

## 2. Decisions

**D1 — Disk-first, not upload-to-R2-immediately.** A chosen image is staged on local disk in the composer; it is uploaded to R2 only when the draft is posted to MailerLite (PR 2). Rejected: eager upload on selection, which fills R2 with objects for abandoned uploads and never-sent drafts and forces an orphaned-object sweep job. The app runs locally with a real filesystem (unlike the Cloudflare-Workers-hosted marketing site the brainstorm notes worried about), so disk staging is available and cheap. `PushDraftAsync` — the app's only send-side action ([`IMailerLiteClient.cs:10`](../../../src/ContentAutomatorX.Domain/Abstractions/IMailerLiteClient.cs#L10)) — is a user-initiated moment where promotion can run with a progress UI; after it, MailerLite owns the scheduled send and the app never touches the images again.

**D2 — One image slot per section; source is irrelevant once staged.** A file upload and a URL import both result in the same thing: bytes on disk under `ImageKey`. The section's image can be replaced or removed. Rejected: separate "uploaded" and "pasted-URL" concepts kept simultaneously — more state, a confusing card, two rendering rules. This also advances #25's core goal (get images off third-party servers) for the paste-URL path, not just for uploads.

**D3 — `ImageKey` is an opaque staging filename, not a content hash.** Unique per upload (a fresh `Guid`-based name, exactly like [`Tenant.AvatarPath`](../../../src/ContentAutomatorX.Domain/Entities/Tenant.cs#L14) / [`TenantAvatarStore`](../../../src/ContentAutomatorX.Web/Services/TenantAvatarStore.cs)). Rejected for PR 1: content-addressed keys. Content-addressing only earns its keep for **R2 promotion idempotency**, which is PR 2's concern; PR 2 computes the content hash at promotion time. Unique names make PR 1's cleanup trivially safe (no two sections ever share a staging file, so deleting one section's image can never orphan another's).

**D4 — URL import hard-fails on any download/validation error.** If the fetched bytes aren't a valid image of an allowed type, or the download 404s / times out, the import is **rejected**: the slot is left unchanged, an error is shown, and **no raw URL is stored**. Rejected: silently falling back to a hotlink. The user's decision — every user-chosen image must be locally staged; a broken import is a fixable error, not a degraded success. (Note the interim consequence in §8: pasting a URL no longer produces a hotlink, so a URL image will not appear in a *pushed* draft until PR 2. This is the accepted cost of shipping PR 1 first.)

**D5 — `ImageUrl` is retained as the legacy / auto-metadata hotlink fallback.** It is no longer written by the card's URL field (that field now *imports*). It still holds: (a) values auto-populated from source-item metadata when topics are generated — [`ImageUrl = MetadataImage(item)`](../../../src/ContentAutomatorX.Application/Services/IssueComposerService.cs#L84) — and (b) pasted URLs on drafts that predate this feature. These are **not** eagerly downloaded (that would mean dozens of network calls during generation, many for topics the user discards). They render as hotlinks via the fallback until the user edits the section or PR 2 handles them. The renderer prefers `ImageKey` over `ImageUrl`.

**D6 — Rendering takes an `imageSrc` resolver delegate; preview and push resolve differently.** The static renderers cannot hardcode how an image resolves, because the composer preview must point at the local `/newsletter-images/{key}` endpoint while a pushed draft must not (a relative path is a dead image in email). So the caller supplies `Func<IssueSection, string?>`. Rejected: the renderer reaching into a Web service or a hardcoded URL scheme — it would couple `Application` to Web and make preview vs. push indistinguishable.

**D7 — The staging store mirrors `TenantAvatarStore` and lives in Web.** It needs `IWebHostEnvironment` (content root) and consumes `IBrowserFile`, both Web concerns, exactly as the avatar store does. Rejected: putting it behind a `Domain.Abstractions` seam in PR 1 — that seam is PR 2's `IImageStore` (R2), a different lifecycle (published, not staged). Keeping PR 1's store as a plain Web service matches the closest existing precedent and keeps the PR small.

## 3. Data model

Add one nullable column to `IssueSection`:

```csharp
public string? ImageKey { get; set; }   // staging file name under data/newsletter-images; null = none
```

- EF Core migration `NewsletterImageKey` (additive, nullable — no backfill).
- `ImageUrl` is unchanged in the schema (see D5).
- Semantics: the renderer resolves a section's image as **`ImageKey` (staged) preferred, else `ImageUrl` (hotlink fallback), else — for `Video` — the YouTube-derived still**.

## 4. Staging store — `NewsletterImageStagingStore`

A Web service, the newsletter-image twin of `TenantAvatarStore`.

```
data/newsletter-images/{guid}{ext}     served at  /newsletter-images/{file}
```

- `const string RequestPath` — but the value is also needed by the `Application` renderer to build preview URLs, and Web cannot be referenced from Application. So the canonical constant lives in **Application** (e.g. `NewsletterImageStaging.RequestPath = "/newsletter-images"`), and the Web store references it.
- Directory `Path.Combine(env.ContentRootPath, "data", "newsletter-images")`, created on construction.
- Served via a second `PhysicalFileProvider` static-file mapping in `Program.cs`, alongside the existing `/avatars` mapping ([`Program.cs:156`](../../../src/ContentAutomatorX.Web/Program.cs#L156)).
- Content-type → extension allow-list, identical to the avatar store: `image/png`→`.png`, `image/jpeg`→`.jpg`, `image/webp`→`.webp`, `image/gif`→`.gif`. Anything else rejected.
- Size cap: **5 MB** (matches the avatar store).

Members:

| Member | Purpose |
|--------|---------|
| `Task<string> SaveAsync(IBrowserFile file, CancellationToken)` | Persist an uploaded file → returns the new file name. Throws `InvalidOperationException` (user-facing message) on unsupported type / too large. |
| `Task<string> SaveFromUrlAsync(string url, CancellationToken)` | Download → validate → persist → returns the new file name. Throws `InvalidOperationException` on any failure (D4). |
| `void Delete(string? key)` | Best-effort delete; path-traversal-guarded (`Path.GetFileName(key) == key`); never throws. |
| `static string? UrlFor(string? key)` | `null` or `"/newsletter-images/{key}"`. |
| `string DirectoryPath` | For the static-file mapping. |

### 4.1 `SaveFromUrlAsync` validation

1. URL must be absolute `http`/`https`.
2. `GET` via a shared `HttpClient` with a short timeout (e.g. 10 s) and a byte cap read (abort past 5 MB).
3. The response's `Content-Type` must be in the allow-list **and** the leading bytes must match the corresponding image magic number (don't trust the header alone — a server can lie or return an HTML error page as `image/png`). Reject on mismatch.
4. Persist with the allow-listed extension.

## 5. Composer service — `IssueComposerService`

New methods (each: stage the bytes, set `ImageKey`, clear `ImageUrl`, best-effort-delete the section's *prior* staged file, save):

```csharp
Task SetSectionImageFromUploadAsync(Guid sectionId, IBrowserFile file, CancellationToken ct = default);
Task SetSectionImageFromUrlAsync(Guid sectionId, string url, CancellationToken ct = default);
Task ClearSectionImageAsync(Guid sectionId, CancellationToken ct = default);   // clears ImageKey + ImageUrl, deletes staged file
```

Because the staging store is a Web service and `IssueComposerService` is in `Application`, the actual file I/O is done by the store; the service is handed the resulting **key** (staged) rather than the store itself — i.e. the Razor page calls the store, then calls a thin `SetSectionImageKeyAsync(sectionId, key)`. *Chosen shape:* the Razor page (`IssueEditor`) orchestrates `store.SaveAsync(...)` → `composer.SetSectionImageKeyAsync(sectionId, key)`, mirroring how tenant avatars are saved then persisted. `SetSectionImageFromUrlAsync` likewise resolves to store + persist at the page layer. (The `From*` names above describe the page-level flow; the service surface is the single `SetSectionImageKeyAsync` + `ClearSectionImageAsync`.)

- The existing `UpdateSectionAsync(..., imageUrl, ...)` **stops writing `ImageUrl` from the card.** The card no longer binds a stored URL field; the `imageUrl` parameter is dropped from the card's edit path (or ignored). Auto-metadata population of `ImageUrl` in `AddTopicsFromItemsAsync` is unchanged.
- On delete of a section/post, best-effort-delete its staged file (safe — unique names).

## 6. Preview resolution — the renderers

`SectionHtmlRenderer.Render` / `RenderSection` / `TemplateHtmlRenderer.Render` gain a parameter:

```csharp
Func<IssueSection, string?> imageSrc
```

Inside `AppendSection`, every `s.ImageUrl` read becomes `imageSrc(s)`; the emitted `<img>` is written when it returns non-null (the caller is trusted to return a valid src — the `IsHttpUrl` gate on pasted URLs moves *into* the resolver). `VideoThumbnail` becomes `imageSrc(s) ?? youTubeFallback(s.LinkUrl)`, preserving the YouTube derivation.

Two resolvers:

- **Preview** (built in `IssueComposerService.RenderPreviewAsync`, or handed in from the page):
  `s => s.ImageKey is {} k ? NewsletterImageStaging.RequestPath + "/" + k : (IsHttpUrl(s.ImageUrl) ? s.ImageUrl : null)`
- **Push / send** (PR 1): `s => IsHttpUrl(s.ImageUrl) ? s.ImageUrl : null` — a staged `ImageKey` resolves to `null` and is **omitted**; hotlinks and (via the `?? youTubeFallback`) YouTube stills still render. **PR 2 replaces this with the R2 resolver.**

The existing default behavior (no resolver / call sites not yet updated) must keep compiling — provide an overload or a default resolver equal to the push resolver so unmodified callers behave exactly as today.

## 7. UI — `SectionCard.razor`

For `Topic`, `Sponsor`, `Video` (the existing `HasImage()` set):

- One **image slot**: a thumbnail of the current image (staged → `UrlFor(ImageKey)`; else hotlink `ImageUrl`) with **Replace** and **Remove**.
- A `MudFileUpload` (accept `image/*`, the allow-list enforced server-side) → on file selected, upload + persist, then refresh preview.
- A URL **import** field + button (label: e.g. "Import from URL") → `SetSectionImageFromUrlAsync`; on failure show the error, leave the slot unchanged.
- Inline hint shown under a **staged** image only: *"Shown in preview — will be hosted when you post to MailerLite (coming soon)."* No hint for a plain hotlink (it already works in email).
- Busy/disabled handling consistent with the card's existing `Busy` gating.

## 8. Interim behavior (PR-1-only window) — explicit

Until PR 2 merges:

- A **staged** image (`ImageKey`) appears in the **composer preview** but is **omitted from a pushed MailerLite draft** (no host for it yet). The inline hint (§7) tells the user.
- This is a temporary **regression for the paste-URL path specifically**: today paste-URL → hotlink → appears in email; under PR 1, URL import → staged → does not appear in a pushed draft until PR 2. Auto-metadata and legacy `ImageUrl` hotlinks are unaffected (they still render in push).
- Mitigation: the hint; and PR 2 is the immediate next PR. If this window is unacceptable, PR 1 and PR 2 can be merged together — but the plan is to ship them in sequence.

## 9. Layering

- `Application`: the `imageSrc` resolver parameter + the `NewsletterImageStaging.RequestPath` constant + composer persist methods. No Web, no file I/O of image bytes.
- `Web`: `NewsletterImageStagingStore` (disk I/O, `IBrowserFile`, `HttpClient` download), the static-file mapping, the card UI.
- `Domain`: the `ImageKey` column.
- No new Infrastructure dependency in PR 1 (no AWS SDK, no ImageSharp — those are PR 2 / PR 3).

## 10. Testing

**Unit**
- `NewsletterImageStagingStore`: save accepts allow-listed types and rejects others / oversize; `Delete` is traversal-guarded and best-effort; `UrlFor` formatting.
- `SaveFromUrlAsync`: happy path stages the file; rejects non-image content-type, magic-byte mismatch (HTML-as-`image/png`), oversize, and non-absolute URLs. (Network faked via a stub `HttpMessageHandler`, as `StubHttpHandler` already does elsewhere.)
- Renderer resolver selection: staged key → `/newsletter-images/...`; else hotlink; else (video) YouTube fallback; push resolver omits a staged key.

**Integration**
- Upload path: `SetSectionImageKeyAsync` sets `ImageKey`, clears `ImageUrl`; preview HTML contains `/newsletter-images/{key}`.
- URL import happy path stages + sets `ImageKey`; failure path throws and leaves the section unchanged.
- Push render omits a staged image but still emits a hotlink `ImageUrl` and a YouTube fallback.
- Replace/remove/section-delete deletes the prior staged file.

## 11. Out of scope (PR 1)

- Any R2 / AWS SDK / hosting (PR 2).
- Promotion at `PushDraftAsync` and its failure handling (PR 2).
- Per-tenant R2 credentials + settings UI (PR 2).
- ImageSharp video-thumbnail compositing and wiring `IYouTubeThumbnailResolver` (PR 3).
- Migrating `TenantBranding.LogoUrl` to uploaded storage (kept a pasted URL for now).
- Content-addressed keys / dedup (PR 2 concern).
