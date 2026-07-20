# Structured Issue Composer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the opaque newsletter flows with one section-based Issue Composer: typed sections (header/topic/sponsor/button/divider/footer) with reorder/edit/AI-regenerate, a live email preview identical to the MailerLite push, and no automatic file delivery.

**Architecture:** New `IssueSection` table holds an ordered, typed section list per `Post`. A pure `SectionHtmlRenderer` turns sections + tenant branding into email-safe HTML with an ESP-neutral `%%UNSUBSCRIBE%%` token (the MailerLite push path substitutes `{$unsubscribe}`). A new `IssueComposerService` owns section lifecycle and LLM generation. The composer UI (`IssueEditor.razor` rewritten) and the Inbox entry point both drive that service.

**Tech Stack:** .NET 10, EF Core 10 + SQLite, Blazor Server + MudBlazor 9.7.0, Markdig, xUnit. Spec: `docs/superpowers/specs/2026-07-19-newsletter-composer-design.md`.

## Global Constraints

- Shell is PowerShell; run all commands from the repo root `e:\Repos\ContentAutomatorX`.
- Every issue has **exactly one** `Header` (first) and **one** `Footer` (last) section; they are editable but never deletable or movable. `Position` values are 0-based and contiguous per post after every mutation.
- Email-safe HTML only: single 600px column, nested tables, **all styles inline**, no `<script>`/external CSS, images and links only with `http(s)` URLs (else dropped), buttons as table-buttons, fonts only from `EmailFonts.All`.
- The renderer emits `SectionHtmlRenderer.UnsubscribeToken` (`%%UNSUBSCRIBE%%`); only the MailerLite push path (`PostService.PushAsync`) replaces it with `{$unsubscribe}`; the preview replaces it with `#`.
- Accent color must match `^#[0-9a-fA-F]{6}$` or fall back to `EmailHtmlRenderer.DefaultAccent` (`#1e88e5`).
- Subject at push: required (falls back to Title), max 255 chars (MailerLite limit).
- Bulk topic generation fills **only** topics with empty `BodyMd`; hand-edited text is never overwritten by bulk generation — per-section ✨ is the sole overwrite path.
- No automatic file delivery anywhere in the newsletter flow; `Export .md` is a browser download.
- Legacy compatibility: a Post with `DraftId` and no sections gets Header + `LegacyBody` + Footer on first composer open; `PushAsync` without sections keeps the old markdown path.
- Match repo idiom: primary-constructor services, `CancellationToken ct = default` last param, SQLite client-side `DateTimeOffset` sorting, xUnit `Assert.*`, no bUnit (UI verified by build + manual run).
- Commit messages end with `Co-Authored-By:` Claude line as configured for this repo.

## File Map

| File | Action | Responsibility |
|---|---|---|
| `src/ContentAutomatorX.Domain/Entities/IssueSection.cs` | Create | Section entity |
| `src/ContentAutomatorX.Domain/Constants.cs` | Modify | `SectionTypes` constants |
| `src/ContentAutomatorX.Domain/Entities/Tenant.cs` | Modify | Newsletter defaults/branding/sender fields |
| `src/ContentAutomatorX.Domain/Models/TenantBranding.cs` | Create | Branding JSON parse/serialize |
| `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs` | Modify | `IssueSections` DbSet |
| `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs` | Modify | DbSet + index + cascade FK |
| `src/ContentAutomatorX.Infrastructure/Migrations/*` | Generate | `IssueSectionsAndTenantNewsletter` migration |
| `src/ContentAutomatorX.Application/Newsletter/EmailFonts.cs` | Create | Curated email-safe font stacks |
| `src/ContentAutomatorX.Application/Newsletter/EmailHtmlRenderer.cs` | Modify | Extract `RenderFragment` + `DefaultAccent` |
| `src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs` | Create | Sections → email HTML / markdown export |
| `src/ContentAutomatorX.Application/Services/IssueComposerService.cs` | Create | Section lifecycle + LLM generation + preview + export |
| `src/ContentAutomatorX.Application/Services/PostService.cs` | Modify | Sectioned push, `SaveIssueMetaAsync`, subject validation, subject ideas from sections |
| `src/ContentAutomatorX.Web/Program.cs` | Modify | DI: `IssueComposerService` |
| `src/ContentAutomatorX.Web/Components/Pages/Tenants.razor` | Modify | Newsletter settings section |
| `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor` | Create | One section card (edit/reorder/✨/delete) |
| `src/ContentAutomatorX.Web/Components/Shared/InboxItemPickerDialog.razor` | Create | "Topic from inbox…" picker |
| `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor` | Rewrite | The composer page |
| `src/ContentAutomatorX.Web/wwwroot/download.js` | Create | Browser download helper |
| `src/ContentAutomatorX.Web/Components/App.razor` | Modify | Script tag for download.js |
| `src/ContentAutomatorX.Web/Components/Pages/Content.razor` | Modify | Inbox entry: Create newsletter |
| `tests/ContentAutomatorX.UnitTests/SectionHtmlRendererTests.cs` | Create | Renderer + fonts + branding unit tests |
| `tests/ContentAutomatorX.UnitTests/TopicParsingTests.cs` | Create | `TryParseTopics` unit tests |
| `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs` | Create | Lifecycle, generation, sectioned push |

Existing test helpers reused (namespace `ContentAutomatorX.IntegrationTests`): `TestDb`, `FakeLlm`, `FailingLlm` (GenerationPipelineTests.cs), `FakeDelivery`, `FakeMailerLite`, `InMemoryCredentials` (PostServiceTests.cs).

---

### Task 1: IssueSection entity, Tenant newsletter fields, migration

**Files:**
- Create: `src/ContentAutomatorX.Domain/Entities/IssueSection.cs`
- Modify: `src/ContentAutomatorX.Domain/Constants.cs`
- Modify: `src/ContentAutomatorX.Domain/Entities/Tenant.cs`
- Create: `src/ContentAutomatorX.Domain/Models/TenantBranding.cs`
- Modify: `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`
- Modify: `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs` (new file, first tests)

**Interfaces:**
- Produces: `IssueSection` entity (properties below), `SectionTypes.{Header,Topic,Sponsor,Button,Divider,Footer,LegacyBody}` string constants, `Tenant.{DefaultHeaderMd,DefaultFooterMd,BrandingJson,SenderIdentity}` (all `string`, default `""`/`"{}"`), `TenantBranding(string? AccentColorHex, string? LogoUrl, string? FontKey)` with `static TenantBranding Parse(string? json)` and `string ToJson()`, `IAppDbContext.IssueSections` (`DbSet<IssueSection>`).

- [ ] **Step 1: Write the failing persistence test**

Create `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs`:

```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class IssueComposerServiceTests
{
    [Fact]
    public async Task IssueSection_round_trips_and_cascades_on_post_delete()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-sections" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "Issue" };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.Add(new IssueSection
        {
            PostId = post.Id, Position = 0, Type = SectionTypes.Topic,
            Title = "Topic A", BodyMd = "blurb", LinkUrl = "https://ex.com", SourceItemId = Guid.NewGuid()
        });
        await test.Db.SaveChangesAsync();

        using (var fresh = test.NewContext())
        {
            var s = await fresh.IssueSections.SingleAsync(x => x.PostId == post.Id);
            Assert.Equal(SectionTypes.Topic, s.Type);
            Assert.Equal("Topic A", s.Title);
        }

        using (var fresh = test.NewContext())
        {
            fresh.Posts.Remove(await fresh.Posts.SingleAsync(p => p.Id == post.Id));
            await fresh.SaveChangesAsync();
            Assert.Equal(0, await fresh.IssueSections.CountAsync());
        }
    }

    [Fact]
    public void TenantBranding_parses_malformed_json_to_empty_and_round_trips()
    {
        Assert.Equal(new TenantBranding(null, null, null), TenantBranding.Parse("not json"));
        Assert.Equal(new TenantBranding(null, null, null), TenantBranding.Parse(""));
        var b = new TenantBranding("#7C3AED", "https://ex.com/logo.png", "georgia");
        Assert.Equal(b, TenantBranding.Parse(b.ToJson()));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IssueComposerServiceTests"`
Expected: compile error — `IssueSections`, `SectionTypes`, `TenantBranding` do not exist.

- [ ] **Step 3: Create the entity and constants**

Create `src/ContentAutomatorX.Domain/Entities/IssueSection.cs`:

```csharp
namespace ContentAutomatorX.Domain.Entities;

public class IssueSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public int Position { get; set; }          // 0-based, contiguous per post
    public required string Type { get; set; }  // SectionTypes.*
    public string? Title { get; set; }         // topic heading / sponsor name
    public string? BodyMd { get; set; }        // markdown copy
    public string? ImageUrl { get; set; }      // topic image / sponsor logo (absolute URL)
    public string? LinkUrl { get; set; }       // read-more / sponsor target / CTA target
    public string? LinkText { get; set; }      // CTA label
    public Guid? SourceItemId { get; set; }    // ContentItem a topic came from (null = manual)
}
```

Append to `src/ContentAutomatorX.Domain/Constants.cs`:

```csharp
public static class SectionTypes
{
    public const string Header = "Header";
    public const string Topic = "Topic";
    public const string Sponsor = "Sponsor";
    public const string Button = "Button";
    public const string Divider = "Divider";
    public const string Footer = "Footer";
    public const string LegacyBody = "LegacyBody";
}
```

- [ ] **Step 4: Extend Tenant and add TenantBranding**

In `src/ContentAutomatorX.Domain/Entities/Tenant.cs` add after `OutputFolderPath`:

```csharp
    public string DefaultHeaderMd { get; set; } = "";
    public string DefaultFooterMd { get; set; } = "";
    public string BrandingJson { get; set; } = "{}";   // TenantBranding
    public string SenderIdentity { get; set; } = "";   // "Name, street, city, country" for the compliance footer
```

Create `src/ContentAutomatorX.Domain/Models/TenantBranding.cs`:

```csharp
using System.Text.Json;

namespace ContentAutomatorX.Domain.Models;

public record TenantBranding(string? AccentColorHex, string? LogoUrl, string? FontKey)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private static readonly TenantBranding Empty = new(null, null, null);

    public static TenantBranding Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try { return JsonSerializer.Deserialize<TenantBranding>(json, JsonOpts) ?? Empty; }
        catch (JsonException) { return Empty; }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);
}
```

- [ ] **Step 5: Wire persistence**

In `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs` add after `DbSet<Post> Posts { get; }`:

```csharp
    DbSet<IssueSection> IssueSections { get; }
```

In `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs` add after the `Posts` property:

```csharp
    public DbSet<IssueSection> IssueSections => Set<IssueSection>();
```

and in `OnModelCreating` add:

```csharp
        b.Entity<IssueSection>().HasIndex(s => new { s.PostId, s.Position });
        b.Entity<IssueSection>()
            .HasOne<Post>().WithMany().HasForeignKey(s => s.PostId).OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 6: Generate the migration**

Run: `dotnet ef migrations add IssueSectionsAndTenantNewsletter --project src/ContentAutomatorX.Infrastructure`
(if `dotnet ef` is missing: `dotnet tool install -g dotnet-ef` first)
Expected: new migration adds table `IssueSections` (with FK to Posts, cascade) and four `TEXT NOT NULL` default-`''`/`'{}'` columns on `Tenants`. Inspect the generated file to confirm; delete any stray `design.db`.

- [ ] **Step 7: Run the tests**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IssueComposerServiceTests"`
Expected: 2 passed.

- [ ] **Step 8: Commit**

```powershell
git add -A src tests
git commit -m "feat: IssueSection entity, tenant newsletter fields, migration (#composer)"
```

### Task 2: EmailFonts + `EmailHtmlRenderer.RenderFragment` refactor

**Files:**
- Create: `src/ContentAutomatorX.Application/Newsletter/EmailFonts.cs`
- Modify: `src/ContentAutomatorX.Application/Newsletter/EmailHtmlRenderer.cs`
- Test: `tests/ContentAutomatorX.UnitTests/SectionHtmlRendererTests.cs` (new file, first tests)

**Interfaces:**
- Consumes: nothing new.
- Produces: `EmailFonts.All` (`IReadOnlyDictionary<string,(string Label, string Stack)>`, case-insensitive keys), `EmailFonts.Stack(string? key)` → CSS stack (unknown/null key → default), `EmailFonts.DefaultKey` (`"segoe"`), `EmailHtmlRenderer.DefaultAccent` (`"#1e88e5"`), `EmailHtmlRenderer.RenderFragment(string markdown, string accentHex = DefaultAccent)` → inline-styled HTML fragment (no `<html>` wrapper) with link/blockquote accents recolored.

- [ ] **Step 1: Write the failing tests**

