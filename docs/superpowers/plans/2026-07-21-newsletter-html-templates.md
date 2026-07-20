# Newsletter HTML Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a tenant store their own email HTML design as a block library, and render generated issues into it instead of the single hardcoded layout in `SectionHtmlRenderer`.

**Architecture:** A template is one HTML document carved into named blocks by `<!-- BLOCK: name -->` comments — one block per section type, plus a `shell` block for the document itself. Three pure collaborators (`TemplateParser`, `TemplateValidator`, `TemplateHtmlRenderer`) sit in `Application/Newsletter` beside the existing renderer, which stays as the fallback for every tenant that has no template. Validation runs on save; rendering never throws.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor 9.7.0, EF Core 10 + SQLite, Markdig, xUnit, Serilog.

**Spec:** `docs/superpowers/specs/2026-07-21-newsletter-html-templates-design.md`. Read the spec section named in each task before implementing it.

## Global Constraints

- **Layering.** Application → Domain only. Infrastructure → Domain + Application. Web → Application + Infrastructure. **Application must never reference an Infrastructure type.**
- **Tenancy.** Plain `TenantId` column, no FK to `Tenant`, no global query filter. Scope with an explicit `.Where(x => x.TenantId == tenantId)` in the service, matching `Platform`, `Recipe` and `Source`.
- **The composer's core rule is unchanged:** every write to a section's text is explicitly chosen, one section at a time. Nothing in this feature writes to an `IssueSection` except the existing Apply and Accept paths.
- **The structural lock is unchanged and extended:** chat cannot add, remove or reorder sections, enforced by the section-id whitelist in `IssueChatService.StoreProposalsAsync` — never by prompt wording. It additionally cannot change a section's `Type`, because `Type` is not in the edit contract.
- **Template HTML is trusted and emitted verbatim. Section content is never trusted** and is always escaped — see spec §5.4. A test that proves `<script>` in a section title is escaped is mandatory, not optional.
- **`{{unsubscribe_url}}` must appear somewhere in every saved template** (error E5). The unsubscribe link is a legal requirement.
- **No `ErrorBoundary` exists in this app.** An exception escaping a Blazor event handler tears down the circuit. Every event handler added by this plan is wrapped in try/catch.
- **Blazor Server scoped `DbContext` lives for the whole circuit.** Any long-running call from a page must run in a fresh DI scope, following the existing `WithChatAsync` / `WithHistoryAsync` helpers in `IssueEditor.razor`.
- **SQLite cannot translate enum comparisons with date arithmetic**, and its `DateTimeOffset` ORDER BY is unreliable. Filter status server-side; do date maths and `Max` client-side.
- **xUnit counts each `[InlineData]` of a `[Theory]` as a separate test case.** Test-count claims must reflect cases, not methods.
- **MudBlazor splats unmatched parameters to HTML.** An undeclared parameter compiles cleanly and silently does nothing — verify a parameter is declared on the component you are passing it to.

## File Structure

### Created

| File | Responsibility |
|---|---|
| `src/ContentAutomatorX.Domain/Entities/NewsletterTemplate.cs` | The entity |
| `src/ContentAutomatorX.Domain/Abstractions/IYouTubeThumbnailResolver.cs` | Seam for the `HEAD` probe, so Application stays free of HTTP |
| `src/ContentAutomatorX.Infrastructure/Newsletter/YouTubeThumbnailResolver.cs` | `HttpClient` implementation of that seam |
| `src/ContentAutomatorX.Application/Newsletter/TemplateModel.cs` | `TemplateBlock`, `ParsedTemplate`, `TemplateIssue`, `TemplateIssueLevel`, `TemplateBlocks`, `TemplatePlaceholders` |
| `src/ContentAutomatorX.Application/Newsletter/TemplateParser.cs` | HTML text → blocks + structural issues. Pure. |
| `src/ContentAutomatorX.Application/Newsletter/TemplateValidator.cs` | Blocks → the full error and warning list. Pure. |
| `src/ContentAutomatorX.Application/Newsletter/TemplateHtmlRenderer.cs` | Blocks + sections → email HTML. Pure. |
| `src/ContentAutomatorX.Application/Newsletter/ReadingTime.cs` | Word count → "9 min read". Pure. |
| `src/ContentAutomatorX.Application/Newsletter/YouTubeUrl.cs` | URL → video id, and id → thumbnail URLs. Pure. |
| `src/ContentAutomatorX.Application/Newsletter/SampleIssue.cs` | The fixed preview issue |
| `src/ContentAutomatorX.Application/Services/NewsletterTemplateService.cs` | CRUD, `IsDefault` exclusivity, template resolution |
| `src/ContentAutomatorX.Web/Components/Shared/TemplateEditorDialog.razor` | The full-screen editor |
| `tests/ContentAutomatorX.UnitTests/TemplateParserTests.cs` | |
| `tests/ContentAutomatorX.UnitTests/TemplateValidatorTests.cs` | |
| `tests/ContentAutomatorX.UnitTests/TemplateHtmlRendererTests.cs` | |
| `tests/ContentAutomatorX.UnitTests/ReadingTimeTests.cs` | |
| `tests/ContentAutomatorX.UnitTests/YouTubeUrlTests.cs` | |
| `tests/ContentAutomatorX.IntegrationTests/NewsletterTemplateServiceTests.cs` | |

### Modified

| File | Change |
|---|---|
| `src/ContentAutomatorX.Domain/Constants.cs` | `SectionTypes.Video` |
| `src/ContentAutomatorX.Domain/Entities/Recipe.cs` | `NewsletterTemplateId` |
| `src/ContentAutomatorX.Domain/Entities/IssueSection.cs` | `Category` |
| `src/ContentAutomatorX.Domain/Entities/IssueSectionProposal.cs` | `ProposedCategory`, `BaselineCategory` |
| `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs` | `NewsletterTemplates` |
| `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs` | DbSet + index |
| `src/ContentAutomatorX.Infrastructure/Migrations/` | One migration |
| `src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs` | Expose `RenderSection`; handle `Video` |
| `src/ContentAutomatorX.Application/Services/IssueComposerService.cs` | `Category` in `UpdateSectionAsync`; template-aware preview; category in prompts |
| `src/ContentAutomatorX.Application/Services/IssueHistoryService.cs` | `Category` in snapshot/capture/restore |
| `src/ContentAutomatorX.Application/Services/IssueChatService.cs` | `Category` in proposals, staleness, merge, accept, prompt |
| `src/ContentAutomatorX.Application/Services/ChatReplyParser.cs` | `Category` in `ChatEdit` and `RawEdit` |
| `src/ContentAutomatorX.Application/Services/PostService.cs` | Template-aware push |
| `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor` | Category field, Video card |
| `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor` | Video in add-section menu; pass category through |
| `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor` | Template row in Output |
| `src/ContentAutomatorX.Web/Program.cs` | DI registrations |

---

## Task 1: Data model and migration

**Spec:** §3.

**Files:**
- Create: `src/ContentAutomatorX.Domain/Entities/NewsletterTemplate.cs`
- Modify: `src/ContentAutomatorX.Domain/Constants.cs`, `src/ContentAutomatorX.Domain/Entities/Recipe.cs`, `src/ContentAutomatorX.Domain/Entities/IssueSection.cs`, `src/ContentAutomatorX.Domain/Entities/IssueSectionProposal.cs`, `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`, `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/NewsletterTemplateServiceTests.cs`

**Interfaces:**
- Produces: `NewsletterTemplate` entity; `SectionTypes.Video`; `Recipe.NewsletterTemplateId`; `IssueSection.Category`; `IssueSectionProposal.ProposedCategory` / `.BaselineCategory`; `IAppDbContext.NewsletterTemplates`.

**Note for the reviewer:** this task adds `ProposedCategory` and `BaselineCategory` to `IssueSectionProposal` even though nothing reads them until Task 8. That is deliberate — it keeps the feature to a single migration rather than two, and two migrations touching the same tables in one branch is a needless merge hazard. Do not flag the unused columns as dead code.

- [ ] **Step 1: Write the failing test**

Create `tests/ContentAutomatorX.IntegrationTests/NewsletterTemplateServiceTests.cs`:

```csharp
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class NewsletterTemplateServiceTests
{
    [Fact]
    public async Task Migration_creates_the_table_and_columns()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();

        t.Db.NewsletterTemplates.Add(new NewsletterTemplate
        {
            TenantId = tenantId, Name = "Into the Latent", Html = "<!-- BLOCK: shell -->{{sections}}<!-- /BLOCK -->",
            IsDefault = true
        });
        await t.Db.SaveChangesAsync();

        var stored = await t.Db.NewsletterTemplates.SingleAsync(x => x.TenantId == tenantId);
        Assert.Equal("Into the Latent", stored.Name);
        Assert.True(stored.IsDefault);
        Assert.NotEqual(default, stored.UpdatedAt);
    }

    [Fact]
    public async Task New_columns_round_trip_on_existing_entities()
    {
        using var t = TestDb.Create();
        var templateId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        t.Db.Recipes.Add(new Recipe
        {
            TenantId = Guid.NewGuid(), Name = "Monthly", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = templateId
        });
        t.Db.IssueSections.Add(new IssueSection
        {
            PostId = postId, Position = 0, Type = "Topic", Category = "Tutorial"
        });
        t.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = postId, SectionId = Guid.NewGuid(), BaselineBodyMd = "",
            ProposedCategory = "News", BaselineCategory = "Tutorial"
        });
        await t.Db.SaveChangesAsync();

        Assert.Equal(templateId, (await t.Db.Recipes.SingleAsync()).NewsletterTemplateId);
        Assert.Equal("Tutorial", (await t.Db.IssueSections.SingleAsync()).Category);
        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal("News", proposal.ProposedCategory);
        Assert.Equal("Tutorial", proposal.BaselineCategory);
    }
}
```

`IssueSection` has an FK to `Post` with cascade delete, so the second test's sections would normally need a real post row. SQLite in EF Core does not enforce FKs unless `PRAGMA foreign_keys=ON` is set, and `TestDb` does not set it — the existing `PersistenceTests` rely on the same latitude. If this test fails on a constraint, add a `Post` row rather than changing the pragma.

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter NewsletterTemplateServiceTests
```

Expected: FAIL to compile — `NewsletterTemplate` does not exist.

- [ ] **Step 3: Add the entity**

Create `src/ContentAutomatorX.Domain/Entities/NewsletterTemplate.cs`:

```csharp
namespace ContentAutomatorX.Domain.Entities;

/// <summary>A tenant's own email design, stored as one HTML document carved into named blocks.
/// Text outside any block is ignored, so the file's explanatory header comment survives editing.</summary>
public class NewsletterTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Html { get; set; }
    /// <summary>At most one per tenant. Enforced by NewsletterTemplateService, not by the database:
    /// the EF SQLite provider has no filtered unique index, and the service is the only writer.</summary>
    public bool IsDefault { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Add the columns and the section type**

In `src/ContentAutomatorX.Domain/Constants.cs`, inside `SectionTypes`, after the `Divider` line:

```csharp
    public const string Video = "Video";
```

In `src/ContentAutomatorX.Domain/Entities/Recipe.cs`, after `TargetPlatformId`:

```csharp
    public Guid? NewsletterTemplateId { get; set; }  // null = built-in design
```

In `src/ContentAutomatorX.Domain/Entities/IssueSection.cs`, after `LinkText`:

```csharp
    public string? Category { get; set; }      // topic label — "Tutorial", "News"
```

In `src/ContentAutomatorX.Domain/Entities/IssueSectionProposal.cs`, after `BaselineTitle`:

```csharp
    public string? ProposedCategory { get; set; }
    public string? BaselineCategory { get; set; }
```

- [ ] **Step 5: Register the DbSet**

In `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`, after `IssueRevisions`:

```csharp
    DbSet<NewsletterTemplate> NewsletterTemplates { get; }
```

In `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`, after the `IssueRevisions` property:

```csharp
    public DbSet<NewsletterTemplate> NewsletterTemplates => Set<NewsletterTemplate>();
```

and at the end of `OnModelCreating`:

```csharp
        // Not unique: a tenant may hold several templates. No FK to Tenant, matching every other
        // tenant-owned entity here.
        b.Entity<NewsletterTemplate>().HasIndex(t => t.TenantId);
```

- [ ] **Step 6: Create the migration**

```
dotnet ef migrations add NewsletterTemplates --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web
```

Expected: three files under `src/ContentAutomatorX.Infrastructure/Migrations/` — `{timestamp}_NewsletterTemplates.cs`, its `.Designer.cs`, and an updated `AppDbContextModelSnapshot.cs`.

Open the generated `Up()` and confirm it contains `CreateTable("NewsletterTemplates", ...)` and four `AddColumn` calls (`Recipes.NewsletterTemplateId`, `IssueSections.Category`, `IssueSectionProposals.ProposedCategory`, `IssueSectionProposals.BaselineCategory`), all nullable. If any column is generated as non-nullable, the entity property is missing its `?`.

- [ ] **Step 7: Run the tests to verify they pass**

```
dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter NewsletterTemplateServiceTests
```

Expected: PASS, 2 tests.

- [ ] **Step 8: Run the full suite**

```
dotnet test
```

Expected: everything green. This confirms the migration applies cleanly to the databases the existing integration tests build.

- [ ] **Step 9: Commit**

```bash
git add src tests
git commit -m "feat(templates): NewsletterTemplate entity, category and template columns, migration"
```

---

## Task 2: Template parser

**Spec:** §4.1, §4.3.

**Files:**
- Create: `src/ContentAutomatorX.Application/Newsletter/TemplateModel.cs`, `src/ContentAutomatorX.Application/Newsletter/TemplateParser.cs`
- Test: `tests/ContentAutomatorX.UnitTests/TemplateParserTests.cs`

**Interfaces:**
- Produces: `TemplateBlocks.Shell/Header/Topic/Video/Sponsor/Button/Divider/Footer` and `TemplateBlocks.All`; `TemplateBlocks.ForSectionType(string) => string?`; `TemplatePlaceholders.For(string blockName) => IReadOnlySet<string>`; `TemplatePlaceholders.Conditions(string blockName) => IReadOnlySet<string>`; `record TemplateBlock(string Name, string Content, int Line)`; `record ParsedTemplate(IReadOnlyDictionary<string, TemplateBlock> Blocks, IReadOnlyList<TemplateIssue> Issues)`; `record TemplateIssue(TemplateIssueLevel Level, int Line, string Message)`; `enum TemplateIssueLevel { Error, Warning }`; `TemplateParser.Parse(string html) => ParsedTemplate`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ContentAutomatorX.UnitTests/TemplateParserTests.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class TemplateParserTests
{
    [Fact]
    public void Extracts_named_blocks_and_ignores_text_between_them()
    {
        var parsed = TemplateParser.Parse("""
            <!-- a long explanatory header comment that is not a block -->
            <!-- BLOCK: shell -->SHELL {{sections}}<!-- /BLOCK -->
            loose text nobody asked about
            <!-- BLOCK: topic -->TOPIC<!-- /BLOCK -->
            """);

        Assert.Empty(parsed.Issues);
        Assert.Equal(2, parsed.Blocks.Count);
        Assert.Equal("SHELL {{sections}}", parsed.Blocks["shell"].Content);
        Assert.Equal("TOPIC", parsed.Blocks["topic"].Content);
    }

    [Fact]
    public void Reports_the_line_a_block_starts_on()
    {
        var parsed = TemplateParser.Parse("one\ntwo\n<!-- BLOCK: topic -->x<!-- /BLOCK -->");
        Assert.Equal(3, parsed.Blocks["topic"].Line);
    }

    [Fact]
    public void Tolerates_whitespace_variations_in_the_markers()
    {
        var parsed = TemplateParser.Parse("<!--BLOCK:topic-->x<!--/BLOCK-->");
        Assert.Empty(parsed.Issues);
        Assert.True(parsed.Blocks.ContainsKey("topic"));
    }

    [Fact]
    public void Unknown_block_name_is_an_error_and_the_block_is_dropped()
    {
        var parsed = TemplateParser.Parse("<!-- BLOCK: banana -->x<!-- /BLOCK -->");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("banana"));
        Assert.Empty(parsed.Blocks);
    }

    [Fact]
    public void Duplicate_block_name_is_an_error_and_the_first_wins()
    {
        var parsed = TemplateParser.Parse(
            "<!-- BLOCK: topic -->first<!-- /BLOCK --><!-- BLOCK: topic -->second<!-- /BLOCK -->");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("more than once"));
        Assert.Equal("first", parsed.Blocks["topic"].Content);
    }

    [Fact]
    public void Unclosed_block_is_an_error()
    {
        var parsed = TemplateParser.Parse("<!-- BLOCK: topic -->x");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("never closed"));
        Assert.Empty(parsed.Blocks);
    }

    [Fact]
    public void Closing_marker_with_no_open_block_is_an_error()
    {
        var parsed = TemplateParser.Parse("x<!-- /BLOCK -->");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("no block is open"));
    }

    [Fact]
    public void Nested_block_open_is_an_error()
    {
        var parsed = TemplateParser.Parse("<!-- BLOCK: topic --><!-- BLOCK: video -->x<!-- /BLOCK --><!-- /BLOCK -->");
        Assert.Contains(parsed.Issues, i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("already open"));
    }

    [Theory]
    [InlineData("Header", "header")]
    [InlineData("Topic", "topic")]
    [InlineData("Video", "video")]
    [InlineData("Sponsor", "sponsor")]
    [InlineData("Button", "button")]
    [InlineData("Divider", "divider")]
    [InlineData("Footer", "footer")]
    [InlineData("LegacyBody", null)]
    public void Maps_section_types_to_block_names(string sectionType, string? expected) =>
        Assert.Equal(expected, TemplateBlocks.ForSectionType(sectionType));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter TemplateParserTests
