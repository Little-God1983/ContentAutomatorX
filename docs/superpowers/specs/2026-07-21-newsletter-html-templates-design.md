# Newsletter HTML Templates — Design

**Status:** Approved, not yet implemented
**Date:** 2026-07-21
**Supersedes:** nothing. Extends the issue composer (`2026-07-19-issue-composer-design.md`) and the chat/regenerate feature (`2026-07-20-issue-chat-and-regenerate-design.md`).

## 1. Goal

Let a tenant store their own email HTML design and have generated issues render into it, instead of into the single hardcoded layout in `SectionHtmlRenderer`.

The reference design is `docs/user-braindumps/preview.html` — a hand-written, email-client-safe template for *Into the Latent*. Its structure drives this spec: it already marks every content region with a `<!-- BLOCK: ... -->` comment, and its blocks map almost one-to-one onto the `IssueSection` types the composer produces.

### 1.1 The rule this preserves

The issue composer's existing guarantee is unchanged: **every write to a section's text is explicitly chosen, one section at a time.** Templates change only how sections are *rendered*, never their content. No template operation writes to an `IssueSection`.

### 1.2 Out of scope — deferred to Spec 2 (image hosting)

Image upload and hosting is a separate subsystem and gets its own spec. It is not a dependency: `IssueSection.ImageUrl` is already a free-text absolute URL and already renders, and YouTube thumbnails resolve from Google's public URLs with no upload at all.

Deferred to Spec 2:

- Cloudflare R2 upload of tenant-supplied images
- ImageSharp compositing of `docs/user-braindumps/PlayButton-Overlay.png` onto video thumbnails
- Upload UI on section cards
- R2 credential storage and orphaned-file cleanup

Until Spec 2 lands, video thumbnails are the raw YouTube still with no play triangle, and cover images are URLs the user pastes.

### 1.3 Also out of scope

- Syntax highlighting in the editor (needs a JS editor component; plain monospace textarea for now)
- Template version history / rollback
- Localized reading-time text (English only; `Recipe.Language` is not consulted)
- Per-issue template switching in the composer UI (the column exists and is stamped at creation; no picker yet)

## 2. Decisions

Each decision below was made explicitly during brainstorming. The rationale matters more than the choice — it is what a later reader needs to know before changing it.

**D1 — Block library, not a chrome wrapper.** The template is parsed into named blocks; the renderer picks the block matching each section's type and emits it once per section. Rejected: a single `{{content}}` slot with today's markup inside, which would put plain renderer cards inside branded chrome and look wrong.

**D2 — Templates are tenant-owned, selected per recipe.** One template can back several automations. Rejected: storing HTML on the Recipe (two newsletters sharing a design means two copies to hand-sync) and one-template-per-tenant (no variants).

**D3 — Placeholders plus optional regions. No loops, no expressions.** `{{placeholder}}` substitution and `<!-- IF: x -->…<!-- /IF -->` regions that vanish when the field is empty. The repeat-per-section loop belongs to the renderer, not the template. Rejected: a full template engine (Scriban/Fluid), which is a scripting language running on user input, needs sandboxing and timeouts, and produces errors about template syntax rather than about the newsletter. Also rejected: plain substitution with no conditionals, which forces duplicate with-image / without-image blocks kept in sync by hand.

**D4 — Extend the data model to fit the template, rather than trimming the template.** Adds a `Video` section type and a `Category` field. The alternative — dropping the category line and rendering video as a plain topic card — was cheaper but loses the design.

**D5 — Reading time is computed, never stored.** Word count ÷ 200. A stored field is one the AI can fill wrongly, one the chat edit contract must carry, one the undo snapshot must hold, and one that goes stale the moment a paragraph is edited. Cost: no manual override.

**D6 — The editor is a full-screen dialog, not inline in the recipe form.** The email is 600px wide by design; an inline two-pane editor gives the preview roughly 400px, so the user would be judging a layout they cannot see at real size.