Create `tests/ContentAutomatorX.UnitTests/SectionHtmlRendererTests.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.UnitTests;

public class SectionHtmlRendererTests
{
    [Fact]
    public void EmailFonts_resolves_known_key_case_insensitively_and_falls_back()
    {
        Assert.Contains("Georgia", EmailFonts.Stack("GEORGIA"));
        Assert.Equal(EmailFonts.Stack(EmailFonts.DefaultKey), EmailFonts.Stack(null));
        Assert.Equal(EmailFonts.Stack(EmailFonts.DefaultKey), EmailFonts.Stack("comic-sans"));
    }

    [Fact]
    public void RenderFragment_returns_inline_styled_html_without_document_wrapper()
    {
        var html = EmailHtmlRenderer.RenderFragment("# Hi\n\nsee [x](https://ex.com)");
        Assert.DoesNotContain("<html", html);
        Assert.Contains("<h1 style=", html);
        Assert.Contains("<a style=\"color:#1e88e5;\" href=\"https://ex.com\">", html);
    }

    [Fact]
    public void RenderFragment_recolors_links_with_the_given_accent()
    {
        var html = EmailHtmlRenderer.RenderFragment("see [x](https://ex.com)", "#7C3AED");
        Assert.Contains("color:#7C3AED;", html);
        Assert.DoesNotContain("#1e88e5", html);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter "FullyQualifiedName~SectionHtmlRendererTests"`
Expected: compile error — `EmailFonts` and `RenderFragment` do not exist.

- [ ] **Step 3: Create EmailFonts**

Create `src/ContentAutomatorX.Application/Newsletter/EmailFonts.cs`:

```csharp
namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Curated email-safe font stacks (spec §9.2). Keys are stored in
/// TenantBranding.FontKey; unknown keys fall back to the default stack.</summary>
public static class EmailFonts
{
    public const string DefaultKey = "segoe";

    public static readonly IReadOnlyDictionary<string, (string Label, string Stack)> All =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["segoe"] = ("Segoe UI (default)", "Segoe UI,Arial,sans-serif"),
            ["arial"] = ("Arial", "Arial,Helvetica,sans-serif"),
            ["helvetica"] = ("Helvetica", "Helvetica,Arial,sans-serif"),
            ["georgia"] = ("Georgia (serif)", "Georgia,'Times New Roman',serif"),
            ["verdana"] = ("Verdana", "Verdana,Geneva,sans-serif"),
            ["trebuchet"] = ("Trebuchet MS", "'Trebuchet MS',Helvetica,sans-serif"),
        };

    public static string Stack(string? key) =>
        key is not null && All.TryGetValue(key, out var font) ? font.Stack : All[DefaultKey].Stack;
}
```

- [ ] **Step 4: Extract RenderFragment in EmailHtmlRenderer**

In `src/ContentAutomatorX.Application/Newsletter/EmailHtmlRenderer.cs`, add the constant and the fragment method, and make `Render` use it. Replace the current `Render` method body's first two lines with a call:

```csharp
    public const string DefaultAccent = "#1e88e5";

    /// <summary>Markdown → inline-styled HTML fragment (no document wrapper). The default
    /// accent (#1e88e5) baked into InlineStyles is recolored when a custom accent is given.</summary>
    public static string RenderFragment(string markdown, string accentHex = DefaultAccent)
    {
        var body = Markdown.ToHtml(markdown ?? "", Pipeline);
        body = InlineStyles(body);
        return accentHex == DefaultAccent
            ? body
            : body.Replace($"color:{DefaultAccent};", $"color:{accentHex};")
                  .Replace($"border-left:3px solid {DefaultAccent};", $"border-left:3px solid {accentHex};");
    }

    public static string Render(string markdown, string title)
    {
        var body = RenderFragment(markdown);
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        // ... (rest of the existing method unchanged: the $""" document template """)
    }
```

- [ ] **Step 5: Run the tests (new + regression)**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter "FullyQualifiedName~SectionHtmlRendererTests|FullyQualifiedName~EmailHtmlRendererTests"`
Expected: all pass (3 new + all existing renderer tests still green).

- [ ] **Step 6: Commit**

```powershell
git add src/ContentAutomatorX.Application/Newsletter tests/ContentAutomatorX.UnitTests/SectionHtmlRendererTests.cs
git commit -m "feat: email font catalog + reusable EmailHtmlRenderer.RenderFragment (#composer)"
```

---

### Task 3: SectionHtmlRenderer — sections → email HTML + markdown export

**Files:**
- Create: `src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs`
- Test: `tests/ContentAutomatorX.UnitTests/SectionHtmlRendererTests.cs` (extend)

**Interfaces:**
- Consumes: `IssueSection`, `SectionTypes`, `Tenant` (Task 1); `EmailFonts.Stack`, `EmailHtmlRenderer.RenderFragment`, `EmailHtmlRenderer.DefaultAccent` (Task 2).
- Produces: `SectionHtmlRenderer.UnsubscribeToken` (`"%%UNSUBSCRIBE%%"`), `SectionHtmlRenderer.Render(IReadOnlyList<IssueSection> sections, Tenant tenant, string title)` → full email HTML document containing the token, `SectionHtmlRenderer.ToMarkdown(IReadOnlyList<IssueSection> sections)` → markdown export.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ContentAutomatorX.UnitTests/SectionHtmlRendererTests.cs` (inside the class):

```csharp
    private static Tenant TestTenant(string branding = "{}") => new()
    {
        Name = "Acme", Slug = "acme", BrandingJson = branding,
        SenderIdentity = "Acme Media, Musterstr. 1, Berlin, DE"
    };

    private static List<IssueSection> AllSectionTypes() =>
    [
        new() { Position = 0, Type = SectionTypes.Header, BodyMd = "Hi friends!" },
        new() { Position = 1, Type = SectionTypes.Topic, Title = "Big <News>", BodyMd = "It happened.",
                ImageUrl = "https://ex.com/pic.png", LinkUrl = "https://ex.com/story" },
        new() { Position = 2, Type = SectionTypes.Sponsor, Title = "Acme Tools", BodyMd = "Ship faster.",
                ImageUrl = "https://ex.com/logo.png", LinkUrl = "https://acme.dev", LinkText = "Try Acme" },
        new() { Position = 3, Type = SectionTypes.Button, LinkUrl = "https://ex.com/cta", LinkText = "Visit" },
        new() { Position = 4, Type = SectionTypes.Divider },
        new() { Position = 5, Type = SectionTypes.Footer, BodyMd = "Bye! — Chris" },
    ];

    [Fact]
    public void Render_produces_one_email_document_with_every_section_type()
    {
        var html = SectionHtmlRenderer.Render(AllSectionTypes(), TestTenant(), "AI Weekly #1");

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("AI Weekly #1", html);                      // title h1
        Assert.Contains("Hi friends!", html);                       // header
        Assert.Contains("Big &lt;News&gt;", html);                  // topic title, encoded
        Assert.Contains("src=\"https://ex.com/pic.png\"", html);    // topic image
        Assert.Contains("Read more", html);                         // topic link
        Assert.Contains("SPONSORED", html);                         // sponsor label
        Assert.Contains("Try Acme", html);                          // sponsor CTA
        Assert.Contains("https://ex.com/cta", html);                // button
        Assert.Contains("Bye! — Chris", html);                      // footer
        Assert.Contains(SectionHtmlRenderer.UnsubscribeToken, html);
        Assert.Contains("Acme Media, Musterstr. 1, Berlin, DE", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_applies_branding_and_ignores_invalid_accent()
    {
        var branded = SectionHtmlRenderer.Render(AllSectionTypes(),
            TestTenant("""{"accentColorHex":"#7C3AED","logoUrl":"https://ex.com/l.png","fontKey":"georgia"}"""),
            "t");
        Assert.Contains("color:#7C3AED;", branded);
        Assert.Contains("src=\"https://ex.com/l.png\"", branded);
        Assert.Contains("Georgia", branded);

        var evil = SectionHtmlRenderer.Render(AllSectionTypes(),
            TestTenant("""{"accentColorHex":"red;} body{display:none"}"""), "t");
        Assert.Contains(EmailHtmlRenderer.DefaultAccent, evil);
        Assert.DoesNotContain("display:none", evil);
    }

    [Fact]
    public void Render_drops_non_http_image_and_link_urls()
    {
        var sections = new List<IssueSection>
        {
            new() { Position = 0, Type = SectionTypes.Topic, Title = "T",
                    BodyMd = "b", ImageUrl = "javascript:alert(1)", LinkUrl = "file://c/x" },
        };
        var html = SectionHtmlRenderer.Render(sections, TestTenant(), "t");
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file://", html);
    }

    [Fact]
    public void Render_orders_by_position_not_list_order()
    {
        var sections = new List<IssueSection>
        {
            new() { Position = 1, Type = SectionTypes.Footer, BodyMd = "LAST" },
            new() { Position = 0, Type = SectionTypes.Header, BodyMd = "FIRST" },
        };
        var html = SectionHtmlRenderer.Render(sections, TestTenant(), "t");
        Assert.True(html.IndexOf("FIRST", StringComparison.Ordinal) < html.IndexOf("LAST", StringComparison.Ordinal));
    }

    [Fact]
    public void ToMarkdown_exports_all_section_types_without_the_compliance_footer()
    {
        var md = SectionHtmlRenderer.ToMarkdown(AllSectionTypes());
        Assert.Contains("Hi friends!", md);
        Assert.Contains("## Big <News>", md);
        Assert.Contains("[Read more](https://ex.com/story)", md);
        Assert.Contains("**Sponsored: Acme Tools**", md);
        Assert.Contains("[Try Acme](https://acme.dev)", md);
        Assert.Contains("[Visit](https://ex.com/cta)", md);
        Assert.Contains("---", md);
        Assert.Contains("Bye! — Chris", md);
        Assert.DoesNotContain("Unsubscribe", md);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter "FullyQualifiedName~SectionHtmlRendererTests"`
Expected: compile error — `SectionHtmlRenderer` does not exist.

- [ ] **Step 3: Implement the renderer**