```

Expected: FAIL to compile — `TemplateParser` does not exist.

- [ ] **Step 3: Write the model**

Create `src/ContentAutomatorX.Application/Newsletter/TemplateModel.cs`:

```csharp
using ContentAutomatorX.Domain;

namespace ContentAutomatorX.Application.Newsletter;

public enum TemplateIssueLevel { Error, Warning }

/// <summary>One validation finding. Line is 1-based and points at the construct that caused it,
/// so the editor can tell the user where to look.</summary>
public record TemplateIssue(TemplateIssueLevel Level, int Line, string Message);

public record TemplateBlock(string Name, string Content, int Line);

public record ParsedTemplate(IReadOnlyDictionary<string, TemplateBlock> Blocks,
    IReadOnlyList<TemplateIssue> Issues);

public static class TemplateBlocks
{
    public const string Shell = "shell";
    public const string Header = "header";
    public const string Topic = "topic";
    public const string Video = "video";
    public const string Sponsor = "sponsor";
    public const string Button = "button";
    public const string Divider = "divider";
    public const string Footer = "footer";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Shell, Header, Topic, Video, Sponsor, Button, Divider, Footer };

    /// <summary>The optional blocks — every block except the shell. A missing one is a warning and
    /// falls back to the built-in renderer for that section type.</summary>
    public static readonly IReadOnlyList<string> Optional =
        [Header, Topic, Video, Sponsor, Button, Divider, Footer];

    /// <summary>LegacyBody deliberately maps to null: legacy free-markdown issues have no sections
    /// at all and never reach the template renderer.</summary>
    public static string? ForSectionType(string sectionType) => sectionType switch
    {
        SectionTypes.Header => Header,
        SectionTypes.Topic => Topic,
        SectionTypes.Video => Video,
        SectionTypes.Sponsor => Sponsor,
        SectionTypes.Button => Button,
        SectionTypes.Divider => Divider,
        SectionTypes.Footer => Footer,
        _ => null
    };
}

public static class TemplatePlaceholders
{
    /// <summary>Available in every block.</summary>
    public static readonly IReadOnlySet<string> Global = new HashSet<string>
        { "tenant_name", "accent", "issue_title", "issue_date", "unsubscribe_url" };

    private static readonly Dictionary<string, HashSet<string>> BlockSpecific = new()
    {
        [TemplateBlocks.Shell]   = ["preheader", "sections"],
        [TemplateBlocks.Header]  = ["title", "body_html"],
        [TemplateBlocks.Topic]   = ["title", "body_html", "image_url", "link_url", "link_text",
                                    "category", "reading_time"],
        [TemplateBlocks.Video]   = ["title", "body_html", "thumbnail_url", "video_url", "link_text"],
        [TemplateBlocks.Sponsor] = ["title", "body_html", "image_url", "link_url", "link_text"],
        [TemplateBlocks.Button]  = ["link_url", "link_text"],
        [TemplateBlocks.Divider] = [],
        [TemplateBlocks.Footer]  = ["body_html", "sender_identity"]
    };

    /// <summary>Condition name → the placeholder it tests. A condition is true when that
    /// placeholder resolves to a non-empty string.</summary>
    private static readonly Dictionary<string, string> ConditionTargets = new()
    {
        ["title"] = "title", ["body"] = "body_html", ["image"] = "image_url",
        ["link"] = "link_url", ["category"] = "category",
        ["thumbnail"] = "thumbnail_url", ["video"] = "video_url"
    };

    public static IReadOnlySet<string> For(string blockName) =>
        BlockSpecific.TryGetValue(blockName, out var own)
            ? new HashSet<string>(own.Concat(Global))
            : Global;

    /// <summary>Conditions valid in this block: those whose target placeholder exists here.</summary>
    public static IReadOnlySet<string> Conditions(string blockName)
    {
        var available = For(blockName);
        return ConditionTargets.Where(p => available.Contains(p.Value)).Select(p => p.Key).ToHashSet();
    }

    public static string? TargetOf(string condition) =>
        ConditionTargets.TryGetValue(condition, out var target) ? target : null;
}
```

- [ ] **Step 4: Write the parser**

Create `src/ContentAutomatorX.Application/Newsletter/TemplateParser.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Carves a template document into named blocks. Structural only — whether a block's
/// placeholders make sense is TemplateValidator's business. Text outside any block is ignored on
/// purpose: the reference template opens with a long explanatory comment that must survive.</summary>
public static partial class TemplateParser
{
    public static ParsedTemplate Parse(string html)
    {
        var blocks = new Dictionary<string, TemplateBlock>(StringComparer.Ordinal);
        var issues = new List<TemplateIssue>();

        string? openName = null;
        var openLine = 0;
        var contentStart = 0;

        foreach (Match match in MarkerRegex().Matches(html ?? ""))
        {
            var line = LineOf(html!, match.Index);
            var isOpen = match.Groups["open"].Success;

            if (isOpen)
            {
                var name = match.Groups["name"].Value.ToLowerInvariant();
                if (openName is not null)
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"BLOCK: {name} starts while BLOCK: {openName} is already open — blocks cannot nest."));
                    continue;
                }
                if (!TemplateBlocks.All.Contains(name))
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"Unknown block name '{name}'. Valid names: {string.Join(", ", TemplateBlocks.All.Order())}."));
                    // Still opened, so its matching close is consumed rather than reported as stray.
                }
                openName = name;
                openLine = line;
                contentStart = match.Index + match.Length;
            }
            else
            {
                if (openName is null)
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        "A closing <!-- /BLOCK --> appears where no block is open."));
                    continue;
                }
                var content = html![contentStart..match.Index];
                if (!TemplateBlocks.All.Contains(openName))
                {
                    // Unknown name already reported at the opening marker; drop the content.
                }
                else if (blocks.ContainsKey(openName))
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, openLine,
                        $"BLOCK: {openName} is defined more than once. Only the first is used."));
                }
                else
                {
                    blocks[openName] = new TemplateBlock(openName, content.Trim('\r', '\n'), openLine);
                }
                openName = null;
            }
        }

        if (openName is not null)
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, openLine,
                $"BLOCK: {openName} is never closed — add <!-- /BLOCK -->."));

        return new ParsedTemplate(blocks, issues);
    }

    /// <summary>1-based line number of a character index.</summary>
    public static int LineOf(string text, int index) =>
        text.AsSpan(0, Math.Min(index, text.Length)).Count('\n') + 1;

    [GeneratedRegex(@"<!--\s*(?:(?<open>BLOCK)\s*:\s*(?<name>[A-Za-z_]+)|/BLOCK)\s*-->",
        RegexOptions.IgnoreCase)]
    private static partial Regex MarkerRegex();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter TemplateParserTests
```

Expected: PASS, 16 test cases (8 facts plus 8 theory cases).

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(templates): block parser and template vocabulary"
```

---

## Task 3: Template validator

**Spec:** §8, and the error table E1–E14 / W1–W2.

**Files:**
- Create: `src/ContentAutomatorX.Application/Newsletter/TemplateValidator.cs`
- Test: `tests/ContentAutomatorX.UnitTests/TemplateValidatorTests.cs`