**D7 — The preview renders a fixed sample issue, not a real one.** The sample exercises every block and every `IF` region — including a topic deliberately missing its image. A real issue leaves whichever blocks it happens not to use untested while the user edits them.

**D8 — Validate on save; fall back per-section on render.** Hard errors block the save. At render time a section whose block is absent falls back to the built-in renderer for that one section, so a template edit can never make a scheduled send fail.

**D9 — The template author is trusted; section content is not.** See §5.4.

**D10 — Video is a thumbnail plus a link, never an embed.** Gmail, Outlook and Apple Mail strip `<video>` and `<iframe>`. There is no version of this that plays inline.

## 3. Data model

### 3.1 New entity

```csharp
public class NewsletterTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Html { get; set; }
    public bool IsDefault { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

Registered on `IAppDbContext` as `DbSet<NewsletterTemplate> NewsletterTemplates`.

Tenancy follows the codebase's existing convention exactly: a plain `TenantId` column, no FK to `Tenant`, no global query filter, scoping enforced by an explicit `.Where(t => t.TenantId == tenantId)` in the service.

`Html` is stored as a text column, not a file on disk, so a template is backed up and moved with the rest of the tenant's data. Capped at **512 KB** on save; larger is a hard validation error.

**`IsDefault`:** at most one per tenant. Setting it on one template clears it on the others in the same `SaveChangesAsync`. Enforced in `NewsletterTemplateService`, not by a database constraint — SQLite has no filtered unique index in the EF provider, and the service is the only writer.

### 3.2 Modified entities

| Entity | Column | Type | Meaning |
|---|---|---|---|
| `Recipe` | `NewsletterTemplateId` | `Guid?` | Which template this automation's issues use. Null = built-in renderer. |
| `IssueSection` | `Category` | `string?` | Free text — "Tutorial", "News". Topic sections only. |

**`Post` needs no column.** `Post.RecipeId` already exists (`Post.cs:10`, "the automation this issue is based on") and `IssueChatService.RunTurnAsync` and `IssueComposerService.GenerateTopicsAsync` both already resolve a recipe through it. The render path walks `Post → Recipe → NewsletterTemplateId`.

Consequence to accept knowingly: changing a recipe's template retroactively changes issues already composed under it. That matches how editing the template's HTML behaves, and per-issue override is deferred (§1.3).

### 3.3 New section type

`SectionTypes` gains `Video`. It reuses existing columns rather than adding any:

| Column | Holds |
|---|---|
| `Title` | Video title |
| `BodyMd` | Short description |
| `LinkUrl` | The YouTube URL (watch, youtu.be, shorts or embed form) |
| `ImageUrl` | Thumbnail override. Empty = derive from `LinkUrl` per §6.2. |

`SectionTypes.All` does not currently exist; `Video` is added to the `SectionTypes` constants and to every `switch` that enumerates types (`SectionHtmlRenderer.AppendSection`, `SectionHtmlRenderer.ToMarkdown`, the composer's add-section menu, `SectionCard.razor`).

### 3.4 Migration

One migration, `NewsletterTemplates`, adding the table plus `Recipe.NewsletterTemplateId` and `IssueSection.Category`. Both columns are nullable, so every existing row is valid without backfill and every existing recipe keeps today's rendering behaviour.

## 4. Template format

### 4.1 Blocks

A block is delimited by `<!-- BLOCK: name -->` and `<!-- /BLOCK -->`. Text outside any block is ignored — this is deliberate, so the reference template's long explanatory header comment survives untouched.

Eight block names are recognised. Seven map to a section type; `shell` is the document.

| Block | Rendered for | Required |
|---|---|---|
| `shell` | The document itself — doctype, head, resets, media queries, body background, Outlook conditional, 600px table, fixed logo bar | **Yes** |
| `header` | `SectionTypes.Header` | No |
| `topic` | `SectionTypes.Topic` | No |
| `video` | `SectionTypes.Video` | No |
| `sponsor` | `SectionTypes.Sponsor` | No |
| `button` | `SectionTypes.Button` | No |
| `divider` | `SectionTypes.Divider` | No |
| `footer` | `SectionTypes.Footer` | No |

`SectionTypes.LegacyBody` has no block. Legacy free-markdown issues have no sections at all and never reach the template renderer (§5.1).

An absent optional block is a save-time **warning**, not an error, and falls back at render time per §5.3.

### 4.2 Placeholders

Every placeholder is `{{snake_case}}`. Unknown names are a hard save error, so a typo like `{{titel}}` is caught immediately rather than shipping as literal text to an inbox.

**Available in every block:**

| Placeholder | Value |
|---|---|
| `{{tenant_name}}` | `Tenant.Name` |
| `{{accent}}` | `TenantBranding.AccentColorHex`, validated `^#[0-9a-fA-F]{6}$`, else `EmailHtmlRenderer.DefaultAccent` |
| `{{issue_title}}` | The issue title passed to `Render` |
| `{{issue_date}}` | `Post.CreatedAt` formatted `MMMM yyyy` (invariant culture) |
| `{{unsubscribe_url}}` | `SectionHtmlRenderer.UnsubscribeToken` — the literal `%%UNSUBSCRIBE%%` |