Create `src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Renders an ordered issue-section list into one email-safe HTML document —
/// single 600px column, nested tables, inline styles only. ESP-neutral: the unsubscribe
/// link is emitted as UnsubscribeToken; the pushing connector substitutes its own variable
/// (MailerLite: {$unsubscribe}). The preview substitutes '#'.</summary>
public static partial class SectionHtmlRenderer
{
    public const string UnsubscribeToken = "%%UNSUBSCRIBE%%";

    public static string Render(IReadOnlyList<IssueSection> sections, Tenant tenant, string title)
    {
        var branding = TenantBranding.Parse(tenant.BrandingJson);
        var accent = SafeAccent(branding.AccentColorHex);
        var font = EmailFonts.Stack(branding.FontKey);
        var safeTitle = WebUtility.HtmlEncode(title);

        var sb = new StringBuilder();
        sb.AppendLine($"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"><title>{safeTitle}</title></head>
            <body style="margin:0;padding:0;background:#f4f4f4;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f4;"><tr><td align="center" style="padding:24px 8px;">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;font-family:{font};font-size:16px;line-height:1.6;color:#222222;"><tr><td style="padding:24px;">
            """);
        if (IsHttpUrl(branding.LogoUrl))
            sb.AppendLine($"""<img src="{WebUtility.HtmlEncode(branding.LogoUrl)}" alt="{WebUtility.HtmlEncode(tenant.Name)}" style="max-width:200px;height:auto;border:0;display:block;margin:0 auto 16px;" />""");
        sb.AppendLine($"""<h1 style="font-size:26px;margin:0 0 16px;color:#111111;">{safeTitle}</h1>""");

        foreach (var section in sections.OrderBy(s => s.Position))
            AppendSection(sb, section, accent);

        sb.AppendLine($"""
            <hr style="border:none;border-top:1px solid #dddddd;margin:24px 0 12px;" />
            <p style="margin:0 0 6px;font-size:12px;color:#888888;">{WebUtility.HtmlEncode(tenant.SenderIdentity)}</p>
            <p style="margin:0;font-size:12px;color:#888888;"><a href="{UnsubscribeToken}" style="color:#888888;">Unsubscribe</a></p>
              </td></tr></table>
              </td></tr></table>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, IssueSection s, string accent)
    {
        var title = WebUtility.HtmlEncode(s.Title ?? "");
        switch (s.Type)
        {
            case SectionTypes.Header:
            case SectionTypes.Footer:
            case SectionTypes.LegacyBody:
                sb.AppendLine(EmailHtmlRenderer.RenderFragment(s.BodyMd ?? "", accent));
                break;

            case SectionTypes.Topic:
                if (title.Length > 0)
                    sb.AppendLine($"""<h2 style="font-size:21px;margin:20px 0 10px;color:{accent};">{title}</h2>""");
                if (IsHttpUrl(s.ImageUrl))
                    sb.AppendLine($"""<img src="{WebUtility.HtmlEncode(s.ImageUrl)}" alt="{title}" style="max-width:100%;height:auto;border:0;display:block;margin:0 0 10px;" />""");
                sb.AppendLine(EmailHtmlRenderer.RenderFragment(s.BodyMd ?? "", accent));
                if (IsHttpUrl(s.LinkUrl))
                    sb.AppendLine($"""<p style="margin:0 0 14px;"><a href="{WebUtility.HtmlEncode(s.LinkUrl)}" style="color:{accent};">Read more &rarr;</a></p>""");
                break;

            case SectionTypes.Sponsor:
                sb.AppendLine("""<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 14px;"><tr><td style="border:1px solid #dddddd;background:#f9f9f9;padding:16px;">""");
                sb.AppendLine("""<p style="margin:0 0 8px;font-size:11px;letter-spacing:1px;color:#888888;">SPONSORED</p>""");
                if (IsHttpUrl(s.ImageUrl))
                    sb.AppendLine($"""<img src="{WebUtility.HtmlEncode(s.ImageUrl)}" alt="{title}" style="max-height:40px;height:auto;border:0;display:block;margin:0 0 8px;" />""");
                if (title.Length > 0)
                    sb.AppendLine($"""<h3 style="font-size:18px;margin:0 0 8px;color:#111111;">{title}</h3>""");
                sb.AppendLine(EmailHtmlRenderer.RenderFragment(s.BodyMd ?? "", accent));
                if (IsHttpUrl(s.LinkUrl))
                    AppendButton(sb, s.LinkUrl!, s.LinkText ?? "Learn more", accent);
                sb.AppendLine("</td></tr></table>");
                break;

            case SectionTypes.Button:
                if (IsHttpUrl(s.LinkUrl))
                    AppendButton(sb, s.LinkUrl!, s.LinkText ?? "Open", accent);
                break;

            case SectionTypes.Divider:
                sb.AppendLine("""<hr style="border:none;border-top:1px solid #dddddd;margin:20px 0;" />""");
                break;
        }
    }

    // "Bulletproof" table button — renders in Outlook and every major client.
    private static void AppendButton(StringBuilder sb, string url, string text, string accent) =>
        sb.AppendLine($"""
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 auto 14px;"><tr><td style="border-radius:4px;background:{accent};">
            <a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;padding:10px 22px;font-size:16px;color:#ffffff;text-decoration:none;">{WebUtility.HtmlEncode(text)}</a>
            </td></tr></table>
            """);

    public static string ToMarkdown(IReadOnlyList<IssueSection> sections)
    {
        var sb = new StringBuilder();
        foreach (var s in sections.OrderBy(x => x.Position))
        {
            switch (s.Type)
            {
                case SectionTypes.Header:
                case SectionTypes.Footer:
                case SectionTypes.LegacyBody:
                    AppendMd(sb, s.BodyMd);
                    break;
                case SectionTypes.Topic:
                    if (!string.IsNullOrWhiteSpace(s.Title)) AppendMd(sb, $"## {s.Title}");
                    AppendMd(sb, s.BodyMd);
                    if (!string.IsNullOrWhiteSpace(s.LinkUrl)) AppendMd(sb, $"[Read more]({s.LinkUrl})");
                    break;
                case SectionTypes.Sponsor:
                    AppendMd(sb, $"**Sponsored{(string.IsNullOrWhiteSpace(s.Title) ? "" : $": {s.Title}")}**");
                    AppendMd(sb, s.BodyMd);
                    if (!string.IsNullOrWhiteSpace(s.LinkUrl)) AppendMd(sb, $"[{s.LinkText ?? "Learn more"}]({s.LinkUrl})");
                    break;
                case SectionTypes.Button:
                    if (!string.IsNullOrWhiteSpace(s.LinkUrl)) AppendMd(sb, $"[{s.LinkText ?? "Open"}]({s.LinkUrl})");
                    break;
                case SectionTypes.Divider:
                    AppendMd(sb, "---");
                    break;
            }
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendMd(StringBuilder sb, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        sb.AppendLine(text);
        sb.AppendLine();
    }

    private static string SafeAccent(string? hex) =>
        hex is not null && AccentRegex().IsMatch(hex) ? hex : EmailHtmlRenderer.DefaultAccent;

    private static bool IsHttpUrl(string? url) =>
        url is not null &&
        (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex AccentRegex();
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter "FullyQualifiedName~SectionHtmlRendererTests"`
Expected: 8 passed.

- [ ] **Step 5: Commit**

```powershell
git add src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs tests/ContentAutomatorX.UnitTests/SectionHtmlRendererTests.cs
git commit -m "feat: SectionHtmlRenderer — sections to email HTML and markdown export (#composer)"
```

### Task 4: IssueComposerService — section lifecycle

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`
- Modify: `src/ContentAutomatorX.Web/Program.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `IAppDbContext.IssueSections` (Task 1), `SectionHtmlRenderer.{Render,ToMarkdown,UnsubscribeToken}` (Task 3), existing `PostService.CreateIssueAsync(Guid tenantId, Guid recipeId, int windowDays, IReadOnlyList<Guid>? sourceIds, string title, CancellationToken ct)`, `ILlmBackend.GenerateAsync(string, CancellationToken)`.
- Produces (all on `IssueComposerService(IAppDbContext db, ILlmBackend llm, PostService posts)`):
  - `Task<List<IssueSection>> GetSectionsAsync(Guid postId, CancellationToken ct = default)` — ordered by Position
  - `Task<Post> CreateFromItemsAsync(Guid tenantId, Guid recipeId, IReadOnlyList<Guid> itemIds, string title, CancellationToken ct = default)`
  - `Task EnsureSectionsAsync(Guid postId, CancellationToken ct = default)`
  - `Task<IssueSection> AddSectionAsync(Guid postId, string type, CancellationToken ct = default)` — inserts above the footer
  - `Task AddTopicsFromItemsAsync(Guid postId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default)`
  - `Task UpdateSectionAsync(Guid sectionId, string? title, string? bodyMd, string? imageUrl, string? linkUrl, string? linkText, CancellationToken ct = default)`
  - `Task RemoveSectionAsync(Guid sectionId, CancellationToken ct = default)`
  - `Task MoveSectionAsync(Guid sectionId, int direction, CancellationToken ct = default)` — direction −1 = up, +1 = down
  - `Task<string> ExportMarkdownAsync(Guid postId, CancellationToken ct = default)`
  - `Task<string> RenderPreviewAsync(Guid postId, string title, CancellationToken ct = default)` — token → `#`
  - (Task 5 adds the generation members to this same class.)

- [ ] **Step 1: Write the failing tests**

Append to `IssueComposerServiceTests.cs`. First the shared world builder (inside the class):

```csharp
    private sealed record World(TestDb Test, PlatformService Platforms, FakeMailerLite MailerLite,
        Tenant Tenant, Recipe Recipe, Source Source, List<ContentItem> Items);

    private static async Task<World> BuildAsync()
    {
        var test = TestDb.Create();
        var tenant = new Tenant
        {
            Name = "T", Slug = "t-composer",
            DefaultHeaderMd = "Hi friends!", DefaultFooterMd = "Bye! — Chris",
            SenderIdentity = "Acme Media, Musterstr. 1, Berlin, DE"
        };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        var template = new PromptTemplate { TenantId = null, Kind = DraftKinds.Newsletter, Template = "{items}" };
        var recipe = new Recipe
        {
            TenantId = tenant.Id, Name = "AI Weekly", Kind = DraftKinds.Newsletter,
            PromptTemplateId = template.Id, ToneModifiers = "punchy"
        };
        test.Db.AddRange(tenant, source, template, recipe);
        var items = new List<ContentItem>();
        for (var n = 1; n <= 3; n++)
        {
            var item = new ContentItem
            {
                TenantId = tenant.Id, SourceId = source.Id, ExternalId = $"e{n}",
                Title = $"Item {n}", Url = $"https://ex.com/{n}", Body = $"body {n}",
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            items.Add(item);
            test.Db.ContentItems.Add(item);
        }
        await test.Db.SaveChangesAsync();
        var ml = new FakeMailerLite();
        var platforms = new PlatformService(test.Db, new InMemoryCredentials(), ml);
        return new World(test, platforms, ml, tenant, recipe, source, items);
    }

    private static IssueComposerService Composer(World w, ILlmBackend llm) =>
        new(w.Test.Db, llm,
            new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery()),
                llm, w.Platforms, w.MailerLite));
```

Then the lifecycle tests:

```csharp
    [Fact]
    public async Task CreateFromItems_builds_header_topics_footer_with_contiguous_positions()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());

        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "AI Weekly #1");
        var sections = await composer.GetSectionsAsync(post.Id);

        Assert.Equal(5, sections.Count);
        Assert.Equal(SectionTypes.Header, sections[0].Type);
        Assert.Equal("Hi friends!", sections[0].BodyMd);
        Assert.Equal(SectionTypes.Footer, sections[^1].Type);
        Assert.Equal(Enumerable.Range(0, 5), sections.Select(s => s.Position));
        var topic = sections[1];
        Assert.Equal(SectionTypes.Topic, topic.Type);
        Assert.Equal("Item 1", topic.Title);
        Assert.Equal("https://ex.com/1", topic.LinkUrl);
        Assert.Equal(w.Items[0].Id, topic.SourceItemId);
        Assert.True(string.IsNullOrEmpty(topic.BodyMd)); // skeleton until generation
    }

    [Fact]
    public async Task EnsureSections_wraps_a_legacy_draft_body_and_is_idempotent()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        var draft = new Draft { TenantId = w.Tenant.Id, RecipeId = w.Recipe.Id, Kind = DraftKinds.Newsletter, Title = "Old", Body = "old markdown body" };
        var post = new Post { TenantId = w.Tenant.Id, PlatformId = platform.Id, DraftId = draft.Id, Kind = DraftKinds.Newsletter, Title = "Old issue" };
        w.Test.Db.AddRange(draft, post);
        await w.Test.Db.SaveChangesAsync();

        await composer.EnsureSectionsAsync(post.Id);
        await composer.EnsureSectionsAsync(post.Id); // idempotent

        var sections = await composer.GetSectionsAsync(post.Id);
        Assert.Equal(3, sections.Count);
        Assert.Equal(new[] { SectionTypes.Header, SectionTypes.LegacyBody, SectionTypes.Footer },
            sections.Select(s => s.Type));
        Assert.Equal("old markdown body", sections[1].BodyMd);
    }

    [Fact]
    public async Task AddSection_inserts_above_footer_and_rejects_header_footer()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");

        var sponsor = await composer.AddSectionAsync(post.Id, SectionTypes.Sponsor);

        var sections = await composer.GetSectionsAsync(post.Id);
        Assert.Equal(sponsor.Id, sections[^2].Id);                 // directly above the footer
        Assert.Equal(SectionTypes.Footer, sections[^1].Type);
        Assert.Equal(Enumerable.Range(0, sections.Count), sections.Select(s => s.Position));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.AddSectionAsync(post.Id, SectionTypes.Header));
    }

    [Fact]
    public async Task Move_swaps_within_bounds_and_never_crosses_header_or_footer()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "t");
        var sections = await composer.GetSectionsAsync(post.Id);
        var first = sections[1];   // topic "Item 1"
        var second = sections[2];  // topic "Item 2"

        await composer.MoveSectionAsync(second.Id, -1);            // swap 1 and 2
        var after = await composer.GetSectionsAsync(post.Id);
        Assert.Equal(new[] { second.Id, first.Id }, after.Skip(1).Take(2).Select(s => s.Id));

        await composer.MoveSectionAsync(second.Id, -1);            // would cross the header — no-op
        Assert.Equal(second.Id, (await composer.GetSectionsAsync(post.Id))[1].Id);
        await composer.MoveSectionAsync(sections[3].Id, 1);        // would cross the footer — no-op
        Assert.Equal(SectionTypes.Footer, (await composer.GetSectionsAsync(post.Id))[^1].Type);
    }

    [Fact]
    public async Task Update_and_remove_persist_and_renumber_but_protect_header_footer()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "t");
        var sections = await composer.GetSectionsAsync(post.Id);

        await composer.UpdateSectionAsync(sections[1].Id, "New title", "new body", null, "https://ex.com/new", null);
        await composer.RemoveSectionAsync(sections[2].Id);

        var after = await composer.GetSectionsAsync(post.Id);
        Assert.Equal(4, after.Count);
        Assert.Equal("New title", after[1].Title);
        Assert.Equal("new body", after[1].BodyMd);
        Assert.Equal(Enumerable.Range(0, 4), after.Select(s => s.Position));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.RemoveSectionAsync(after[0].Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.RemoveSectionAsync(after[^1].Id));
    }

    [Fact]
    public async Task Export_and_preview_render_the_sections()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "My issue");

        var md = await composer.ExportMarkdownAsync(post.Id);
        Assert.Contains("## Item 1", md);
        Assert.Contains("Hi friends!", md);

        var html = await composer.RenderPreviewAsync(post.Id, "My issue");
        Assert.Contains("My issue", html);
        Assert.Contains("href=\"#\"", html);                                    // token replaced for preview
        Assert.DoesNotContain(SectionHtmlRenderer.UnsubscribeToken, html);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IssueComposerServiceTests"`
Expected: compile error — `IssueComposerService` does not exist.

- [ ] **Step 3: Implement the service**

Create `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`:

```csharp
using System.Text;
using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

/// <summary>Owns the structured-issue composer: section lifecycle (Task 4) and AI topic
/// generation (Task 5). An issue always has exactly one Header (first) and one Footer (last);
/// positions stay 0-based and contiguous after every mutation.</summary>
public class IssueComposerService(IAppDbContext db, ILlmBackend llm, PostService posts)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<IssueSection>> GetSectionsAsync(Guid postId, CancellationToken ct = default) =>
        await db.IssueSections.Where(s => s.PostId == postId).OrderBy(s => s.Position).ToListAsync(ct);

    public async Task<Post> CreateFromItemsAsync(Guid tenantId, Guid recipeId, IReadOnlyList<Guid> itemIds,
        string title, CancellationToken ct = default)
    {
        if (itemIds.Count == 0) throw new InvalidOperationException("Select at least one inbox item.");
        var post = await posts.CreateIssueAsync(tenantId, recipeId, 7, null, title, ct);
        await EnsureSectionsAsync(post.Id, ct);
        await AddTopicsFromItemsAsync(post.Id, itemIds, ct);
        return post;
    }

    public async Task EnsureSectionsAsync(Guid postId, CancellationToken ct = default)
    {
        if (await db.IssueSections.AnyAsync(s => s.PostId == postId, ct)) return;
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        db.IssueSections.Add(new IssueSection
            { PostId = postId, Position = 0, Type = SectionTypes.Header, BodyMd = tenant.DefaultHeaderMd });
        var position = 1;
        if (post.DraftId is Guid draftId) // legacy free-markdown issue → keep its body editable
        {
            var draft = await db.Drafts.SingleAsync(d => d.Id == draftId, ct);
            db.IssueSections.Add(new IssueSection
                { PostId = postId, Position = position++, Type = SectionTypes.LegacyBody, BodyMd = draft.Body });
        }
        db.IssueSections.Add(new IssueSection
            { PostId = postId, Position = position, Type = SectionTypes.Footer, BodyMd = tenant.DefaultFooterMd });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IssueSection> AddSectionAsync(Guid postId, string type, CancellationToken ct = default)
    {
        if (type is SectionTypes.Header or SectionTypes.Footer)
            throw new InvalidOperationException("An issue has exactly one header and one footer.");
        var sections = await GetSectionsAsync(postId, ct);
        var section = new IssueSection { PostId = postId, Type = type };
        sections.Insert(Math.Max(sections.Count - 1, 0), section); // above the footer
        Renumber(sections);
        db.IssueSections.Add(section);
        await db.SaveChangesAsync(ct);
        return section;
    }

    public async Task AddTopicsFromItemsAsync(Guid postId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var byId = (await db.ContentItems
                .Where(i => i.TenantId == post.TenantId && itemIds.Contains(i.Id)).ToListAsync(ct))
            .ToDictionary(i => i.Id);
        var sections = await GetSectionsAsync(postId, ct);
        var insertAt = Math.Max(sections.Count - 1, 0); // above the footer
        foreach (var id in itemIds) // preserve the caller's order
        {
            if (!byId.TryGetValue(id, out var item)) continue;
            var topic = new IssueSection
            {
                PostId = postId, Type = SectionTypes.Topic, Title = item.Title,
                LinkUrl = item.Url, SourceItemId = item.Id, ImageUrl = MetadataImage(item)
            };
            sections.Insert(insertAt++, topic);
            db.IssueSections.Add(topic);
        }
        Renumber(sections);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateSectionAsync(Guid sectionId, string? title, string? bodyMd,
        string? imageUrl, string? linkUrl, string? linkText, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        section.Title = title;
        section.BodyMd = bodyMd;
        section.ImageUrl = imageUrl;
        section.LinkUrl = linkUrl;
        section.LinkText = linkText;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveSectionAsync(Guid sectionId, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        if (section.Type is SectionTypes.Header or SectionTypes.Footer)
            throw new InvalidOperationException("Header and footer cannot be removed — edit them instead.");
        var sections = await GetSectionsAsync(section.PostId, ct);
        sections.RemoveAll(s => s.Id == sectionId);
        db.IssueSections.Remove(section);
        Renumber(sections);
        await db.SaveChangesAsync(ct);
    }

    public async Task MoveSectionAsync(Guid sectionId, int direction, CancellationToken ct = default)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        if (section.Type is SectionTypes.Header or SectionTypes.Footer) return;
        var sections = await GetSectionsAsync(section.PostId, ct);
        var index = sections.FindIndex(s => s.Id == sectionId);
        var target = index + direction;
        if (target <= 0 || target >= sections.Count - 1) return; // stay between header and footer
        (sections[index], sections[target]) = (sections[target], sections[index]);
        Renumber(sections);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> ExportMarkdownAsync(Guid postId, CancellationToken ct = default)
    {
        var sections = await GetSectionsAsync(postId, ct);
        if (sections.Count == 0) throw new InvalidOperationException("Nothing to export yet.");
        return SectionHtmlRenderer.ToMarkdown(sections);
    }

    public async Task<string> RenderPreviewAsync(Guid postId, string title, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        var sections = await GetSectionsAsync(postId, ct);
        return SectionHtmlRenderer.Render(sections, tenant, title)
            .Replace(SectionHtmlRenderer.UnsubscribeToken, "#");
    }

    private static void Renumber(List<IssueSection> ordered)
    {
        for (var n = 0; n < ordered.Count; n++) ordered[n].Position = n;
    }

    private static string? MetadataImage(ContentItem item)
    {
        try
        {
            using var doc = JsonDocument.Parse(item.MetadataJson);
            return doc.RootElement.TryGetProperty("image", out var v) ? v.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
```

- [ ] **Step 4: Register in DI**

In `src/ContentAutomatorX.Web/Program.cs`, after `builder.Services.AddScoped<PostService>();` add:

```csharp
builder.Services.AddScoped<IssueComposerService>();
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IssueComposerServiceTests"`
Expected: 8 passed (2 from Task 1 + 6 new).

- [ ] **Step 6: Commit**

```powershell
git add src/ContentAutomatorX.Application/Services/IssueComposerService.cs src/ContentAutomatorX.Web/Program.cs tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs
git commit -m "feat: IssueComposerService section lifecycle (#composer)"
```

### Task 5: IssueComposerService — AI generation

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`
- Test: `tests/ContentAutomatorX.UnitTests/TopicParsingTests.cs` (create)
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs` (extend)

**Interfaces:**
- Consumes: Task 4's service internals; `ILlmBackend.GenerateAsync`; `ContentItemStatus.Used`.
- Produces: `public record TopicBlurb(Guid ItemId, string Title, string Blurb)` (same file, namespace `ContentAutomatorX.Application.Services`); on `IssueComposerService`:
  - `Task<int> GenerateTopicsAsync(Guid postId, string? extraInstructions, CancellationToken ct = default)` — fills only empty-`BodyMd` topics with a `SourceItemId`, one retry on malformed JSON, marks source items `Used`, returns filled count, throws `InvalidOperationException` after two bad replies
  - `Task RegenerateSectionAsync(Guid sectionId, string? instruction, CancellationToken ct = default)` — Topic and Header only
  - `public static bool TryParseTopics(string text, out List<TopicBlurb>? topics)` — strips ``` fences like `PostService.TryParseStringArray`

- [ ] **Step 1: Write the failing parser unit tests**

Create `tests/ContentAutomatorX.UnitTests/TopicParsingTests.cs`:

```csharp
using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.UnitTests;

public class TopicParsingTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Parses_a_plain_json_array()
    {
        var ok = IssueComposerService.TryParseTopics(
            $$"""[{"itemId":"{{Id}}","title":"T","blurb":"B"}]""", out var topics);
        Assert.True(ok);
        Assert.Equal(new TopicBlurb(Id, "T", "B"), Assert.Single(topics!));
    }

    [Fact]
    public void Parses_a_fenced_json_array()
    {
        var ok = IssueComposerService.TryParseTopics(
            $$"""
            ```json
            [{"itemId":"{{Id}}","title":"T","blurb":"B"}]
            ```
            """, out var topics);
        Assert.True(ok);
        Assert.Single(topics!);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]                                                    // empty array is useless
    [InlineData("""[{"itemId":"11111111-2222-3333-4444-555555555555","title":"T","blurb":""}]""")]  // blank blurb
    [InlineData("""[{"itemId":"00000000-0000-0000-0000-000000000000","title":"T","blurb":"B"}]""")] // empty guid
    public void Rejects_unusable_replies(string reply)
    {
        Assert.False(IssueComposerService.TryParseTopics(reply, out var topics));
        Assert.Null(topics);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter "FullyQualifiedName~TopicParsingTests"`
Expected: compile error — `TryParseTopics` / `TopicBlurb` do not exist.

- [ ] **Step 3: Write the failing integration tests**

Append to `IssueComposerServiceTests.cs`. First a reply-sequencing fake (top of file, next to the class, same namespace):

```csharp
public class SequenceLlm(params string[] replies) : ILlmBackend
{
    private int _n;
    public string Name => "seq";
    public List<string> Prompts { get; } = [];
    public Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        Prompts.Add(prompt);
        var reply = replies[Math.Min(_n++, replies.Length - 1)];
        return Task.FromResult(new LlmResult(reply, "seq-model"));
    }
}
```

Then, inside the test class, a helper and the tests:

```csharp
    private static string TopicsJson(IEnumerable<ContentItem> items) =>
        "[" + string.Join(",", items.Select(i =>
            $$"""{"itemId":"{{i.Id}}","title":"{{i.Title}} improved","blurb":"Blurb for {{i.Title}}."}""")) + "]";

    [Fact]
    public async Task GenerateTopics_fills_only_empty_topics_and_marks_items_used()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm(TopicsJson(w.Items));
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "t");
        var sections = await composer.GetSectionsAsync(post.Id);
        await composer.UpdateSectionAsync(sections[1].Id, sections[1].Title, "HAND EDITED", null, sections[1].LinkUrl, null);

        var filled = await composer.GenerateTopicsAsync(post.Id, "keep it short");

        Assert.Equal(2, filled);                                            // topic 1 was hand-edited
        var after = await composer.GetSectionsAsync(post.Id);
        Assert.Equal("HAND EDITED", after[1].BodyMd);                       // bulk never overwrites edits
        Assert.Equal("Blurb for Item 2.", after[2].BodyMd);
        Assert.Equal("Item 2 improved", after[2].Title);
        Assert.Contains("keep it short", llm.Prompts.Single());
        Assert.Contains("punchy", llm.Prompts.Single());                    // recipe tone reached the prompt
        Assert.Equal(2, await w.Test.Db.ContentItems.CountAsync(i => i.Status == ContentItemStatus.Used));
    }

    [Fact]
    public async Task GenerateTopics_retries_once_then_succeeds()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("garbage", TopicsJson(w.Items.Take(1)));
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");

        var filled = await composer.GenerateTopicsAsync(post.Id, null);

        Assert.Equal(1, filled);
        Assert.Equal(2, llm.Prompts.Count);
        Assert.Contains("was not valid JSON", llm.Prompts[1]);
    }

    [Fact]
    public async Task GenerateTopics_throws_after_two_bad_replies_and_keeps_skeletons()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var composer = Composer(w, new SequenceLlm("garbage", "more garbage"));
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");

        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.GenerateTopicsAsync(post.Id, null));

        var sections = await composer.GetSectionsAsync(post.Id);
        Assert.True(string.IsNullOrEmpty(sections[1].BodyMd));              // skeleton intact for retry
        Assert.Equal(0, await w.Test.Db.ContentItems.CountAsync(i => i.Status == ContentItemStatus.Used));
    }

    [Fact]
    public async Task GenerateTopics_with_no_empty_topics_is_a_noop_without_llm_calls()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("should never be used");
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var sections = await composer.GetSectionsAsync(post.Id);
        await composer.UpdateSectionAsync(sections[1].Id, "T", "done", null, null, null);

        Assert.Equal(0, await composer.GenerateTopicsAsync(post.Id, null));
        Assert.Empty(llm.Prompts);
    }

    [Fact]
    public async Task RegenerateSection_rewrites_a_topic_blurb_from_its_source_item()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("A fresh new blurb.");
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var topic = (await composer.GetSectionsAsync(post.Id))[1];

        await composer.RegenerateSectionAsync(topic.Id, "shorter");

        Assert.Equal("A fresh new blurb.", (await composer.GetSectionsAsync(post.Id))[1].BodyMd);
        Assert.Contains("body 1", llm.Prompts.Single());                    // source item material in prompt
        Assert.Contains("shorter", llm.Prompts.Single());
    }

    [Fact]
    public async Task RegenerateSection_writes_a_header_intro_referencing_topics_and_rejects_other_types()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("Welcome! This week: things.");
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "t");
        var sections = await composer.GetSectionsAsync(post.Id);

        await composer.RegenerateSectionAsync(sections[0].Id, null);

        Assert.Equal("Welcome! This week: things.", (await composer.GetSectionsAsync(post.Id))[0].BodyMd);
        Assert.Contains("Item 1", llm.Prompts.Single());                    // topic titles in prompt
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => composer.RegenerateSectionAsync(sections[^1].Id, null));  // footer is not regenerable
    }
```

- [ ] **Step 4: Run to verify the new integration tests fail**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IssueComposerServiceTests"`
Expected: compile error — `GenerateTopicsAsync` / `RegenerateSectionAsync` / `SequenceLlm` missing.

- [ ] **Step 5: Implement generation**

In `IssueComposerService.cs`, add the record above the class:

```csharp
public record TopicBlurb(Guid ItemId, string Title, string Blurb);
```

and these members to the class:

```csharp
    public async Task<int> GenerateTopicsAsync(Guid postId, string? extraInstructions, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        var recipe = post.RecipeId is Guid recipeId
            ? await db.Recipes.SingleAsync(r => r.Id == recipeId, ct) : null;
        var skeletons = (await GetSectionsAsync(postId, ct))
            .Where(s => s.Type == SectionTypes.Topic && string.IsNullOrWhiteSpace(s.BodyMd) && s.SourceItemId is not null)
            .ToList();
        if (skeletons.Count == 0) return 0;

        var itemIds = skeletons.Select(s => s.SourceItemId!.Value).ToList();
        var items = await db.ContentItems.Where(i => itemIds.Contains(i.Id)).ToListAsync(ct);
        var prompt = BuildTopicsPrompt(tenant, recipe, items, extraInstructions);

        List<TopicBlurb>? topics = null;
        for (var attempt = 1; attempt <= 2 && topics is null; attempt++)
        {
            var reply = await llm.GenerateAsync(attempt == 1 ? prompt
                : prompt + "\nYour previous reply was not valid JSON. Respond with ONLY the JSON array.", ct);
            TryParseTopics(reply.Text, out topics);
        }
        if (topics is null)
            throw new InvalidOperationException("The model did not return topic blurbs as JSON — try again.");

        var byItem = topics.ToDictionary(t => t.ItemId);
        var filled = 0;
        foreach (var section in skeletons)
        {
            if (!byItem.TryGetValue(section.SourceItemId!.Value, out var topic)) continue;
            section.BodyMd = topic.Blurb;
            if (!string.IsNullOrWhiteSpace(topic.Title)) section.Title = topic.Title;
            filled++;
        }
        foreach (var item in items) item.Status = ContentItemStatus.Used;
        await db.SaveChangesAsync(ct);
        return filled;
    }

    public async Task RegenerateSectionAsync(Guid sectionId, string? instruction, CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        var post = await db.Posts.SingleAsync(p => p.Id == section.PostId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        var voice = string.IsNullOrWhiteSpace(tenant.VoiceProfile) ? "" : $"Voice: {tenant.VoiceProfile}\n";
        var extra = string.IsNullOrWhiteSpace(instruction) ? "" : $"Extra instructions: {instruction}\n";
        string prompt;
        if (section.Type == SectionTypes.Header)
        {
            var topicTitles = (await GetSectionsAsync(section.PostId, ct))
                .Where(s => s.Type == SectionTypes.Topic && !string.IsNullOrWhiteSpace(s.Title))
                .Select(s => $"- {s.Title}");
            prompt = $"""
                Write a 2-3 sentence newsletter intro greeting the readers and teasing these topics.
                {voice}{extra}Topics:
                {string.Join("\n", topicTitles)}
                Respond with ONLY the intro markdown, no heading, no fences.
                """;
        }
        else if (section.Type == SectionTypes.Topic)
        {
            var item = section.SourceItemId is Guid itemId
                ? await db.ContentItems.FirstOrDefaultAsync(i => i.Id == itemId, ct) : null;
            var material = item is null ? section.BodyMd ?? ""
                : item.Body.Length > 2000 ? item.Body[..2000] : item.Body;
            prompt = $"""
                Rewrite this newsletter topic blurb (2-4 sentences, markdown, no heading).
                {voice}{extra}Topic: {section.Title}
                Material:
                {material}
                Respond with ONLY the blurb markdown, no fences.
                """;
        }
        else
        {
            throw new InvalidOperationException("Only topics and the header can be regenerated.");
        }
        var reply = await llm.GenerateAsync(prompt, ct);
        section.BodyMd = reply.Text.Trim();
        await db.SaveChangesAsync(ct);
    }

    private static string BuildTopicsPrompt(Tenant tenant, Recipe? recipe,
        IReadOnlyList<ContentItem> items, string? extraInstructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You write newsletter topic blurbs.");
        if (!string.IsNullOrWhiteSpace(tenant.VoiceProfile)) sb.AppendLine($"Voice: {tenant.VoiceProfile}");
        if (!string.IsNullOrWhiteSpace(recipe?.ToneModifiers)) sb.AppendLine($"Tone: {recipe.ToneModifiers}");
        if (!string.IsNullOrWhiteSpace(recipe?.Language)) sb.AppendLine($"Write in: {recipe.Language}");
        if (!string.IsNullOrWhiteSpace(extraInstructions)) sb.AppendLine($"Extra instructions: {extraInstructions}");
        sb.AppendLine();
        sb.AppendLine("Write one short markdown blurb (2-4 sentences) per item below. Improve the title when it helps.");
        sb.AppendLine("""Respond with ONLY a JSON array, no prose, no markdown fences: [{"itemId":"<id>","title":"...","blurb":"..."}]""");
        foreach (var item in items)
        {
            sb.AppendLine($"--- itemId: {item.Id} ---");
            sb.AppendLine($"Title: {item.Title}");
            if (item.Url is not null) sb.AppendLine($"URL: {item.Url}");
            var body = item.Body.Length > 2000 ? item.Body[..2000] + " [truncated]" : item.Body;
            if (body.Length > 0) sb.AppendLine(body);
        }
        return sb.ToString();
    }

    // Public because the unit-test project asserts the contract directly (no InternalsVisibleTo in this repo).
    public static bool TryParseTopics(string text, out List<TopicBlurb>? topics)
    {
        topics = null;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }
        try
        {
            var parsed = JsonSerializer.Deserialize<List<TopicBlurb>>(trimmed, JsonOpts);
            if (parsed is { Count: > 0 } &&
                parsed.All(t => t.ItemId != Guid.Empty && !string.IsNullOrWhiteSpace(t.Blurb)))
            {
                topics = parsed;
                return true;
            }
            return false;
        }
        catch (JsonException) { return false; }
    }
```

- [ ] **Step 6: Run all new tests**

Run: `dotnet test tests/ContentAutomatorX.UnitTests --filter "FullyQualifiedName~TopicParsingTests"`
Expected: 6 passed.
Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IssueComposerServiceTests"`
Expected: 14 passed.

- [ ] **Step 7: Commit**

```powershell
git add src/ContentAutomatorX.Application/Services/IssueComposerService.cs tests
git commit -m "feat: AI topic generation with strict-JSON contract and per-section rewrite (#composer)"
```

### Task 6: PostService — sectioned push, meta save, subject rules

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/PostService.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `SectionHtmlRenderer.{Render,ToMarkdown,UnsubscribeToken}` (Task 3), `db.IssueSections` (Task 1).
- Produces: `PostService.SaveIssueMetaAsync(Guid postId, string title, string? subject, string? previewText, CancellationToken ct = default)`; `PushAsync` now renders sections when any exist (token → `{$unsubscribe}`), keeps the legacy draft-markdown path otherwise, and validates subject (required via Title fallback, ≤255 chars); `SubjectIdeasAsync` reads section markdown when sections exist.

- [ ] **Step 1: Write the failing tests**

Append to `IssueComposerServiceTests.cs` (inside the class):

```csharp
    private static async Task ConfigureMailerLiteAsync(World w)
    {
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        await w.Platforms.SetApiKeyAsync(platform, "KEY");
        await w.Platforms.SaveConfigAsync(platform, new MailerLiteConfig("g1", "Main", "Acme", "n@x.com"));
    }

    [Fact]
    public async Task Push_renders_sections_with_the_mailerlite_unsubscribe_variable_and_needs_no_draft()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        await ConfigureMailerLiteAsync(w);
        var llm = new SequenceLlm(TopicsJson(w.Items.Take(1)));
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "Sectioned issue");
        await composer.GenerateTopicsAsync(post.Id, null);
        var posts = new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery()),
            llm, w.Platforms, w.MailerLite);

        var pushed = await posts.PushAsync(post.Id);

        Assert.Equal(PostStatus.Pushed, pushed.Status);
        Assert.Null(pushed.DraftId);                                        // no Draft row was ever needed
        var html = w.MailerLite.Pushes.Single().Draft.Html;
        Assert.Contains("Blurb for Item 1.", html);
        Assert.Contains("Hi friends!", html);                               // tenant default header
        Assert.Contains("{$unsubscribe}", html);
        Assert.DoesNotContain(SectionHtmlRenderer.UnsubscribeToken, html);
    }

    [Fact]
    public async Task Push_rejects_an_overlong_subject_before_calling_mailerlite()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        await ConfigureMailerLiteAsync(w);
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var posts = new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, new FakeLlm(), new FakeDelivery()),
            new FakeLlm(), w.Platforms, w.MailerLite);
        await posts.SaveIssueMetaAsync(post.Id, "t", new string('x', 256), null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => posts.PushAsync(post.Id));

        Assert.Contains("255", ex.Message);
        Assert.Empty(w.MailerLite.Pushes);
    }

    [Fact]
    public async Task SaveIssueMeta_persists_title_subject_preview_without_touching_sections()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "Old");
        var posts = new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, new FakeLlm(), new FakeDelivery()),
            new FakeLlm(), w.Platforms, w.MailerLite);

        await posts.SaveIssueMetaAsync(post.Id, "New title", "Subj", "Pv");

        using var fresh = w.Test.NewContext();
        var reloaded = await fresh.Posts.SingleAsync(p => p.Id == post.Id);
        Assert.Equal(("New title", "Subj", "Pv"), (reloaded.Title, reloaded.Subject, reloaded.PreviewText));
        Assert.Null(reloaded.DraftId);                                      // meta save never creates a Draft
        Assert.Equal(3, await fresh.IssueSections.CountAsync(s => s.PostId == post.Id));
    }

    [Fact]
    public async Task SubjectIdeas_reads_section_markdown_for_sectioned_issues()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm(TopicsJson(w.Items.Take(1)), "[\"s1\",\"s2\",\"s3\",\"s4\",\"s5\"]");
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        await composer.GenerateTopicsAsync(post.Id, null);
        var posts = new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery()),
            llm, w.Platforms, w.MailerLite);

        var ideas = await posts.SubjectIdeasAsync(post.Id);

        Assert.Equal(5, ideas.Count);
        Assert.Contains("Blurb for Item 1.", llm.Prompts[^1]);              // section markdown reached the prompt
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IssueComposerServiceTests"`
Expected: compile error — `SaveIssueMetaAsync` does not exist (and push/subject tests would fail).

- [ ] **Step 3: Implement the PostService changes**

In `src/ContentAutomatorX.Application/Services/PostService.cs`:

Add after `SaveIssueAsync`:

```csharp
    /// <summary>Title/subject/preview for a sectioned issue — body lives in IssueSections,
    /// so unlike SaveIssueAsync this never creates or touches a Draft.</summary>
    public async Task SaveIssueMetaAsync(Guid postId, string title, string? subject,
        string? previewText, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        post.Title = title;
        post.Subject = subject;
        post.PreviewText = previewText;
        await db.SaveChangesAsync(ct);
    }
```

Replace `SubjectIdeasAsync`'s body-loading lines (the `var draft = ...` and `var excerpt = ...` lines — keep the existing `var post = ...` line above them) with:

```csharp
        var sections = await db.IssueSections.Where(s => s.PostId == postId)
            .OrderBy(s => s.Position).ToListAsync(ct);
        string body;
        if (sections.Count > 0)
        {
            body = SectionHtmlRenderer.ToMarkdown(sections);
        }
        else
        {
            var draft = post.DraftId is Guid id ? await db.Drafts.SingleAsync(d => d.Id == id, ct)
                : throw new InvalidOperationException("Nothing to write subjects for yet.");
            body = draft.Body;
        }
        var excerpt = body.Length <= 4000 ? body : body[..4000];
```

Replace the top of `PushAsync` (everything from the `var draft = ...` line through the `var html = EmailHtmlRenderer.Render(...)` line) with:

```csharp
        var platform = await db.Platforms.SingleAsync(p => p.Id == post.PlatformId, ct);
        var config = platforms.GetConfig(platform);
        var apiKey = await platforms.GetApiKeyAsync(platform, ct);
        if (apiKey is null || config.GroupId is null || config.FromName is null || config.FromEmail is null)
            throw new InvalidOperationException("MailerLite is not fully configured — finish setup on the Platforms page.");

        var subject = post.Subject ?? post.Title;
        if (string.IsNullOrWhiteSpace(subject))
            throw new InvalidOperationException("Set a subject (or title) before pushing.");
        if (subject.Length > 255)
            throw new InvalidOperationException("Subject must be 255 characters or fewer (MailerLite limit).");

        var sections = await db.IssueSections.Where(s => s.PostId == postId)
            .OrderBy(s => s.Position).ToListAsync(ct);
        string html;
        if (sections.Count > 0)
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
            html = SectionHtmlRenderer.Render(sections, tenant, post.Title)
                .Replace(SectionHtmlRenderer.UnsubscribeToken, "{$unsubscribe}"); // MailerLite's variable
        }
        else // legacy free-markdown issue
        {
            var draft = post.DraftId is Guid id ? await db.Drafts.SingleAsync(d => d.Id == id, ct)
                : throw new InvalidOperationException("Compose or write the issue first.");
            html = EmailHtmlRenderer.Render(draft.Body, post.Title);
        }
```