**Interfaces:**
- Consumes: `TemplateParser.Parse`, `TemplateBlocks`, `TemplatePlaceholders`, `TemplateIssue`.
- Produces: `TemplateValidator.MaxBytes` (`const int` = 512 * 1024); `TemplateValidator.Validate(string html) => IReadOnlyList<TemplateIssue>`; `TemplateValidator.HasErrors(IReadOnlyList<TemplateIssue>) => bool`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ContentAutomatorX.UnitTests/TemplateValidatorTests.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class TemplateValidatorTests
{
    // A minimal template that must validate clean. Every negative test below is this, broken
    // in exactly one way — so a failure names the rule it broke, not a pile of unrelated noise.
    private const string Valid = """
        <!-- BLOCK: shell -->
        <html><body>{{sections}}<a href="{{unsubscribe_url}}">Unsubscribe</a></body></html>
        <!-- /BLOCK -->
        <!-- BLOCK: topic -->
        <!-- IF: image --><img src="{{image_url}}" /><!-- /IF -->
        <h2>{{title}}</h2>{{body_html}}
        <!-- /BLOCK -->
        """;

    [Fact]
    public void A_valid_template_produces_no_errors()
    {
        var issues = TemplateValidator.Validate(Valid);
        Assert.False(TemplateValidator.HasErrors(issues));
    }

    [Theory]
    // E1 empty
    [InlineData("", "is empty")]
    // E3 no shell
    [InlineData("<!-- BLOCK: topic -->x<!-- /BLOCK -->", "must contain a BLOCK: shell")]
    // E6 unknown block
    [InlineData("<!-- BLOCK: shell -->{{sections}}{{unsubscribe_url}}<!-- /BLOCK -->"
              + "<!-- BLOCK: banana -->x<!-- /BLOCK -->", "Unknown block name")]
    // E8 unclosed block
    [InlineData("<!-- BLOCK: shell -->{{sections}}{{unsubscribe_url}}", "never closed")]
    public void Structural_problems_are_errors(string html, string expectedFragment)
    {
        var issues = TemplateValidator.Validate(html);
        Assert.True(TemplateValidator.HasErrors(issues));
        Assert.Contains(issues, i => i.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact] // E2
    public void Oversized_template_is_an_error()
    {
        var html = Valid + new string('x', TemplateValidator.MaxBytes);
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("too large"));
    }

    [Fact] // E4
    public void Shell_without_the_sections_slot_is_an_error()
    {
        var html = "<!-- BLOCK: shell --><html>{{unsubscribe_url}}</html><!-- /BLOCK -->";
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("{{sections}}"));
    }

    [Fact] // E5 — the one that matters most
    public void Template_without_an_unsubscribe_link_is_an_error()
    {
        var html = "<!-- BLOCK: shell --><html>{{sections}}</html><!-- /BLOCK -->";
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("unsubscribe"));
    }

    [Fact] // E10
    public void Unknown_placeholder_is_an_error_naming_the_block()
    {
        var html = Valid.Replace("{{title}}", "{{titel}}");
        var issue = Assert.Single(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("titel"));
        Assert.Contains("topic", issue.Message);
    }

    [Fact] // E10 — a placeholder legal elsewhere is still illegal here
    public void Placeholder_from_another_block_is_an_error()
    {
        var html = Valid.Replace("{{title}}", "{{thumbnail_url}}");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("thumbnail_url"));
    }

    [Fact] // E11
    public void Sections_slot_outside_the_shell_is_an_error()
    {
        var html = Valid.Replace("<h2>{{title}}</h2>", "<h2>{{sections}}</h2>");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("only in the shell"));
    }

    [Fact] // E12
    public void Unclosed_if_region_is_an_error()
    {
        var html = Valid.Replace("<!-- /IF -->", "");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("never closed"));
    }

    [Fact] // E12 — stray close
    public void Closing_if_with_nothing_open_is_an_error()
    {
        var html = Valid.Replace("<h2>{{title}}</h2>", "<!-- /IF --><h2>{{title}}</h2>");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("no IF is open"));
    }

    [Fact] // E13
    public void Nested_if_region_is_an_error()
    {
        var html = Valid.Replace("<img src=\"{{image_url}}\" />",
            "<!-- IF: link --><img src=\"{{image_url}}\" /><!-- /IF -->");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("cannot nest"));
    }

    [Fact] // E14
    public void Unknown_if_condition_is_an_error()
    {
        var html = Valid.Replace("<!-- IF: image -->", "<!-- IF: banana -->");
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Error && i.Message.Contains("banana"));
    }

    [Fact] // W1
    public void Missing_optional_blocks_are_warnings_not_errors()
    {
        var issues = TemplateValidator.Validate(Valid);
        Assert.False(TemplateValidator.HasErrors(issues));
        // Valid defines shell and topic; the other six optional blocks warn.
        Assert.Equal(6, issues.Count(i => i.Level == TemplateIssueLevel.Warning
            && i.Message.Contains("built-in design")));
    }

    [Fact] // W2
    public void Block_with_no_placeholders_at_all_is_a_warning()
    {
        var html = Valid + "\n<!-- BLOCK: sponsor --><td>nothing dynamic</td><!-- /BLOCK -->";
        Assert.Contains(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Warning && i.Message.Contains("no placeholders"));
    }

    [Fact]
    public void Divider_needs_no_placeholders_and_does_not_warn()
    {
        var html = Valid + "\n<!-- BLOCK: divider --><hr /><!-- /BLOCK -->";
        Assert.DoesNotContain(TemplateValidator.Validate(html),
            i => i.Level == TemplateIssueLevel.Warning && i.Message.Contains("no placeholders"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter TemplateValidatorTests
```

Expected: FAIL to compile — `TemplateValidator` does not exist.

- [ ] **Step 3: Write the validator**

Create `src/ContentAutomatorX.Application/Newsletter/TemplateValidator.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Everything checked before a template may be saved. Rendering never consults this —
/// a template that somehow reaches render in a bad state still produces sendable HTML.</summary>
public static partial class TemplateValidator
{
    public const int MaxBytes = 512 * 1024;

    public static bool HasErrors(IReadOnlyList<TemplateIssue> issues) =>
        issues.Any(i => i.Level == TemplateIssueLevel.Error);

    public static IReadOnlyList<TemplateIssue> Validate(string html)
    {
        var issues = new List<TemplateIssue>();

        if (string.IsNullOrWhiteSpace(html))
        {
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1, "The template is empty."));
            return issues;
        }
        if (Encoding.UTF8.GetByteCount(html) > MaxBytes)
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1,
                $"The template is too large — {MaxBytes / 1024} KB is the limit."));

        var parsed = TemplateParser.Parse(html);
        issues.AddRange(parsed.Issues);

        if (!parsed.Blocks.ContainsKey(TemplateBlocks.Shell))
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1,
                "The template must contain a BLOCK: shell — it is the document everything else sits inside."));

        foreach (var block in parsed.Blocks.Values)
        {
            var allowed = TemplatePlaceholders.For(block.Name);
            var conditions = TemplatePlaceholders.Conditions(block.Name);
            var used = 0;

            foreach (Match m in PlaceholderRegex().Matches(block.Content))
            {
                used++;
                var name = m.Groups["name"].Value;
                var line = block.Line + TemplateParser.LineOf(block.Content, m.Index) - 1;

                if (name == "sections" && block.Name != TemplateBlocks.Shell)
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        "{{sections}} may appear only in the shell block."));
                else if (!allowed.Contains(name))
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"Unknown placeholder {{{{{name}}}}} in BLOCK: {block.Name}. "
                        + $"Available here: {string.Join(", ", allowed.Order())}."));
            }

            ValidateRegions(block, conditions, issues);

            // Divider is the one block with nothing to substitute, by design.
            if (used == 0 && block.Name != TemplateBlocks.Divider)
                issues.Add(new TemplateIssue(TemplateIssueLevel.Warning, block.Line,
                    $"BLOCK: {block.Name} contains no placeholders — it will render the same markup every time."));
        }

        if (parsed.Blocks.TryGetValue(TemplateBlocks.Shell, out var shell)
            && !PlaceholderRegex().Matches(shell.Content).Any(m => m.Groups["name"].Value == "sections"))
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, shell.Line,
                "BLOCK: shell must contain {{sections}} — otherwise no section is ever emitted."));

        // Checked across the whole template rather than inside the footer, so a design that puts
        // unsubscribe in the shell still passes. This is a legal requirement, not a style rule.
        if (!parsed.Blocks.Values.Any(b => b.Content.Contains("{{unsubscribe_url}}", StringComparison.Ordinal)))
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, 1,
                "The template contains no {{unsubscribe_url}} — commercial email must carry an unsubscribe link."));

        foreach (var name in TemplateBlocks.Optional.Where(n => !parsed.Blocks.ContainsKey(n)))
            issues.Add(new TemplateIssue(TemplateIssueLevel.Warning, 1,
                $"No BLOCK: {name} — those sections will use the built-in design."));

        return issues.OrderBy(i => i.Line).ToList();
    }

    private static void ValidateRegions(TemplateBlock block, IReadOnlySet<string> conditions,
        List<TemplateIssue> issues)
    {
        string? open = null;
        var openLine = 0;

        foreach (Match m in RegionRegex().Matches(block.Content))
        {
            var line = block.Line + TemplateParser.LineOf(block.Content, m.Index) - 1;
            if (m.Groups["open"].Success)
            {
                var condition = m.Groups["cond"].Value.ToLowerInvariant();
                if (open is not null)
                {
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"IF: {condition} starts inside IF: {open} — IF regions cannot nest."));
                    continue;
                }
                if (!conditions.Contains(condition))
                    issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                        $"Unknown condition '{condition}' in BLOCK: {block.Name}. "
                        + $"Available here: {string.Join(", ", conditions.Order())}."));
                open = condition;
                openLine = line;
            }
            else if (open is null)
            {
                issues.Add(new TemplateIssue(TemplateIssueLevel.Error, line,
                    "A closing <!-- /IF --> appears where no IF is open."));
            }
            else
            {
                open = null;
            }
        }

        if (open is not null)
            issues.Add(new TemplateIssue(TemplateIssueLevel.Error, openLine,
                $"<!-- IF: {open} --> is never closed — add <!-- /IF -->."));
    }

    [GeneratedRegex(@"\{\{\s*(?<name>[a-z_]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"<!--\s*(?:(?<open>IF)\s*:\s*(?<cond>[A-Za-z_]+)|/IF)\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex RegionRegex();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter TemplateValidatorTests
```

Expected: PASS, 18 test cases (14 facts plus 4 theory cases).

- [ ] **Step 5: Verify the reference template validates clean**

The real file is the acceptance test for the whole vocabulary. Add this to `TemplateValidatorTests.cs`:

```csharp
    [Fact]
    public void The_reference_template_is_not_yet_annotated_and_that_is_expected()
    {
        // docs/user-braindumps/preview.html carries BLOCK comments but no placeholders yet —
        // Task 10 converts it. This test documents the current state so the conversion has a
        // before-and-after, and fails loudly if someone converts it without updating this.
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "docs", "user-braindumps", "preview.html");
        if (!File.Exists(path)) return; // not shipped with the test output on all machines
        var issues = TemplateValidator.Validate(File.ReadAllText(path));
        Assert.True(TemplateValidator.HasErrors(issues));
    }
```

Run it and confirm PASS.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(templates): save-time validation with 14 errors and 2 warnings"
```

---

## Task 4: Reading time and YouTube URLs

**Spec:** §5.5, §6.2.

**Files:**
- Create: `src/ContentAutomatorX.Application/Newsletter/ReadingTime.cs`, `src/ContentAutomatorX.Application/Newsletter/YouTubeUrl.cs`, `src/ContentAutomatorX.Domain/Abstractions/IYouTubeThumbnailResolver.cs`, `src/ContentAutomatorX.Infrastructure/Newsletter/YouTubeThumbnailResolver.cs`
- Test: `tests/ContentAutomatorX.UnitTests/ReadingTimeTests.cs`, `tests/ContentAutomatorX.UnitTests/YouTubeUrlTests.cs`

**Interfaces:**
- Produces: `ReadingTime.Describe(string? markdown) => string`; `YouTubeUrl.TryGetVideoId(string? url, out string? id) => bool`; `YouTubeUrl.MaxResThumbnail(string id) => string`; `YouTubeUrl.FallbackThumbnail(string id) => string`; `IYouTubeThumbnailResolver.ResolveAsync(string videoId, CancellationToken) => Task<string>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ContentAutomatorX.UnitTests/ReadingTimeTests.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class ReadingTimeTests
{
    [Theory]
    [InlineData(null, "1 min read")]
    [InlineData("", "1 min read")]
    [InlineData("   ", "1 min read")]
    [InlineData("one two three", "1 min read")]
    public void Short_or_absent_bodies_still_read_as_one_minute(string? body, string expected) =>
        Assert.Equal(expected, ReadingTime.Describe(body));

    [Theory]
    [InlineData(200, "1 min read")]
    [InlineData(300, "2 min read")]   // 1.5 rounds up
    [InlineData(1800, "9 min read")]
    public void Longer_bodies_scale_at_two_hundred_words_a_minute(int words, string expected) =>
        Assert.Equal(expected, ReadingTime.Describe(string.Join(" ", Enumerable.Repeat("word", words))));

    [Fact]
    public void Markdown_syntax_is_not_counted_as_words()
    {
        // Without stripping, the hashes, asterisks, brackets and URL inflate the count.
        var markdown = "## Heading\n\n**bold** _italic_ [link](https://example.com/a/very/long/path)";
        Assert.Equal("1 min read", ReadingTime.Describe(markdown));
        Assert.Equal(ReadingTime.Describe("Heading bold italic link"), ReadingTime.Describe(markdown));
    }
}
```

Create `tests/ContentAutomatorX.UnitTests/YouTubeUrlTests.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;

namespace ContentAutomatorX.UnitTests;

public class YouTubeUrlTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&t=42s", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc123", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ#t=10", "dQw4w9WgXcQ")]
    [InlineData("http://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void Extracts_the_video_id_from_every_url_shape(string url, string expected)
    {
        Assert.True(YouTubeUrl.TryGetVideoId(url, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://vimeo.com/12345")]
    [InlineData("https://www.youtube.com/")]
    [InlineData("https://www.youtube.com/watch?list=PL123")]
    [InlineData("not a url at all")]
    public void Returns_false_for_anything_it_cannot_read(string? url)
    {
        Assert.False(YouTubeUrl.TryGetVideoId(url, out var id));
        Assert.Null(id);
    }

    [Fact]
    public void Builds_both_thumbnail_urls()
    {
        Assert.Equal("https://img.youtube.com/vi/abc/maxresdefault.jpg", YouTubeUrl.MaxResThumbnail("abc"));
        Assert.Equal("https://img.youtube.com/vi/abc/hqdefault.jpg", YouTubeUrl.FallbackThumbnail("abc"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter "ReadingTimeTests|YouTubeUrlTests"
```

Expected: FAIL to compile.

- [ ] **Step 3: Write reading time**

Create `src/ContentAutomatorX.Application/Newsletter/ReadingTime.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Derived, never stored. A stored field is one the model can fill wrongly, one the chat
/// contract has to carry, and one that goes stale the moment a paragraph is edited.</summary>
public static partial class ReadingTime
{
    private const int WordsPerMinute = 200;

    public static string Describe(string? markdown)
    {
        var words = CountWords(markdown);
        var minutes = Math.Max(1, (int)Math.Round(words / (double)WordsPerMinute, MidpointRounding.AwayFromZero));
        return $"{minutes} min read";
    }

    public static int CountWords(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return 0;
        // Link targets first, so a long URL does not count as a word; then the rest of the syntax.
        var text = LinkRegex().Replace(markdown, "$1");
        text = SyntaxRegex().Replace(text, " ");
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"[#*_>`~\[\]()|-]+")]
    private static partial Regex SyntaxRegex();
}
```

- [ ] **Step 4: Write the YouTube URL helper**

Create `src/ContentAutomatorX.Application/Newsletter/YouTubeUrl.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Pure URL work. The HEAD probe that decides which thumbnail actually exists lives
/// behind IYouTubeThumbnailResolver, because Application does not do HTTP.</summary>
public static class YouTubeUrl
{
    public static bool TryGetVideoId(string? url, [NotNullWhen(true)] out string? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var candidate = host.ToLowerInvariant() switch
        {
            "youtu.be" => segments.FirstOrDefault(),
            "youtube.com" or "m.youtube.com" => segments switch
            {
                ["watch"] => QueryValue(uri.Query, "v"),
                ["shorts", var s, ..] => s,
                ["embed", var e, ..] => e,
                _ => null
            },
            _ => null
        };

        if (string.IsNullOrWhiteSpace(candidate)) return false;
        id = candidate;
        return true;
    }

    public static string MaxResThumbnail(string videoId) =>
        $"https://img.youtube.com/vi/{videoId}/maxresdefault.jpg";

    /// <summary>Always exists, for every video, at 480x360.</summary>
    public static string FallbackThumbnail(string videoId) =>
        $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";

    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split('=', 2);
            if (split.Length == 2 && split[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(split[1]);
        }
        return null;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter "ReadingTimeTests|YouTubeUrlTests"
```

Expected: PASS, 24 test cases.

- [ ] **Step 6: Add the resolver seam and its implementation**

Create `src/ContentAutomatorX.Domain/Abstractions/IYouTubeThumbnailResolver.cs`:

```csharp
namespace ContentAutomatorX.Domain.Abstractions;

/// <summary>Decides which of a video's thumbnails actually exists. maxresdefault.jpg is published
/// only for videos uploaded above 720p, so it cannot be used blindly — a dead image in a sent
/// newsletter is worse than a low-resolution one.</summary>
public interface IYouTubeThumbnailResolver
{
    /// <summary>Returns an absolute thumbnail URL. Never throws: a failed probe falls back.</summary>
    Task<string> ResolveAsync(string videoId, CancellationToken ct = default);
}
```

Create `src/ContentAutomatorX.Infrastructure/Newsletter/YouTubeThumbnailResolver.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace ContentAutomatorX.Infrastructure.Newsletter;

public class YouTubeThumbnailResolver(HttpClient http, ILogger<YouTubeThumbnailResolver> log)
    : IYouTubeThumbnailResolver
{
    public async Task<string> ResolveAsync(string videoId, CancellationToken ct = default)
    {
        var maxRes = YouTubeUrl.MaxResThumbnail(videoId);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, maxRes);
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return maxRes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline, or slow. Falling back beats failing the user's save.
            log.LogDebug(ex, "maxresdefault probe failed for {VideoId}; using hqdefault", videoId);
        }
        return YouTubeUrl.FallbackThumbnail(videoId);
    }
}
```

Register it in `src/ContentAutomatorX.Web/Program.cs`, beside the other HTTP-backed services:

```csharp
builder.Services.AddHttpClient<IYouTubeThumbnailResolver, YouTubeThumbnailResolver>(c =>
    c.Timeout = TimeSpan.FromSeconds(5));
```

- [ ] **Step 7: Build to verify the wiring compiles**

```
dotnet build
```

Expected: build succeeds with no warnings introduced.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(templates): reading time, YouTube url parsing, thumbnail resolver seam"
```

---

## Task 5: Template renderer

**Spec:** §5.2, §5.3, §5.4, §4.2, §4.3.

**Files:**
- Create: `src/ContentAutomatorX.Application/Newsletter/TemplateHtmlRenderer.cs`, `src/ContentAutomatorX.Application/Newsletter/SampleIssue.cs`
- Modify: `src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs`
- Test: `tests/ContentAutomatorX.UnitTests/TemplateHtmlRendererTests.cs`

**Interfaces:**
- Consumes: `TemplateParser.Parse`, `TemplateBlocks`, `TemplatePlaceholders.TargetOf`, `ReadingTime.Describe`, `EmailHtmlRenderer.RenderFragment`, `SectionHtmlRenderer.UnsubscribeToken`.
- Produces: `TemplateHtmlRenderer.Render(IReadOnlyList<IssueSection> sections, Tenant tenant, string title, string templateHtml, DateTimeOffset issueDate) => string`; `SectionHtmlRenderer.RenderSection(IssueSection section, string accent) => string`; `SampleIssue.Sections => IReadOnlyList<IssueSection>`; `SampleIssue.Tenant => Tenant`.

- [ ] **Step 1: Write the failing tests**

