# ContentAutomatorX — Structured Issue Composer (Phase 2b) Design

**Date:** 2026-07-19
**Status:** Approved in brainstorming 2026-07-19; not yet implemented
**Depends on:** Phase 2a (newsletter-first, shipped 2026-07-18)
**Mockups:** `docs/mockups/12-issue-composer.md`
**Supersedes:** the free-markdown Issue editor (`IssueEditor.razor`) and the
Inbox `Generate draft` → file-delivery flow (`Content.razor.RunWithSelection`)

## 1. Goal

The current newsletter flow gives near-zero control over the result: the LLM
produces one opaque markdown blob, and the Inbox path's only output is a file
write that errors on any tenant without `OutputFolderPath`. Redesign both
entry points into **one structured Issue Composer** where an issue is an
ordered list of typed sections — header, topics, sponsor, button, divider,
footer — that the user can edit, reorder, add, remove, and selectively
regenerate, with a live preview that is pixel-identical to what MailerLite
receives.

Success test: select items in the Inbox → land in the composer with one topic
per item, blurbs filled by AI → move a topic, drop in a sponsor block, tweak
the header → Push ⚡ → the draft campaign in MailerLite looks exactly like the
preview. No file write, no `OutputFolderPath`, no error.

## 2. Decisions (brainstormed 2026-07-19)

| # | Question | Decision |
|---|---|---|
| 1 | Topic model | **1 inbox item = 1 topic.** Predictable; count controlled by selection. |
| 2 | Header/footer | **Tenant defaults, editable per issue.** Defaults live on the tenant; every new issue prefills from them. |
| 3 | Sponsorship | **Dedicated sponsor block** (name, blurb, link, logo), insertable at any position, one-off per issue. No sponsor library yet. |
| 4 | Entry points | **Converge.** `+ New → Newsletter issue…` and Inbox `Create newsletter` both land in the same composer. |
| 5 | Topic actions | Edit + delete, **reorder, add manual topic, per-topic ✨ regenerate, pull in more inbox items later** — all in v1. |
| 6 | Layout | **Side-by-side**: structure panel left, live email preview right. |
| 7 | File output | **No automatic file delivery.** `Export .md` button (browser download) on demand. Removes the `OutputFolderPath` error by construction. |
| 8 | Visual scope | Images in topics, button/CTA block, tenant branding (accent color, logo, font), dividers — **all rendered as email-safe HTML that any ESP accepts** (approach A data model, approach C fidelity). |

## 3. MailerLite constraints (researched 2026-07-19)

- Campaign content via API is **one flat HTML string** — no block structure.
  All composition structure therefore lives in our app; MailerLite only ever
  receives rendered HTML.
- The HTML **must contain an unsubscribe link** (`{$unsubscribe}` variable)
  plus account name, address, and country — otherwise MailerLite appends its
  own default footer.
- Custom HTML content via API requires the **Advanced plan** (verify against
  the real account during E2E).
- A campaign pushed as custom HTML is editable in MailerLite only in their
  HTML editor, not the visual builder. Fine — editing happens in our composer;
  MailerLite is for Send.

## 4. Scope

### In (v1)

1. **`IssueSection` model** — ordered, typed sections per Post (see §5).
2. **Issue Composer page** replacing `IssueEditor.razor` at `/issue/{postId}`:
   structure panel (cards with edit/reorder/delete/✨), live preview pane,
   subject/preview-text/✨ subjects (kept from today), Save, Export .md,
   Push ⚡, Open in MailerLite ↗.
3. **Converged entry points:**
   - Inbox: select items → `Create newsletter` (recipe picker filtered to
     newsletter-kind recipes) → Post + skeleton sections created → navigate
     to composer → one LLM call fills blurbs in place (progress shown).
   - `+ New → Newsletter issue…`: dialog (recipe, title) → Post with
     header/footer only → composer; topics come via `Topic from inbox…`
     picker or manual add.