and in the `mailerLite.PushDraftAsync` call, pass `Subject: subject` instead of `Subject: post.Subject ?? post.Title`.

- [ ] **Step 4: Run the tests (new + regression)**

Run: `dotnet test tests/ContentAutomatorX.IntegrationTests --filter "FullyQualifiedName~IssueComposerServiceTests|FullyQualifiedName~PostServiceTests"`
Expected: all pass — 18 composer tests and every pre-existing `PostServiceTests` case (legacy push path unchanged).

- [ ] **Step 5: Commit**

```powershell
git add src/ContentAutomatorX.Application/Services/PostService.cs tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs
git commit -m "feat: sectioned MailerLite push with subject validation and meta save (#composer)"
```

---

### Task 7: Tenant settings — Newsletter section

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Tenants.razor`

**Interfaces:**
- Consumes: `Tenant.{DefaultHeaderMd,DefaultFooterMd,BrandingJson,SenderIdentity}`, `TenantBranding` (Task 1), `EmailFonts.All` (Task 2).
- Produces: UI only — no new APIs.

- [ ] **Step 1: Add the Newsletter fields to the edit form**

In `Tenants.razor`, after the `Output folder` MudTextField (line with `_folder`), insert:

```razor
    <MudDivider Class="my-3" />
    <MudText Typo="Typo.subtitle1">Newsletter</MudText>
    <div class="d-flex flex-wrap" style="gap:16px">
        <MudTextField @bind-Value="_accent" Label="Accent color (#rrggbb — headings, links, buttons)" Style="min-width:220px" />
        <MudTextField @bind-Value="_logoUrl" Label="Logo URL (https)" Style="min-width:280px" Class="flex-grow-1" />
        <MudSelect T="string" @bind-Value="_fontKey" Label="Font" Style="min-width:200px">
            @foreach (var f in EmailFonts.All)
            {
                <MudSelectItem T="string" Value="@f.Key">@f.Value.Label</MudSelectItem>
            }
        </MudSelect>
    </div>
    <MudTextField @bind-Value="_defaultHeader" Label="Default header (markdown — prefills every new issue)" Lines="3" />
    <MudTextField @bind-Value="_defaultFooter" Label="Default footer (markdown — sign-off, socials)" Lines="3" />
    <MudTextField @bind-Value="_senderIdentity" Label="Sender name & address (required by MailerLite / anti-spam law)" />
    <MudText Typo="Typo.caption" Class="mud-text-secondary">
        The unsubscribe link is inserted automatically below the footer — it cannot be forgotten or removed.
    </MudText>
```

Add at the top of the file (below the existing `@inject` lines):

```razor
@using ContentAutomatorX.Application.Newsletter
@using ContentAutomatorX.Domain.Models
```

- [ ] **Step 2: Extend the code block**

In `@code`, extend the field row:

```csharp
    private string _name = "", _slug = "", _voice = "", _folder = "";
    private string _accent = "", _logoUrl = "", _fontKey = EmailFonts.DefaultKey;
    private string _defaultHeader = "", _defaultFooter = "", _senderIdentity = "";
```

Extend `Edit(Tenant t)`:

```csharp
    private void Edit(Tenant t)
    {
        _editing = t;
        (_name, _slug, _voice, _folder, _active) = (t.Name, t.Slug, t.VoiceProfile, t.OutputFolderPath, t.IsActive);
        var branding = TenantBranding.Parse(t.BrandingJson);
        (_accent, _logoUrl, _fontKey) = (branding.AccentColorHex ?? "", branding.LogoUrl ?? "",
            branding.FontKey ?? EmailFonts.DefaultKey);
        (_defaultHeader, _defaultFooter, _senderIdentity) = (t.DefaultHeaderMd, t.DefaultFooterMd, t.SenderIdentity);
    }
```

Extend `Reset()` with:

```csharp
        (_accent, _logoUrl, _fontKey) = ("", "", EmailFonts.DefaultKey);
        (_defaultHeader, _defaultFooter, _senderIdentity) = ("", "", "");
```

In `Save()`, build the branding JSON and set the new fields for both branches. Add this helper to the code block:

```csharp
    private string BrandingJson() => new TenantBranding(
        string.IsNullOrWhiteSpace(_accent) ? null : _accent.Trim(),
        string.IsNullOrWhiteSpace(_logoUrl) ? null : _logoUrl.Trim(),
        _fontKey).ToJson();
```

In the create branch, extend the object initializer:

```csharp
            await TenantSvc.CreateAsync(new Tenant
            {
                Name = _name, Slug = _slug, VoiceProfile = _voice, OutputFolderPath = _folder, IsActive = _active,
                BrandingJson = BrandingJson(), DefaultHeaderMd = _defaultHeader,
                DefaultFooterMd = _defaultFooter, SenderIdentity = _senderIdentity
            });
```

In the edit branch, after the existing tuple assignment add:

```csharp
            _editing.BrandingJson = BrandingJson();
            _editing.DefaultHeaderMd = _defaultHeader;
            _editing.DefaultFooterMd = _defaultFooter;
            _editing.SenderIdentity = _senderIdentity;
```

- [ ] **Step 3: Build and verify manually**

Run: `dotnet build`
Expected: 0 errors.
Manual check (use the project's `verify` skill or run the app): Tenants page → edit a tenant → set accent `#7C3AED`, font Georgia, default header/footer, sender identity → Save → re-open Edit → all values round-trip.

- [ ] **Step 4: Commit**

```powershell
git add src/ContentAutomatorX.Web/Components/Pages/Tenants.razor
git commit -m "feat: tenant newsletter settings — branding, defaults, sender identity (#composer)"
```