Create `tests/ContentAutomatorX.UnitTests/TemplateHtmlRendererTests.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.UnitTests;

public class TemplateHtmlRendererTests
{
    private const string Template = """
        <!-- BLOCK: shell -->
        <html><head><title>{{issue_title}}</title></head>
        <body><span class="pre">{{preheader}}</span>{{sections}}
        <a href="{{unsubscribe_url}}">Unsubscribe</a></body></html>
        <!-- /BLOCK -->
        <!-- BLOCK: header -->
        <h1>{{title}}</h1>{{body_html}}
        <!-- /BLOCK -->
        <!-- BLOCK: topic -->
        <!-- IF: image --><img src="{{image_url}}" alt="{{title}}" /><!-- /IF -->
        <!-- IF: category --><span class="cat">{{category}} · {{reading_time}}</span><!-- /IF -->
        <h2>{{title}}</h2>{{body_html}}
        <!-- IF: link --><a class="more" href="{{link_url}}">{{link_text}}</a><!-- /IF -->
        <!-- /BLOCK -->
        <!-- BLOCK: video -->
        <!-- IF: thumbnail --><img class="thumb" src="{{thumbnail_url}}" /><!-- /IF -->
        <h2>{{title}}</h2><a href="{{video_url}}">{{link_text}}</a>
        <!-- /BLOCK -->
        <!-- BLOCK: divider --><hr class="rule" /><!-- /BLOCK -->
        <!-- BLOCK: footer -->{{body_html}}<p>{{sender_identity}}</p><!-- /BLOCK -->
        """;

    private static Tenant MakeTenant() => new()
    {
        Name = "Into the Latent", Slug = "itl", SenderIdentity = "Christian Wenzl · Greven",
        BrandingJson = """{"accentColorHex":"#1AE6D5"}"""
    };

    private static IssueSection Section(string type, string? title = null, string? body = null,
        string? image = null, string? link = null, string? linkText = null, string? category = null,
        int position = 0) =>
        new()
        {
            PostId = Guid.NewGuid(), Position = position, Type = type, Title = title, BodyMd = body,
            ImageUrl = image, LinkUrl = link, LinkText = linkText, Category = category
        };

    private static string RenderOne(IssueSection section) => TemplateHtmlRenderer.Render(
        [section], MakeTenant(), "July issue", Template, new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Wraps_sections_in_the_shell_and_fills_globals()
    {
        var html = RenderOne(Section(SectionTypes.Divider));
        Assert.Contains("<title>July issue</title>", html);
        Assert.Contains("<hr class=\"rule\" />", html);
        Assert.Contains(SectionHtmlRenderer.UnsubscribeToken, html);
        Assert.DoesNotContain("{{", html);
    }

    [Fact]
    public void Emits_one_block_per_section_in_position_order()
    {
        var html = TemplateHtmlRenderer.Render(
            [Section(SectionTypes.Topic, title: "Second", position: 1),
             Section(SectionTypes.Topic, title: "First", position: 0)],
            MakeTenant(), "t", Template, DateTimeOffset.UtcNow);
        Assert.True(html.IndexOf("First", StringComparison.Ordinal)
                  < html.IndexOf("Second", StringComparison.Ordinal));
    }

    [Fact]
    public void Present_fields_keep_their_regions()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "Flux LoRA", body: "Body text.",
            image: "https://img.example.com/a.png", link: "https://example.com/a", category: "Tutorial"));
        Assert.Contains("<img src=\"https://img.example.com/a.png\"", html);
        Assert.Contains("Tutorial · 1 min read", html);
        Assert.Contains("class=\"more\"", html);
        Assert.DoesNotContain("<!-- IF", html);
        Assert.DoesNotContain("<!-- /IF", html);
    }

    [Fact]
    public void Absent_fields_drop_their_whole_region()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "No extras", body: "Body."));
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("class=\"cat\"", html);
        Assert.DoesNotContain("class=\"more\"", html);
        Assert.Contains("<h2>No extras</h2>", html);
    }

    [Fact]
    public void Link_text_falls_back_per_section_type()
    {
        Assert.Contains("Read more", RenderOne(Section(SectionTypes.Topic,
            title: "t", link: "https://example.com")));
        Assert.Contains("Watch on YouTube", RenderOne(Section(SectionTypes.Video,
            title: "v", link: "https://youtu.be/dQw4w9WgXcQ")));
    }

    [Fact]
    public void Body_markdown_becomes_html()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "t", body: "A **bold** word."));
        Assert.Contains("<strong>bold</strong>", html);
    }

    [Fact] // The trust boundary. Section content is never trusted.
    public void Script_in_a_section_title_is_escaped_not_emitted()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "<script>alert(1)</script>", body: "x"));
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Raw_html_in_a_section_body_is_escaped_not_emitted()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "t", body: "<script>alert(1)</script>"));
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Javascript_url_resolves_empty_and_collapses_its_region()
    {
        var html = RenderOne(Section(SectionTypes.Topic, title: "t", body: "b",
            link: "javascript:alert(1)", image: "javascript:alert(2)"));
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("class=\"more\"", html);   // region collapsed, not left with href="#"
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void Video_thumbnail_is_derived_from_the_url_when_no_override_is_set()
    {
        var html = RenderOne(Section(SectionTypes.Video, title: "v",
            link: "https://youtu.be/dQw4w9WgXcQ"));
        Assert.Contains("https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg", html);
    }

    [Fact]
    public void Video_thumbnail_override_wins_over_the_derived_one()
    {
        var html = RenderOne(Section(SectionTypes.Video, title: "v",
            link: "https://youtu.be/dQw4w9WgXcQ", image: "https://img.example.com/custom.png"));
        Assert.Contains("https://img.example.com/custom.png", html);
        Assert.DoesNotContain("img.youtube.com", html);
    }

    [Fact]
    public void A_section_whose_block_is_missing_falls_back_to_the_built_in_design()
    {
        // Template above defines no sponsor block.
        var html = RenderOne(Section(SectionTypes.Sponsor, title: "Acme", body: "Pitch."));
        Assert.Contains("SPONSORED", html);          // built-in sponsor markup
        Assert.Contains("Acme", html);
        Assert.Contains("<html>", html);             // still inside the template shell
    }

    [Fact]
    public void Preheader_comes_from_the_header_body_with_markdown_stripped()
    {
        var html = TemplateHtmlRenderer.Render(
            [Section(SectionTypes.Header, body: "**Flux** LoRA training that holds a face.")],
            MakeTenant(), "t", Template, DateTimeOffset.UtcNow);
        Assert.Contains("Flux LoRA training that holds a face.", html);
        Assert.DoesNotContain("**Flux**", html);
    }

    [Fact]
    public void Issue_date_is_formatted_as_month_and_year()
    {
        const string dated = "<!-- BLOCK: shell -->{{issue_date}}{{sections}}{{unsubscribe_url}}<!-- /BLOCK -->";
        var html = TemplateHtmlRenderer.Render([], MakeTenant(), "t", dated,
            new DateTimeOffset(2026, 7, 21, 0, 0, 0, TimeSpan.Zero));
        Assert.Contains("July 2026", html);
    }

    [Fact]
    public void A_template_with_no_shell_renders_nothing_rather_than_throwing()
    {
        var html = TemplateHtmlRenderer.Render([Section(SectionTypes.Divider)], MakeTenant(), "t",
            "<!-- BLOCK: topic -->x<!-- /BLOCK -->", DateTimeOffset.UtcNow);
        Assert.Equal("", html);
    }

    [Fact]
    public void The_sample_issue_exercises_every_block()
    {
        var types = SampleIssue.Sections.Select(s => s.Type).ToHashSet();
        foreach (var type in new[] { SectionTypes.Header, SectionTypes.Topic, SectionTypes.Video,
                                     SectionTypes.Sponsor, SectionTypes.Button, SectionTypes.Divider,
                                     SectionTypes.Footer })
            Assert.Contains(type, types);

        // And at least one topic with nothing optional set, so IF-collapse is visible in the preview.
        Assert.Contains(SampleIssue.Sections, s => s.Type == SectionTypes.Topic
            && s.ImageUrl is null && s.LinkUrl is null && s.Category is null);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter TemplateHtmlRendererTests
```

Expected: FAIL to compile — `TemplateHtmlRenderer` does not exist.

- [ ] **Step 3: Expose per-section fallback on the built-in renderer**

In `src/ContentAutomatorX.Application/Newsletter/SectionHtmlRenderer.cs`, change the signature of `AppendSection` from private to a public wrapper. Add this public method immediately after `Render`, and leave `AppendSection` exactly as it is:

```csharp
    /// <summary>The built-in markup for one section, used as TemplateHtmlRenderer's per-section
    /// fallback when a template has no block for that type. Assumes it sits inside a 600px table
    /// cell, which the template's shell provides.</summary>
    public static string RenderSection(IssueSection section, string accent)
    {
        var sb = new StringBuilder();
        AppendSection(sb, section, accent);
        return sb.ToString();
    }
```

Then add a `Video` case to `AppendSection`, immediately after the `Topic` case. It reuses the topic shape because the built-in design has no distinct video treatment — the template is where the dark video panel lives:

```csharp
            case SectionTypes.Video:
                if (title.Length > 0)
                    sb.AppendLine($"""<h2 style="font-size:21px;margin:20px 0 10px;color:{accent};">{title}</h2>""");
                var thumbnail = VideoThumbnail(s);
                if (thumbnail is not null)
                    sb.AppendLine($"""<a href="{WebUtility.HtmlEncode(s.LinkUrl)}"><img src="{WebUtility.HtmlEncode(thumbnail)}" alt="{title}" style="max-width:100%;height:auto;border:0;display:block;margin:0 0 10px;" /></a>""");
                sb.AppendLine(EmailHtmlRenderer.RenderFragment(s.BodyMd ?? "", accent));
                if (IsHttpUrl(s.LinkUrl))
                    AppendButton(sb, s.LinkUrl!, s.LinkText ?? "Watch on YouTube", accent);
                break;
```

and this helper beside `IsHttpUrl`:

```csharp
    /// <summary>Override wins; otherwise derive from the YouTube URL. Null when neither works.</summary>
    internal static string? VideoThumbnail(IssueSection s) =>
        IsHttpUrl(s.ImageUrl) ? s.ImageUrl
        : YouTubeUrl.TryGetVideoId(s.LinkUrl, out var id) ? YouTubeUrl.FallbackThumbnail(id)
        : null;
```

Add a `Video` case to `ToMarkdown` as well, immediately after the `Topic` case:

```csharp
                case SectionTypes.Video:
                    if (!string.IsNullOrWhiteSpace(s.Title)) AppendMd(sb, $"## {s.Title}");
                    AppendMd(sb, s.BodyMd);
                    if (!string.IsNullOrWhiteSpace(s.LinkUrl))
                        AppendMd(sb, $"[{s.LinkText ?? "Watch on YouTube"}]({s.LinkUrl})");
                    break;
```

- [ ] **Step 4: Write the sample issue**

Create `src/ContentAutomatorX.Application/Newsletter/SampleIssue.cs`:

```csharp
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>The fixed issue the template editor previews against. Deliberately exercises every
/// block and both sides of every IF region — section 4 has nothing optional set, so a template
/// author sees immediately whether their regions collapse cleanly. A real issue would leave
/// whichever blocks it happens not to use untested while they are being edited.</summary>
public static class SampleIssue
{
    private static readonly Guid PostId = new("00000000-0000-0000-0000-0000000005a3");

    public static Tenant Tenant { get; } = new()
    {
        Name = "Into the Latent",
        Slug = "sample",
        SenderIdentity = "Your Name · c/o Your Address · 00000 Your City · Country",
        BrandingJson = """{"accentColorHex":"#1AE6D5"}"""
    };

    public static IReadOnlyList<IssueSection> Sections { get; } =
    [
        Make(0, SectionTypes.Header, title: "Signals from the latent space",
            body: "Hey — this month was mostly about consistency: getting a face to survive 50 "
                + "generations without drifting. Two write-ups below, plus a new build video."),
        Make(1, SectionTypes.Topic, title: "Training a Flux LoRA that actually holds a face",
            body: "Caption strategy, learning-rate schedule, and the regularisation set that stopped "
                + "my character drifting after twenty generations.",
            image: "https://placehold.co/1072x600/0F0F1A/1AE6D5/png?text=Cover+image",
            link: "https://example.com/blog/flux-lora-consistency", category: "Tutorial"),
        Make(2, SectionTypes.Divider),
        // Nothing optional set: this is the one that proves IF regions collapse.
        Make(3, SectionTypes.Topic, title: "A shorter note with no cover image",
            body: "No image, no link, no category — everything optional is absent here on purpose."),
        Make(4, SectionTypes.Video, title: "Building a character-consistent pipeline, end to end",
            body: "42 minutes, no cuts, including the parts that broke.",
            link: "https://www.youtube.com/watch?v=dQw4w9WgXcQ", linkText: "Watch the build →"),
        Make(5, SectionTypes.Sponsor, title: "Sponsor name — one-line pitch goes here",
            body: "Two or three sentences of sponsor copy. Keeps the same rhythm as the rest of the "
                + "issue, but the label and the tinted panel make it unmistakably paid placement.",
            image: "https://placehold.co/200x60/EAEBF1/5A5E70/png?text=Logo",
            link: "https://example.com", linkText: "Visit sponsor →"),
        Make(6, SectionTypes.Button, link: "https://example.com/services", linkText: "See the services"),
        Make(7, SectionTypes.Footer,
            body: "You're receiving this because you subscribed at example.com.")
    ];

    private static IssueSection Make(int position, string type, string? title = null,
        string? body = null, string? image = null, string? link = null, string? linkText = null,
        string? category = null) =>
        new()
        {
            // Stable ids: the preview re-renders on every keystroke and must not churn.
            Id = new Guid($"00000000-0000-0000-0000-0000000000{position:d2}"),
            PostId = PostId, Position = position, Type = type, Title = title, BodyMd = body,
            ImageUrl = image, LinkUrl = link, LinkText = linkText, Category = category
        };
}
```

- [ ] **Step 5: Write the renderer**