**Block-specific:**

| Block | Placeholders |
|---|---|
| `shell` | `{{preheader}}`, `{{sections}}` |
| `header` | `{{title}}`, `{{body_html}}` |
| `topic` | `{{title}}`, `{{body_html}}`, `{{image_url}}`, `{{link_url}}`, `{{link_text}}`, `{{category}}`, `{{reading_time}}` |
| `video` | `{{title}}`, `{{body_html}}`, `{{thumbnail_url}}`, `{{video_url}}`, `{{link_text}}` |
| `sponsor` | `{{title}}`, `{{body_html}}`, `{{image_url}}`, `{{link_url}}`, `{{link_text}}` |
| `button` | `{{link_url}}`, `{{link_text}}` |
| `divider` | globals only |
| `footer` | `{{body_html}}`, `{{sender_identity}}` |

`{{sections}}` is the concatenation of every rendered section block, in `Position` order. It appears in `shell` only and is **required** there.

`{{sender_identity}}` is `Tenant.SenderIdentity` — the postal address, legally required in commercial email.

`{{link_text}}` falls back to a per-type default when the section's `LinkText` is empty: `"Read more →"` for topic, `"Watch on YouTube →"` for video, `"Learn more"` for sponsor, `"Open"` for button.

`{{preheader}}` is the first 200 characters of the first `Header` section's `BodyMd`, markdown stripped, HTML-encoded. Empty when there is no header section — the template's invisible padding characters still do their job.

### 4.3 Optional regions

```html
<!-- IF: image -->
  <a href="{{link_url}}"><img src="{{image_url}}" alt="{{title}}" /></a>
<!-- /IF -->
```

The region and its contents are emitted only when the named field resolves to a non-empty value; otherwise the whole region — markup included — is dropped.

Recognised condition names, per block, matching that block's placeholders: `title`, `body`, `image`, `link`, `category`, `thumbnail`, `video`. An unrecognised name for the enclosing block is a hard save error.

`image` tests `{{image_url}}`, `link` tests `{{link_url}}`, `thumbnail` tests `{{thumbnail_url}}`, `video` tests `{{video_url}}`, `body` tests `{{body_html}}`. The rest test their like-named placeholder.

**Nesting is not supported** and is a hard save error with a clear message. The reference template needs none — its deepest case is an `<img>` inside an `<a>`, which is one region. This keeps the parser a single forward scan.

### 4.4 What is not supported

No loops, no `ELSE`, no expressions, no arithmetic, no filters, no includes, no comments-within-comments. Anything resembling these is either ignored as ordinary HTML or caught as an unknown placeholder.

## 5. Rendering

### 5.1 Template resolution

At render time, in order:

1. `Post.RecipeId` resolves to a recipe whose `NewsletterTemplateId` is set, and that template exists and belongs to the post's tenant → use it.
2. Otherwise the tenant has a template with `IsDefault` → use it.
3. Otherwise → `SectionHtmlRenderer`, byte-identical to today.