### Task 8: Composer UI — SectionCard, page rewrite, export download

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`
- Rewrite: `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`
- Create: `src/ContentAutomatorX.Web/wwwroot/download.js`
- Modify: `src/ContentAutomatorX.Web/Components/App.razor`

**Interfaces:**
- Consumes: every `IssueComposerService` member (Tasks 4–5), `PostService.{GetAsync,SaveIssueMetaAsync,SubjectIdeasAsync,PushAsync}` (Task 6), `SectionTypes`.
- Produces: `SectionCard` component with `SectionCard.SectionEdit(string? Title, string? BodyMd, string? ImageUrl, string? LinkUrl, string? LinkText)` record and parameters `Section`, `TopicNumber`, `CanMoveUp`, `CanMoveDown`, `Busy`, `OnMove (EventCallback<int>)`, `OnDelete`, `OnRegenerate`, `OnApply (EventCallback<SectionCard.SectionEdit>)`; JS global `contentxDownload(filename, text)`; the `/issue/{PostId:guid}` page reads query params `generate=1`, `instructions=<urlencoded>` (and tolerates `gather=1` until Task 9 wires it).

**Concurrency rule for this page (why `ScopeFactory` everywhere):** LLM calls are long-running, and a cross-scope write leaves the circuit's tracked entities stale (see the `GetFreshAsync` comment in `PostService.cs`). The page therefore runs **every** `IssueComposerService` call in a fresh DI scope and treats returned sections as display data — nothing section-shaped is ever tracked by the circuit context. `PostSvc` (circuit-scoped) is used only for post meta, subjects and push.

- [ ] **Step 1: Create the download helper**

Create `src/ContentAutomatorX.Web/wwwroot/download.js`:

```js
window.contentxDownload = (filename, text) => {
    const blob = new Blob([text], { type: "text/markdown;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
};
```

In `src/ContentAutomatorX.Web/Components/App.razor`, after the MudBlazor script line add:

```html
    <script src="@Assets["download.js"]"></script>
```

- [ ] **Step 2: Create SectionCard.razor**

Create `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`:

```razor
@using ContentAutomatorX.Domain
@using ContentAutomatorX.Domain.Entities

<MudPaper Outlined="true" Class="pa-2 mb-2">
    <div class="d-flex align-center" style="gap:4px">
        <MudIcon Icon="@TypeIcon()" Size="Size.Small" Class="mx-1" />
        <MudText Typo="Typo.subtitle2" Class="flex-grow-1"
                 Style="min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">@Label()</MudText>
        @if (Movable())
        {
            <MudIconButton Icon="@Icons.Material.Filled.ArrowUpward" Size="Size.Small"
                           Disabled="@(!CanMoveUp || Busy)" OnClick="@(() => OnMove.InvokeAsync(-1))" />
            <MudIconButton Icon="@Icons.Material.Filled.ArrowDownward" Size="Size.Small"
                           Disabled="@(!CanMoveDown || Busy)" OnClick="@(() => OnMove.InvokeAsync(1))" />
        }
        @if (Section.Type is SectionTypes.Topic or SectionTypes.Header)
        {
            <MudTooltip Text="Rewrite with AI (uses the extra-instructions field)">
                <MudIconButton Icon="@Icons.Material.Filled.AutoAwesome" Size="Size.Small"
                               Disabled="@Busy" OnClick="@(() => OnRegenerate.InvokeAsync())" />
            </MudTooltip>
        }
        @if (Section.Type != SectionTypes.Divider)
        {
            <MudIconButton Icon="@(_expanded ? Icons.Material.Filled.ExpandLess : Icons.Material.Filled.Edit)"
                           Size="Size.Small" OnClick="ToggleExpand" />
        }
        @if (Movable())
        {
            <MudIconButton Icon="@Icons.Material.Filled.Delete" Size="Size.Small" Color="Color.Error"
                           Disabled="@Busy" OnClick="@(() => OnDelete.InvokeAsync())" />
        }
    </div>
    @if (_expanded)
    {
        <div class="mt-2">
            @if (HasTitle())
            {
                <MudTextField @bind-Value="_title" Label="@(Section.Type == SectionTypes.Sponsor ? "Sponsor name" : "Title")" />
            }
            @if (HasBody())
            {
                <MudTextField @bind-Value="_body" Label="Text (markdown)" Lines="6" Style="font-family:monospace" />
            }
            @if (HasImage())
            {
                <MudTextField @bind-Value="_image" Label="@(Section.Type == SectionTypes.Sponsor ? "Logo URL (https)" : "Image URL (https)")" />
            }
            @if (HasLink())
            {
                <MudTextField @bind-Value="_link" Label="Link URL (https)" />
            }
            @if (HasLinkText())
            {
                <MudTextField @bind-Value="_linkText" Label="Button label" />
            }
            <div class="mt-2">
                <MudButton Size="Size.Small" Variant="Variant.Filled" Color="Color.Primary"
                           Disabled="@Busy" OnClick="Apply">Apply</MudButton>
                <MudButton Size="Size.Small" OnClick="@(() => _expanded = false)">Close</MudButton>
            </div>
        </div>
    }
</MudPaper>

@code {
    public record SectionEdit(string? Title, string? BodyMd, string? ImageUrl, string? LinkUrl, string? LinkText);

    [Parameter, EditorRequired] public IssueSection Section { get; set; } = default!;
    [Parameter] public int TopicNumber { get; set; }
    [Parameter] public bool CanMoveUp { get; set; }
    [Parameter] public bool CanMoveDown { get; set; }
    [Parameter] public bool Busy { get; set; }
    [Parameter] public EventCallback<int> OnMove { get; set; }
    [Parameter] public EventCallback OnDelete { get; set; }
    [Parameter] public EventCallback OnRegenerate { get; set; }
    [Parameter] public EventCallback<SectionEdit> OnApply { get; set; }

    private bool _expanded;
    private string _title = "", _body = "", _image = "", _link = "", _linkText = "";

    private void ToggleExpand()
    {
        _expanded = !_expanded;
        if (_expanded)
            (_title, _body, _image, _link, _linkText) = (Section.Title ?? "", Section.BodyMd ?? "",
                Section.ImageUrl ?? "", Section.LinkUrl ?? "", Section.LinkText ?? "");
    }

    private async Task Apply()
    {
        await OnApply.InvokeAsync(new SectionEdit(NullIfEmpty(_title), NullIfEmpty(_body),
            NullIfEmpty(_image), NullIfEmpty(_link), NullIfEmpty(_linkText)));
        _expanded = false;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private bool Movable() => Section.Type is not (SectionTypes.Header or SectionTypes.Footer);
    private bool HasTitle() => Section.Type is SectionTypes.Topic or SectionTypes.Sponsor;
    private bool HasBody() => Section.Type is not (SectionTypes.Button or SectionTypes.Divider);
    private bool HasImage() => Section.Type is SectionTypes.Topic or SectionTypes.Sponsor;
    private bool HasLink() => Section.Type is SectionTypes.Topic or SectionTypes.Sponsor or SectionTypes.Button;
    private bool HasLinkText() => Section.Type is SectionTypes.Sponsor or SectionTypes.Button;

    private string Label() => Section.Type switch
    {
        SectionTypes.Header => "Header",
        SectionTypes.Footer => "Footer",
        SectionTypes.LegacyBody => "Body (from the old editor)",
        SectionTypes.Divider => "— Divider —",
        SectionTypes.Button => $"Button: {Section.LinkText ?? "(set label)"}",
        SectionTypes.Sponsor => $"Sponsor: {Section.Title ?? "(set name)"}",
        _ => $"{TopicNumber}. {Section.Title ?? "(untitled topic)"}"
    };

    private string TypeIcon() => Section.Type switch
    {
        SectionTypes.Header => Icons.Material.Filled.VerticalAlignTop,
        SectionTypes.Footer => Icons.Material.Filled.VerticalAlignBottom,
        SectionTypes.Sponsor => Icons.Material.Filled.Campaign,
        SectionTypes.Button => Icons.Material.Filled.SmartButton,
        SectionTypes.Divider => Icons.Material.Filled.HorizontalRule,
        _ => Icons.Material.Filled.Article
    };
}
```

- [ ] **Step 3: Rewrite IssueEditor.razor**

Replace the entire content of `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor` with:

```razor
@page "/issue/{PostId:guid}"
@implements IDisposable
@inject TenantContext Ctx
@inject PostService PostSvc
@inject IServiceScopeFactory ScopeFactory
@inject ISnackbar Snackbar
@inject NavigationManager Nav
@inject IDialogService DialogService
@inject IJSRuntime JS

<MudText Typo="Typo.h4" Class="mb-4">Issue composer</MudText>

@if (_post is null)
{
    <MudProgressLinear Indeterminate="true" />
}
else
{
    <div class="d-flex align-center flex-wrap mb-2" style="gap:12px">
        <MudTextField @bind-Value="_title" Label="Title" Class="flex-grow-1" Style="min-width:280px" />
        <MudChip T="string" Color="@StatusColor(_post.Status)">@_post.Status</MudChip>
        <MudButton OnClick="SaveAsync" Variant="Variant.Outlined" Disabled="@AnyBusy">Save</MudButton>
        <MudButton OnClick="ExportAsync" Variant="Variant.Outlined" Disabled="@AnyBusy">Export .md</MudButton>
        @if (_post.Status != PostStatus.Published)
        {
            <MudButton OnClick="PushAsync" Variant="Variant.Filled" Color="Color.Primary" Disabled="@_pushing">
                @(_post.Status == PostStatus.Pushed ? "Re-push" : "Push ⚡")
            </MudButton>
        }
        @if (_post.ExternalUrl is not null)
        {
            <MudButton Href="@_post.ExternalUrl" Target="_blank" EndIcon="@Icons.Material.Filled.OpenInNew">
                Open in MailerLite
            </MudButton>
        }
    </div>
    @if (_pushing || _generating)
    {
        <MudProgressLinear Indeterminate="true" Class="mb-2" />
    }

    <div class="d-flex align-center flex-wrap mb-2" style="gap:12px">
        <MudTextField @bind-Value="_subject" Label="Subject" Class="flex-grow-1" Style="min-width:240px" />
        <MudTextField @bind-Value="_preview" Label="Preview text" Class="flex-grow-1" Style="min-width:240px" />
        <MudButton OnClick="SubjectsAsync" Disabled="@_subjectsLoading">✨ Subjects</MudButton>
    </div>
    @if (_subjectIdeas.Count > 0)
    {
        <MudChipSet T="string" Class="mb-2">
            @foreach (var idea in _subjectIdeas)
            {
                <MudChip T="string" OnClick="@(() => _subject = idea)">@idea</MudChip>
            }
        </MudChipSet>
    }

    @if (_generateFailed)
    {
        <MudAlert Severity="Severity.Error" Class="mb-2">
            Topic generation failed — the drafted topics are untouched.
            <MudButton Size="Size.Small" Variant="Variant.Filled" Color="Color.Error"
                       Class="ml-2" OnClick="GenerateAsync">Retry generation</MudButton>
        </MudAlert>
    }
    else if (!_generating && SkeletonCount > 0)
    {
        <MudAlert Severity="Severity.Info" Class="mb-2">
            @SkeletonCount topic(s) have no text yet.
            <MudButton Size="Size.Small" Variant="Variant.Filled" Color="Color.Primary"
                       Class="ml-2" OnClick="GenerateAsync">Generate ✨</MudButton>
        </MudAlert>
    }

    <MudGrid>
        <MudItem xs="12" md="5">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6" Class="mb-2">Structure</MudText>
                @{ var topicNumber = 0; }
                @for (var n = 0; n < _sections.Count; n++)
                {
                    var index = n;
                    var section = _sections[index];
                    if (section.Type == SectionTypes.Topic) { topicNumber++; }
                    <SectionCard Section="@section" TopicNumber="@topicNumber" Busy="@AnyBusy"
                                 CanMoveUp="@(index > 1)" CanMoveDown="@(index < _sections.Count - 2)"
                                 OnMove="@(dir => MoveAsync(section, dir))"
                                 OnDelete="@(() => DeleteAsync(section))"
                                 OnRegenerate="@(() => RegenerateAsync(section))"
                                 OnApply="@(edit => ApplyAsync(section, edit))" />
                }
                <MudMenu Label="Add section" StartIcon="@Icons.Material.Filled.Add"
                         Variant="Variant.Outlined" Class="mt-2" Disabled="@AnyBusy">
                    <MudMenuItem OnClick="@(() => AddSectionAsync(SectionTypes.Topic))">Topic (write manually)</MudMenuItem>
                    <MudMenuItem OnClick="@(() => AddSectionAsync(SectionTypes.Sponsor))">Sponsor block</MudMenuItem>
                    <MudMenuItem OnClick="@(() => AddSectionAsync(SectionTypes.Button))">Button / CTA</MudMenuItem>
                    <MudMenuItem OnClick="@(() => AddSectionAsync(SectionTypes.Divider))">Divider</MudMenuItem>
                </MudMenu>
                <MudTextField @bind-Value="_extraInstructions"
                              Label="Extra instructions (used by Generate and every ✨)" Lines="2" Class="mt-3" />
            </MudPaper>
        </MudItem>

        <MudItem xs="12" md="7">
            <MudPaper Class="pa-4">
                <MudText Typo="Typo.h6" Class="mb-2">Email preview — exactly what MailerLite receives</MudText>
                <div class="pa-3" style="border:1px solid var(--mud-palette-lines-default); border-radius:4px; max-height:800px; overflow-y:auto; background:#f4f4f4;">
                    @((MarkupString)_previewHtml)
                </div>
            </MudPaper>
        </MudItem>
    </MudGrid>
}

@code {
    [Parameter] public Guid PostId { get; set; }
    private Post? _post;
    private List<IssueSection> _sections = [];
    private string _previewHtml = "";
    private string _title = "", _subject = "", _preview = "", _extraInstructions = "";
    private IReadOnlyList<string> _subjectIdeas = [];
    private bool _generating, _generateFailed, _pushing, _subjectsLoading, _mutating;

    private bool AnyBusy => _generating || _pushing || _subjectsLoading || _mutating;
    private int SkeletonCount => _sections.Count(s =>
        s.Type == SectionTypes.Topic && string.IsNullOrWhiteSpace(s.BodyMd) && s.SourceItemId is not null);

    protected override async Task OnInitializedAsync()
    {
        _post = await PostSvc.GetAsync(PostId);
        if (_post is null) { Nav.NavigateTo("/posts"); return; }
        _title = _post.Title;
        _subject = _post.Subject ?? "";
        _preview = _post.PreviewText ?? "";

        Ctx.Changed += OnTenantChanged;
        CheckTenantGuard();

        await WithComposerAsync(c => c.EnsureSectionsAsync(PostId));
        await ReloadSectionsAsync();

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(Nav.Uri).Query);
        _extraInstructions = query["instructions"] ?? "";
        if (query["generate"] == "1" && SkeletonCount > 0) await GenerateAsync();
    }

    // Every composer call runs in a fresh DI scope: LLM calls are long-running, and cross-scope
    // writes would leave circuit-tracked section entities stale (see PostService.GetFreshAsync).
    private async Task<T> WithComposerAsync<T>(Func<IssueComposerService, Task<T>> op)
    {
        using var scope = ScopeFactory.CreateScope();
        return await op(scope.ServiceProvider.GetRequiredService<IssueComposerService>());
    }

    private async Task WithComposerAsync(Func<IssueComposerService, Task> op)
    {
        using var scope = ScopeFactory.CreateScope();
        await op(scope.ServiceProvider.GetRequiredService<IssueComposerService>());
    }

    private async Task ReloadSectionsAsync()
    {
        _sections = await WithComposerAsync(c => c.GetSectionsAsync(PostId));
        _previewHtml = await WithComposerAsync(c => c.RenderPreviewAsync(PostId, _title));
        StateHasChanged();
    }

    private async Task RunMutationAsync(Func<IssueComposerService, Task> op, string errorLabel)
    {
        if (_mutating) return;
        _mutating = true;
        try
        {
            await WithComposerAsync(op);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"{errorLabel} failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _mutating = false;
            await ReloadSectionsAsync();
        }
    }

    private Task MoveAsync(IssueSection s, int direction) =>
        RunMutationAsync(c => c.MoveSectionAsync(s.Id, direction), "Move");

    private Task DeleteAsync(IssueSection s) =>
        RunMutationAsync(c => c.RemoveSectionAsync(s.Id), "Delete");

    private Task AddSectionAsync(string type) =>
        RunMutationAsync(c => c.AddSectionAsync(PostId, type), "Add");

    private Task ApplyAsync(IssueSection s, SectionCard.SectionEdit e) =>
        RunMutationAsync(c => c.UpdateSectionAsync(s.Id, e.Title, e.BodyMd, e.ImageUrl, e.LinkUrl, e.LinkText), "Save");

    private async Task GenerateAsync()
    {
        if (_generating) return;
        _generating = true;
        _generateFailed = false;
        StateHasChanged();
        try
        {
            var filled = await WithComposerAsync(c => c.GenerateTopicsAsync(PostId,
                string.IsNullOrWhiteSpace(_extraInstructions) ? null : _extraInstructions));
            Snackbar.Add($"Filled {filled} topic(s).", Severity.Success);
        }
        catch (Exception ex)
        {
            _generateFailed = true;
            Snackbar.Add($"Generation failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _generating = false;
            await ReloadSectionsAsync();
        }
    }

    private async Task RegenerateAsync(IssueSection s)
    {
        if (_generating) return;
        _generating = true;
        StateHasChanged();
        try
        {
            await WithComposerAsync(c => c.RegenerateSectionAsync(s.Id,
                string.IsNullOrWhiteSpace(_extraInstructions) ? null : _extraInstructions));
            Snackbar.Add("Rewritten.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Rewrite failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _generating = false;
            await ReloadSectionsAsync();
        }
    }

    private Task SaveMetaAsync() => PostSvc.SaveIssueMetaAsync(PostId, _title,
        string.IsNullOrWhiteSpace(_subject) ? null : _subject,
        string.IsNullOrWhiteSpace(_preview) ? null : _preview);

    private async Task SaveAsync()
    {
        try
        {
            await SaveMetaAsync();
            Snackbar.Add("Saved", Severity.Success);
            await ReloadSectionsAsync(); // preview title may have changed
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Save failed: {ex.Message}", Severity.Error);
        }
    }

    private async Task ExportAsync()
    {
        try
        {
            var markdown = await WithComposerAsync(c => c.ExportMarkdownAsync(PostId));
            await JS.InvokeVoidAsync("contentxDownload", $"{FileSlug(_title)}.md", markdown);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Export failed: {ex.Message}", Severity.Error);
        }
    }

    private static string FileSlug(string title)
    {
        var slug = new string([.. title.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')]).Trim('-');
        return slug.Length == 0 ? "issue" : slug;
    }

    private async Task SubjectsAsync()
    {
        if (_subjectsLoading) return;
        _subjectsLoading = true;
        StateHasChanged();
        try
        {
            await SaveMetaAsync();
            _subjectIdeas = await PostSvc.SubjectIdeasAsync(PostId);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Subject ideas failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _subjectsLoading = false;
            StateHasChanged();
        }
    }

    private async Task PushAsync()
    {
        if (_pushing) return;
        _pushing = true;
        StateHasChanged();
        try
        {
            await SaveMetaAsync();
            _post = await PostSvc.PushAsync(PostId);
            var url = _post.ExternalUrl;
            Snackbar.Add("Pushed to MailerLite", Severity.Success, config =>
            {
                if (url is null) return;
                config.Action = "Open in MailerLite";
                config.OnClick = _ =>
                {
                    Nav.NavigateTo(url, forceLoad: true);
                    return Task.CompletedTask;
                };
            });
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Push failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _pushing = false;
            StateHasChanged();
        }
    }

    private void OnTenantChanged() => _ = InvokeAsync(() =>
    {
        CheckTenantGuard();
        StateHasChanged();
    });

    // Post-scoped page: data comes from the post's own TenantId, not Ctx.Active, so it keeps
    // working while Ctx initializes. Once Ctx is ready, a mismatch means the user switched away.
    private void CheckTenantGuard()
    {
        if (_post is not null && Ctx.Initialized && (Ctx.Active is null || Ctx.Active.Id != _post.TenantId))
            Nav.NavigateTo("/posts");
    }

    private static Color StatusColor(PostStatus status) => status switch
    {
        PostStatus.Pushed => Color.Info,
        PostStatus.Published => Color.Success,
        PostStatus.Failed => Color.Error,
        _ => Color.Default
    };

    public void Dispose() => Ctx.Changed -= OnTenantChanged;
}
```

Note: the old page's Material/Gather/candidates panel and `ComposeAsync` UI are intentionally gone — replaced by sections; item intake returns as the picker in Task 9. `PostService.ComposeAsync`, `GetCandidatesAsync`, `SetIssueSourcesAsync` stay in code (legacy/scheduled path + existing tests).

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: 0 errors (warnings about the now-unused `EmailHtmlRenderer` import in the page are fine to fix by dropping the import).

- [ ] **Step 5: Verify manually**

Run the app (use the project's `verify` skill). Checklist:
1. Tenants → ensure the active tenant has default header/footer + sender identity.
2. `+ New → Newsletter issue…` → create → composer shows Header + Footer cards, preview shows both plus the compliance footer with a dead `Unsubscribe` link.
3. Add section → Sponsor → edit → name/text/link/logo → Apply → preview shows the SPONSORED box.
4. Add a manual Topic, a Button, a Divider; reorder with ↑/↓ — header/footer never move; delete works.
5. Edit the Header text → Apply → preview updates.
6. Export .md downloads a file with the section markdown.
7. Open an issue created before this feature (if any exists) → its body appears as a "Body (from the old editor)" card between Header and Footer.

- [ ] **Step 6: Commit**

```powershell
git add src/ContentAutomatorX.Web
git commit -m "feat: structured issue composer UI with live email preview and md export (#composer)"
```

### Task 9: Inbox item picker + gather-on-open

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Shared/InboxItemPickerDialog.razor`
- Modify: `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`

**Interfaces:**
- Consumes: `ContentService.ListAsync(Guid tenantId, ContentItemStatus? status = null, DateTimeOffset? since = null)`, `IssueComposerService.AddTopicsFromItemsAsync`, `PostService.GetIssueSourceIdsAsync(Post post, CancellationToken ct)`, `IngestionPipeline.RunAsync(Guid tenantId, Guid sourceId)`.
- Produces: `InboxItemPickerDialog` with `[Parameter] Guid TenantId`; closes with `DialogResult.Ok(List<Guid>)` of picked item ids. Composer menu gains "Topic from inbox…"; `?gather=1` runs ingestion for the issue's sources, then opens the picker.

- [ ] **Step 1: Create the picker dialog**

Create `src/ContentAutomatorX.Web/Components/Shared/InboxItemPickerDialog.razor`:

```razor
@using ContentAutomatorX.Domain.Entities
@inject ContentService ContentSvc
@inject ISnackbar Snackbar

<MudDialog>
    <DialogContent>
        @if (_loading)
        {
            <MudProgressLinear Indeterminate="true" />
        }
        else
        {
            <MudTextField T="string" Value="_search" ValueChanged="@((string v) => { _search = v; ApplyFilter(); })"
                          Immediate="true" DebounceInterval="300" Label="Search title & text" Clearable="true"
                          Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search" />
            <MudTable T="ContentItem" Items="_filtered" Hover="true" Dense="true" MultiSelection="true"
                      @bind-SelectedItems="_selected" Style="max-height:420px;overflow-y:auto;">
                <HeaderContent>
                    <MudTh>Title</MudTh><MudTh>Published</MudTh><MudTh>Status</MudTh>
                </HeaderContent>
                <RowTemplate>
                    <MudTd>@context.Title</MudTd>
                    <MudTd>@((context.PublishedAt ?? context.FetchedAt).ToLocalTime().ToString("g"))</MudTd>
                    <MudTd>@context.Status</MudTd>
                </RowTemplate>
                <NoRecordsContent>
                    <MudText>No unused inbox items — gather from your sources first.</MudText>
                </NoRecordsContent>
            </MudTable>
        }
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => MudDialog.Cancel())">Cancel</MudButton>
        <MudButton Color="Color.Primary" Variant="Variant.Filled" Disabled="@(_selected.Count == 0)"
                   OnClick="@(() => MudDialog.Close(DialogResult.Ok(_selected.Select(i => i.Id).ToList())))">
            Add @_selected.Count topic(s)
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid TenantId { get; set; }

    private List<ContentItem> _all = [];
    private List<ContentItem> _filtered = [];
    private HashSet<ContentItem> _selected = [];
    private string _search = "";
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _all = (await ContentSvc.ListAsync(TenantId))
                .Where(i => i.Status is not (ContentItemStatus.Used or ContentItemStatus.Ignored))
                .ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to load inbox items: {ex.Message}", Severity.Error);
        }
        finally { _loading = false; }
    }

    private void ApplyFilter() => _filtered = string.IsNullOrWhiteSpace(_search)
        ? _all
        : _all.Where(i => i.Title.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                          i.Body.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
}
```

- [ ] **Step 2: Wire the picker and gather into the composer**

In `IssueEditor.razor`:

Add the menu item after "Topic (write manually)":

```razor
                    <MudMenuItem OnClick="OpenItemPickerAsync">Topic from inbox…</MudMenuItem>
```

Add to the `@code` block:

```csharp
    private async Task OpenItemPickerAsync()
    {
        if (_post is null) return;
        var parameters = new DialogParameters<InboxItemPickerDialog> { { d => d.TenantId, _post.TenantId } };
        var dialog = await DialogService.ShowAsync<InboxItemPickerDialog>("Add topics from inbox", parameters);
        var result = await dialog.Result;
        if (result is null || result.Canceled || result.Data is not List<Guid> itemIds || itemIds.Count == 0) return;
        await RunMutationAsync(c => c.AddTopicsFromItemsAsync(PostId, itemIds), "Add topics");
        if (SkeletonCount > 0) await GenerateAsync(); // picked topics get their blurbs immediately
    }

    private async Task GatherAsync()
    {
        if (_post is null || _gathering) return;
        _gathering = true;
        StateHasChanged();
        try
        {
            var sourceIds = await PostSvc.GetIssueSourceIdsAsync(_post);
            using var scope = ScopeFactory.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<IngestionPipeline>();
            var failed = 0;
            foreach (var id in sourceIds)
            {
                var run = await pipeline.RunAsync(_post.TenantId, id);
                if (run.Status == RunStatus.Failed) failed++;
            }
            Snackbar.Add(failed > 0 ? $"Gathered — {failed} source(s) failed." : "Gathered.",
                failed > 0 ? Severity.Warning : Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Gather failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _gathering = false;
            StateHasChanged();
        }
    }
```

Add `_gathering` to the flag declarations and to `AnyBusy`:

```csharp
    private bool _generating, _generateFailed, _pushing, _subjectsLoading, _mutating, _gathering;

    private bool AnyBusy => _generating || _pushing || _subjectsLoading || _mutating || _gathering;
```

At the end of `OnInitializedAsync`, after the `generate` handling, add:

```csharp
        if (query["gather"] == "1") // "+ New → Create & gather": fetch fresh material, then pick
        {
            await GatherAsync();
            await OpenItemPickerAsync();
        }
```

- [ ] **Step 3: Build and verify manually**

Run: `dotnet build` — expected: 0 errors.
Manual: composer → Add section → "Topic from inbox…" → pick 2 items → they appear as topics above the footer and blurbs generate; `+ New → Create & gather` runs ingestion, then opens the picker.

- [ ] **Step 4: Commit**

```powershell
git add src/ContentAutomatorX.Web
git commit -m "feat: inbox item picker and gather-on-open for the composer (#composer)"
```

---

### Task 10: Inbox entry point — Create newsletter

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Content.razor`

**Interfaces:**
- Consumes: `IssueComposerService.CreateFromItemsAsync(Guid tenantId, Guid recipeId, IReadOnlyList<Guid> itemIds, string title, CancellationToken ct)`, `PostService.SuggestTitleAsync(Guid recipeId, CancellationToken ct)`.
- Produces: UI only. Navigates to `/issue/{id}?generate=1[&instructions=<urlencoded>]`. The old `RunWithSelection` (direct `GenerationPipeline` call + file-delivery error snackbar) is deleted.

- [ ] **Step 1: Rewire the page**

In `src/ContentAutomatorX.Web/Components/Pages/Content.razor`:

Add `@inject NavigationManager Nav` below the existing `@inject` lines.

In `ReloadAllAsync`, filter the recipe list to newsletter recipes (the composer flow is newsletter-only):

```csharp
        _recipes = Ctx.Initialized && Ctx.Active is not null
            ? (await RecipeSvc.ListAsync(Ctx.Active.Id)).Where(r => r.Kind == DraftKinds.Newsletter).ToList()
            : [];
```

Replace the action paper's title and button markup:

```razor
    <MudPaper Class="pa-4 mb-4">
        <MudText Typo="Typo.subtitle1">Create a newsletter from hand-picked items (@_selectedItems.Count selected)</MudText>
        <MudSelect T="Guid?" @bind-Value="_recipeId" Label="Newsletter automation">
            @foreach (var r in _recipes)
            {
                <MudSelectItem T="Guid?" Value="@((Guid?)r.Id)">@r.Name</MudSelectItem>
            }
        </MudSelect>
        <MudTextField @bind-Value="_extraInstructions" Label="Extra instructions for this issue (optional)" />
        <MudButton OnClick="CreateNewsletterAsync" Variant="Variant.Filled" Color="Color.Primary" Class="mt-2"
                   Disabled="@(_recipeId is null || _selectedItems.Count == 0 || _running)">Create newsletter</MudButton>
        <MudButton OnClick="DeleteSelected" Variant="Variant.Outlined" Color="Color.Error" Class="mt-2 ml-2"
                   Disabled="@(_selectedItems.Count == 0 || _running)">Delete selected</MudButton>
    </MudPaper>
```

Replace the entire `RunWithSelection` method with:

```csharp
    private async Task CreateNewsletterAsync()
    {
        _running = true;
        try
        {
            var itemIds = _selectedItems.Select(i => i.Id).ToList();
            using var scope = ScopeFactory.CreateScope();
            var composer = scope.ServiceProvider.GetRequiredService<IssueComposerService>();
            var postSvc = scope.ServiceProvider.GetRequiredService<PostService>();
            var title = await postSvc.SuggestTitleAsync(_recipeId!.Value);
            var post = await composer.CreateFromItemsAsync(Ctx.Active!.Id, _recipeId!.Value, itemIds, title);
            var instructions = string.IsNullOrWhiteSpace(_extraInstructions) ? ""
                : $"&instructions={Uri.EscapeDataString(_extraInstructions)}";
            Nav.NavigateTo($"/issue/{post.Id}?generate=1{instructions}");
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to create the newsletter: {ex.Message}", Severity.Error);
            _running = false; // only reset on failure — success navigates away
        }
    }
```

- [ ] **Step 2: Build and verify manually**

Run: `dotnet build` — expected: 0 errors.
Manual: Inbox → select 3 items → pick automation → type an instruction → **Create newsletter** → lands in the composer, three skeleton topics appear, generation runs with the instruction, blurbs fill in. No error snackbar, no file write, regardless of `OutputFolderPath`.

- [ ] **Step 3: Commit**

```powershell
git add src/ContentAutomatorX.Web/Components/Pages/Content.razor
git commit -m "feat: inbox creates structured newsletter issues — file-delivery flow removed (#composer)"
```

---

### Task 11: Full verification sweep

**Files:** none new — verification and fixes only.

- [ ] **Step 1: Run the complete test suite**

Run: `dotnet test`
Expected: all unit + integration tests pass. Fix any failure before proceeding (report honestly if something can't be fixed).

- [ ] **Step 2: Manual E2E checklist**

Run the app (project `verify` skill):
1. Tenant settings: branding + defaults + sender identity saved.
2. Inbox → select → Create newsletter → composer → reorder/edit/sponsor/✨ → preview matches every change.
3. Push ⚡ with MailerLite configured (test group!): campaign appears as draft; HTML contains branding, unsubscribe link, sender identity; MailerLite does **not** append its own footer.
4. Verify in a real inbox (Gmail + Outlook) after sending to the test group.
5. Spec §9.1: confirm the account plan accepts custom HTML `content` via API — if the push returns a plan error, that's an account upgrade, not a code change.
6. Legacy check: a pre-existing issue opens with its body as a legacy card and can still push.

- [ ] **Step 3: Update the spec status line**

In `docs/superpowers/specs/2026-07-19-newsletter-composer-design.md`, change the `**Status:**` line to `Implemented <date> (plan: docs/superpowers/plans/2026-07-19-issue-composer.md)`.

- [ ] **Step 4: Final commit**

```powershell
git add -A
git commit -m "docs: mark issue-composer spec implemented (#composer)"
```

---

## Self-Review Notes (kept for the executor)

- Spec §4.6 error table is covered by: generation banner (Task 8), per-✨ snackbar (Task 8), push failure = unchanged `Failed` path (existing tests), subject validation (Task 6), dead image URL = browser broken-image in preview (no code needed), tenant-switch guard (Task 8).
- Spec "Out" items deliberately absent: sponsor library, image upload, drag-and-drop (↑/↓ only), second ESP, auto-send.
- `PostService.ComposeAsync` / `GetCandidatesAsync` / `SetIssueSourcesAsync` / `SaveIssueAsync` are intentionally retained for the scheduled-automation path (`GenerationPipeline` review posts) and existing tests, even though the new UI no longer calls most of them.