Create `src/ContentAutomatorX.Application/Newsletter/TemplateHtmlRenderer.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Application.Newsletter;

/// <summary>Renders an issue into a tenant's own template. Never throws on a template problem:
/// a missing block falls back to the built-in design for that section, an unresolvable placeholder
/// resolves empty. Validation is a save-time concern (TemplateValidator), not a render-time one —
/// a template edit must never be able to fail a scheduled send.</summary>
public static partial class TemplateHtmlRenderer
{
    public static string Render(IReadOnlyList<IssueSection> sections, Tenant tenant, string title,
        string templateHtml, DateTimeOffset issueDate)
    {
        var parsed = TemplateParser.Parse(templateHtml);
        if (!parsed.Blocks.TryGetValue(TemplateBlocks.Shell, out var shell)) return "";

        var branding = TenantBranding.Parse(tenant.BrandingJson);
        var accent = SafeAccent(branding.AccentColorHex);
        var globals = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tenant_name"] = WebUtility.HtmlEncode(tenant.Name),
            ["accent"] = accent,
            ["issue_title"] = WebUtility.HtmlEncode(title),
            ["issue_date"] = issueDate.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
            ["unsubscribe_url"] = SectionHtmlRenderer.UnsubscribeToken
        };

        var body = new StringBuilder();
        foreach (var section in sections.OrderBy(s => s.Position))
        {
            var blockName = TemplateBlocks.ForSectionType(section.Type);
            if (blockName is not null && parsed.Blocks.TryGetValue(blockName, out var block))
                body.AppendLine(RenderBlock(block, SectionValues(section, tenant, accent, globals)));
            else
                body.AppendLine(SectionHtmlRenderer.RenderSection(section, accent));
        }

        var shellValues = new Dictionary<string, string>(globals, StringComparer.Ordinal)
        {
            ["preheader"] = Preheader(sections),
            ["sections"] = body.ToString()
        };
        return RenderBlock(shell, shellValues);
    }

    /// <summary>Regions first, then placeholders: a dropped region's placeholders should never be
    /// substituted, and substituting first would let a value's own text disturb region matching.</summary>
    private static string RenderBlock(TemplateBlock block, IReadOnlyDictionary<string, string> values)
    {
        var text = ApplyRegions(block.Content, values);
        return PlaceholderRegex().Replace(text, m =>
            values.TryGetValue(m.Groups["name"].Value, out var value) ? value : "");
    }

    private static string ApplyRegions(string text, IReadOnlyDictionary<string, string> values)
    {
        var sb = new StringBuilder();
        var cursor = 0;

        while (true)
        {
            var open = RegionOpenRegex().Match(text, cursor);
            if (!open.Success) break;

            var close = RegionCloseRegex().Match(text, open.Index + open.Length);
            if (!close.Success) break;   // unclosed: leave the rest verbatim rather than truncating

            sb.Append(text, cursor, open.Index - cursor);

            var target = TemplatePlaceholders.TargetOf(open.Groups["cond"].Value.ToLowerInvariant());
            var keep = target is not null
                && values.TryGetValue(target, out var value)
                && !string.IsNullOrWhiteSpace(value);
            if (keep)
                sb.Append(text, open.Index + open.Length, close.Index - (open.Index + open.Length));

            cursor = close.Index + close.Length;
        }

        sb.Append(text, cursor, text.Length - cursor);
        return sb.ToString();
    }

    private static Dictionary<string, string> SectionValues(IssueSection section, Tenant tenant,
        string accent, IReadOnlyDictionary<string, string> globals)
    {
        var values = new Dictionary<string, string>(globals, StringComparer.Ordinal)
        {
            ["title"] = WebUtility.HtmlEncode(section.Title ?? ""),
            ["body_html"] = EmailHtmlRenderer.RenderFragment(section.BodyMd ?? "", accent),
            ["category"] = WebUtility.HtmlEncode(section.Category ?? ""),
            ["reading_time"] = ReadingTime.Describe(section.BodyMd),
            ["link_url"] = SafeUrl(section.LinkUrl, allowMailto: true),
            ["link_text"] = WebUtility.HtmlEncode(section.LinkText ?? DefaultLinkText(section.Type)),
            ["sender_identity"] = WebUtility.HtmlEncode(tenant.SenderIdentity ?? "")
        };

        if (section.Type == SectionTypes.Video)
        {
            values["video_url"] = values["link_url"];
            values["thumbnail_url"] = SafeUrl(SectionHtmlRenderer.VideoThumbnail(section), allowMailto: false);
        }
        else
        {
            values["image_url"] = SafeUrl(section.ImageUrl, allowMailto: false);
        }
        return values;
    }

    private static string DefaultLinkText(string sectionType) => sectionType switch
    {
        SectionTypes.Topic => "Read more →",
        SectionTypes.Video => "Watch on YouTube →",
        SectionTypes.Sponsor => "Learn more",
        _ => "Open"
    };

    /// <summary>A rejected URL resolves to empty, not to '#', so the enclosing IF region collapses
    /// and no broken image or dead link is emitted at all.</summary>
    private static string SafeUrl(string? url, bool allowMailto)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        var ok = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
              || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
              || (allowMailto && url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase));
        return ok ? WebUtility.HtmlEncode(url) : "";
    }

    private static string Preheader(IReadOnlyList<IssueSection> sections)
    {
        var header = sections.FirstOrDefault(s => s.Type == SectionTypes.Header);
        if (string.IsNullOrWhiteSpace(header?.BodyMd)) return "";
        var text = MarkdownSyntaxRegex().Replace(header.BodyMd, "").Replace('\n', ' ').Trim();
        if (text.Length > 200) text = text[..200];
        return WebUtility.HtmlEncode(text);
    }

    private static string SafeAccent(string? hex) =>
        hex is not null && AccentRegex().IsMatch(hex) ? hex : EmailHtmlRenderer.DefaultAccent;

    [GeneratedRegex(@"\{\{\s*(?<name>[a-z_]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"<!--\s*IF\s*:\s*(?<cond>[A-Za-z_]+)\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex RegionOpenRegex();

    [GeneratedRegex(@"<!--\s*/IF\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex RegionCloseRegex();

    [GeneratedRegex(@"[#*_`]+")]
    private static partial Regex MarkdownSyntaxRegex();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex AccentRegex();
}
```

Note: `SectionHtmlRenderer.VideoThumbnail` is `internal`, and `TemplateHtmlRenderer` is in the same assembly, so this compiles. If a later refactor moves either type across assemblies, promote it to `public` rather than duplicating the logic.

- [ ] **Step 6: Run the tests to verify they pass**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter TemplateHtmlRendererTests
```

Expected: PASS, 16 tests.

- [ ] **Step 7: Run the full unit suite**

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj
```

Expected: all green. `SectionHtmlRendererTests` must still pass unchanged — the `Video` case and `RenderSection` are additive.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(templates): template renderer with region collapse and per-section fallback"
```

---

## Task 6: Template service and render wiring