A dangling id — template deleted, or belonging to another tenant — falls through to step 2, not to an error. A post with no `RecipeId` (created by hand) starts at step 2.

Issues with no sections at all (legacy free-markdown drafts) continue to use `EmailHtmlRenderer.Render` and never consult a template.

### 5.2 The renderer

`TemplateHtmlRenderer` sits beside `SectionHtmlRenderer` in `ContentAutomatorX.Application.Newsletter`, with a matching signature:

```csharp
public static string Render(
    IReadOnlyList<IssueSection> sections,
    Tenant tenant,
    string title,
    NewsletterTemplate template,
    DateTimeOffset issueDate)
```

It emits the same `UnsubscribeToken`, so both existing call sites keep their `.Replace(...)` — `PostService.PushAsync` substitutes MailerLite's `{$unsubscribe}`, `IssueComposerService.PreviewHtmlAsync` substitutes `#`. Neither substitution changes.

Three collaborators, each independently testable:

- **`TemplateParser`** — HTML text → `ParsedTemplate` (an ordered block map plus each block's placeholder and region positions). Pure, no I/O.
- **`TemplateValidator`** — `ParsedTemplate` → a list of errors and warnings. Pure.
- **`TemplateHtmlRenderer`** — `ParsedTemplate` + sections + tenant → HTML string. Pure.

Parsing happens on every render rather than being cached. The templates are ≤512 KB and rendering already involves database and network work; a cache is a correctness risk (stale after an edit) for an unmeasured gain.

### 5.3 Per-section fallback

A section whose block is absent from the template renders through the built-in renderer for that section alone. This requires exposing `SectionHtmlRenderer.AppendSection` as a public `RenderSection(IssueSection section, string accent) => string`. The fallback markup assumes it sits inside a 600px table cell, which the template's `shell` provides.

Consequence to accept knowingly: a template with no `sponsor` block renders sponsor sections in the *old* design inside the new chrome. It looks inconsistent — and that is the point. It ships, it is obviously wrong in the preview, and it does not fail a 9am scheduled send.

### 5.4 Escaping and the trust boundary

The template author is a tenant administrator and is **trusted**: template HTML is emitted verbatim, never sanitized. Section content originates from an LLM and from RSS feeds and is **never trusted**.

| Value | Treatment |
|---|---|
| `{{body_html}}` | `EmailHtmlRenderer.RenderFragment(BodyMd, accent)` — Markdig with `.DisableHtml()`, so raw HTML in section text is escaped, not passed through |
| `{{sections}}` | Already-rendered block output; inserted verbatim |
| `{{unsubscribe_url}}` | The literal token; inserted verbatim |
| Every other placeholder | `WebUtility.HtmlEncode` |
| `{{link_url}}`, `{{image_url}}`, `{{video_url}}`, `{{thumbnail_url}}` | Scheme-checked before encoding |

URL scheme check reuses the existing rule: `http://`, `https://` for images and video; those plus `mailto:` for link targets. **A URL that fails the check resolves to empty string, not to `#`** — so the enclosing `IF` region collapses and no broken image or dead link is emitted at all.

This means a hostile RSS item cannot inject markup through a section body, and cannot produce a `javascript:` href.

The preview iframe carries a `sandbox` attribute without `allow-scripts` (§7.2), so even a template containing a `<script>` cannot reach the Blazor circuit.

### 5.5 The unsubscribe backstop

Added during implementation, after two independent reviewers each defeated the save-time rule by a different route: a token inside an `IF` region that collapses, and a token split across a stripped region boundary so that the check reassembled one that did not exist.

After assembling the final HTML, `TemplateHtmlRenderer.Render` checks for `UnsubscribeToken` and, if it is absent, appends a minimal muted unsubscribe paragraph before `</body>`.

It fires only when the token is genuinely missing, so a correct template renders byte-identically. It should never fire in practice — `TemplateValidator` is the primary gate. It exists because the cost of a miss is a legal violation rather than a cosmetic defect, and a guarantee that rests on one text-matching rule being perfect is not a guarantee.

### 5.6 Reading time

```
words   = whitespace-delimited token count of BodyMd with markdown syntax stripped
minutes = max(1, round(words / 200.0))
text    = $"{minutes} min read"
```

Always at least "1 min read"; an empty body still yields "1 min read". English only.

## 6. Video sections

### 6.1 Why thumbnail-and-link

Every major email client strips `<video>` and `<iframe>`. A video block is an image linking to the video — universally. The reference template's own comments note the play triangle must be burned into the image file, because reliable element overlay does not exist in email.

Until Spec 2 lands, no triangle is composited; `{{thumbnail_url}}` is the raw YouTube still.

### 6.2 Thumbnail derivation

When a Video section's `ImageUrl` is empty, the thumbnail is derived from `LinkUrl`.

Video-id extraction handles four URL shapes: `youtube.com/watch?v=ID`, `youtu.be/ID`, `youtube.com/shorts/ID`, `youtube.com/embed/ID`. Query strings and fragments are stripped. An unrecognised URL yields no id, `{{thumbnail_url}}` resolves empty, and the `IF: thumbnail` region collapses.

`maxresdefault.jpg` exists only for videos uploaded above 720p, so it cannot be used blindly — a dead image in a sent newsletter is worse than a low-resolution one. On saving a Video section, the composer issues a `HEAD` request for `https://img.youtube.com/vi/{id}/maxresdefault.jpg` and stores the resolved URL, falling back to `https://img.youtube.com/vi/{id}/hqdefault.jpg`, which always exists.

The probe lives behind `IYouTubeThumbnailResolver` in `Domain.Abstractions`, implemented in Infrastructure over `HttpClient` — matching how `IDraftDelivery` and the connectors are already structured, and keeping Application free of HTTP. A probe failure (timeout, offline) falls back to `hqdefault.jpg` rather than blocking the save.

## 7. The editor

### 7.1 Entry point

The recipe form (`Recipes.razor`) gains one row in its **Output** section:

```
Recipes / Automations
  Output
    Target platform      [ Newsletter (MailerLite)  v ]
    Schedule             [ 0 9 1 * *              ]
    Newsletter template  [ Into the Latent   v ] [Edit] [New]
```

The dropdown lists the tenant's templates plus a "Built-in design" entry meaning null. **Edit** opens the dialog on the selected template; **New** opens it on a blank one.

### 7.2 The dialog

```
┌──────────────────────────────────────────────────────────────────────┐
│ Into the Latent                  [Upload .html]  [Delete] [Save] [X] │
├───────────────────────────────┬──────────────────────────────────────┤
│  <!-- BLOCK: topic -->        │  Preview: sample issue               │
│  <tr><td>                     │  ┌─────────────────────────────────┐ │
│    <!-- IF: image -->         │  │  ▓▓▓▓ Into the Latent           │ │
│    <a href="{{link_url}}">    │  │  ░ Signals from the             │ │
│      <img src="{{image_url}}" │  │  ░ latent space                 │ │
│           alt="{{title}}" />  │  │  ┌──────────────┐               │ │
│    </a>                       │  │  │ cover image  │               │ │
│    <!-- /IF -->               │  │  └──────────────┘               │ │
│    {{category}} · {{reading_  │  │  Tutorial · 9 min read          │ │
│     time}}                    │  │  Training a Flux LoRA…          │ │
│    {{title}}                  │  │  ─────────────────────          │ │
│  </td></tr>                   │  │  Second topic (no image —       │ │
│  <!-- /BLOCK -->              │  │   IF region collapsed)          │ │
│                               │  └─────────────────────────────────┘ │
├───────────────────────────────┴──────────────────────────────────────┤
│ ✗ line 236  unknown placeholder {{titel}} in BLOCK: topic            │
│ ✗ line 233  <!-- IF: image --> is never closed                       │
│ ⚠ no BLOCK: sponsor — sponsor sections use the built-in design       │
└──────────────────────────────────────────────────────────────────────┘
                        Save disabled while any ✗ remains
```

**Left pane** — a monospace `MudTextField` with `Lines` filling the pane. No syntax highlighting; that needs a JS editor component and is deferred. Errors carry a line number and the offending text so the user can locate them.

**Upload** — `MudFileUpload` accepting `.html`/`.htm`, capped at 512 KB, replacing the pane's contents. It is a convenience for getting the file in, not a separate storage path: the HTML always lives in the database column.

**Right pane** — an `<iframe sandbox srcdoc="...">` rendering the sample issue through the current editor text, debounced 400 ms. `sandbox` without `allow-scripts` is required: it stops any script in the template from reaching the Blazor circuit, and it stops a template `<style>` from leaking into the app's own CSS.

Preview failures never break the editor. A parse error renders the error list and leaves the last good preview in place.

**Save** is disabled while any hard error is present. Warnings do not block.

### 7.3 The sample issue

Fixed in code as `SampleIssue.Sections`, deliberately exercising every block and both sides of every `IF`:

| # | Type | Notably |
|---|---|---|
| 1 | Header | Title and body |
| 2 | Topic | Image, category, link — every region present |
| 3 | Divider | |
| 4 | Topic | **No image, no link, no category** — every region absent |
| 5 | Video | Thumbnail and URL |
| 6 | Sponsor | Logo, title, body, CTA |
| 7 | Button | |
| 8 | Footer | Body, sender identity, unsubscribe |

Sample image URLs point at `placehold.co`, matching the reference template, so the preview needs internet — the same trade-off that file already makes.

## 8. Validation

### 8.1 Hard errors — save is blocked

| # | Condition |
|---|---|
| E1 | HTML is empty or whitespace |
| E2 | HTML exceeds 512 KB |
| E3 | No `<!-- BLOCK: shell -->` |
| E4 | `shell` does not contain `{{sections}}` |
| E5 | No `{{unsubscribe_url}}` that is guaranteed to render — see below |
| E6 | Unknown block name |
| E7 | Duplicate block name |
| E8 | `<!-- BLOCK: -->` without a matching `<!-- /BLOCK -->` |
| E9 | `<!-- /BLOCK -->` with no open block |
| E10 | Unknown placeholder for the enclosing block |
| E11 | `{{sections}}` used outside `shell` |
| E12 | `<!-- IF: -->` without a matching `<!-- /IF -->` |
| E13 | Nested `<!-- IF: -->` |
| E14 | Unknown `IF` condition for the enclosing block |

**E5 is the one that matters most.** The unsubscribe link is a legal requirement, and a template is the single place a user could delete it without noticing.

Its original form — "the token appears somewhere in some block" — proved too weak, and was defeated twice during implementation. It now requires a token that will actually render: one that appears in the `shell` or `footer` block (the two guaranteed to be emitted for every issue, since an issue always has exactly one header and one footer and neither can be deleted) and that sits **outside** every `IF` region, since a region collapses when its field is empty. When the template has no `footer` block, only the `shell` can satisfy the rule — the footer section then falls back to the built-in per-section renderer, which emits the section body alone.

The region check matches placeholders against the original text and rejects those whose index falls inside a region span. It must not strip regions and rescan the result: deleting a region joins the text either side, which can assemble a token that was never there.

The name comparison is `Ordinal` on purpose. The renderer's value lookup is `Ordinal`, so `{{UNSUBSCRIBE_URL}}` would resolve to empty; accepting it here would ship an email with no link and no error anywhere.

§5.5's render-time backstop is the second line of defence behind all of this.

### 8.2 Warnings — save proceeds

| # | Condition |
|---|---|
| W1 | An optional block is missing (one warning per block, naming the fallback consequence) |
| W2 | A block is defined but the template contains no `{{...}}` inside it |

### 8.3 Render-time

Validation runs on save only. Rendering never throws on a template problem: a missing block falls back per §5.3, an unresolvable placeholder resolves empty. A template that somehow reaches render in an invalid state still produces sendable HTML.

## 9. Composer changes

Adding `Category` and `Video` creates new editable surface, which propagates through the composer, the chat feature and the generation prompts.

**Section cards** (`SectionCard.razor`) — Topic cards gain a `Category` text field. A new Video card exposes Title, Body, YouTube URL and an optional thumbnail override.

**Add-section menu** — gains Video.

**Chat edit contract** (`ChatReplyParser`) — `ChatEdit` gains `Category`. The model may propose a category exactly as it proposes a title or body: as a reviewable proposal, never a direct write. `IssueSectionProposal` gains `ProposedCategory` and `BaselineCategory`, and the staleness check in `IssueChatService.IsStale` extends to it, following the pattern already established for title.

**The structural lock is unchanged and extended.** Chat still cannot add, remove or reorder sections, enforced by the section-id whitelist rather than by prompt wording. It additionally cannot change a section's `Type` — `Type` is not in the edit contract, so a Topic can never become a Video.

**Generation prompts** — `GenerateTopicsAsync` and `RegenerateSectionAsync` ask for a short category label per topic. Video sections are added manually and are not generated.

**Undo** — verified during planning, and the optimistic reading was wrong: `IssueHistoryService.SectionSnapshot` is an **explicit nine-field record**, not entity serialization. `Category` must be added in three places — the `SectionSnapshot` record, the projection in `CaptureAsync`, and the assignment loop in `RestoreAsync`. Missing any one silently drops the category on every undo.

## 10. Error handling

| Situation | Behaviour |
|---|---|
| Template deleted while a recipe points at it | Recipe's id is cleared on delete; posts fall through to the tenant default, then the built-in renderer |
| Template belongs to another tenant | Treated as absent — resolution continues at step 2 |
| Malformed template reaches render | Renders anyway; missing blocks fall back, bad placeholders resolve empty |
| YouTube thumbnail probe fails | Falls back to `hqdefault.jpg`; the save succeeds |
| Unparseable YouTube URL | `{{thumbnail_url}}` empty, `IF: thumbnail` collapses, video block still renders title, body and link |
| Preview render throws in the editor | Error list shown, last good preview retained, editor stays usable |
| Upload exceeds 512 KB | Rejected at the file picker with a message; the pane is not replaced |

Every editor and dialog event handler is wrapped in try/catch. There is no `ErrorBoundary` in this app — an exception escaping a Blazor event handler tears down the circuit.

## 11. Testing

**Unit — `TemplateParser`:** block extraction; text outside blocks ignored; `IF` region boundaries; unclosed block; unclosed region; nested region; duplicate block; unknown block; unknown placeholder; unknown condition; `{{sections}}` outside shell.

**Unit — `TemplateValidator`:** one test per hard error E1–E14 and per warning W1–W2, each asserting the specific error is raised and that valid templates raise nothing.

**Unit — `TemplateHtmlRenderer`:** golden render of the sample issue against the reference template; `IF` region collapse when a field is empty; `{{body_html}}` markdown conversion; **`<script>` in a section title and body is escaped, not emitted**; `javascript:` in `LinkUrl` resolves empty and collapses its region; missing block falls back to built-in markup; `UnsubscribeToken` present in output; `{{link_text}}` per-type defaults.

**Unit — reading time:** empty body → "1 min read"; 200 words → "1 min read"; 1,800 words → "9 min read"; markdown syntax not counted as words.

**Unit — YouTube id parsing:** all four URL shapes; query string and fragment stripped; unrecognised URL yields no id.

**Integration:** template CRUD scoped to tenant; `IsDefault` exclusivity across a save; recipe→template link; post template stamping at creation; the full three-step resolution order including a dangling id and a cross-tenant id; the migration applying to a database holding existing posts, recipes and sections.

Per the repo's convention, integration tests run against a real file-backed SQLite database via `TestDb.Create()`.

## 12. Open questions

None blocking. Deferred by explicit decision, recorded in §1.2 and §1.3.
