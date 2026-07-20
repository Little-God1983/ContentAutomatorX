# 12 — Issue Composer v2 (structured sections, WYSIWYG for email)

**Date:** 2026-07-19 · **Status:** Approved (spec:
`docs/superpowers/specs/2026-07-19-newsletter-composer-design.md`).
Replaces the free-markdown issue editor (08a's composer pane) and the Inbox
`Generate draft` file-drop path.

The insight that shapes everything: MailerLite's API takes **one flat HTML
string** — no blocks, no structure. So all structure lives in *our* model
(typed sections), and one renderer guarantees the preview, the pushed
campaign, and any future ESP all see the same email.

## The converged flow (both entry points, one composer)

```
  INBOX (/content)                          "+" NEW (app bar)
  ─────────────────                         ─────────────────
  ☑ select items                            "Newsletter issue…"
  [Create newsletter ▾]                     dialog: recipe, title
   └ pick recipe (newsletter kind only)          │
        │                                        │  (no topics yet)
        ▼                                        ▼
 ┌──────────────────── ISSUE COMPOSER  /issue/{id} ────────────────────┐
 │  created instantly with skeleton sections:                          │
 │    Header (tenant default) · 1 Topic per selected item · Footer     │
 │  one LLM call (strict JSON) fills all topic blurbs — progress shown │
 │  then: edit · reorder · add/remove topics · sponsor · per-topic ✨  │
 └───────────────┬────────────────────────────────────┬────────────────┘
                 ▼                                    ▼
         [Export .md]                        [Push ⚡ to MailerLite]
      browser download,                   sections → email HTML → draft
      no folder config needed             campaign. YOU hit Send there.
```

- **No file delivery in this flow.** The `OutputFolderPath` error class is
  gone by construction; Export is an on-demand browser download.
- Generation failure leaves you *in the composer* with skeleton topics
  (title + link from each item) and a `Retry generation` banner — never a
  dead-end snackbar.

## The composer

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Title [ AI Weekly #42                 ]  (Draft)                            │
│  Subject [ Robots take over gardening  ]  Preview [ This week: ... ] [✨]    │
│                        [ Save ]  [ Export .md ]  [ Push ⚡ ]  [ Open ML ↗ ]  │
├───────────────────────────────────┬──────────────────────────────────────────┤
│  STRUCTURE                        │  EMAIL PREVIEW (live, exact)             │
│                                   │ ┌──────────────────────────────────────┐ │
│ ┌───────────────────────────────┐ │ │        [logo]                        │ │
│ │ ≡ HEADER              [✨][✎] │ │ │  AI Weekly #42                       │ │
│ │ "Hi friends, this week…"      │ │ │  Hi friends, this week we look at…   │ │
│ └───────────────────────────────┘ │ │                                      │ │
│ ┌───────────────────────────────┐ │ │  ────────────────────────────        │ │
│ │ ≡ 1·TOPIC  [↑][↓][✨][✎][🗑] │ │ │  ┌────────┐  Anthropic ships…        │ │
│ │ Anthropic ships new model     │ │ │  │ (img)  │  Short blurb text        │ │
│ │ img ✔ · link ✔ · from: RSS    │ │ │  └────────┘  Read more →             │ │
│ └───────────────────────────────┘ │ │                                      │ │
│ ┌───────────────────────────────┐ │ │  ────────────────────────────        │ │
│ │ ≡ 2·TOPIC  [↑][↓][✨][✎][🗑] │ │ │  EU AI act update                    │ │
│ │ EU AI act update              │ │ │  Short blurb text…                   │ │
│ │ img – · link ✔ · from: Web    │ │ │  Read more →                         │ │
│ └───────────────────────────────┘ │ │                                      │ │
│ ┌───────────────────────────────┐ │ │  ┌ SPONSORED ─────────────────┐      │ │
│ │ ≡ SPONSOR      [↑][↓][✎][🗑] │ │ │  │ [logo] Acme Dev Tools      │      │ │
│ │ Acme Dev Tools + link + logo  │ │ │  │ Ship faster with Acme.     │      │ │
│ └───────────────────────────────┘ │ │  │ ┌──────────────────┐       │      │ │
│ ┌───────────────────────────────┐ │ │  │ │  Try Acme free   │◄──────┼─CTA  │ │
│ │ ≡ ── DIVIDER ──  [↑][↓][🗑]  │ │ │  └────────────────────────────┘      │ │
│ └───────────────────────────────┘ │ │  ────────────────────────────        │ │
│ ┌───────────────────────────────┐ │ │  3rd topic …                         │ │
│ │ ≡ 3·TOPIC  [↑][↓][✨][✎][🗑] │ │ │                                      │ │
│ └───────────────────────────────┘ │ │  See you next week! — Chris          │ │
│ ┌───────────────────────────────┐ │ │  [X] [YT] [Web]                      │ │
│ │ ≡ FOOTER              [✎]    │ │ │  You get this because…               │ │
│ │ "See you next week…" + socials│ │ │  Unsubscribe · Acme Media, Berlin DE │ │
│ └───────────────────────────────┘ │ └──────────────────────────────────────┘ │
│                                   │   600px · tenant colors · same HTML      │
│ [+ Add ▾]                         │   that MailerLite receives               │
│   ├ Topic (write manually)        │                                          │
│   ├ Topic from inbox…             │                                          │
│   ├ Sponsor block                 │                                          │
│   ├ Button / CTA                  │                                          │
│   └ Divider                       │                                          │
└───────────────────────────────────┴──────────────────────────────────────────┘
```

Card behavior:

- **[✎]** expands the card in place: title, markdown blurb, link URL, image
  URL (topics prefill image/link from their source item).
- **[✨] on a topic** regenerates only that blurb (optional instruction:
  "shorter", "more technical"). **[✨] on the header** writes an intro that
  references the current topics.
- **[↑][↓]** reorder (drag on `≡` is a later nicety); every section type
  moves the same way. Header and Footer are fixed at the ends — exactly one
  of each, editable but not deletable.
- **Topic from inbox…** opens a picker (search + checkboxes over this
  tenant's inbox items) → each pick becomes a skeleton topic + gets a blurb.
- 1 inbox item = 1 topic (decided). The count of topics is exactly the
  count you selected — plus whatever you add or remove by hand.

## Tenant settings — Newsletter section

```
┌─ Tenant settings ── Newsletter ──────────────────────────────────────┐
│  BRANDING                                                            │
│  Accent color  [ #7C3AED ▓ ]     (headings, links, buttons)          │
│  Logo URL      [ https://…/logo.png        ]  [preview ✔]            │
│  Font          [ Georgia ▾ ]     (curated email-safe list)           │
│                                                                      │
│  DEFAULTS (prefill every new issue — editable per issue)             │
│  Header  ┌────────────────────────────────────────────┐              │
│          │ Hi friends, …                              │              │
│          └────────────────────────────────────────────┘              │
│  Footer  ┌────────────────────────────────────────────┐              │
│          │ See you next week! — Chris                 │              │
│          │ [X](url) · [YouTube](url) · [Web](url)     │              │
│          └────────────────────────────────────────────┘              │
│                                                                      │
│  COMPLIANCE (required by MailerLite & anti-spam law)                 │
│  Sender name/address  [ Acme Media, Musterstr. 1, Berlin, DE ]       │
│  ⓘ Unsubscribe link is inserted automatically at the bottom —        │
│    you never have to (and can't) forget it.                          │
└──────────────────────────────────────────────────────────────────────┘
```

Branding is applied at **render time**, never baked into sections — restyle
the tenant and every issue (past and future) renders in the new look.

## One renderer, three consumers

```
                      IssueSection list (ordered)
                                │
                                ▼
              ┌─ SectionHtmlRenderer (ESP-neutral) ─┐
              │  • 600px single column, table layout │
              │  • ALL styles inline, no <style> dep │
              │  • images: absolute URLs, alt text   │
              │  • buttons: bulletproof table-button │
              │  • fonts: safe stack from fontKey    │
              │  • branding (color/logo) injected    │
              │  • emits token: %%UNSUBSCRIBE%%      │
              └──────────────┬──────────────────────┘
          ┌──────────────────┼──────────────────────┐
          ▼                  ▼                      ▼
   COMPOSER PREVIEW    MAILERLITE PUSH        EXPORT .md
   token → '#' link    token → {$unsubscribe} (markdown concat,
   (rendered live)     via MailerLiteClient    not HTML — for
                       (unchanged API code)    your own reuse)
```

ESP-specific tokens are substituted **in the platform connector**, so a
future Buttondown/Mailchimp connector maps the token differently and the
renderer/composer never change. This honors the decision that everything
must stay compatible with MailerLite *or any other* newsletter service.

## MailerLite facts this design leans on (researched 2026-07-19)

- Campaign `content` = one HTML string; subject ≤255 chars; from-email must
  be verified. No block/drag-drop structure exists in the API.
- HTML must include `{$unsubscribe}` + account name/address/country, or
  MailerLite appends its default footer (our compliance footer prevents
  that).
- Custom HTML via API needs their **Advanced plan** — verify on the real
  account during E2E.

## Open questions

1. Advanced-plan check on the real account (E2E gate, not a design change).
2. Which 4–6 curated email-safe font stacks to offer (implementation-time
   config).