**Spec:** §3.1 (`IsDefault`), §5.1, §10.

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/NewsletterTemplateService.cs`
- Modify: `src/ContentAutomatorX.Application/Services/PostService.cs`, `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`, `src/ContentAutomatorX.Web/Program.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/NewsletterTemplateServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `IAppDbContext.NewsletterTemplates`, `TemplateHtmlRenderer.Render`, `TemplateValidator`.
- Produces: `NewsletterTemplateService.ListAsync(Guid tenantId, CancellationToken)`; `.GetAsync(Guid id, CancellationToken)`; `.SaveAsync(NewsletterTemplate template, CancellationToken)`; `.DeleteAsync(Guid id, CancellationToken)`; `.ResolveForPostAsync(Guid postId, CancellationToken) => Task<NewsletterTemplate?>`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ContentAutomatorX.IntegrationTests/NewsletterTemplateServiceTests.cs`:

```csharp
    private const string MinimalHtml =
        "<!-- BLOCK: shell -->{{sections}}<a href=\"{{unsubscribe_url}}\">u</a><!-- /BLOCK -->";

    private static NewsletterTemplateService Service(TestDb t) => new(t.Db);

    [Fact]
    public async Task Setting_default_clears_it_on_the_tenants_other_templates()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);

        var first = new NewsletterTemplate { TenantId = tenantId, Name = "A", Html = MinimalHtml, IsDefault = true };
        await service.SaveAsync(first);
        var second = new NewsletterTemplate { TenantId = tenantId, Name = "B", Html = MinimalHtml, IsDefault = true };
        await service.SaveAsync(second);

        var all = await service.ListAsync(tenantId);
        Assert.Single(all, x => x.IsDefault);
        Assert.Equal("B", all.Single(x => x.IsDefault).Name);
    }

    [Fact]
    public async Task Default_is_scoped_to_one_tenant()
    {
        using var t = TestDb.Create();
        var service = Service(t);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await service.SaveAsync(new NewsletterTemplate { TenantId = mine, Name = "Mine", Html = MinimalHtml, IsDefault = true });
        await service.SaveAsync(new NewsletterTemplate { TenantId = theirs, Name = "Theirs", Html = MinimalHtml, IsDefault = true });

        Assert.Single(await service.ListAsync(mine));
        Assert.True((await service.ListAsync(theirs)).Single().IsDefault);
    }

    [Fact]
    public async Task Save_rejects_a_template_with_validation_errors()
    {
        using var t = TestDb.Create();
        var template = new NewsletterTemplate
        {
            TenantId = Guid.NewGuid(), Name = "Broken",
            Html = "<!-- BLOCK: shell -->no slot, no unsubscribe<!-- /BLOCK -->"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t).SaveAsync(template));
        Assert.Empty(await t.Db.NewsletterTemplates.ToListAsync());
    }

    [Fact]
    public async Task Delete_clears_the_reference_from_every_recipe_that_used_it()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);
        var template = new NewsletterTemplate { TenantId = tenantId, Name = "A", Html = MinimalHtml };
        await service.SaveAsync(template);

        t.Db.Recipes.Add(new Recipe
        {
            TenantId = tenantId, Name = "Monthly", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = template.Id
        });
        await t.Db.SaveChangesAsync();

        await service.DeleteAsync(template.Id);

        Assert.Empty(await t.Db.NewsletterTemplates.ToListAsync());
        Assert.Null((await t.Db.Recipes.SingleAsync()).NewsletterTemplateId);
    }

    [Fact]
    public async Task Resolution_prefers_the_recipes_template()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);
        var chosen = new NewsletterTemplate { TenantId = tenantId, Name = "Chosen", Html = MinimalHtml };
        var fallback = new NewsletterTemplate { TenantId = tenantId, Name = "Default", Html = MinimalHtml, IsDefault = true };
        await service.SaveAsync(fallback);
        await service.SaveAsync(chosen);

        var recipe = new Recipe
        {
            TenantId = tenantId, Name = "R", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = chosen.Id
        };
        t.Db.Recipes.Add(recipe);
        var post = new Post { TenantId = tenantId, PlatformId = Guid.NewGuid(), RecipeId = recipe.Id,
            Kind = "Newsletter", Title = "Issue" };
        t.Db.Posts.Add(post);
        await t.Db.SaveChangesAsync();

        Assert.Equal("Chosen", (await service.ResolveForPostAsync(post.Id))!.Name);
    }

    [Theory]
    [InlineData(true)]   // recipe exists but points at nothing
    [InlineData(false)]  // post has no recipe at all
    public async Task Resolution_falls_back_to_the_tenant_default(bool withRecipe)
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();
        var service = Service(t);
        await service.SaveAsync(new NewsletterTemplate
            { TenantId = tenantId, Name = "Default", Html = MinimalHtml, IsDefault = true });

        Guid? recipeId = null;
        if (withRecipe)
        {
            var recipe = new Recipe { TenantId = tenantId, Name = "R", Kind = "Newsletter",
                PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = null };
            t.Db.Recipes.Add(recipe);
            recipeId = recipe.Id;
        }
        var post = new Post { TenantId = tenantId, PlatformId = Guid.NewGuid(), RecipeId = recipeId,
            Kind = "Newsletter", Title = "Issue" };
        t.Db.Posts.Add(post);
        await t.Db.SaveChangesAsync();

        Assert.Equal("Default", (await service.ResolveForPostAsync(post.Id))!.Name);
    }

    [Fact]
    public async Task A_template_belonging_to_another_tenant_is_ignored()
    {
        using var t = TestDb.Create();
        var mine = Guid.NewGuid();
        var service = Service(t);
        var theirs = new NewsletterTemplate { TenantId = Guid.NewGuid(), Name = "Theirs", Html = MinimalHtml };
        await service.SaveAsync(theirs);

        var recipe = new Recipe { TenantId = mine, Name = "R", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = theirs.Id };
        t.Db.Recipes.Add(recipe);
        var post = new Post { TenantId = mine, PlatformId = Guid.NewGuid(), RecipeId = recipe.Id,
            Kind = "Newsletter", Title = "Issue" };
        t.Db.Posts.Add(post);
        await t.Db.SaveChangesAsync();

        Assert.Null(await service.ResolveForPostAsync(post.Id));
    }

    [Fact]
    public async Task With_no_template_anywhere_resolution_returns_null()
    {
        using var t = TestDb.Create();
        var post = new Post { TenantId = Guid.NewGuid(), PlatformId = Guid.NewGuid(),
            Kind = "Newsletter", Title = "Issue" };
        t.Db.Posts.Add(post);
        await t.Db.SaveChangesAsync();

        Assert.Null(await Service(t).ResolveForPostAsync(post.Id));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter NewsletterTemplateServiceTests
```

Expected: FAIL to compile — `NewsletterTemplateService` does not exist.

- [ ] **Step 3: Write the service**

Create `src/ContentAutomatorX.Application/Services/NewsletterTemplateService.cs`:

```csharp
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public class NewsletterTemplateService(IAppDbContext db)
{
    public async Task<List<NewsletterTemplate>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
        await db.NewsletterTemplates.Where(t => t.TenantId == tenantId)
            .OrderBy(t => t.Name).ToListAsync(ct);

    public Task<NewsletterTemplate?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.NewsletterTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <summary>Creates or updates. Validation runs here rather than in the UI so a template can
    /// never reach the database in a state that would send an email with no unsubscribe link.</summary>
    public async Task SaveAsync(NewsletterTemplate template, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(template.Name))
            throw new InvalidOperationException("Give the template a name.");

        var issues = TemplateValidator.Validate(template.Html);
        if (TemplateValidator.HasErrors(issues))
            throw new InvalidOperationException("Fix the template errors first: "
                + string.Join(" ", issues.Where(i => i.Level == TemplateIssueLevel.Error).Select(i => i.Message)));

        var existing = await db.NewsletterTemplates.FirstOrDefaultAsync(t => t.Id == template.Id, ct);
        if (existing is null)
        {
            template.UpdatedAt = DateTimeOffset.UtcNow;
            db.NewsletterTemplates.Add(template);
        }
        else
        {
            existing.Name = template.Name;
            existing.Html = template.Html;
            existing.IsDefault = template.IsDefault;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // At most one default per tenant, enforced here because the EF SQLite provider has no
        // filtered unique index and this service is the only writer.
        if (template.IsDefault)
            foreach (var other in await db.NewsletterTemplates
                         .Where(t => t.TenantId == template.TenantId && t.Id != template.Id).ToListAsync(ct))
                other.IsDefault = false;

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.NewsletterTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return;

        // No FK from Recipe, so nothing clears these for us. Left dangling they would fall through
        // to the tenant default anyway, but an explicit null is honest about what happened.
        foreach (var recipe in await db.Recipes.Where(r => r.NewsletterTemplateId == id).ToListAsync(ct))
            recipe.NewsletterTemplateId = null;

        db.NewsletterTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Which template an issue renders with: the recipe's, else the tenant's default,
    /// else none — in which case the caller uses the built-in renderer. A dangling or cross-tenant
    /// id falls through rather than failing.</summary>
    public async Task<NewsletterTemplate?> ResolveForPostAsync(Guid postId, CancellationToken ct = default)
    {
        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, ct);
        if (post is null) return null;

        if (post.RecipeId is Guid recipeId)
        {
            var recipe = await db.Recipes.FirstOrDefaultAsync(r => r.Id == recipeId, ct);
            if (recipe?.NewsletterTemplateId is Guid templateId)
            {
                var chosen = await db.NewsletterTemplates
                    .FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == post.TenantId, ct);
                if (chosen is not null) return chosen;
            }
        }

        return await db.NewsletterTemplates
            .FirstOrDefaultAsync(t => t.TenantId == post.TenantId && t.IsDefault, ct);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```
dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter NewsletterTemplateServiceTests
```

Expected: PASS, 11 test cases.

- [ ] **Step 5: Wire the renderer into the two render call sites**

Both services gain the new dependency. In `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`, change the primary constructor to add it:

```csharp
public class IssueComposerService(IAppDbContext db, ILlmBackend llm, PostService posts,
    ILlmSettingsProvider llmSettings, IssueHistoryService history, NewsletterTemplateService templates)
```

and replace the body of `RenderPreviewAsync` (currently lines 145–152) with:

```csharp
    public async Task<string> RenderPreviewAsync(Guid postId, string title, CancellationToken ct = default)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        var sections = await GetSectionsAsync(postId, ct);
        var template = await templates.ResolveForPostAsync(postId, ct);
        var html = template is null
            ? SectionHtmlRenderer.Render(sections, tenant, title)
            : TemplateHtmlRenderer.Render(sections, tenant, title, template.Html, post.CreatedAt);
        return html.Replace(SectionHtmlRenderer.UnsubscribeToken, "#");
    }
```

In `src/ContentAutomatorX.Application/Services/PostService.cs`, add `NewsletterTemplateService templates` to its primary constructor parameter list, and replace the `if (sections.Count > 0)` branch at lines 227–232 with:

```csharp
        if (sections.Count > 0)
        {
            var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
            var template = await templates.ResolveForPostAsync(post.Id, ct);
            html = (template is null
                    ? SectionHtmlRenderer.Render(sections, tenant, post.Title)
                    : TemplateHtmlRenderer.Render(sections, tenant, post.Title, template.Html, post.CreatedAt))
                .Replace(SectionHtmlRenderer.UnsubscribeToken, "{$unsubscribe}"); // MailerLite's variable
        }
```

Add `using ContentAutomatorX.Application.Newsletter;` to `PostService.cs` if it is not already there.

- [ ] **Step 6: Register the service**

In `src/ContentAutomatorX.Web/Program.cs`, beside the other scoped Application services:

```csharp
builder.Services.AddScoped<NewsletterTemplateService>();
```

- [ ] **Step 7: Run the full suite**

```
dotnet test
```

Expected: all green. If `PostServiceTests` or `IssueComposerServiceTests` fail to compile, they construct these services directly — add the new `NewsletterTemplateService` argument at each construction site.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat(templates): template service, resolution order, and render wiring"
```

---

## Task 7: Video sections and category through the composer

**Spec:** §3.3, §9 (section cards, add-section menu, undo).

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`, `src/ContentAutomatorX.Application/Services/IssueHistoryService.cs`, `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`, `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `SectionTypes.Video`, `IssueSection.Category`, `IYouTubeThumbnailResolver`.
- Produces: `IssueComposerService.UpdateSectionAsync(Guid sectionId, string? title, string? bodyMd, string? imageUrl, string? linkUrl, string? linkText, string? category, CancellationToken)`; `SectionCard.SectionEdit(string? Title, string? BodyMd, string? ImageUrl, string? LinkUrl, string? LinkText, string? Category)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs`:

```csharp
    [Fact]
    public async Task Category_round_trips_through_update()
    {
        using var t = TestDb.Create();
        var (composer, postId) = await NewIssueAsync(t);
        var section = await composer.AddSectionAsync(postId, SectionTypes.Topic);

        await composer.UpdateSectionAsync(section.Id, "Title", "Body", null, null, null, "Tutorial");

        Assert.Equal("Tutorial", (await t.Db.IssueSections.SingleAsync(s => s.Id == section.Id)).Category);
    }

    [Fact]
    public async Task Undo_restores_the_category()
    {
        using var t = TestDb.Create();
        var (composer, postId) = await NewIssueAsync(t);
        var history = new IssueHistoryService(t.Db);
        var section = await composer.AddSectionAsync(postId, SectionTypes.Topic);

        await composer.UpdateSectionAsync(section.Id, "T", "B", null, null, null, "Tutorial");
        await composer.UpdateSectionAsync(section.Id, "T", "B", null, null, null, "News");
        await history.UndoAsync(postId);

        Assert.Equal("Tutorial", (await t.Db.IssueSections.SingleAsync(s => s.Id == section.Id)).Category);
    }

    [Fact]
    public async Task A_video_section_can_be_added_and_edited()
    {
        using var t = TestDb.Create();
        var (composer, postId) = await NewIssueAsync(t);

        var section = await composer.AddSectionAsync(postId, SectionTypes.Video);
        await composer.UpdateSectionAsync(section.Id, "The build", "42 minutes.", null,
            "https://youtu.be/dQw4w9WgXcQ", "Watch the build →", null);

        var stored = await t.Db.IssueSections.SingleAsync(s => s.Id == section.Id);
        Assert.Equal(SectionTypes.Video, stored.Type);
        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", stored.LinkUrl);
    }
```

If `IssueComposerServiceTests` has no `NewIssueAsync` helper, add one that creates a tenant, a post and calls `EnsureSectionsAsync`, matching whatever the file's existing tests already do to set up an issue — reuse that setup rather than writing a second one.

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter IssueComposerServiceTests
```

Expected: FAIL to compile — `UpdateSectionAsync` takes six parameters, not seven.

- [ ] **Step 3: Add category to the update path**

In `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`, replace `UpdateSectionAsync` (lines 92–103) with:

```csharp
    public async Task UpdateSectionAsync(Guid sectionId, string? title, string? bodyMd,
        string? imageUrl, string? linkUrl, string? linkText, string? category,
        CancellationToken ct = default)
    {
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        await history.SnapshotAsync(section.PostId, "Edit section", ct);
        section.Title = title;
        section.BodyMd = bodyMd;
        section.ImageUrl = imageUrl;
        section.LinkUrl = linkUrl;
        section.LinkText = linkText;
        section.Category = category;
        await db.SaveChangesAsync(ct);
    }
```

- [ ] **Step 4: Add category to the undo snapshot — all three places**

This is the step the spec calls out specifically: `SectionSnapshot` is an explicit field list, so missing any one of these silently drops the category on every undo.

In `src/ContentAutomatorX.Application/Services/IssueHistoryService.cs`, change the record (line 11):

```csharp
internal record SectionSnapshot(Guid Id, int Position, string Type, string? Title, string? BodyMd,
    string? ImageUrl, string? LinkUrl, string? LinkText, Guid? SourceItemId, string? Category);
```

In `CaptureAsync`, change the projection:

```csharp
            sections.Select(s => new SectionSnapshot(s.Id, s.Position, s.Type, s.Title, s.BodyMd,
                s.ImageUrl, s.LinkUrl, s.LinkText, s.SourceItemId, s.Category)).ToList());
```

In `RestoreAsync`, add to the assignment loop after `section.SourceItemId = want.SourceItemId;`:

```csharp
            section.Category = want.Category;
```

Older revisions stored before this change deserialize with `Category` as null, which restores a null category — correct, because those revisions were taken when no section had one.

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test tests/ContentAutomatorX.IntegrationTests/ContentAutomatorX.IntegrationTests.csproj --filter IssueComposerServiceTests
```

Expected: PASS.

- [ ] **Step 6: Verify the undo test actually guards the fix**

Revert only the `RestoreAsync` line added in Step 4, re-run `Undo_restores_the_category`, and confirm it FAILS with `Assert.Equal() Failure: Values differ — Expected: "Tutorial", Actual: "News"`. Then restore the line. A guard test that passes with the fix reverted is not a guard test.

- [ ] **Step 7: Update the section card**

In `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`, change the `SectionEdit` record (line 85):

```csharp
    public record SectionEdit(string? Title, string? BodyMd, string? ImageUrl, string? LinkUrl,
        string? LinkText, string? Category);
```

Add the backing field to line 101:

```csharp
    private string _title = "", _body = "", _image = "", _link = "", _linkText = "", _category = "";
```

Update `ToggleExpand`:

```csharp
    private void ToggleExpand()
    {
        _expanded = !_expanded;
        if (_expanded)
            (_title, _body, _image, _link, _linkText, _category) = (Section.Title ?? "",
                Section.BodyMd ?? "", Section.ImageUrl ?? "", Section.LinkUrl ?? "",
                Section.LinkText ?? "", Section.Category ?? "");
    }
```

Update `Apply`:

```csharp
    private async Task Apply()
    {
        await OnApply.InvokeAsync(new SectionEdit(NullIfEmpty(_title), NullIfEmpty(_body),
            NullIfEmpty(_image), NullIfEmpty(_link), NullIfEmpty(_linkText), NullIfEmpty(_category)));
        _expanded = false;
    }
```

Add a category field to the expanded editor, immediately after the title field's `@if (HasTitle())` block:

```razor
            @if (HasCategory())
            {
                <MudTextField @bind-Value="_category" Label="Category (e.g. Tutorial, News)" />
            }
```

Teach the predicates about Video, and add the category one:

```csharp
    private bool HasTitle() => Section.Type is SectionTypes.Topic or SectionTypes.Sponsor or SectionTypes.Video;
    private bool HasCategory() => Section.Type is SectionTypes.Topic;
    private bool HasBody() => Section.Type is not (SectionTypes.Button or SectionTypes.Divider);
    private bool HasImage() => Section.Type is SectionTypes.Topic or SectionTypes.Sponsor or SectionTypes.Video;
    private bool HasLink() => Section.Type is SectionTypes.Topic or SectionTypes.Sponsor
        or SectionTypes.Button or SectionTypes.Video;
    private bool HasLinkText() => Section.Type is SectionTypes.Sponsor or SectionTypes.Button or SectionTypes.Video;
```

The image and link labels need Video-specific wording. Replace those two `MudTextField` declarations:

```razor
            @if (HasImage())
            {
                <MudTextField @bind-Value="_image" Label="@ImageLabel()" />
            }
            @if (HasLink())
            {
                <MudTextField @bind-Value="_link" Label="@LinkLabel()" />
            }
```

with these helpers added to `@code`:

```csharp
    private string ImageLabel() => Section.Type switch
    {
        SectionTypes.Sponsor => "Logo URL (https)",
        SectionTypes.Video => "Thumbnail URL (optional — derived from the video when blank)",
        _ => "Image URL (https)"
    };

    private string LinkLabel() => Section.Type == SectionTypes.Video
        ? "YouTube URL" : "Link URL (https)";
```

Add Video to `Label()` and `TypeIcon()`:

```csharp
        SectionTypes.Video => $"Video: {Section.Title ?? "(set title)"}",
```
```csharp
        SectionTypes.Video => Icons.Material.Filled.PlayCircleOutline,
```

- [ ] **Step 8: Wire the page**

In `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`, find the add-section menu (it lists Topic, Sponsor, Button, Divider) and add a Video entry alongside them, following the existing item's exact markup. Then find the handler that receives `SectionCard.SectionEdit` and pass the new field through — it currently calls `UpdateSectionAsync` with five values plus the id:

```csharp
        await composer.UpdateSectionAsync(sectionId, edit.Title, edit.BodyMd, edit.ImageUrl,
            edit.LinkUrl, edit.LinkText, edit.Category, ct);
```

- [ ] **Step 9: Build and run the full suite**

```
dotnet build && dotnet test
```

Expected: all green.

- [ ] **Step 10: Commit**

```bash
git add src tests
git commit -m "feat(templates): video sections and topic categories through the composer"
```

---

## Task 8: Category in the chat contract

**Spec:** §9 (chat edit contract, structural lock, prompts).

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/ChatReplyParser.cs`, `src/ContentAutomatorX.Application/Services/IssueChatService.cs`, `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`, `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`
- Test: `tests/ContentAutomatorX.UnitTests/ChatReplyParserTests.cs` (extend), `tests/ContentAutomatorX.IntegrationTests/IssueChatServiceTests.cs` (extend)

**Interfaces:**
- Consumes: `IssueSectionProposal.ProposedCategory` / `.BaselineCategory` (Task 1).
- Produces: `ChatEdit(Guid SectionId, string? Title, string? BodyMd, string? Category)`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ContentAutomatorX.UnitTests/ChatReplyParserTests.cs`:

```csharp
    [Fact]
    public void Parses_a_category_edit()
    {
        var id = Guid.NewGuid();
        var json = $$"""{"reply":"ok","edits":[{"sectionId":"{{id}}","category":"Tutorial"}]}""";

        Assert.True(ChatReplyParser.TryParse(json, out var reply));
        var edit = Assert.Single(reply!.Edits);
        Assert.Equal("Tutorial", edit.Category);
        Assert.Null(edit.Title);
        Assert.Null(edit.BodyMd);
    }

    [Fact]
    public void An_edit_carrying_only_a_category_is_kept_not_dropped()
    {
        var json = $$"""{"reply":"ok","edits":[{"sectionId":"{{Guid.NewGuid()}}","category":"News"}]}""";
        Assert.True(ChatReplyParser.TryParse(json, out var reply));
        Assert.Single(reply!.Edits);
        Assert.Equal(0, reply.DroppedEdits);
    }

    [Fact]
    public void An_edit_with_no_usable_field_at_all_is_still_dropped()
    {
        var json = $$"""{"reply":"ok","edits":[{"sectionId":"{{Guid.NewGuid()}}"}]}""";
        Assert.True(ChatReplyParser.TryParse(json, out var reply));
        Assert.Empty(reply!.Edits);
        Assert.Equal(1, reply.DroppedEdits);
    }
```

Append to `tests/ContentAutomatorX.IntegrationTests/IssueChatServiceTests.cs`:

```csharp
    [Fact]
    public async Task Accepting_a_category_proposal_writes_it_to_the_section()
    {
        using var t = TestDb.Create();
        var (chat, postId, sectionId) = await ChatWithOneTopicAsync(t, """
            {"reply":"Relabelled.","edits":[{"sectionId":"SECTION","category":"Tutorial"}]}
            """);

        await chat.SendAsync(postId, "Give this a category.");
        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal("Tutorial", proposal.ProposedCategory);

        await chat.AcceptAsync(proposal.Id, force: false);
        Assert.Equal("Tutorial", (await t.Db.IssueSections.SingleAsync(s => s.Id == sectionId)).Category);
    }

    [Fact]
    public async Task A_category_proposal_goes_stale_when_the_category_changes_underneath_it()
    {
        using var t = TestDb.Create();
        var (chat, postId, sectionId) = await ChatWithOneTopicAsync(t, """
            {"reply":"Relabelled.","edits":[{"sectionId":"SECTION","category":"Tutorial"}]}
            """);

        await chat.SendAsync(postId, "Give this a category.");
        var section = await t.Db.IssueSections.SingleAsync(s => s.Id == sectionId);
        section.Category = "Changed by hand";
        await t.Db.SaveChangesAsync();

        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        await Assert.ThrowsAsync<StaleProposalException>(() => chat.AcceptAsync(proposal.Id, force: false));
    }

    [Fact]
    public async Task A_body_only_proposal_is_not_stale_because_the_category_changed()
    {
        using var t = TestDb.Create();
        var (chat, postId, sectionId) = await ChatWithOneTopicAsync(t, """
            {"reply":"Rewrote it.","edits":[{"sectionId":"SECTION","bodyMd":"New body."}]}
            """);

        await chat.SendAsync(postId, "Rewrite this.");
        var section = await t.Db.IssueSections.SingleAsync(s => s.Id == sectionId);
        section.Category = "Tutorial";
        await t.Db.SaveChangesAsync();

        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        await chat.AcceptAsync(proposal.Id, force: false);   // must not throw
        Assert.Equal("New body.", (await t.Db.IssueSections.SingleAsync(s => s.Id == sectionId)).BodyMd);
    }
```

`ChatWithOneTopicAsync` is a helper you add to that file if it does not already exist: build a tenant, a post, a header/topic/footer section set, a `FakeLlm` returning the given reply with `SECTION` replaced by the topic's real id, and return the service plus both ids. Follow whatever the file's existing chat tests already do for `FakeLlm` construction rather than inventing a second pattern.

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test --filter "ChatReplyParserTests|IssueChatServiceTests"
```

Expected: FAIL to compile — `ChatEdit` has no `Category`.

- [ ] **Step 3: Extend the parser contract**

In `src/ContentAutomatorX.Application/Services/ChatReplyParser.cs`:

```csharp
public record ChatEdit(Guid SectionId, string? Title, string? BodyMd, string? Category);
```
```csharp
    private record RawEdit(string? SectionId, string? Title, string? BodyMd, string? Category);
```

and in `TryParse`, widen the "has a usable field" check and the construction:

```csharp
                var hasField = !string.IsNullOrWhiteSpace(edit.Title)
                    || !string.IsNullOrWhiteSpace(edit.BodyMd)
                    || !string.IsNullOrWhiteSpace(edit.Category);
```
```csharp
                edits.Add(new ChatEdit(sectionId, NullIfBlank(edit.Title), NullIfBlank(edit.BodyMd),
                    NullIfBlank(edit.Category)));
```

`RawEdit.Category` stays `string?` for the same reason `SectionId` does: a typed property that throws inside `Deserialize` loses every other edit in the same reply.

- [ ] **Step 4: Extend the chat service**

In `src/ContentAutomatorX.Application/Services/IssueChatService.cs`, four changes.

The per-field merge — a reply naming one section twice must merge all three fields, or the unique index rejects the second insert and the `DbUpdateException` takes down the whole turn:

```csharp
        var merged = edits.GroupBy(e => e.SectionId)
            .Select(g => new ChatEdit(g.Key,
                g.Select(e => e.Title).LastOrDefault(t => t is not null),
                g.Select(e => e.BodyMd).LastOrDefault(b => b is not null),
                g.Select(e => e.Category).LastOrDefault(c => c is not null)))
            .ToList();
```

The proposal it writes:

```csharp
            db.IssueSectionProposals.Add(new IssueSectionProposal
            {
                PostId = postId, SectionId = section.Id,
                ProposedTitle = edit.Title, ProposedBodyMd = edit.BodyMd,
                ProposedCategory = edit.Category,
                BaselineBodyMd = section.BodyMd ?? "", BaselineTitle = section.Title,
                BaselineCategory = section.Category
            });
```

Staleness — stale only in the fields this proposal actually writes:

```csharp
    private static bool IsStale(IssueSection section, IssueSectionProposal proposal) =>
        (proposal.ProposedBodyMd is not null && (section.BodyMd ?? "") != proposal.BaselineBodyMd)
        || (proposal.ProposedTitle is not null && (section.Title ?? "") != (proposal.BaselineTitle ?? ""))
        || (proposal.ProposedCategory is not null && (section.Category ?? "") != (proposal.BaselineCategory ?? ""));
```

Accept:

```csharp
        if (proposal.ProposedTitle is not null) section.Title = proposal.ProposedTitle;
        if (proposal.ProposedBodyMd is not null) section.BodyMd = proposal.ProposedBodyMd;
        if (proposal.ProposedCategory is not null) section.Category = proposal.ProposedCategory;
```

- [ ] **Step 5: Teach the prompt about categories**

In `BuildPrompt`, change the contract lines so the model knows the field exists and that types are off-limits:

```csharp
        sb.AppendLine("You may rewrite the title, body and category of the sections listed below.");
        sb.AppendLine("You may NOT add sections, delete sections, change their order, or change a section's type.");
```

and the JSON shape line:

```csharp
        sb.AppendLine("""{"reply":"what you want to say","edits":[{"sectionId":"<id>","title":"...","bodyMd":"...","category":"..."}]}""");
        sb.AppendLine("category is a short label like Tutorial or News, and applies to topic sections only.");
        sb.AppendLine("Omit any field to leave it unchanged. Use an empty edits array to just answer.");
```

In the issue listing loop, show the current category so the model can see what it is changing:

```csharp
            if (!string.IsNullOrWhiteSpace(section.Category)) sb.AppendLine($"Category: {section.Category}");
```

The structural lock is still enforced by the id whitelist in `StoreProposalsAsync`, not by this wording. `Type` is absent from `ChatEdit`, so a Topic cannot become a Video no matter what the model emits.

- [ ] **Step 6: Ask the topic generator for a category**

In `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`, extend the blurb record and prompt. Change `TopicBlurb`:

```csharp
public record TopicBlurb(Guid ItemId, string Title, string Blurb, string? Category);
```

In `BuildTopicsPrompt`, replace the response-shape line:

```csharp
        sb.AppendLine("""Respond with ONLY a JSON array, no prose, no markdown fences: [{"itemId":"<id>","title":"...","blurb":"...","category":"..."}]""");
        sb.AppendLine("category is a one- or two-word label for the piece, such as Tutorial, News or Release.");
```

In `GenerateTopicsAsync`, inside the fill loop after the title assignment:

```csharp
            if (!string.IsNullOrWhiteSpace(topic.Category)) section.Category = topic.Category;
```

`TryParseTopics` needs no change — it validates `ItemId` and `Blurb` only, and a missing `category` deserializes to null.

- [ ] **Step 7: Show the proposed category on the card**

In `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`, inside the `@if (Proposal is not null)` diff panel, after the Proposed body block:

```razor
            @if (Proposal.ProposedCategory is string c)
            {
                <MudDivider Class="my-2" />
                <MudText Typo="Typo.overline">Category</MudText>
                <MudText Typo="Typo.body2">@(Section.Category ?? "(none)") → @c</MudText>
            }
```

- [ ] **Step 8: Run the tests to verify they pass**

```
dotnet test
```

Expected: all green.

- [ ] **Step 9: Verify the staleness test guards the fix**

Revert only the third clause added to `IsStale` in Step 4, re-run `A_category_proposal_goes_stale_when_the_category_changes_underneath_it`, and confirm it FAILS with `Assert.Throws() Failure: No exception was thrown`. Restore the clause.

- [ ] **Step 10: Commit**

```bash
git add src tests
git commit -m "feat(templates): category through the chat contract, proposals and prompts"
```

---

## Task 9: The template editor

**Spec:** §7.

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Shared/TemplateEditorDialog.razor`
- Modify: `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor`

**Interfaces:**
- Consumes: `NewsletterTemplateService`, `TemplateValidator.Validate`, `TemplateHtmlRenderer.Render`, `SampleIssue.Sections`, `SampleIssue.Tenant`.

- [ ] **Step 1: Write the dialog**

Create `src/ContentAutomatorX.Web/Components/Shared/TemplateEditorDialog.razor`:

```razor
@using ContentAutomatorX.Application.Newsletter
@using ContentAutomatorX.Application.Services
@using ContentAutomatorX.Domain.Entities
@using Microsoft.AspNetCore.Components.Forms
@inject NewsletterTemplateService Templates
@inject ISnackbar Snackbar

<MudDialog>
    <TitleContent>
        <div class="d-flex align-center" style="gap:8px">
            <MudTextField @bind-Value="_name" Label="Template name" Margin="Margin.Dense" Class="flex-grow-1" />
            <MudSwitch @bind-Value="_isDefault" Color="Color.Primary" Label="Tenant default" />
        </div>
    </TitleContent>
    <DialogContent>
        <div class="d-flex" style="gap:12px; height:65vh">
            <div class="d-flex flex-column" style="flex:1 1 50%; min-width:0">
                <div class="d-flex align-center mb-2" style="gap:8px">
                    <MudFileUpload T="IBrowserFile" Accept=".html,.htm" FilesChanged="UploadAsync">
                        <ActivatorContent>
                            <MudButton Variant="Variant.Outlined" Size="Size.Small"
                                       StartIcon="@Icons.Material.Filled.UploadFile">Upload .html</MudButton>
                        </ActivatorContent>
                    </MudFileUpload>
                    <MudText Typo="Typo.caption">@(_html.Length / 1024) KB</MudText>
                </div>
                <MudTextField @bind-Value="_html" @bind-Value:after="OnHtmlChanged" Lines="24"
                              Variant="Variant.Outlined" Style="font-family:monospace;font-size:12px"
                              Immediate="true" DebounceInterval="400" />
            </div>
            <div class="d-flex flex-column" style="flex:1 1 50%; min-width:0">
                <MudText Typo="Typo.caption" Class="mb-2">Preview — sample issue</MudText>
                <iframe sandbox srcdoc="@_preview" title="Template preview"
                        style="flex:1;width:100%;border:1px solid var(--mud-palette-lines-default);background:#fff"></iframe>
            </div>
        </div>
        @if (_issues.Count > 0)
        {
            <div class="mt-2" style="max-height:18vh;overflow:auto">
                @foreach (var issue in _issues)
                {
                    <MudText Typo="Typo.caption" Class="d-block"
                             Color="@(issue.Level == TemplateIssueLevel.Error ? Color.Error : Color.Warning)">
                        @(issue.Level == TemplateIssueLevel.Error ? "✗" : "⚠") line @issue.Line — @issue.Message
                    </MudText>
                }
            </div>
        }
    </DialogContent>
    <DialogActions>
        @if (_template.Id != Guid.Empty)
        {
            <MudButton Color="Color.Error" Disabled="@_busy" OnClick="DeleteAsync">Delete</MudButton>
        }
        <MudSpacer />
        <MudButton OnClick="@(() => Dialog.Cancel())">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   Disabled="@(_busy || _hasErrors)" OnClick="SaveAsync">Save</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter, EditorRequired] public NewsletterTemplate Template { get; set; } = default!;

    private NewsletterTemplate _template = default!;
    private string _name = "", _html = "", _preview = "";
    private bool _isDefault, _busy, _hasErrors;
    private IReadOnlyList<TemplateIssue> _issues = [];

    protected override void OnInitialized()
    {
        _template = Template;
        _name = _template.Name;
        _html = _template.Html;
        _isDefault = _template.IsDefault;
        OnHtmlChanged();
    }

    /// <summary>Never throws: a broken template must leave the editor usable, and there is no
    /// ErrorBoundary in this app — an exception here would tear down the circuit.</summary>
    private void OnHtmlChanged()
    {
        try
        {
            _issues = TemplateValidator.Validate(_html);
            _hasErrors = TemplateValidator.HasErrors(_issues);
            var rendered = TemplateHtmlRenderer.Render(SampleIssue.Sections, SampleIssue.Tenant,
                "Signals from the latent space", _html, DateTimeOffset.UtcNow);
            // Keep the last good preview when the new one is empty (no shell yet, mid-typing).
            if (!string.IsNullOrWhiteSpace(rendered))
                _preview = rendered.Replace(SectionHtmlRenderer.UnsubscribeToken, "#");
        }
        catch (Exception ex)
        {
            _issues = [new TemplateIssue(TemplateIssueLevel.Error, 1, $"Preview failed: {ex.Message}")];
            _hasErrors = true;
        }
    }

    private async Task UploadAsync(IBrowserFile? file)
    {
        if (file is null) return;
        try
        {
            if (file.Size > TemplateValidator.MaxBytes)
            {
                Snackbar.Add($"That file is larger than {TemplateValidator.MaxBytes / 1024} KB.", Severity.Error);
                return;
            }
            using var stream = file.OpenReadStream(TemplateValidator.MaxBytes);
            using var reader = new StreamReader(stream);
            _html = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(_name)) _name = Path.GetFileNameWithoutExtension(file.Name);
            OnHtmlChanged();
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Could not read that file: {ex.Message}", Severity.Error);
        }
    }

    private async Task SaveAsync()
    {
        _busy = true;
        try
        {
            _template.Name = _name;
            _template.Html = _html;
            _template.IsDefault = _isDefault;
            await Templates.SaveAsync(_template);
            Snackbar.Add("Template saved.", Severity.Success);
            Dialog.Close(DialogResult.Ok(_template));
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _busy = false; }
    }

    private async Task DeleteAsync()
    {
        _busy = true;
        try
        {
            await Templates.DeleteAsync(_template.Id);
            Snackbar.Add("Template deleted.", Severity.Success);
            Dialog.Close(DialogResult.Ok((NewsletterTemplate?)null));
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _busy = false; }
    }
}
```

Two MudBlazor details that will bite if changed: `IMudDialogInstance` is the cascading type in MudBlazor 9 (not `MudDialogInstance`), and `sandbox` with no value is the most restrictive setting — do not add `allow-scripts`, which would let a template's script reach the Blazor circuit.

- [ ] **Step 2: Add the row to the recipe form**

In `src/ContentAutomatorX.Web/Components/Pages/Recipes.razor`, immediately after the target-platform `MudSelect` that closes around line 75, add:

```razor
        <div class="d-flex align-center" style="gap:8px">
            <MudSelect T="Guid?" @bind-Value="_newsletterTemplateId" Label="Newsletter template" Class="flex-grow-1">
                <MudSelectItem T="Guid?" Value="@((Guid?)null)">Built-in design</MudSelectItem>
                @foreach (var nt in _newsletterTemplates)
                {
                    <MudSelectItem T="Guid?" Value="@((Guid?)nt.Id)">@nt.Name@(nt.IsDefault ? " (default)" : "")</MudSelectItem>
                }
            </MudSelect>
            <MudButton Variant="Variant.Outlined" Size="Size.Small" Disabled="@(_newsletterTemplateId is null)"
                       OnClick="EditTemplateAsync">Edit</MudButton>
            <MudButton Variant="Variant.Outlined" Size="Size.Small" OnClick="NewTemplateAsync">New</MudButton>
        </div>