4. **Section renderer** (`SectionHtmlRenderer`): sections → email-safe HTML —
   600 px single column, table layout, inline styles only, absolute image
   URLs with alt text, bulletproof table-buttons, curated email-safe font
   stacks, tenant branding injected at render time, ESP-neutral
   `%%UNSUBSCRIBE%%` token. One renderer feeds preview, push, and (via
   markdown concat, not HTML) export.
5. **Tenant newsletter settings**: branding (accent color hex, logo URL,
   font key from curated list), default header/footer markdown, sender
   identity line (name, address, country). Compliance footer (sender
   identity + unsubscribe) is always rendered below the footer section —
   not user-removable.
6. **Generation contract**: one LLM call returning strict JSON
   `[{itemId, title, blurb}]`, one retry on malformed output (same pattern
   as `LlmResearchConnector`). Recipe prompt template still supplies
   voice/tone; extra-instructions box stays. Per-topic ✨ = single-section
   call with optional instruction; header ✨ writes an intro referencing
   current topics.
7. **Legacy compatibility**: a Post with `DraftId` but no sections renders
   as one `LegacyBody` section (read/edit/push keep working).

### Out (deliberately)

- Sponsor library (recurring sponsors) — revisit when the same sponsor
  appears twice.
- Image upload/hosting — images are **by absolute URL only** in v1 (topic
  images prefill from source-item metadata when available).
- Drag-and-drop reordering — v1 ships ↑/↓ buttons; drag is a nicety on top
  of the same position math.
- Automatic file delivery for newsletters (replaced by Export). File
  delivery machinery stays untouched for other recipe kinds.
- Any second ESP connector — but the renderer's ESP-neutral token design is
  the seam for one.
- Auto-send. Still never.

## 5. Data model changes

```
IssueSection  (new table)
  Id            Guid PK
  PostId        Guid FK → Post (cascade delete)
  Position      int (0-based, contiguous per post)
  Type          string: Header | Topic | Sponsor | Button | Divider |
                Footer | LegacyBody
  Title         string?   — topic heading / sponsor name (null: divider/button)
  BodyMd        string?   — markdown copy (null: divider/button)
  ImageUrl      string?   — topic image / sponsor logo (absolute URL)
  LinkUrl       string?   — read-more / sponsor target / CTA target
  LinkText      string?   — CTA label
  SourceItemId  Guid? FK → ContentItem (set for inbox-born topics)

Tenant  (additions)
  DefaultHeaderMd   string ("")
  DefaultFooterMd   string ("")
  BrandingJson      string ("{}") — { accentColorHex, logoUrl, fontKey }
  SenderIdentity    string ("")  — "Name, street, city, country" for the
                                    compliance footer

Post    unchanged. DraftId becomes legacy-only (see §4.7).
```

Invariants: exactly one Header and one Footer per issue (created with the
Post, not deletable, only editable); positions are compacted on every
insert/delete/move.

## 6. Components

### 6.1 IssueComposerService (Application)

- `CreateFromItemsAsync(tenantId, recipeId, itemIds, title)` → Post +
  Header (tenant default) + one skeleton Topic per item (Title/LinkUrl/
  ImageUrl prefilled from the ContentItem) + Footer (tenant default).
- `CreateEmptyAsync(tenantId, recipeId, title)` → Post + Header + Footer.
- `GenerateTopicsAsync(postId, extraInstructions?)` → one LLM call (strict
  JSON, one retry) → fills **only Topic sections with empty BodyMd**
  (skeletons), setting BodyMd (+ Title when the LLM improves it). Hand-
  edited blurbs are never touched by bulk generation — per-topic ✨ is the
  sole overwrite path, and it acts on one explicitly chosen section.
- `RegenerateSectionAsync(sectionId, instruction?)` → single-section call.
  For Header: intro referencing current topic titles.
- `AddSectionAsync / RemoveSectionAsync / MoveSectionAsync / UpdateSectionAsync`
  — plain list operations with position compaction.
