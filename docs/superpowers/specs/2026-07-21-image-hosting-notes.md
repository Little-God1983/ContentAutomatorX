# Image Hosting — Notes

**Status:** NOT A SPEC. Notes captured during the newsletter-template brainstorm, to be designed properly when picked up.
**Date:** 2026-07-21
**Blocks:** nothing. See "Why this is not urgent" below.

## The problem

Newsletter images must be absolute `https://` URLs on a host that allows hotlinking — email clients cannot read local files. The app has no image hosting: `IssueSection.ImageUrl` and `TenantBranding.LogoUrl` are free-text URL strings the user pastes, validated only for scheme.

MailerLite cannot help. Its File Manager is dashboard-only with no upload endpoint, confirmed in the reference template's own comments.

## Why this is not urgent

Pasted URLs already work and will keep working through `TemplateHtmlRenderer` unchanged. YouTube thumbnails resolve from Google's public URLs with no upload at all. The newsletter-template feature ships complete without any of this.

What image hosting adds: the play-button overlay, and getting the user's own images off other people's servers.

## Decided direction

**Cloudflare R2.** The site is already hosted on Cloudflare Workers, so the account and DNS exist. R2 is S3-compatible — the standard AWS SDK for .NET pointed at `https://<accountid>.r2.cloudflarestorage.com` — with a custom domain such as `img.intothelatent.com` bound to a public bucket.

The deciding factor is egress. Every newsletter open re-downloads every image, so 5,000 subscribers is 5,000 downloads per image per send. R2 charges nothing for egress; S3 charges per gigabyte.

Rejected: Cloudinary (solves hosting, overlay compositing and retina resizing in one move via URL parameters, but ties image delivery to one vendor's URL format on a free tier that can change); committing images to the site repo (a scheduled 9am send would need git credentials and a completed build before images resolve); the existing `tenant.OutputFolderPath` file share (there is no filesystem behind a Workers-hosted site).

**Compositing with ImageSharp**, not by CDN. The app fetches the YouTube still, overlays `docs/user-braindumps/PlayButton-Overlay.png` (512×512, transparent, teal `#1AE6D5` matching the palette), and uploads the result. Works with any hosting choice and keeps the overlay asset in the repo.

## Scope when designed

- R2 client behind an abstraction in `Domain.Abstractions`, implemented in Infrastructure
- Credentials via the existing DPAPI `ICredentialStore`, as MailerLite already does
- Upload UI on section cards, replacing paste-a-URL as the primary path
- Thumbnail compositing for Video sections
- Orphaned-object cleanup when a section or post is deleted
- Behaviour when an upload fails mid-send

## Open questions to resolve at design time

- Does an upload failure block a scheduled send, or fall back to the un-composited URL?
- Are objects keyed per tenant, per post, or content-addressed by hash?
- Retention: are images of deleted posts removed immediately, or swept on the schedule the chat-retention job already uses?
- Does `TenantBranding.LogoUrl` move to uploaded storage too, or stay a pasted URL?