```

Add to `@code`, and add `@inject IDialogService DialogService` plus `@inject NewsletterTemplateService NewsletterTemplates` at the top of the file if they are not already injected:

```csharp
    private Guid? _newsletterTemplateId;
    private List<NewsletterTemplate> _newsletterTemplates = [];

    private const string StarterTemplate = """
        <!-- BLOCK: shell -->
        <!DOCTYPE html>
        <html><head><meta charset="utf-8"><title>{{issue_title}}</title></head>
        <body style="margin:0;background:#EDEEF3;">
          <div style="display:none;max-height:0;overflow:hidden;">{{preheader}}</div>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:24px 12px;">
          <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;">
          {{sections}}
          <tr><td style="padding:24px;font:12px sans-serif;color:#888;">
            <a href="{{unsubscribe_url}}" style="color:#888;">Unsubscribe</a>
          </td></tr>
          </table></td></tr></table>
        </body></html>
        <!-- /BLOCK -->

        <!-- BLOCK: header -->
        <tr><td style="padding:24px 24px 0;font:700 28px sans-serif;">{{title}}</td></tr>
        <tr><td style="padding:8px 24px 0;font:16px/1.6 sans-serif;">{{body_html}}</td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: topic -->
        <tr><td style="padding:24px 24px 0;">
          <!-- IF: image --><img src="{{image_url}}" alt="{{title}}" width="536" style="display:block;width:100%;max-width:536px;height:auto;" /><!-- /IF -->
          <!-- IF: category --><p style="margin:12px 0 0;font:11px monospace;color:#8A8FA0;text-transform:uppercase;">{{category}} &middot; {{reading_time}}</p><!-- /IF -->
          <p style="margin:8px 0 0;font:700 21px sans-serif;">{{title}}</p>
          <div style="font:15px/1.6 sans-serif;color:#5A5E70;">{{body_html}}</div>
          <!-- IF: link --><p style="margin:10px 0 0;"><a href="{{link_url}}" style="color:{{accent}};font:600 14px sans-serif;text-decoration:none;">{{link_text}}</a></p><!-- /IF -->
        </td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: video -->
        <tr><td style="padding:24px 24px 0;">
          <!-- IF: thumbnail --><a href="{{video_url}}"><img src="{{thumbnail_url}}" alt="{{title}}" width="536" style="display:block;width:100%;max-width:536px;height:auto;" /></a><!-- /IF -->
          <p style="margin:12px 0 0;font:700 18px sans-serif;">{{title}}</p>
          <div style="font:14px/1.6 sans-serif;color:#5A5E70;">{{body_html}}</div>
          <p style="margin:10px 0 0;"><a href="{{video_url}}" style="color:{{accent}};font:600 14px sans-serif;text-decoration:none;">{{link_text}}</a></p>
        </td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: sponsor -->
        <tr><td style="padding:24px 24px 0;">
          <table role="presentation" width="100%" style="background:#F6F7FA;border:1px solid #E4E6ED;"><tr><td style="padding:20px;">
            <p style="margin:0;font:700 10px monospace;letter-spacing:1.1px;color:#5A5E70;">ADVERTISEMENT</p>
            <p style="margin:12px 0 0;font:700 18px sans-serif;">{{title}}</p>
            <div style="font:15px/1.6 sans-serif;color:#5A5E70;">{{body_html}}</div>
            <!-- IF: link --><p style="margin:10px 0 0;"><a href="{{link_url}}" style="color:{{accent}};text-decoration:none;">{{link_text}}</a></p><!-- /IF -->
            <p style="margin:14px 0 0;font:italic 12px sans-serif;color:#8A8FA0;">This section is paid promotion.</p>
          </td></tr></table>
        </td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: button -->
        <tr><td style="padding:24px 24px 0;">
          <table role="presentation" cellpadding="0" cellspacing="0"><tr>
            <td align="center" bgcolor="{{accent}}" style="border-radius:8px;">
              <a href="{{link_url}}" style="display:inline-block;padding:12px 24px;font:700 14px sans-serif;color:#090915;text-decoration:none;">{{link_text}}</a>
            </td>
          </tr></table>
        </td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: divider -->
        <tr><td style="padding:24px 24px 0;"><table role="presentation" width="100%"><tr><td style="border-top:1px solid #E4E6ED;font-size:0;line-height:0;">&nbsp;</td></tr></table></td></tr>
        <!-- /BLOCK -->

        <!-- BLOCK: footer -->
        <tr><td style="padding:24px;font:12px/1.6 sans-serif;color:#6B7085;">
          <div>{{body_html}}</div>
          <p style="margin:12px 0 0;">{{sender_identity}}</p>
        </td></tr>
        <!-- /BLOCK -->
        """;

    private async Task LoadTemplatesAsync()
    {
        if (Tenant.Active is null) return;
        _newsletterTemplates = await NewsletterTemplates.ListAsync(Tenant.Active.Id);
    }

    private Task EditTemplateAsync() =>
        _newsletterTemplateId is Guid id
            ? OpenTemplateAsync(_newsletterTemplates.FirstOrDefault(t => t.Id == id))
            : Task.CompletedTask;

    private Task NewTemplateAsync() => OpenTemplateAsync(new NewsletterTemplate
    {
        TenantId = Tenant.Active!.Id, Name = "", Html = StarterTemplate,
        IsDefault = _newsletterTemplates.Count == 0
    });

    private async Task OpenTemplateAsync(NewsletterTemplate? template)
    {
        if (template is null) return;
        try
        {
            var parameters = new DialogParameters<TemplateEditorDialog> { { x => x.Template, template } };
            var options = new DialogOptions { FullScreen = true, CloseButton = true };
            var dialog = await DialogService.ShowAsync<TemplateEditorDialog>("Newsletter template", parameters, options);
            var result = await dialog.Result;
            if (result is null || result.Canceled) return;

            await LoadTemplatesAsync();
            // Data is a NewsletterTemplate on save, null on delete.
            _newsletterTemplateId = result.Data is NewsletterTemplate saved ? saved.Id : null;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Template editor failed: {ex.Message}", Severity.Error);
        }
    }