- `AddTopicsFromItemsAsync(postId, itemIds)` — the "pull in more items"
  picker path; skeletons + generation for just the new topics.
- `ExportMarkdownAsync(postId)` → markdown concat of sections (for browser
  download).

### 6.2 SectionHtmlRenderer (Application)

- Pure function: `(sections, branding, senderIdentity, title) → HTML string`
  containing `%%UNSUBSCRIBE%%`.
- Reuses the existing `EmailHtmlRenderer` markdown pipeline (Markdig) for
  each section's `BodyMd`, wrapped in the section's table markup.
- Email-safety rules (enforced by construction + tests): single 600 px
  column, nested tables, all styles inline, no `<script>`/external CSS,
  `max-width:100%` images with alt, table-based buttons, font stacks from
  the curated `fontKey` list.
- Consumers: composer preview (token → `#` dead link), MailerLite push
  (token → `{$unsubscribe}` — substitution lives in the platform connector
  layer, keeping the renderer ESP-neutral), never the export (export is
  markdown).

### 6.3 UI

- **Issue Composer** (`/issue/{postId}`, replaces `IssueEditor.razor`):
  see mockup 12. Structure cards expand in place for editing (title,
  markdown body, link URL, image URL). `[+ Add ▾]` menu: Topic (manual),
  Topic from inbox…, Sponsor, Button/CTA, Divider. Preview re-renders on
  every change. Tenant-switch guard as today.
- **Inbox** (`Content.razor`): `Generate draft` becomes `Create newsletter`;
  recipe dropdown filtered to `DraftKinds.Newsletter`; on click →
  `CreateFromItemsAsync` → navigate. `RunWithSelection`'s direct
  `GenerationPipeline` call and its error snackbar are deleted.
- **NewIssueDialog**: kept (recipe, window, title); the per-issue source
  checklist stays; result navigates to the composer.
- **Tenant settings**: new "Newsletter" section — branding, default
  header/footer, sender identity (mockup 12 §settings).

### 6.4 What is NOT touched

`MailerLiteClient`, `Post` statuses, push/sent-detection/stats jobs,
`GenerationPipeline` for non-newsletter recipes, file delivery for other
recipe kinds, MCP tools (they keep working against Posts; a
`compose_issue` MCP tool is a later nicety).

## 7. Error handling

| Failure | Behavior |
|---|---|
| Initial generation fails / malformed JSON after retry | Stay in composer; topics remain skeletons (title + link); banner with `Retry generation`. Nothing lost. |
| Per-topic ✨ fails | Snackbar on that card; existing blurb untouched. |
| Push fails | Post → `Failed` + retry — existing pipeline behavior, unchanged. |
| Missing/overlong subject at push | Inline validation before the API call (≤255 chars, required). |
| Dead image URL | Broken-image placeholder in preview; no hard error. |
| Tenant switch mid-edit | Same guard as today (back to `/posts`). |

## 8. Testing

- **Unit:** section→HTML golden-file snapshots (every section type,
  branding on/off, token substitution, compliance footer always present);
  generation-JSON parsing incl. malformed + retry; position math for
  insert/delete/move; markdown export; Header/Footer invariants.
- **Integration:** inbox selection → Post + skeleton sections → generated
  sections (fake LLM); push renders sections through the stubbed
  MailerLite handler; legacy Post (DraftId, no sections) renders and
  pushes; migration.
- **Manual E2E:** one real issue to the test group; verify in Gmail +
  Outlook that branding/images/buttons/unsubscribe survive and MailerLite
  does **not** append its default footer; confirm the account plan accepts
  custom HTML via API.

## 9. Open questions

1. **Advanced plan**: does the real MailerLite account accept `content`
   via API? Verify first during E2E — if not, this is a plan upgrade, not
   a design change.
2. **Curated font list**: which 4–6 email-safe stacks to offer (Arial,
   Helvetica, Georgia, Verdana, Times, Trebuchet)? Decide at
   implementation; pure config.