```

Call `await LoadTemplatesAsync();` from wherever the page already loads its recipes and platforms in `OnInitializedAsync`, and from the tenant-changed handler if one exists.

Then thread the value through the two save paths. In `Edit(Recipe r)`, beside the existing `_targetPlatformId = r.TargetPlatformId;`:

```csharp
        _newsletterTemplateId = r.NewsletterTemplateId;
```

In the reset that runs after saving (`_editing = null; _template = null;`):

```csharp
        _newsletterTemplateId = null;
```

In `Save()`, in the create branch beside `TargetPlatformId = _targetPlatformId`:

```csharp
                NewsletterTemplateId = _newsletterTemplateId
```

and in the update branch beside `_editing.TargetPlatformId = _targetPlatformId;`:

```csharp
            _editing.NewsletterTemplateId = _newsletterTemplateId;
```

Note the existing `_template` field on this page holds a `PromptTemplate` — deliberately distinct from `_newsletterTemplateId`. Do not merge them.

- [ ] **Step 3: Build**

```
dotnet build
```

Expected: build succeeds. If `IMudDialogInstance` does not resolve, check the MudBlazor version in `Directory.Packages.props` — MudBlazor 8 and earlier use `MudDialogInstance`.

- [ ] **Step 4: Run the full suite**

```
dotnet test
```

Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src
git commit -m "feat(templates): full-screen template editor with live sample preview"
```

---

## Task 10: Convert the reference template and walk the feature

**Spec:** the whole document. This task proves the design against the real file it was derived from.

**Files:**
- Create: `docs/user-braindumps/preview-template.html`
- Modify: `tests/ContentAutomatorX.UnitTests/TemplateValidatorTests.cs`

- [ ] **Step 1: Convert the reference template**

Copy `docs/user-braindumps/preview.html` to `docs/user-braindumps/preview-template.html` and edit it into block form. Keep the original untouched — it is the user's design reference.

The conversion, block by block:

| Original lines | Becomes |
|---|---|
| Everything from `<!DOCTYPE` through the opening 600px `<table>`, plus the closing tags at the end, plus the fixed logo header `<tr>` (lines 145–174) | `<!-- BLOCK: shell -->`, with `{{preheader}}` replacing the hardcoded preheader text and `{{sections}}` placed where the content rows go |
| Hero `<tr>` (lines 179–209) | `<!-- BLOCK: header -->` — badge becomes `{{issue_date}}`, headline `{{title}}`, subline `{{body_html}}` |
| Article card `<tr>` (lines 235–273) | `<!-- BLOCK: topic -->` — wrap the `<a><img></a>` in `<!-- IF: image -->`, the category line in `<!-- IF: category -->` using `{{category}}` and `{{reading_time}}`, and the read-more paragraph in `<!-- IF: link -->` |
| Divider `<tr>` (lines 278–282) | `<!-- BLOCK: divider -->` |
| Video feature `<tr>` (lines 328–377) | `<!-- BLOCK: video -->` — thumbnail becomes `{{thumbnail_url}}` inside `<!-- IF: thumbnail -->`, both hrefs `{{video_url}}`, the button label `{{link_text}}` |
| Text+CTA `<tr>` (lines 382–405) | `<!-- BLOCK: button -->` — heading and paragraph drop (a Button section carries only a link and a label), keeping the bulletproof button table with `{{link_url}}` and `{{link_text}}` |
| Sponsorship `<tr>` (lines 417–458) | `<!-- BLOCK: sponsor -->` — keep the ADVERTISEMENT pill and the italic disclosure line exactly as they are; they are legal requirements, not decoration |
| Footer `<tr>` (lines 466–528) | `<!-- BLOCK: footer -->` — `{{body_html}}` for the editable copy, `{{sender_identity}}` for the postal address, and `href="{$unsubscribe}"` becomes `href="{{unsubscribe_url}}"` |
| The second article card (lines 287–317) | Deleted — it was a duplicate shown only to demonstrate two cards together. The renderer repeats the topic block per section. |

Delete the "HOW TO USE" paragraph from the header comment and replace it with a short note that blocks are now filled automatically. Leave the palette, fonts, images and merge-tag notes — they are still true and still useful.

- [ ] **Step 2: Prove it validates clean**

Replace the placeholder test added in Task 3 Step 5 with the real one:

```csharp
    [Fact]
    public void The_reference_template_validates_with_no_errors()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "docs", "user-braindumps", "preview-template.html");
        Assert.True(File.Exists(path), $"Reference template not found at {Path.GetFullPath(path)}");

        var issues = TemplateValidator.Validate(File.ReadAllText(path));
        var errors = issues.Where(i => i.Level == TemplateIssueLevel.Error).ToList();
        Assert.True(errors.Count == 0,
            "Reference template has errors:\n" + string.Join("\n", errors.Select(e => $"line {e.Line}: {e.Message}")));
    }

    [Fact]
    public void The_reference_template_defines_every_block()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "docs", "user-braindumps", "preview-template.html");
        var parsed = TemplateParser.Parse(File.ReadAllText(path));
        foreach (var name in TemplateBlocks.All)
            Assert.True(parsed.Blocks.ContainsKey(name), $"Missing BLOCK: {name}");
    }
```

Run them:

```
dotnet test tests/ContentAutomatorX.UnitTests/ContentAutomatorX.UnitTests.csproj --filter TemplateValidatorTests
```

Expected: PASS. The first test's failure message lists every problem with its line, so iterate on the conversion until it is clean.

- [ ] **Step 3: Render it and check the output by eye**

Add a temporary test that writes the rendered sample issue to disk, run it, open the file in a browser, and confirm it looks like `preview.html` — dark header and hero, light article cards, dark video panel, tinted sponsor box, dark footer. Confirm the second topic (the one with no image, link or category) shows no broken image, no empty category line and no dangling "Read more".

```csharp
    [Fact]
    public void Write_the_rendered_sample_for_manual_inspection()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "docs", "user-braindumps", "preview-template.html");
        var html = TemplateHtmlRenderer.Render(SampleIssue.Sections, SampleIssue.Tenant,
            "Signals from the latent space", File.ReadAllText(path), DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "rendered-sample.html"),
            html.Replace(SectionHtmlRenderer.UnsubscribeToken, "#"));
    }
```

**Delete this test once you have looked at the output.** A test that asserts nothing is not a test.

- [ ] **Step 4: Run the app and walk the feature**

```
dotnet run --project src/ContentAutomatorX.Web
```

Walk this path and note anything that misbehaves:

1. Automations → edit a newsletter recipe → the "Newsletter template" row is present with "Built-in design" selected.
2. **New** → the dialog opens full-screen with the starter template and a live preview showing all eight blocks.
3. Upload `preview-template.html` → the preview switches to the real design.
4. Break something on purpose — change `{{title}}` to `{{titel}}` → an error appears with a line number and **Save is disabled**.
5. Delete the `{{unsubscribe_url}}` line → the unsubscribe error appears and Save stays disabled. Put it back.
6. Name it, mark it default, Save → the dialog closes and the dropdown shows it.
7. Save the recipe. Reopen it → the template is still selected.
8. Open an issue in the composer → the email preview now uses the template.
9. Add a Video section, paste a YouTube URL, Apply → the preview shows the thumbnail.
10. Add a category to a topic, Apply → the preview shows "Tutorial · N min read".
11. Undo → the category reverts. Redo → it comes back.
12. Chat: "give every topic a category" → proposals appear with the Category diff on the cards. Accept one.

- [ ] **Step 5: Fix what the walkthrough found**

Anything found here is a real defect the tests missed. Fix it, add a test that would have caught it, and record it in the progress ledger.

- [ ] **Step 6: Run everything**

```
dotnet test
```

Expected: all green, with the manual-inspection test deleted.

- [ ] **Step 7: Commit**

```bash
git add docs tests src
git commit -m "feat(templates): convert the reference design to a block template"
```

---

## Deliberate deviations from the spec

Recorded so a reviewer does not flag them as drift:

1. **`IssueSectionProposal.ProposedCategory` / `BaselineCategory` land in Task 1's migration** though nothing reads them until Task 8. One migration per branch beats two touching the same tables.
2. **`SectionHtmlRenderer` gains a `Video` case** that reuses the topic shape. The spec describes the video block as a template concern, but the built-in renderer is the per-section fallback (§5.3), so it needs *some* video markup — otherwise a Video section in a template with no `video` block would render as nothing at all.
3. **The starter template in `Recipes.razor` is not in the spec.** A blank editor with a hard `shell` requirement and an unsubscribe requirement is a wall, not a starting point. The starter is a minimal valid template that exercises every block.
4. **`preview-template.html` is a new file rather than an edit to `preview.html`.** The original is the user's design reference and stays untouched.
5. **The reference-template test in Task 3 Step 5 asserts the file is *invalid*,** then Task 10 flips it. That is deliberate scaffolding so the conversion has a before-and-after, and it is replaced, not left behind.

## Plan self-review

**Spec coverage.** §3 → Task 1. §4 → Tasks 2, 3. §5.1 → Task 6. §5.2–5.4 → Task 5. §5.5 → Task 4. §6 → Tasks 4, 5, 7. §7 → Task 9. §8 → Task 3. §9 → Tasks 7, 8. §10 → Tasks 6, 9 (error handling is inline in each). §11 → every task's test step. §1.2/§1.3 out-of-scope items are not implemented anywhere, as intended.

**Type consistency checked.** `TemplateIssue`/`TemplateIssueLevel` defined Task 2, used Tasks 3, 9. `TemplateBlocks.ForSectionType` defined Task 2, used Task 5. `ReadingTime.Describe` and `YouTubeUrl.*` defined Task 4, used Task 5. `SectionHtmlRenderer.RenderSection` and `.VideoThumbnail` defined Task 5, used Task 5. `NewsletterTemplateService.ResolveForPostAsync` defined Task 6, used Task 6. `SectionEdit` grows to six fields in Task 7 and is consumed in the same task. `ChatEdit` grows to four fields in Task 8, and every construction site (`ChatReplyParser.TryParse`, the merge in `StoreProposalsAsync`) is updated in that task.

**Known risk not resolved by this plan.** `TemplateHtmlRenderer` calls `SectionHtmlRenderer.VideoThumbnail`, which is `internal`. Both are in `ContentAutomatorX.Application`, so it compiles today. If the implementer finds otherwise, promote to `public` — do not duplicate the derivation logic in two places.
