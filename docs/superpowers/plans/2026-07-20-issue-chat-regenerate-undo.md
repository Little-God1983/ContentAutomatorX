# Issue Chat, Whole-Issue Regenerate & Composer Undo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a persistent per-issue AI chat, a whole-issue regenerate, and undo/redo over every composer edit — where chat and regenerate produce reviewable *proposals* rather than direct writes.

**Architecture:** Three new entities hang off `Post` by cascade. `IssueHistoryService` snapshots the whole issue before every mutation and restores it on undo. `IssueChatService` flattens the transcript into the existing single-shot `ILlmBackend`, parses a JSON reply, and stores per-section proposals that the UI renders inline in each `SectionCard`. Regenerate-all is the same pipeline with a canned instruction.

**Tech Stack:** .NET 10, Blazor Server, MudBlazor, EF Core 10 + SQLite, xUnit.

**Spec:** `docs/superpowers/specs/2026-07-20-issue-chat-and-regenerate-design.md`

## Global Constraints

- **Branch:** work on `feature/issue-composer`. Do **not** create a new branch.
- **Layering:** Application → Domain only. Infrastructure → Domain + Application. Web → Application + Infrastructure. Application must never reference an Infrastructure type.
- **The model may edit existing sections only.** Never add, remove, or reorder. Enforced in the service by a section-ID whitelist, not by prompt wording.
- **Chat may edit any section type. Regenerate-all touches `SectionTypes.Header` and `SectionTypes.Topic` only.**
- **At most one pending proposal per section**, enforced by a unique DB index on `IssueSectionProposal.SectionId`.
- **History cap: 20 chat messages** sent to the model per turn (`IssueChatService.MaxHistoryMessages`).
- **Undo cap: 25 revisions per stack** (`IssueHistoryService.MaxDepth`).
- **Retention:** published issues purge 30 days after `PublishedAt`; everything else 90 days after the newest chat message *or* revision, whichever is later.
- **Every LLM call site resolves per-tenant settings**: `await llmSettings.GetAsync(tenantId, ct)`, result passed to `GenerateAsync`. Never call `GenerateAsync` without it.
- **Adding a member to `IAppDbContext` is a 5-file change**: the interface, `AppDbContext`, and all three test fakes (`RacingPlatformDbContext` and `FailingSaveDbContext` in `PlatformServiceTests.cs`, `SaveHookDbContext` in `IngestionPipelineTests.cs`). Missing one is CS0535.
- **`TestDb` runs migrated file-backed SQLite**, not EF InMemory. A new entity without a migration fails every integration test at `db.Database.Migrate()`.
- **SQLite cannot translate enum comparisons together with date arithmetic**, and its `DateTimeOffset` ordering is unreliable. Filter status server-side, do date maths and `Max` client-side. Precedent: `PostSyncService.TickAsync` lines 16–24.
- **Test placement:** pure functions → `tests/ContentAutomatorX.UnitTests/`. Anything needing a DbContext → `tests/ContentAutomatorX.IntegrationTests/`. Both projects are flat; `Xunit` is a global using (no `using Xunit;` line). Assertions use built-in `Assert` — no FluentAssertions.
- **Test naming:** `Method_snake_case_description`.
- **Commands:** build/test from repo root. `dotnet build ContentAutomatorX.slnx`, `dotnet test ContentAutomatorX.slnx`. EF is a local tool: `dotnet tool restore` once, then `dotnet ef …`.
- **Baseline test count: 309 (164 unit + 145 integration).** Report the new totals.
- **If `dotnet build` fails with MSB3021/MSB3027 file-lock errors**, a running `ContentAutomatorX.Web` or Visual Studio holds the DLLs. Stop that process and retry — it is an environment lock, not a code failure.

## Deliberate deviations from the spec

Recorded here so a reviewer does not flag them as drift:

1. **Spec §5.3 names the parser `TryParseChatReply`.** This plan puts it in its own file as `ChatReplyParser.TryParse`, because `IssueComposerService` is already 294 lines and the parser is independently unit-testable. Same contract.
2. **Spec §10 calls the chat-service tests "Unit".** This repo puts DB-backed service tests in `IntegrationTests` (`IssueComposerServiceTests.cs` lives there, and `SequenceLlm` is defined inside it). The plan follows the repo.
3. **Fence-stripping is extracted to `MarkdownFence.Strip`** and `IssueComposerService.TryParseTopics` is refactored to use it. The spec implies a second copy; two copies of the same fence parser is a review finding waiting to happen. The existing `TopicParsingTests` are the regression guard.
4. **The parser counts its own dropped edits.** Spec §5.3 puts all edit-dropping in the service, but the parser must already reject structurally invalid edits (empty GUID, no fields), and silently losing them would understate `DroppedEdits`. `ChatReply` carries a `DroppedEdits` count that the service adds to.

## File structure

| File | Responsibility |
|---|---|
| `src/ContentAutomatorX.Domain/Entities/IssueChatMessage.cs` (new) | One turn of conversation |
| `src/ContentAutomatorX.Domain/Entities/IssueSectionProposal.cs` (new) | One pending suggested edit |
| `src/ContentAutomatorX.Domain/Entities/IssueRevision.cs` (new) | One undo/redo stack entry |
| `src/ContentAutomatorX.Domain/Constants.cs` (modify) | `ChatRoles`, `RevisionStacks` |
| `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs` (modify) | Three new `DbSet`s |
| `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs` (modify) | Sets, indexes, cascades |
| `src/ContentAutomatorX.Application/Services/MarkdownFence.cs` (new) | Strip ``` fences — shared by both parsers |
| `src/ContentAutomatorX.Application/Services/ChatReplyParser.cs` (new) | Parse + structurally validate the model's JSON |
| `src/ContentAutomatorX.Application/Services/IssueHistoryService.cs` (new) | Snapshot / undo / redo / state |
| `src/ContentAutomatorX.Application/Services/IssueChatService.cs` (new) | Turns, proposals, accept/reject, purge |
| `src/ContentAutomatorX.Application/Services/StaleProposalException.cs` (new) | Lets the UI tell "stale" from "broken" |
| `src/ContentAutomatorX.Application/Services/IssueComposerService.cs` (modify) | Snapshot before every mutation |
| `src/ContentAutomatorX.Web/Jobs/ChatRetentionJob.cs` (new) | Daily purge tick |
| `src/ContentAutomatorX.Web/Program.cs` (modify) | DI + hosted service |
| `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor` (modify) | Inline before/after + Accept/Reject |
| `src/ContentAutomatorX.Web/Components/Shared/IssueChatPanel.razor` (new) | Presentational transcript + input |
| `src/ContentAutomatorX.Web/Components/Shared/RegenerateAllDialog.razor` (new) | Confirm listing affected sections |
| `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor` (modify) | Tabs, toolbar, wiring |

---

### Task 1: Entities, DbContext, migration

**Files:**
- Create: `src/ContentAutomatorX.Domain/Entities/IssueChatMessage.cs`
- Create: `src/ContentAutomatorX.Domain/Entities/IssueSectionProposal.cs`
- Create: `src/ContentAutomatorX.Domain/Entities/IssueRevision.cs`
- Modify: `src/ContentAutomatorX.Domain/Constants.cs` (append)
- Modify: `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`
- Modify: `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `tests/ContentAutomatorX.IntegrationTests/PlatformServiceTests.cs` (two fakes)
- Modify: `tests/ContentAutomatorX.IntegrationTests/IngestionPipelineTests.cs` (one fake)
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueChatPersistenceTests.cs`

**Interfaces:**
- Produces: entities `IssueChatMessage`, `IssueSectionProposal`, `IssueRevision`; constants `ChatRoles.User`, `ChatRoles.Assistant`, `RevisionStacks.Undo`, `RevisionStacks.Redo`; `IAppDbContext.IssueChatMessages`, `.IssueSectionProposals`, `.IssueRevisions`.

- [ ] **Step 1: Write the failing test**

Create `tests/ContentAutomatorX.IntegrationTests/IssueChatPersistenceTests.cs`:

```csharp
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class IssueChatPersistenceTests
{
    private static async Task<(TestDb Test, Post Post, IssueSection Section)> SeedAsync()
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = $"t-chat-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "Issue" };
        var section = new IssueSection { PostId = post.Id, Position = 0, Type = SectionTypes.Topic, Title = "T" };
        test.Db.AddRange(tenant, platform, post, section);
        await test.Db.SaveChangesAsync();
        return (test, post, section);
    }

    [Fact]
    public async Task Chat_rows_round_trip_and_cascade_on_post_delete()
    {
        var (test, post, section) = await SeedAsync();
        using var _ = test;
        test.Db.IssueChatMessages.Add(new IssueChatMessage { PostId = post.Id, Role = ChatRoles.User, Text = "hi" });
        test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = section.Id, ProposedBodyMd = "new", BaselineBodyMd = ""
        });
        test.Db.IssueRevisions.Add(new IssueRevision
        {
            PostId = post.Id, Stack = RevisionStacks.Undo, Ordinal = 1, Label = "Edit", SnapshotJson = "{}"
        });
        await test.Db.SaveChangesAsync();

        using (var fresh = test.NewContext())
        {
            Assert.Equal(ChatRoles.User, (await fresh.IssueChatMessages.SingleAsync()).Role);
            Assert.Equal("new", (await fresh.IssueSectionProposals.SingleAsync()).ProposedBodyMd);
            Assert.Equal("Edit", (await fresh.IssueRevisions.SingleAsync()).Label);
        }

        using (var deleter = test.NewContext())
        {
            deleter.Posts.Remove(await deleter.Posts.SingleAsync(p => p.Id == post.Id));
            await deleter.SaveChangesAsync();
        }

        using (var after = test.NewContext())
        {
            Assert.Empty(await after.IssueChatMessages.ToListAsync());
            Assert.Empty(await after.IssueSectionProposals.ToListAsync());
            Assert.Empty(await after.IssueRevisions.ToListAsync());
        }
    }

    [Fact]
    public async Task A_section_can_have_only_one_pending_proposal()
    {
        var (test, post, section) = await SeedAsync();
        using var _ = test;
        test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = section.Id, ProposedBodyMd = "first", BaselineBodyMd = ""
        });
        await test.Db.SaveChangesAsync();

        using var second = test.NewContext();
        second.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = section.Id, ProposedBodyMd = "second", BaselineBodyMd = ""
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ContentAutomatorX.slnx --filter IssueChatPersistenceTests`
Expected: FAIL — compile error, `IssueChatMessage` does not exist.

- [ ] **Step 3: Create the three entities**

`src/ContentAutomatorX.Domain/Entities/IssueChatMessage.cs`:

```csharp
namespace ContentAutomatorX.Domain.Entities;

/// <summary>One turn of the conversation about an issue. The transcript is a record of what was
/// said and is never rewritten by undo.</summary>
public class IssueChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public required string Role { get; set; }   // ChatRoles.*
    public required string Text { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

`src/ContentAutomatorX.Domain/Entities/IssueSectionProposal.cs`:

```csharp
namespace ContentAutomatorX.Domain.Entities;

/// <summary>A suggested rewrite of one existing section, awaiting Accept or Reject. Existence is
/// the status: accepting applies the text and deletes the row, rejecting just deletes it.</summary>
public class IssueSectionProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Guid SectionId { get; set; }
    public string? ProposedTitle { get; set; }   // null = title unchanged
    public string? ProposedBodyMd { get; set; }  // null = body unchanged

    /// <summary>The section's body when this was generated. Proposals persist across sessions, so
    /// the section can be hand-edited underneath one; comparing against this is what stops Accept
    /// from silently discarding that edit.</summary>
    public required string BaselineBodyMd { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

`src/ContentAutomatorX.Domain/Entities/IssueRevision.cs`:

```csharp
namespace ContentAutomatorX.Domain.Entities;

/// <summary>One entry on the undo or redo stack: the issue's complete editable state before some
/// mutation. Snapshots rather than command inverses — one restore routine is correct for every
/// mutation, including ones not written yet.</summary>
public class IssueRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public required string Stack { get; set; }   // RevisionStacks.*
    public int Ordinal { get; set; }             // monotonic per (post, stack); highest = top
    public required string Label { get; set; }   // what produced it, shown in the button tooltip
    public required string SnapshotJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Append the constants**

Append to `src/ContentAutomatorX.Domain/Constants.cs`:

```csharp

public static class ChatRoles
{
    public const string User = "User";
    public const string Assistant = "Assistant";
}

public static class RevisionStacks
{
    public const string Undo = "Undo";
    public const string Redo = "Redo";
}
```

- [ ] **Step 5: Add the DbSets to the interface**

In `src/ContentAutomatorX.Application/Persistence/IAppDbContext.cs`, after the `TenantLlmSettings` line:

```csharp
    DbSet<IssueChatMessage> IssueChatMessages { get; }
    DbSet<IssueSectionProposal> IssueSectionProposals { get; }
    DbSet<IssueRevision> IssueRevisions { get; }
```

- [ ] **Step 6: Add the DbSets, indexes and cascades to AppDbContext**

In `src/ContentAutomatorX.Infrastructure/Persistence/AppDbContext.cs`, after the `TenantLlmSettings` property:

```csharp
    public DbSet<IssueChatMessage> IssueChatMessages => Set<IssueChatMessage>();
    public DbSet<IssueSectionProposal> IssueSectionProposals => Set<IssueSectionProposal>();
    public DbSet<IssueRevision> IssueRevisions => Set<IssueRevision>();
```

At the end of `OnModelCreating`:

```csharp
        b.Entity<IssueChatMessage>().HasIndex(m => m.PostId);
        b.Entity<IssueChatMessage>()
            .HasOne<Post>().WithMany().HasForeignKey(m => m.PostId).OnDelete(DeleteBehavior.Cascade);
        // Unique so "at most one pending proposal per section" is enforced by the database, not by
        // hoping every writer remembers to delete the previous one first.
        b.Entity<IssueSectionProposal>().HasIndex(p => p.SectionId).IsUnique();
        b.Entity<IssueSectionProposal>().HasIndex(p => p.PostId);
        b.Entity<IssueSectionProposal>()
            .HasOne<Post>().WithMany().HasForeignKey(p => p.PostId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<IssueRevision>().HasIndex(r => new { r.PostId, r.Stack, r.Ordinal });
        b.Entity<IssueRevision>()
            .HasOne<Post>().WithMany().HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 7: Add the three lines to each of the three test fakes**

In `tests/ContentAutomatorX.IntegrationTests/PlatformServiceTests.cs`, in **both** `RacingPlatformDbContext` and `FailingSaveDbContext`, after `public DbSet<TenantLlmSetting> TenantLlmSettings => inner.TenantLlmSettings;`:

```csharp
    public DbSet<IssueChatMessage> IssueChatMessages => inner.IssueChatMessages;
    public DbSet<IssueSectionProposal> IssueSectionProposals => inner.IssueSectionProposals;
    public DbSet<IssueRevision> IssueRevisions => inner.IssueRevisions;
```

Add the identical three lines to `SaveHookDbContext` in `tests/ContentAutomatorX.IntegrationTests/IngestionPipelineTests.cs`.

- [ ] **Step 8: Create the migration**

```bash
dotnet tool restore
dotnet ef migrations add IssueChatProposalsAndRevisions --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web
```

Expected: creates `src/ContentAutomatorX.Infrastructure/Migrations/<timestamp>_IssueChatProposalsAndRevisions.cs` plus `.Designer.cs`, and updates `AppDbContextModelSnapshot.cs`. Open the generated `Up` and confirm it creates three tables with `onDelete: ReferentialAction.Cascade` on each `PostId` FK and a unique index on `IssueSectionProposals.SectionId`.

- [ ] **Step 9: Run the tests**

Run: `dotnet test ContentAutomatorX.slnx --filter IssueChatPersistenceTests`
Expected: PASS, 2 tests.

Then the full suite: `dotnet test ContentAutomatorX.slnx`
Expected: PASS — 311 total (164 unit + 147 integration).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: chat, proposal and revision entities (#composer)"
```

---

### Task 2: Reply parser

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/MarkdownFence.cs`
- Create: `src/ContentAutomatorX.Application/Services/ChatReplyParser.cs`
- Modify: `src/ContentAutomatorX.Application/Services/IssueComposerService.cs` (use `MarkdownFence.Strip` in `TryParseTopics`)
- Test: `tests/ContentAutomatorX.UnitTests/ChatReplyParsingTests.cs`

**Interfaces:**
- Produces: `record ChatEdit(Guid SectionId, string? Title, string? BodyMd)`, `record ChatReply(string Reply, IReadOnlyList<ChatEdit> Edits, int DroppedEdits)`, `static bool ChatReplyParser.TryParse(string text, out ChatReply? reply)`, `static string MarkdownFence.Strip(string text)`.

- [ ] **Step 1: Write the failing test**

Create `tests/ContentAutomatorX.UnitTests/ChatReplyParsingTests.cs`:

```csharp
using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.UnitTests;

public class ChatReplyParsingTests
{
    private static readonly Guid S1 = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Parses_a_reply_with_one_edit()
    {
        var ok = ChatReplyParser.TryParse(
            $$"""{"reply":"Done.","edits":[{"sectionId":"{{S1}}","title":"New","bodyMd":"Body"}]}""",
            out var reply);
        Assert.True(ok);
        Assert.Equal("Done.", reply!.Reply);
        var edit = Assert.Single(reply.Edits);
        Assert.Equal(S1, edit.SectionId);
        Assert.Equal("New", edit.Title);
        Assert.Equal("Body", edit.BodyMd);
        Assert.Equal(0, reply.DroppedEdits);
    }

    [Fact]
    public void Parses_a_fenced_reply()
    {
        var ok = ChatReplyParser.TryParse(
            $$"""
            ```json
            {"reply":"Done.","edits":[{"sectionId":"{{S1}}","bodyMd":"Body"}]}
            ```
            """, out var reply);
        Assert.True(ok);
        Assert.Single(reply!.Edits);
        Assert.Null(reply.Edits[0].Title);        // omitted field means "unchanged"
    }

    [Fact]
    public void Parses_a_reply_with_no_edits()
    {
        var ok = ChatReplyParser.TryParse("""{"reply":"That intro reads fine to me.","edits":[]}""", out var reply);
        Assert.True(ok);
        Assert.Empty(reply!.Edits);
    }

    [Fact]
    public void Accepts_a_missing_edits_array()
    {
        var ok = ChatReplyParser.TryParse("""{"reply":"Just answering."}""", out var reply);
        Assert.True(ok);
        Assert.Empty(reply!.Edits);
    }

    [Fact]
    public void Drops_structurally_invalid_edits_and_counts_them()
    {
        var ok = ChatReplyParser.TryParse(
            $$"""
            {"reply":"Two of these are junk.","edits":[
              {"sectionId":"{{S1}}","bodyMd":"Good"},
              {"sectionId":"00000000-0000-0000-0000-000000000000","bodyMd":"No id"},
              {"sectionId":"{{S1}}"}
            ]}
            """, out var reply);
        Assert.True(ok);
        Assert.Single(reply!.Edits);
        Assert.Equal(2, reply.DroppedEdits);      // empty guid, and neither field set
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]                                       // an array is the topics shape, not this one
    [InlineData("""{"reply":"","edits":[]}""")]              // nothing said and nothing proposed
    [InlineData("""{"edits":[]}""")]
    public void Rejects_unusable_replies(string text)
    {
        Assert.False(ChatReplyParser.TryParse(text, out var reply));
        Assert.Null(reply);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ContentAutomatorX.slnx --filter ChatReplyParsingTests`
Expected: FAIL — compile error, `ChatReplyParser` does not exist.

- [ ] **Step 3: Create the fence helper**

`src/ContentAutomatorX.Application/Services/MarkdownFence.cs`:

```csharp
namespace ContentAutomatorX.Application.Services;

/// <summary>Models routinely wrap JSON in a ``` fence despite being told not to. Both reply
/// parsers strip it the same way; this is that one way.</summary>
public static class MarkdownFence
{
    public static string Strip(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```")) return trimmed;
        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }
}
```

- [ ] **Step 4: Create the parser**

`src/ContentAutomatorX.Application/Services/ChatReplyParser.cs`:

```csharp
using System.Text.Json;

namespace ContentAutomatorX.Application.Services;

/// <summary>One proposed rewrite of one existing section. A null Title or BodyMd means that field
/// is unchanged — the model is not required to restate what it is not touching.</summary>
public record ChatEdit(Guid SectionId, string? Title, string? BodyMd);

/// <summary>What the model said, plus what it wants to change. DroppedEdits counts edits that were
/// structurally unusable, so the UI can say so instead of quietly proposing fewer changes.</summary>
public record ChatReply(string Reply, IReadOnlyList<ChatEdit> Edits, int DroppedEdits);

public static class ChatReplyParser
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private record RawEdit(Guid SectionId, string? Title, string? BodyMd);
    private record RawReply(string? Reply, List<RawEdit>? Edits);

    /// <summary>Structural validation only. Whether a sectionId actually belongs to the issue is
    /// the service's business — the parser has never heard of the issue.</summary>
    public static bool TryParse(string text, out ChatReply? reply)
    {
        reply = null;
        try
        {
            var raw = JsonSerializer.Deserialize<RawReply>(MarkdownFence.Strip(text), JsonOpts);
            if (raw is null) return false;

            var edits = new List<ChatEdit>();
            var dropped = 0;
            foreach (var edit in raw.Edits ?? [])
            {
                var hasField = !string.IsNullOrWhiteSpace(edit.Title) || !string.IsNullOrWhiteSpace(edit.BodyMd);
                if (edit.SectionId == Guid.Empty || !hasField) { dropped++; continue; }
                edits.Add(new ChatEdit(edit.SectionId, NullIfBlank(edit.Title), NullIfBlank(edit.BodyMd)));
            }

            var prose = raw.Reply?.Trim() ?? "";
            // A turn that neither says anything nor proposes anything is a failed turn, not an
            // empty success — the caller should retry rather than show a blank bubble.
            if (prose.Length == 0 && edits.Count == 0) return false;

            reply = new ChatReply(prose, edits, dropped);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
```

- [ ] **Step 5: Refactor TryParseTopics onto the shared helper**

In `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`, replace the fence block inside `TryParseTopics`:

```csharp
    public static bool TryParseTopics(string text, out List<TopicBlurb>? topics)
    {
        topics = null;
        var trimmed = MarkdownFence.Strip(text);
        try
```

(Delete the four lines that previously computed `firstNewline` / `lastFence`, and the `var trimmed = text.Trim();` line they followed.)

- [ ] **Step 6: Run the tests**

Run: `dotnet test ContentAutomatorX.slnx --filter "ChatReplyParsingTests|TopicParsingTests"`
Expected: PASS — 10 tests (6 new + 4 existing). The existing `TopicParsingTests` passing is what proves the fence refactor is behaviour-preserving.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: chat reply parser with shared fence stripping (#composer)"
```

---

### Task 3: IssueHistoryService

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/IssueHistoryService.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueHistoryServiceTests.cs`

**Interfaces:**
- Consumes: `IssueRevision`, `RevisionStacks` (Task 1).
- Produces: `IssueHistoryService(IAppDbContext db)` with `Task SnapshotAsync(Guid postId, string label, CancellationToken ct = default)`, `Task<string?> UndoAsync(Guid postId, CancellationToken ct = default)`, `Task<string?> RedoAsync(Guid postId, CancellationToken ct = default)`, `Task<HistoryState> GetStateAsync(Guid postId, CancellationToken ct = default)`, `const int MaxDepth = 25`; `record HistoryState(string? UndoLabel, string? RedoLabel)`.

- [ ] **Step 1: Write the failing test**

Create `tests/ContentAutomatorX.IntegrationTests/IssueHistoryServiceTests.cs`:

```csharp
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class IssueHistoryServiceTests
{
    private static async Task<(TestDb Test, Post Post, List<IssueSection> Sections)> SeedAsync()
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = $"t-hist-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Issue", Subject = "Subj"
        };
        var sections = new List<IssueSection>
        {
            new() { PostId = post.Id, Position = 0, Type = SectionTypes.Header, BodyMd = "intro" },
            new() { PostId = post.Id, Position = 1, Type = SectionTypes.Topic, Title = "A", BodyMd = "a" },
            new() { PostId = post.Id, Position = 2, Type = SectionTypes.Footer, BodyMd = "bye" }
        };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.AddRange(sections);
        await test.Db.SaveChangesAsync();
        return (test, post, sections);
    }

    [Fact]
    public async Task Undo_restores_an_edited_body_and_redo_reapplies_it()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var history = new IssueHistoryService(test.Db);

        await history.SnapshotAsync(post.Id, "Edit topic");
        sections[1].BodyMd = "edited";
        await test.Db.SaveChangesAsync();

        Assert.Equal("Edit topic", await history.UndoAsync(post.Id));
        Assert.Equal("a", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);

        Assert.Equal("Edit topic", await history.RedoAsync(post.Id));
        Assert.Equal("edited", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task Undo_resurrects_a_deleted_section_with_its_original_id()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var history = new IssueHistoryService(test.Db);
        var doomed = sections[1].Id;

        await history.SnapshotAsync(post.Id, "Delete section");
        test.Db.IssueSections.Remove(sections[1]);
        await test.Db.SaveChangesAsync();
        Assert.Equal(2, await test.Db.IssueSections.CountAsync(s => s.PostId == post.Id));

        await history.UndoAsync(post.Id);

        var restored = await test.Db.IssueSections.SingleAsync(s => s.Id == doomed);
        Assert.Equal("A", restored.Title);
        Assert.Equal(1, restored.Position);
    }

    [Fact]
    public async Task Undo_restores_post_header_fields()
    {
        var (test, post, _) = await SeedAsync();
        using var _t = test;
        var history = new IssueHistoryService(test.Db);

        await history.SnapshotAsync(post.Id, "Rename");
        post.Title = "Changed";
        post.Subject = "Changed too";
        await test.Db.SaveChangesAsync();

        await history.UndoAsync(post.Id);

        var after = await test.Db.Posts.SingleAsync(p => p.Id == post.Id);
        Assert.Equal("Issue", after.Title);
        Assert.Equal("Subj", after.Subject);
    }

    [Fact]
    public async Task A_new_snapshot_clears_the_redo_stack()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var history = new IssueHistoryService(test.Db);

        await history.SnapshotAsync(post.Id, "First");
        sections[1].BodyMd = "one";
        await test.Db.SaveChangesAsync();
        await history.UndoAsync(post.Id);
        Assert.Equal("First", (await history.GetStateAsync(post.Id)).RedoLabel);

        await history.SnapshotAsync(post.Id, "Second");

        var state = await history.GetStateAsync(post.Id);
        Assert.Equal("Second", state.UndoLabel);
        Assert.Null(state.RedoLabel);
    }

    [Fact]
    public async Task Undo_on_an_empty_stack_returns_null()
    {
        var (test, post, _) = await SeedAsync();
        using var _t = test;
        var history = new IssueHistoryService(test.Db);

        Assert.Null(await history.UndoAsync(post.Id));
        Assert.Null(await history.RedoAsync(post.Id));
        var state = await history.GetStateAsync(post.Id);
        Assert.Null(state.UndoLabel);
        Assert.Null(state.RedoLabel);
    }

    [Fact]
    public async Task The_undo_stack_trims_to_max_depth()
    {
        var (test, post, _) = await SeedAsync();
        using var _t = test;
        var history = new IssueHistoryService(test.Db);

        for (var n = 0; n < IssueHistoryService.MaxDepth + 5; n++)
            await history.SnapshotAsync(post.Id, $"Edit {n}");

        var rows = await test.Db.IssueRevisions
            .Where(r => r.PostId == post.Id && r.Stack == RevisionStacks.Undo).ToListAsync();
        Assert.Equal(IssueHistoryService.MaxDepth, rows.Count);
        // The oldest went, not the newest.
        Assert.Equal($"Edit {IssueHistoryService.MaxDepth + 4}", (await history.GetStateAsync(post.Id)).UndoLabel);
        Assert.DoesNotContain(rows, r => r.Label == "Edit 0");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ContentAutomatorX.slnx --filter IssueHistoryServiceTests`
Expected: FAIL — compile error, `IssueHistoryService` does not exist.

- [ ] **Step 3: Write the service**

`src/ContentAutomatorX.Application/Services/IssueHistoryService.cs`:

```csharp
using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public record HistoryState(string? UndoLabel, string? RedoLabel);

internal record SectionSnapshot(Guid Id, int Position, string Type, string? Title, string? BodyMd,
    string? ImageUrl, string? LinkUrl, string? LinkText, Guid? SourceItemId);

internal record IssueSnapshot(string Title, string? Subject, string? PreviewText,
    List<SectionSnapshot> Sections);

/// <summary>Undo/redo for the composer, by whole-issue snapshot rather than per-command inverses.
/// Eight operations mutate an issue and more will follow; one restore routine is correct for all of
/// them, where eight hand-written inverses are eight chances to be subtly wrong — and delete's
/// inverse in particular has to resurrect a row with its original Id, because the Razor @key and
/// IssueSectionProposal.SectionId both point at it.</summary>
public class IssueHistoryService(IAppDbContext db)
{
    public const int MaxDepth = 25;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Records the issue's state as it is right now, before the caller changes it. Call
    /// this as the first statement of any mutation — but after its guard clauses, so a rejected
    /// call does not leave a revision that undoes to the same thing.</summary>
    public async Task SnapshotAsync(Guid postId, string label, CancellationToken ct = default)
    {
        await PushAsync(postId, RevisionStacks.Undo, label, await CaptureAsync(postId, ct), ct);
        // A fresh edit invalidates the redo branch, as in every editor.
        db.IssueRevisions.RemoveRange(await db.IssueRevisions
            .Where(r => r.PostId == postId && r.Stack == RevisionStacks.Redo).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    public Task<string?> UndoAsync(Guid postId, CancellationToken ct = default) =>
        StepAsync(postId, RevisionStacks.Undo, RevisionStacks.Redo, ct);

    public Task<string?> RedoAsync(Guid postId, CancellationToken ct = default) =>
        StepAsync(postId, RevisionStacks.Redo, RevisionStacks.Undo, ct);

    public async Task<HistoryState> GetStateAsync(Guid postId, CancellationToken ct = default)
    {
        var rows = await db.IssueRevisions.Where(r => r.PostId == postId)
            .OrderByDescending(r => r.Ordinal).ToListAsync(ct);
        return new HistoryState(
            rows.FirstOrDefault(r => r.Stack == RevisionStacks.Undo)?.Label,
            rows.FirstOrDefault(r => r.Stack == RevisionStacks.Redo)?.Label);
    }

    /// <summary>Returns the label of the step taken, or null when that stack is empty.</summary>
    private async Task<string?> StepAsync(Guid postId, string from, string to, CancellationToken ct)
    {
        var top = await db.IssueRevisions.Where(r => r.PostId == postId && r.Stack == from)
            .OrderByDescending(r => r.Ordinal).FirstOrDefaultAsync(ct);
        if (top is null) return null;

        var snapshot = JsonSerializer.Deserialize<IssueSnapshot>(top.SnapshotJson, JsonOpts)!;
        await PushAsync(postId, to, top.Label, await CaptureAsync(postId, ct), ct);
        db.IssueRevisions.Remove(top);
        await RestoreAsync(postId, snapshot, ct);
        await db.SaveChangesAsync(ct);
        return top.Label;
    }

    private async Task PushAsync(Guid postId, string stack, string label, IssueSnapshot snapshot,
        CancellationToken ct)
    {
        var existing = await db.IssueRevisions.Where(r => r.PostId == postId && r.Stack == stack)
            .OrderBy(r => r.Ordinal).ToListAsync(ct);
        db.IssueRevisions.Add(new IssueRevision
        {
            PostId = postId, Stack = stack, Label = label,
            Ordinal = existing.Count == 0 ? 1 : existing[^1].Ordinal + 1,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOpts)
        });
        // Trim from the bottom: the oldest history is the most disposable.
        var overflow = existing.Count + 1 - MaxDepth;
        if (overflow > 0) db.IssueRevisions.RemoveRange(existing.Take(overflow));
    }

    private async Task<IssueSnapshot> CaptureAsync(Guid postId, CancellationToken ct)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var sections = await db.IssueSections.Where(s => s.PostId == postId)
            .OrderBy(s => s.Position).ToListAsync(ct);
        return new IssueSnapshot(post.Title, post.Subject, post.PreviewText,
            sections.Select(s => new SectionSnapshot(s.Id, s.Position, s.Type, s.Title, s.BodyMd,
                s.ImageUrl, s.LinkUrl, s.LinkText, s.SourceItemId)).ToList());
    }

    private async Task RestoreAsync(Guid postId, IssueSnapshot snapshot, CancellationToken ct)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        post.Title = snapshot.Title;
        post.Subject = snapshot.Subject;
        post.PreviewText = snapshot.PreviewText;

        var live = await db.IssueSections.Where(s => s.PostId == postId).ToListAsync(ct);
        var wanted = snapshot.Sections.ToDictionary(s => s.Id);
        foreach (var section in live.Where(s => !wanted.ContainsKey(s.Id)))
            db.IssueSections.Remove(section);

        var byId = live.ToDictionary(s => s.Id);
        foreach (var want in snapshot.Sections)
        {
            if (!byId.TryGetValue(want.Id, out var section))
            {
                // Re-created with the original Id on purpose: the Razor @key and any proposal
                // pointing at this section both key off it.
                section = new IssueSection { Id = want.Id, PostId = postId, Type = want.Type };
                db.IssueSections.Add(section);
            }
            section.Position = want.Position;
            section.Type = want.Type;
            section.Title = want.Title;
            section.BodyMd = want.BodyMd;
            section.ImageUrl = want.ImageUrl;
            section.LinkUrl = want.LinkUrl;
            section.LinkText = want.LinkText;
            section.SourceItemId = want.SourceItemId;
        }
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test ContentAutomatorX.slnx --filter IssueHistoryServiceTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: snapshot-based undo/redo for issues (#composer)"
```

---

### Task 4: Snapshot every composer mutation

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`
- Modify: `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs` (the `Composer` factory gains an argument)
- Test: `tests/ContentAutomatorX.IntegrationTests/ComposerHistoryTests.cs`

**Interfaces:**
- Consumes: `IssueHistoryService` (Task 3).
- Produces: `IssueComposerService(IAppDbContext db, ILlmBackend llm, PostService posts, ILlmSettingsProvider llmSettings, IssueHistoryService history)` — note the **new fifth parameter**.

- [ ] **Step 1: Write the failing test**

Create `tests/ContentAutomatorX.IntegrationTests/ComposerHistoryTests.cs`:

```csharp
using System.Reflection;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class ComposerHistoryTests
{
    /// <summary>Pins the public surface so a new method cannot be added without someone deciding
    /// whether it mutates and therefore needs a SnapshotAsync call. There is no chokepoint that
    /// could enforce this automatically; this test is the guard instead.</summary>
    [Fact]
    public void Composer_public_surface_is_pinned_so_new_mutations_must_opt_into_history()
    {
        var names = typeof(IssueComposerService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[]
        {
            "AddSectionAsync", "AddTopicsFromItemsAsync", "CreateFromItemsAsync", "EnsureSectionsAsync",
            "ExportMarkdownAsync", "GenerateTopicsAsync", "GetSectionsAsync", "MoveSectionAsync",
            "RegenerateSectionAsync", "RemoveSectionAsync", "RenderPreviewAsync", "TryParseTopics",
            "UpdateSectionAsync"
        }, names);
    }

    [Fact]
    public async Task Every_mutation_pushes_exactly_one_revision()
    {
        var w = await IssueComposerServiceTests.BuildWorldAsync();
        using var _ = w.Test;
        var history = new IssueHistoryService(w.Test.Db);
        var composer = IssueComposerServiceTests.ComposerWith(w, new SequenceLlm(IssueComposerServiceTests.TopicsJsonFor(w.Items)), history);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, w.Items.Select(i => i.Id).ToList(), "t");

        async Task<int> RevisionsAsync() =>
            await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id && r.Stack == RevisionStacks.Undo);

        var sections = await composer.GetSectionsAsync(post.Id);
        var before = await RevisionsAsync();

        await composer.AddSectionAsync(post.Id, SectionTypes.Divider);
        Assert.Equal(before + 1, await RevisionsAsync());

        await composer.UpdateSectionAsync(sections[1].Id, "T", "B", null, null, null);
        Assert.Equal(before + 2, await RevisionsAsync());

        await composer.MoveSectionAsync(sections[2].Id, -1);
        Assert.Equal(before + 3, await RevisionsAsync());

        await composer.GenerateTopicsAsync(post.Id, null);
        Assert.Equal(before + 4, await RevisionsAsync());

        await composer.RegenerateSectionAsync(sections[1].Id, null);
        Assert.Equal(before + 5, await RevisionsAsync());

        await composer.RemoveSectionAsync(sections[2].Id);
        Assert.Equal(before + 6, await RevisionsAsync());

        await composer.AddTopicsFromItemsAsync(post.Id, [w.Items[0].Id]);
        Assert.Equal(before + 7, await RevisionsAsync());
    }

    [Fact]
    public async Task A_rejected_mutation_leaves_no_revision()
    {
        var w = await IssueComposerServiceTests.BuildWorldAsync();
        using var _ = w.Test;
        var history = new IssueHistoryService(w.Test.Db);
        var composer = IssueComposerServiceTests.ComposerWith(w, new SequenceLlm("x"), history);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var sections = await composer.GetSectionsAsync(post.Id);
        var before = await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.RemoveSectionAsync(sections[0].Id));
        await composer.MoveSectionAsync(sections[1].Id, -1);   // already directly under the header: a no-op

        Assert.Equal(before, await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id));
    }
}
```

- [ ] **Step 2: Expose the existing test fixture**

`ComposerHistoryTests` reuses the world builder that currently lives as a private member of `IssueComposerServiceTests`. In `tests/ContentAutomatorX.IntegrationTests/IssueComposerServiceTests.cs`:

- Change `private sealed record World(...)` to `public sealed record World(...)`.
- Change `private static async Task<World> BuildAsync()` to `public static async Task<World> BuildWorldAsync()` and update its call sites within the file.
- Change `private static string TopicsJson(...)` to `public static string TopicsJsonFor(...)` and update its call sites.
- Add a new factory beside the existing `Composer` helper:

```csharp
    public static IssueComposerService ComposerWith(World w, ILlmBackend llm, IssueHistoryService history) =>
        new(w.Test.Db, llm,
            new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery(), new StubLlmSettings()),
                llm, w.Platforms, w.MailerLite, new StubLlmSettings()),
            new StubLlmSettings(), history);
```

- Update the existing private `Composer` helper to pass a history service too:

```csharp
    private static IssueComposerService Composer(World w, ILlmBackend llm, StubLlmSettings? settings = null) =>
        new(w.Test.Db, llm,
            new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery(), new StubLlmSettings()),
                llm, w.Platforms, w.MailerLite, new StubLlmSettings()),
            settings ?? new StubLlmSettings(), new IssueHistoryService(w.Test.Db));
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test ContentAutomatorX.slnx --filter ComposerHistoryTests`
Expected: FAIL — `IssueComposerService` has no 5-argument constructor.

- [ ] **Step 4: Add the constructor parameter**

In `src/ContentAutomatorX.Application/Services/IssueComposerService.cs`:

```csharp
public class IssueComposerService(IAppDbContext db, ILlmBackend llm, PostService posts,
    ILlmSettingsProvider llmSettings, IssueHistoryService history)
```

- [ ] **Step 5: Snapshot in each mutating method**

Each call goes **after** the method's guard clauses and early returns, so a rejected or no-op call leaves no revision.

`AddSectionAsync` — after the header/footer guard, before `GetSectionsAsync`:

```csharp
        if (type is SectionTypes.Header or SectionTypes.Footer)
            throw new InvalidOperationException("An issue has exactly one header and one footer.");
        await history.SnapshotAsync(postId, "Add section", ct);
```

`AddTopicsFromItemsAsync` — as the first statement:

```csharp
    public async Task AddTopicsFromItemsAsync(Guid postId, IReadOnlyList<Guid> itemIds, CancellationToken ct = default)
    {
        await history.SnapshotAsync(postId, "Add topics", ct);
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
```

`UpdateSectionAsync` — after loading the section (it is the only way to learn the post id):

```csharp
        var section = await db.IssueSections.SingleAsync(s => s.Id == sectionId, ct);
        await history.SnapshotAsync(section.PostId, "Edit section", ct);
        section.Title = title;
```

`RemoveSectionAsync` — after the header/footer guard:

```csharp
        if (section.Type is SectionTypes.Header or SectionTypes.Footer)
            throw new InvalidOperationException("Header and footer cannot be removed — edit them instead.");
        await history.SnapshotAsync(section.PostId, "Delete section", ct);
```

`MoveSectionAsync` — after **both** early returns, so a move that clamps to a no-op records nothing:

```csharp
        if (target <= 0 || target >= sections.Count - 1) return; // stay between header and footer
        await history.SnapshotAsync(section.PostId, "Move section", ct);
        (sections[index], sections[target]) = (sections[target], sections[index]);
```

`GenerateTopicsAsync` — after the `skeletons.Count == 0` early return:

```csharp
        if (skeletons.Count == 0) return 0;
        await history.SnapshotAsync(postId, "Generate topics", ct);
```

`RegenerateSectionAsync` — after the prompt-building `if/else` chain (its `else` throws for unsupported types), immediately before the LLM call:

```csharp
        await history.SnapshotAsync(section.PostId, "Rewrite section", ct);
        var reply = await llm.GenerateAsync(prompt, settings, ct);
        section.BodyMd = reply.Text.Trim();
```

- [ ] **Step 6: Register the service so the app still boots**

In `src/ContentAutomatorX.Web/Program.cs`, immediately before `builder.Services.AddScoped<IssueComposerService>();`:

```csharp
builder.Services.AddScoped<IssueHistoryService>();
```

- [ ] **Step 7: Run the tests**

Run: `dotnet test ContentAutomatorX.slnx`
Expected: PASS — 320 total (170 unit + 150 integration). All pre-existing `IssueComposerServiceTests` must still pass unchanged in behaviour; only their construction changed.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: snapshot before every composer mutation (#composer)"
```

---

### Task 5: IssueChatService

**Files:**
- Create: `src/ContentAutomatorX.Application/Services/StaleProposalException.cs`
- Create: `src/ContentAutomatorX.Application/Services/IssueChatService.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/IssueChatServiceTests.cs`

**Interfaces:**
- Consumes: `ChatReplyParser.TryParse` (Task 2), `IssueHistoryService` (Task 3), entities (Task 1).
- Produces: `IssueChatService(IAppDbContext db, ILlmBackend llm, ILlmSettingsProvider llmSettings, IssueHistoryService history)` with `GetThreadAsync`, `SendAsync`, `RegenerateAllAsync`, `AcceptAsync`, `RejectAsync`; records `IssueChat(IReadOnlyList<IssueChatMessage> Messages, IReadOnlyList<IssueSectionProposal> Proposals)` and `ChatTurnResult(string Reply, int ProposalCount, int DroppedEdits)`; `class StaleProposalException : Exception`; `const int MaxHistoryMessages = 20`.
- `PurgeAsync` is added in Task 6.

- [ ] **Step 1: Write the failing test**

Create `tests/ContentAutomatorX.IntegrationTests/IssueChatServiceTests.cs`:

```csharp
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class IssueChatServiceTests
{
    private static async Task<(TestDb Test, Post Post, List<IssueSection> Sections)> SeedAsync()
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = $"t-chat-svc-{Guid.NewGuid():N}", VoiceProfile = "dry wit" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "Issue" };
        var sections = new List<IssueSection>
        {
            new() { PostId = post.Id, Position = 0, Type = SectionTypes.Header, BodyMd = "intro" },
            new() { PostId = post.Id, Position = 1, Type = SectionTypes.Topic, Title = "A", BodyMd = "a" },
            new() { PostId = post.Id, Position = 2, Type = SectionTypes.Sponsor, Title = "Acme", BodyMd = "ad" },
            new() { PostId = post.Id, Position = 3, Type = SectionTypes.Footer, BodyMd = "bye" }
        };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.AddRange(sections);
        await test.Db.SaveChangesAsync();
        return (test, post, sections);
    }

    private static IssueChatService Chat(TestDb test, ILlmBackend llm) =>
        new(test.Db, llm, new StubLlmSettings(), new IssueHistoryService(test.Db));

    private static string ReplyJson(string prose, params (Guid Id, string Body)[] edits) =>
        $$"""{"reply":"{{prose}}","edits":[{{string.Join(",",
            edits.Select(e => $$"""{"sectionId":"{{e.Id}}","bodyMd":"{{e.Body}}"}"""))}}]}""";

    [Fact]
    public async Task A_turn_records_both_messages_and_creates_a_proposal()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("Shortened it.", (sections[1].Id, "shorter")));

        var result = await Chat(test, llm).SendAsync(post.Id, "make topic A shorter");

        Assert.Equal("Shortened it.", result.Reply);
        Assert.Equal(1, result.ProposalCount);
        Assert.Equal(0, result.DroppedEdits);

        var messages = await test.Db.IssueChatMessages.OrderBy(m => m.CreatedAt).ToListAsync();
        Assert.Equal([ChatRoles.User, ChatRoles.Assistant], messages.Select(m => m.Role));

        var proposal = await test.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal(sections[1].Id, proposal.SectionId);
        Assert.Equal("shorter", proposal.ProposedBodyMd);
        Assert.Equal("a", proposal.BaselineBodyMd);       // captured from the live section
        Assert.Equal("a", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task The_prompt_carries_the_issue_the_voice_and_the_transcript()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("One."), ReplyJson("Two."));
        var chat = Chat(test, llm);

        await chat.SendAsync(post.Id, "first question");
        await chat.SendAsync(post.Id, "second question");

        var second = llm.Prompts[^1];
        Assert.Contains(sections[1].Id.ToString(), second);   // section ids so the model can target them
        Assert.Contains("dry wit", second);                   // tenant voice
        Assert.Contains("first question", second);            // prior turn
        Assert.Contains("One.", second);                      // prior assistant turn
        Assert.Contains("second question", second);
    }

    [Fact]
    public async Task An_edit_naming_an_unknown_section_is_dropped_and_reported()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("Tried.", (sections[1].Id, "ok"), (Guid.NewGuid(), "nope")));

        var result = await Chat(test, llm).SendAsync(post.Id, "change things");

        Assert.Equal(1, result.ProposalCount);
        Assert.Equal(1, result.DroppedEdits);
        Assert.Equal(sections[1].Id, (await test.Db.IssueSectionProposals.SingleAsync()).SectionId);
    }

    [Fact]
    public async Task A_second_turn_replaces_the_proposal_for_the_same_section()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("v1", (sections[1].Id, "first")),
                                  ReplyJson("v2", (sections[1].Id, "second")));
        var chat = Chat(test, llm);

        await chat.SendAsync(post.Id, "try");
        await chat.SendAsync(post.Id, "try again");

        var proposal = await test.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal("second", proposal.ProposedBodyMd);
    }

    [Fact]
    public async Task A_failed_turn_keeps_the_user_message_and_creates_nothing()
    {
        var (test, post, _) = await SeedAsync();
        using var _t = test;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Chat(test, new SequenceLlm("garbage", "still garbage")).SendAsync(post.Id, "hello"));

        var message = await test.Db.IssueChatMessages.SingleAsync();
        Assert.Equal(ChatRoles.User, message.Role);
        Assert.Equal("hello", message.Text);
        Assert.Empty(await test.Db.IssueSectionProposals.ToListAsync());
    }

    [Fact]
    public async Task Chat_may_propose_against_any_section_type()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("Fixed the sponsor.", (sections[2].Id, "better ad")));

        var result = await Chat(test, llm).SendAsync(post.Id, "fix the sponsor blurb");

        Assert.Equal(1, result.ProposalCount);
        Assert.Equal(sections[2].Id, (await test.Db.IssueSectionProposals.SingleAsync()).SectionId);
    }

    [Fact]
    public async Task RegenerateAll_targets_header_and_topics_only_and_writes_no_chat_messages()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("Here you go.",
            (sections[0].Id, "new intro"), (sections[1].Id, "new blurb"), (sections[2].Id, "new ad")));

        var count = await Chat(test, llm).RegenerateAllAsync(post.Id, "punchier");

        Assert.Equal(2, count);                                  // the sponsor edit is refused
        var ids = await test.Db.IssueSectionProposals.Select(p => p.SectionId).ToListAsync();
        Assert.Contains(sections[0].Id, ids);
        Assert.Contains(sections[1].Id, ids);
        Assert.DoesNotContain(sections[2].Id, ids);
        Assert.Empty(await test.Db.IssueChatMessages.ToListAsync());
        Assert.Contains("punchier", llm.Prompts.Single());
    }

    [Fact]
    public async Task RegenerateAll_returns_zero_when_there_is_nothing_to_regenerate()
    {
        var test = TestDb.Create();
        using var _ = test;
        var tenant = new Tenant { Name = "T", Slug = $"t-empty-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "I" };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.Add(new IssueSection { PostId = post.Id, Position = 0, Type = SectionTypes.Footer });
        await test.Db.SaveChangesAsync();
        var llm = new SequenceLlm("unused");

        Assert.Equal(0, await Chat(test, llm).RegenerateAllAsync(post.Id, null));
        Assert.Empty(llm.Prompts);                               // no model call at all
    }

    [Fact]
    public async Task Accept_applies_the_text_deletes_the_proposal_and_is_undoable()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var chat = Chat(test, new SequenceLlm(ReplyJson("ok", (sections[1].Id, "accepted body"))));
        await chat.SendAsync(post.Id, "rewrite");
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();

        await chat.AcceptAsync(proposal.Id, force: false);

        Assert.Equal("accepted body", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
        Assert.Empty(await test.Db.IssueSectionProposals.ToListAsync());

        await new IssueHistoryService(test.Db).UndoAsync(post.Id);
        Assert.Equal("a", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task Accept_refuses_a_stale_proposal_unless_forced()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var chat = Chat(test, new SequenceLlm(ReplyJson("ok", (sections[1].Id, "proposed"))));
        await chat.SendAsync(post.Id, "rewrite");
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();

        sections[1].BodyMd = "hand edited since";               // the user typed over it meanwhile
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<StaleProposalException>(() => chat.AcceptAsync(proposal.Id, force: false));
        Assert.Equal("hand edited since", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);

        await chat.AcceptAsync(proposal.Id, force: true);
        Assert.Equal("proposed", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task Reject_deletes_the_proposal_without_writing()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var chat = Chat(test, new SequenceLlm(ReplyJson("ok", (sections[1].Id, "not wanted"))));
        await chat.SendAsync(post.Id, "rewrite");
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();

        await chat.RejectAsync(proposal.Id);

        Assert.Empty(await test.Db.IssueSectionProposals.ToListAsync());
        Assert.Equal("a", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task GetThread_returns_messages_in_order_with_pending_proposals()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var chat = Chat(test, new SequenceLlm(ReplyJson("ok", (sections[1].Id, "p"))));
        await chat.SendAsync(post.Id, "hello");

        var thread = await chat.GetThreadAsync(post.Id);

        Assert.Equal([ChatRoles.User, ChatRoles.Assistant], thread.Messages.Select(m => m.Role));
        Assert.Single(thread.Proposals);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ContentAutomatorX.slnx --filter IssueChatServiceTests`
Expected: FAIL — compile error, `IssueChatService` does not exist.

- [ ] **Step 3: Create the exception**

`src/ContentAutomatorX.Application/Services/StaleProposalException.cs`:

```csharp
namespace ContentAutomatorX.Application.Services;

/// <summary>The section changed after the proposal was generated. Distinct from a general failure
/// so the UI can offer "overwrite anyway" instead of just reporting an error.</summary>
public class StaleProposalException(string message) : Exception(message);
```

- [ ] **Step 4: Write the service**

`src/ContentAutomatorX.Application/Services/IssueChatService.cs`:

```csharp
using System.Text;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public record IssueChat(IReadOnlyList<IssueChatMessage> Messages, IReadOnlyList<IssueSectionProposal> Proposals);

public record ChatTurnResult(string Reply, int ProposalCount, int DroppedEdits);

/// <summary>Conversation about one issue, and the proposals it produces. Nothing here writes to a
/// section except AcceptAsync — the model only ever suggests.</summary>
public class IssueChatService(IAppDbContext db, ILlmBackend llm, ILlmSettingsProvider llmSettings,
    IssueHistoryService history)
{
    /// <summary>How much transcript goes into each prompt. Bounded because the whole thing is
    /// re-sent every turn against a 300 s CLI timeout. Safe to truncate: the current issue is sent
    /// in full every time, so old turns carry nuance, never the document itself.</summary>
    public const int MaxHistoryMessages = 20;

    public async Task<IssueChat> GetThreadAsync(Guid postId, CancellationToken ct = default)
    {
        var messages = await db.IssueChatMessages.Where(m => m.PostId == postId).ToListAsync(ct);
        var proposals = await db.IssueSectionProposals.Where(p => p.PostId == postId).ToListAsync(ct);
        // Ordered client-side: SQLite stores DateTimeOffset in a form its ORDER BY sorts wrongly.
        return new IssueChat(messages.OrderBy(m => m.CreatedAt).ToList(), proposals);
    }

    public async Task<ChatTurnResult> SendAsync(Guid postId, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Say something first.", nameof(message));
        var text = message.Trim();

        // Persisted before the model is called, so a failed turn leaves the question in the thread
        // to retry from rather than losing what the user typed.
        db.IssueChatMessages.Add(new IssueChatMessage { PostId = postId, Role = ChatRoles.User, Text = text });
        await db.SaveChangesAsync(ct);

        var reply = await RunTurnAsync(postId, text, includeTranscript: true, restrictTo: null, ct);
        db.IssueChatMessages.Add(new IssueChatMessage
        {
            PostId = postId, Role = ChatRoles.Assistant, Text = reply.Reply
        });
        var (stored, dropped) = await StoreProposalsAsync(postId, reply.Edits, null, ct);
        await db.SaveChangesAsync(ct);
        return new ChatTurnResult(reply.Reply, stored, dropped + reply.DroppedEdits);
    }

    /// <summary>Regenerate-all is the same pipeline with a canned instruction, restricted to the
    /// sections worth rewriting wholesale. It is a button, not a conversation turn, so it leaves no
    /// chat messages behind.</summary>
    public async Task<int> RegenerateAllAsync(Guid postId, string? instruction, CancellationToken ct = default)
    {
        var targets = (await db.IssueSections.Where(s => s.PostId == postId).ToListAsync(ct))
            .Where(s => s.Type is SectionTypes.Header or SectionTypes.Topic)
            .Select(s => s.Id).ToHashSet();
        if (targets.Count == 0) return 0;

        var extra = string.IsNullOrWhiteSpace(instruction) ? "" : $"Extra instructions: {instruction.Trim()}\n";
        var ask = $"{extra}Rewrite the header intro and every topic blurb in this issue. "
                + "Keep each topic's subject matter; improve the writing. Propose an edit for every "
                + "header and topic section listed above, and for nothing else.";

        var reply = await RunTurnAsync(postId, ask, includeTranscript: false, restrictTo: targets, ct);
        var (stored, _) = await StoreProposalsAsync(postId, reply.Edits, targets, ct);
        await db.SaveChangesAsync(ct);
        return stored;
    }

    public async Task AcceptAsync(Guid proposalId, bool force, CancellationToken ct = default)
    {
        var proposal = await db.IssueSectionProposals.SingleAsync(p => p.Id == proposalId, ct);
        var section = await db.IssueSections.FirstOrDefaultAsync(s => s.Id == proposal.SectionId, ct);
        if (section is null)
        {
            db.IssueSectionProposals.Remove(proposal);
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("That section no longer exists — the suggestion was discarded.");
        }
        if (!force && (section.BodyMd ?? "") != proposal.BaselineBodyMd)
            throw new StaleProposalException("This section changed after the suggestion was made.");

        await history.SnapshotAsync(section.PostId, "Accept suggestion", ct);
        if (proposal.ProposedTitle is not null) section.Title = proposal.ProposedTitle;
        if (proposal.ProposedBodyMd is not null) section.BodyMd = proposal.ProposedBodyMd;
        db.IssueSectionProposals.Remove(proposal);
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid proposalId, CancellationToken ct = default)
    {
        var proposal = await db.IssueSectionProposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
        if (proposal is null) return;
        db.IssueSectionProposals.Remove(proposal);
        await db.SaveChangesAsync(ct);
    }

    private async Task<ChatReply> RunTurnAsync(Guid postId, string ask, bool includeTranscript,
        HashSet<Guid>? restrictTo, CancellationToken ct)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        var tenant = await db.Tenants.SingleAsync(t => t.Id == post.TenantId, ct);
        var recipe = post.RecipeId is Guid recipeId
            ? await db.Recipes.SingleAsync(r => r.Id == recipeId, ct) : null;
        var sections = (await db.IssueSections.Where(s => s.PostId == postId).ToListAsync(ct))
            .OrderBy(s => s.Position).ToList();
        var transcript = includeTranscript
            ? (await db.IssueChatMessages.Where(m => m.PostId == postId).ToListAsync(ct))
                .OrderBy(m => m.CreatedAt).TakeLast(MaxHistoryMessages).ToList()
            : [];

        var prompt = BuildPrompt(tenant, recipe, sections, restrictTo, transcript, ask);
        var settings = await llmSettings.GetAsync(tenant.Id, ct);

        ChatReply? reply = null;
        for (var attempt = 1; attempt <= 2 && reply is null; attempt++)
        {
            var raw = await llm.GenerateAsync(attempt == 1 ? prompt
                : prompt + "\nYour previous reply was not valid JSON. Respond with ONLY the JSON object.",
                settings, ct);
            ChatReplyParser.TryParse(raw.Text, out reply);
        }
        return reply ?? throw new InvalidOperationException("The model did not reply as JSON — try again.");
    }

    private static string BuildPrompt(Tenant tenant, Recipe? recipe, List<IssueSection> sections,
        HashSet<Guid>? restrictTo, List<IssueChatMessage> transcript, string ask)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are editing an existing newsletter issue with its author.");
        sb.AppendLine("You may rewrite the title and body of the sections listed below.");
        sb.AppendLine("You may NOT add sections, delete sections, or change their order.");
        sb.AppendLine("Only ever use a sectionId that appears below; any other id is discarded.");
        if (!string.IsNullOrWhiteSpace(tenant.VoiceProfile)) sb.AppendLine($"Voice: {tenant.VoiceProfile}");
        if (!string.IsNullOrWhiteSpace(recipe?.ToneModifiers)) sb.AppendLine($"Tone: {recipe.ToneModifiers}");
        if (!string.IsNullOrWhiteSpace(recipe?.Language)) sb.AppendLine($"Write in: {recipe.Language}");
        sb.AppendLine();
        sb.AppendLine("""Respond with ONLY a JSON object, no prose outside it, no markdown fences:""");
        sb.AppendLine("""{"reply":"what you want to say","edits":[{"sectionId":"<id>","title":"...","bodyMd":"..."}]}""");
        sb.AppendLine("Omit title or bodyMd to leave that field unchanged. Use an empty edits array to just answer.");
        sb.AppendLine();
        sb.AppendLine("--- the issue ---");
        foreach (var section in sections)
        {
            var editable = restrictTo is null || restrictTo.Contains(section.Id);
            sb.AppendLine($"sectionId: {section.Id} | type: {section.Type}{(editable ? "" : " | READ-ONLY, do not edit")}");
            if (!string.IsNullOrWhiteSpace(section.Title)) sb.AppendLine($"Title: {section.Title}");
            if (!string.IsNullOrWhiteSpace(section.BodyMd)) sb.AppendLine(section.BodyMd);
            sb.AppendLine();
        }
        if (transcript.Count > 0)
        {
            sb.AppendLine("--- conversation so far ---");
            foreach (var message in transcript)
                sb.AppendLine($"{(message.Role == ChatRoles.User ? "Author" : "You")}: {message.Text}");
            sb.AppendLine();
        }
        sb.AppendLine("--- now ---");
        sb.AppendLine(ask);
        return sb.ToString();
    }

    /// <summary>Writes proposals for the edits that survive validation. This is where the structural
    /// lock is enforced: an id that is not a section of this issue (or is outside restrictTo) is
    /// dropped, so the model cannot invent a section by inventing an id.</summary>
    private async Task<(int Stored, int Dropped)> StoreProposalsAsync(Guid postId,
        IReadOnlyList<ChatEdit> edits, HashSet<Guid>? restrictTo, CancellationToken ct)
    {
        if (edits.Count == 0) return (0, 0);
        var sections = (await db.IssueSections.Where(s => s.PostId == postId).ToListAsync(ct))
            .ToDictionary(s => s.Id);
        var existing = await db.IssueSectionProposals.Where(p => p.PostId == postId).ToListAsync(ct);

        var stored = 0;
        var dropped = 0;
        foreach (var edit in edits)
        {
            if (!sections.TryGetValue(edit.SectionId, out var section) ||
                (restrictTo is not null && !restrictTo.Contains(edit.SectionId)))
            {
                dropped++;
                continue;
            }
            // At most one pending proposal per section — a later suggestion supersedes an earlier
            // one rather than queueing behind it. The unique index enforces this too.
            var previous = existing.FirstOrDefault(p => p.SectionId == edit.SectionId);
            if (previous is not null)
            {
                db.IssueSectionProposals.Remove(previous);
                existing.Remove(previous);
            }
            db.IssueSectionProposals.Add(new IssueSectionProposal
            {
                PostId = postId, SectionId = section.Id,
                ProposedTitle = edit.Title, ProposedBodyMd = edit.BodyMd,
                BaselineBodyMd = section.BodyMd ?? ""
            });
            stored++;
        }
        return (stored, dropped);
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test ContentAutomatorX.slnx --filter IssueChatServiceTests`
Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: issue chat service with section-locked proposals (#composer)"
```

---

### Task 6: Retention

**Files:**
- Modify: `src/ContentAutomatorX.Application/Services/IssueChatService.cs` (add `PurgeAsync`)
- Create: `src/ContentAutomatorX.Web/Jobs/ChatRetentionJob.cs`
- Modify: `src/ContentAutomatorX.Web/Program.cs`
- Test: `tests/ContentAutomatorX.IntegrationTests/ChatRetentionTests.cs`

**Interfaces:**
- Produces: `Task<int> IssueChatService.PurgeAsync(DateTimeOffset now, CancellationToken ct = default)` returning the number of issues whose data was collected.

- [ ] **Step 1: Write the failing test**

Create `tests/ContentAutomatorX.IntegrationTests/ChatRetentionTests.cs`:

```csharp
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class ChatRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static IssueChatService Chat(TestDb test) =>
        new(test.Db, new SequenceLlm("unused"), new StubLlmSettings(), new IssueHistoryService(test.Db));

    private static async Task<Post> AddIssueAsync(TestDb test, PostStatus status,
        DateTimeOffset? publishedAt, DateTimeOffset lastMessageAt)
    {
        var tenant = new Tenant { Name = "T", Slug = $"t-ret-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Issue", Status = status, PublishedAt = publishedAt
        };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueChatMessages.Add(new IssueChatMessage
        {
            PostId = post.Id, Role = ChatRoles.User, Text = "hi", CreatedAt = lastMessageAt
        });
        test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = Guid.NewGuid(), ProposedBodyMd = "x", BaselineBodyMd = ""
        });
        test.Db.IssueRevisions.Add(new IssueRevision
        {
            PostId = post.Id, Stack = RevisionStacks.Undo, Ordinal = 1, Label = "E",
            SnapshotJson = "{}", CreatedAt = lastMessageAt
        });
        await test.Db.SaveChangesAsync();
        return post;
    }

    [Theory]
    [InlineData(31, true)]    // published long enough ago
    [InlineData(29, false)]   // still inside the 30-day window
    public async Task Published_issues_purge_thirty_days_after_publication(int daysAgo, bool purged)
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Published, Now.AddDays(-daysAgo), Now.AddDays(-daysAgo));

        var count = await Chat(test).PurgeAsync(Now);

        Assert.Equal(purged ? 1 : 0, count);
        Assert.Equal(purged ? 0 : 1, await test.Db.IssueChatMessages.CountAsync(m => m.PostId == post.Id));
        Assert.Equal(purged ? 0 : 1, await test.Db.IssueSectionProposals.CountAsync(p => p.PostId == post.Id));
        Assert.Equal(purged ? 0 : 1, await test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id));
    }

    [Theory]
    [InlineData(91, true)]
    [InlineData(89, false)]
    public async Task Unpublished_issues_purge_ninety_days_after_the_last_activity(int daysAgo, bool purged)
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Draft, null, Now.AddDays(-daysAgo));

        Assert.Equal(purged ? 1 : 0, await Chat(test).PurgeAsync(Now));
        Assert.Equal(purged ? 0 : 1, await test.Db.IssueChatMessages.CountAsync(m => m.PostId == post.Id));
    }

    [Fact]
    public async Task A_pushed_but_never_sent_issue_uses_the_activity_rule_not_the_publish_rule()
    {
        using var test = TestDb.Create();
        // Pushed to MailerLite but never sent, so PublishedAt was never set. Without the activity
        // rule this thread would never be collected at all.
        var post = await AddIssueAsync(test, PostStatus.Pushed, null, Now.AddDays(-100));

        Assert.Equal(1, await Chat(test).PurgeAsync(Now));
        Assert.Empty(await test.Db.IssueChatMessages.Where(m => m.PostId == post.Id).ToListAsync());
    }

    [Fact]
    public async Task Recent_chat_keeps_an_old_issue_alive()
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Draft, null, Now.AddDays(-1));
        // Created long ago, but still being worked on — the clock runs from activity, not creation.
        (await test.Db.Posts.SingleAsync(p => p.Id == post.Id)).CreatedAt = Now.AddDays(-400);
        await test.Db.SaveChangesAsync();

        Assert.Equal(0, await Chat(test).PurgeAsync(Now));
        Assert.Single(await test.Db.IssueChatMessages.Where(m => m.PostId == post.Id).ToListAsync());
    }

    [Fact]
    public async Task An_issue_with_revisions_but_no_chat_uses_the_revision_timestamp()
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Draft, null, Now.AddDays(-2));
        test.Db.IssueChatMessages.RemoveRange(
            await test.Db.IssueChatMessages.Where(m => m.PostId == post.Id).ToListAsync());
        await test.Db.SaveChangesAsync();

        Assert.Equal(0, await Chat(test).PurgeAsync(Now));
        Assert.Single(await test.Db.IssueRevisions.Where(r => r.PostId == post.Id).ToListAsync());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ContentAutomatorX.slnx --filter ChatRetentionTests`
Expected: FAIL — `IssueChatService` has no `PurgeAsync`.

- [ ] **Step 3: Add PurgeAsync**

Append to `IssueChatService`, after `RejectAsync`:

```csharp
    /// <summary>Collects chat, proposals and revisions for issues nobody is working on any more.
    /// Two rules, because PublishedAt is set only by the MailerLite poll on Pushed → Published: an
    /// issue that is drafted, discussed and abandoned never gets one, so a publish-only rule would
    /// leak its thread forever.</summary>
    public async Task<int> PurgeAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var postIds = (await db.IssueChatMessages.Select(m => m.PostId).Distinct().ToListAsync(ct))
            .Union(await db.IssueRevisions.Select(r => r.PostId).Distinct().ToListAsync(ct))
            .ToList();
        if (postIds.Count == 0) return 0;

        var posts = await db.Posts.Where(p => postIds.Contains(p.Id)).ToListAsync(ct);
        // Timestamps are compared client-side: SQLite cannot translate this date arithmetic
        // alongside the enum status, and its DateTimeOffset ordering is unreliable besides
        // (see PostSyncService.TickAsync).
        var messageTimes = await db.IssueChatMessages.Where(m => postIds.Contains(m.PostId))
            .Select(m => new { m.PostId, m.CreatedAt }).ToListAsync(ct);
        var revisionTimes = await db.IssueRevisions.Where(r => postIds.Contains(r.PostId))
            .Select(r => new { r.PostId, r.CreatedAt }).ToListAsync(ct);

        var collected = 0;
        foreach (var post in posts)
        {
            var lastActivity = messageTimes.Where(m => m.PostId == post.Id).Select(m => m.CreatedAt)
                .Concat(revisionTimes.Where(r => r.PostId == post.Id).Select(r => r.CreatedAt))
                .DefaultIfEmpty(post.CreatedAt).Max();

            var due = post is { Status: PostStatus.Published, PublishedAt: DateTimeOffset published }
                ? published.AddDays(30)
                : lastActivity.AddDays(90);
            if (now < due) continue;

            db.IssueChatMessages.RemoveRange(
                await db.IssueChatMessages.Where(m => m.PostId == post.Id).ToListAsync(ct));
            db.IssueSectionProposals.RemoveRange(
                await db.IssueSectionProposals.Where(p => p.PostId == post.Id).ToListAsync(ct));
            db.IssueRevisions.RemoveRange(
                await db.IssueRevisions.Where(r => r.PostId == post.Id).ToListAsync(ct));
            collected++;
        }
        if (collected > 0) await db.SaveChangesAsync(ct);
        return collected;
    }
```

Add `using ContentAutomatorX.Domain.Entities;` is already present; no new usings needed.

- [ ] **Step 4: Create the job**

`src/ContentAutomatorX.Web/Jobs/ChatRetentionJob.cs`:

```csharp
using ContentAutomatorX.Application.Services;

namespace ContentAutomatorX.Web.Jobs;

/// <summary>Daily sweep for expired issue chat, proposals and revisions. Separate from
/// PlatformSyncJob rather than folded into its hourly tick: retention is not platform sync, and
/// shared ticks are how jobs become junk drawers.</summary>
public class ChatRetentionJob(IServiceScopeFactory scopeFactory, ILogger<ChatRetentionJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var chat = scope.ServiceProvider.GetRequiredService<IssueChatService>();
                var collected = await chat.PurgeAsync(DateTimeOffset.UtcNow, ct);
                if (collected > 0) logger.LogInformation("chat retention collected {Count} issue threads", collected);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "chat retention tick failed"); }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
```

- [ ] **Step 5: Register the service and the job**

In `src/ContentAutomatorX.Web/Program.cs`, after `builder.Services.AddScoped<IssueComposerService>();`:

```csharp
builder.Services.AddScoped<IssueChatService>();
```

And after `builder.Services.AddHostedService<PlatformSyncJob>();`:

```csharp
builder.Services.AddHostedService<ChatRetentionJob>();
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test ContentAutomatorX.slnx`
Expected: PASS — 339 total (170 unit + 169 integration).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: chat retention sweep with publish and activity rules (#composer)"
```

---

### Task 7: SectionCard before/after

**Files:**
- Modify: `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`

**Interfaces:**
- Consumes: `IssueSectionProposal` (Task 1).
- Produces: `SectionCard` parameters `[Parameter] public IssueSectionProposal? Proposal`, `[Parameter] public EventCallback OnAcceptProposal`, `[Parameter] public EventCallback OnRejectProposal`.

- [ ] **Step 1: Add the parameters**

In the `@code` block of `src/ContentAutomatorX.Web/Components/Shared/SectionCard.razor`, after the `OnApply` parameter:

```csharp
    [Parameter] public IssueSectionProposal? Proposal { get; set; }
    [Parameter] public EventCallback OnAcceptProposal { get; set; }
    [Parameter] public EventCallback OnRejectProposal { get; set; }
```

- [ ] **Step 2: Render the title change in the header row**

In the header `<div>`, replace the label `MudText` with a version that shows a proposed title change inline:

```razor
        <MudText Typo="Typo.subtitle2" Class="flex-grow-1"
                 Style="min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">
            @Label()@(Proposal?.ProposedTitle is string t ? $"  →  {t}" : "")
        </MudText>
```

- [ ] **Step 3: Render the before/after block**

Immediately after the closing `</div>` of the header row and **before** the `@if (_expanded)` block:

```razor
    @if (Proposal is not null)
    {
        <div class="mt-2 pa-2" style="border-left:3px solid var(--mud-palette-primary); background:var(--mud-palette-background-grey);">
            <MudText Typo="Typo.overline">Current</MudText>
            <MudText Typo="Typo.body2" Style="white-space:pre-wrap;">@(Section.BodyMd ?? "(empty)")</MudText>
            <MudDivider Class="my-2" />
            <MudText Typo="Typo.overline" Color="Color.Primary">Proposed</MudText>
            <MudText Typo="Typo.body2" Style="white-space:pre-wrap;">@(Proposal.ProposedBodyMd ?? Section.BodyMd ?? "(empty)")</MudText>
            <div class="mt-2 d-flex" style="gap:8px">
                <MudButton Size="Size.Small" Variant="Variant.Filled" Color="Color.Primary"
                           Disabled="@Busy" OnClick="@(() => OnAcceptProposal.InvokeAsync())">Accept</MudButton>
                <MudButton Size="Size.Small" Variant="Variant.Outlined"
                           Disabled="@Busy" OnClick="@(() => OnRejectProposal.InvokeAsync())">Reject</MudButton>
            </div>
        </div>
    }
```

- [ ] **Step 4: Verify it compiles**

Run: `dotnet build ContentAutomatorX.slnx`
Expected: Build succeeded, 0 warnings. (No new test here — the component has no bUnit harness in this repo; Task 10's walkthrough drives it live.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: inline before/after with accept and reject on section cards (#composer)"
```

---

### Task 8: Chat panel and tab

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Shared/IssueChatPanel.razor`
- Modify: `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`

**Interfaces:**
- Consumes: `IssueChatService`, `IssueChat`, `ChatTurnResult`, `StaleProposalException` (Tasks 5–6); `SectionCard.Proposal` (Task 7).
- Produces: `IssueChatPanel` with `[Parameter] public IReadOnlyList<IssueChatMessage> Messages`, `[Parameter] public bool Sending`, `[Parameter] public EventCallback<string> OnSend`.

- [ ] **Step 1: Create the panel**

`src/ContentAutomatorX.Web/Components/Shared/IssueChatPanel.razor`:

```razor
@using ContentAutomatorX.Domain
@using ContentAutomatorX.Domain.Entities

<div style="max-height:640px; overflow-y:auto;" class="mb-3">
    @if (Messages.Count == 0)
    {
        <MudText Typo="Typo.body2" Class="mud-text-secondary">
            Ask for changes in plain language — "make topic 3 shorter", "the intro is too salesy".
            Suggestions appear on the section cards for you to accept or reject; nothing is written until you do.
        </MudText>
    }
    @foreach (var message in Messages)
    {
        var mine = message.Role == ChatRoles.User;
        <div class="mb-2 pa-2" style="@(mine ? "background:var(--mud-palette-background-grey); border-radius:6px;" : "")">
            <MudText Typo="Typo.overline" Color="@(mine ? Color.Default : Color.Primary)">
                @(mine ? "you" : "ai")
            </MudText>
            <MudText Typo="Typo.body2" Style="white-space:pre-wrap;">@message.Text</MudText>
        </div>
    }
</div>

<MudTextField @bind-Value="_draft" Label="Ask for a change…" Lines="3" Immediate="true"
              Disabled="@Sending" Variant="Variant.Outlined" />
<MudButton Class="mt-2" Variant="Variant.Filled" Color="Color.Primary"
           Disabled="@(Sending || string.IsNullOrWhiteSpace(_draft))" OnClick="SendAsync">Send</MudButton>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<IssueChatMessage> Messages { get; set; } = [];
    [Parameter] public bool Sending { get; set; }
    [Parameter] public EventCallback<string> OnSend { get; set; }

    private string _draft = "";

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(_draft)) return;
        var text = _draft;
        // Cleared before awaiting so a slow turn cannot be double-sent by an impatient second click;
        // the Sending flag disables the button, this stops the text lingering behind it.
        _draft = "";
        await OnSend.InvokeAsync(text);
    }
}
```

- [ ] **Step 2: Add chat state and the scope helper to IssueEditor**

In the `@code` block of `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`, add fields beside the existing ones:

```csharp
    private IssueChat _chat = new([], []);
    private bool _chatting;
```

Update `AnyBusy` to include it:

```csharp
    private bool AnyBusy => _generating || _pushing || _subjectsLoading || _mutating || _gathering || _chatting;
```

Add the chat-scope helpers next to `WithComposerAsync`:

```csharp
    // Chat gets the same fresh-scope treatment as the composer, for the same reason — and more so:
    // a chat turn is the longest-running call on this page.
    private async Task<T> WithChatAsync<T>(Func<IssueChatService, Task<T>> op)
    {
        using var scope = ScopeFactory.CreateScope();
        return await op(scope.ServiceProvider.GetRequiredService<IssueChatService>());
    }

    private async Task WithChatAsync(Func<IssueChatService, Task> op)
    {
        using var scope = ScopeFactory.CreateScope();
        await op(scope.ServiceProvider.GetRequiredService<IssueChatService>());
    }
```

- [ ] **Step 3: Load the thread with the sections**

Replace `ReloadSectionsAsync`:

```csharp
    private async Task ReloadSectionsAsync()
    {
        _sections = await WithComposerAsync(c => c.GetSectionsAsync(PostId));
        _previewHtml = await WithComposerAsync(c => c.RenderPreviewAsync(PostId, _title));
        _chat = await WithChatAsync(c => c.GetThreadAsync(PostId));
        StateHasChanged();
    }
```

- [ ] **Step 4: Add the send, accept and reject handlers**

Add to the `@code` block:

```csharp
    private async Task SendChatAsync(string message)
    {
        if (_chatting) return;
        _chatting = true;
        StateHasChanged();
        try
        {
            var result = await WithChatAsync(c => c.SendAsync(PostId, message));
            if (result.ProposalCount > 0)
                Snackbar.Add($"{result.ProposalCount} suggestion(s) — review them on the cards.", Severity.Success);
            if (result.DroppedEdits > 0)
                Snackbar.Add($"Ignored {result.DroppedEdits} change(s) to sections that aren't in this issue.",
                    Severity.Warning);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Chat failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _chatting = false;
            await ReloadSectionsAsync();
        }
    }

    private async Task AcceptProposalAsync(IssueSectionProposal proposal)
    {
        if (_mutating) return;
        _mutating = true;
        try
        {
            await WithChatAsync(c => c.AcceptAsync(proposal.Id, force: false));
            Snackbar.Add("Applied.", Severity.Success);
        }
        catch (StaleProposalException)
        {
            // The section moved under the suggestion. Overwriting is legitimate — losing the user's
            // typing without asking is not.
            var confirmed = await DialogService.ShowMessageBox("This section changed",
                "You edited this section after the suggestion was made. Applying it will replace what you wrote.",
                yesText: "Replace it", cancelText: "Keep mine");
            if (confirmed == true)
            {
                try
                {
                    await WithChatAsync(c => c.AcceptAsync(proposal.Id, force: true));
                    Snackbar.Add("Applied.", Severity.Success);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Apply failed: {ex.Message}", Severity.Error);
                }
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Apply failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _mutating = false;
            await ReloadSectionsAsync();
        }
    }

    private async Task RejectProposalAsync(IssueSectionProposal proposal)
    {
        if (_mutating) return;
        _mutating = true;
        try
        {
            await WithChatAsync(c => c.RejectAsync(proposal.Id));
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Discard failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _mutating = false;
            await ReloadSectionsAsync();
        }
    }

    private IssueSectionProposal? ProposalFor(IssueSection section) =>
        _chat.Proposals.FirstOrDefault(p => p.SectionId == section.Id);
```

- [ ] **Step 5: Pass proposals to the cards**

In the `@for` loop, extend the `SectionCard` usage:

```razor
                    var proposal = ProposalFor(section);
                    <SectionCard @key="section.Id" Section="@section" TopicNumber="@topicNumber" Busy="@AnyBusy"
                                 CanMoveUp="@(index > 1)" CanMoveDown="@(index < _sections.Count - 2)"
                                 Proposal="@proposal"
                                 OnMove="@(dir => MoveAsync(section, dir))"
                                 OnDelete="@(() => DeleteAsync(section))"
                                 OnRegenerate="@(() => RegenerateAsync(section))"
                                 OnAcceptProposal="@(() => AcceptProposalAsync(proposal!))"
                                 OnRejectProposal="@(() => RejectProposalAsync(proposal!))"
                                 OnApply="@(edit => ApplyAsync(section, edit))" />
```

(`proposal!` is safe: the card only raises those callbacks when `Proposal` is non-null.)

- [ ] **Step 6: Tab the right pane**

Replace the second `MudItem` (the preview pane) with:

```razor
        <MudItem xs="12" md="7">
            <MudPaper Class="pa-4">
                <MudTabs Elevation="0" Rounded="true" ApplyEffectsToContainer="true" PanelClass="pt-4">
                    <MudTabPanel Text="Preview">
                        <MudText Typo="Typo.subtitle2" Class="mb-2">Exactly what MailerLite receives</MudText>
                        <div class="pa-3" style="border:1px solid var(--mud-palette-lines-default); border-radius:4px; max-height:800px; overflow-y:auto; background:#f4f4f4;">
                            @((MarkupString)_previewHtml)
                        </div>
                    </MudTabPanel>
                    <MudTabPanel Text="Chat">
                        <IssueChatPanel Messages="@_chat.Messages" Sending="@_chatting" OnSend="SendChatAsync" />
                    </MudTabPanel>
                </MudTabs>
            </MudPaper>
        </MudItem>
```

- [ ] **Step 7: Verify it compiles and the suite still passes**

Run: `dotnet build ContentAutomatorX.slnx && dotnet test ContentAutomatorX.slnx`
Expected: Build succeeded, 0 warnings; 339 tests pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: chat tab wired to proposals on section cards (#composer)"
```

---

### Task 9: Toolbar — undo, redo, regenerate all, model badge

**Files:**
- Create: `src/ContentAutomatorX.Web/Components/Shared/RegenerateAllDialog.razor`
- Modify: `src/ContentAutomatorX.Web/Components/Pages/IssueEditor.razor`

**Interfaces:**
- Consumes: `IssueHistoryService` (Task 3), `IssueChatService.RegenerateAllAsync` (Task 5), `ILlmSettingsProvider.GetAsync` (existing).

- [ ] **Step 1: Create the confirm dialog**

`src/ContentAutomatorX.Web/Components/Shared/RegenerateAllDialog.razor`:

```razor
<MudDialog>
    <DialogContent>
        <MudText Class="mb-2">
            The AI will suggest a fresh version of @Sections.Count section(s). Nothing is
            overwritten — each suggestion appears on its card for you to accept or reject.
        </MudText>
        <MudList T="string" Dense="true">
            @foreach (var name in Sections)
            {
                <MudListItem T="string" Icon="@Icons.Material.Filled.AutoAwesome">@name</MudListItem>
            }
        </MudList>
        <MudTextField @bind-Value="_instruction" Label="Anything to steer it? (optional)" Lines="2" Class="mt-3" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => Dialog.Cancel())">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="@(() => Dialog.Close(DialogResult.Ok(_instruction)))">Suggest rewrites</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = default!;
    [Parameter, EditorRequired] public IReadOnlyList<string> Sections { get; set; } = [];

    private string _instruction = "";
}
```

- [ ] **Step 2: Add toolbar state to IssueEditor**

Add fields to the `@code` block:

```csharp
    private HistoryState _history = new(null, null);
    private string _modelLabel = "";
```

Add the history-scope helper beside `WithChatAsync`:

```csharp
    private async Task<T> WithHistoryAsync<T>(Func<IssueHistoryService, Task<T>> op)
    {
        using var scope = ScopeFactory.CreateScope();
        return await op(scope.ServiceProvider.GetRequiredService<IssueHistoryService>());
    }
```

- [ ] **Step 3: Load history state and the model name on every reload**

Extend `ReloadSectionsAsync`, after the `_chat` line:

```csharp
        _history = await WithHistoryAsync(h => h.GetStateAsync(PostId));
        if (_post is not null)
        {
            using var scope = ScopeFactory.CreateScope();
            var settings = await scope.ServiceProvider.GetRequiredService<ILlmSettingsProvider>()
                .GetAsync(_post.TenantId);
            // The resolved model, not the stored one: the question this badge answers is "what
            // wrote this text". When nothing names a model the CLI picks and we genuinely do not
            // know which, so say that rather than guessing a name.
            _modelLabel = settings.Model.Length > 0 ? settings.Model : "CLI default";
        }
```

- [ ] **Step 4: Add the handlers**

```csharp
    private Task UndoAsync() => StepHistoryAsync(h => h.UndoAsync(PostId), "Undo");
    private Task RedoAsync() => StepHistoryAsync(h => h.RedoAsync(PostId), "Redo");

    private async Task StepHistoryAsync(Func<IssueHistoryService, Task<string?>> step, string verb)
    {
        if (_mutating) return;
        _mutating = true;
        try
        {
            var label = await WithHistoryAsync(step);
            Snackbar.Add(label is null ? $"Nothing to {verb.ToLowerInvariant()}." : $"{verb}: {label}",
                label is null ? Severity.Info : Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"{verb} failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _mutating = false;
            await ReloadSectionsAsync();
        }
    }

    private async Task RegenerateAllAsync()
    {
        if (_generating) return;
        var targets = _sections
            .Where(s => s.Type is SectionTypes.Header or SectionTypes.Topic)
            .Select(s => s.Type == SectionTypes.Header
                ? "Header"
                : $"Topic: {s.Title ?? "(untitled)"}")
            .ToList();
        if (targets.Count == 0)
        {
            Snackbar.Add("Nothing to regenerate — this issue has no header or topics.", Severity.Info);
            return;
        }

        var parameters = new DialogParameters<RegenerateAllDialog> { { x => x.Sections, targets } };
        var dialog = await DialogService.ShowAsync<RegenerateAllDialog>("Regenerate the whole issue", parameters);
        var result = await dialog.Result;
        if (result is null || result.Canceled) return;
        var instruction = result.Data as string;

        _generating = true;
        StateHasChanged();
        try
        {
            var count = await WithChatAsync(c => c.RegenerateAllAsync(PostId,
                string.IsNullOrWhiteSpace(instruction) ? null : instruction));
            Snackbar.Add(count == 0
                ? "The model proposed nothing — try again with an instruction."
                : $"{count} suggestion(s) — review them on the cards.",
                count == 0 ? Severity.Warning : Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Regenerate failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _generating = false;
            await ReloadSectionsAsync();
        }
    }
```

- [ ] **Step 5: Add the toolbar row**

In the markup, immediately after the `@if (_pushing || _generating) { <MudProgressLinear … /> }` block:

```razor
    <div class="d-flex align-center flex-wrap mb-2" style="gap:12px">
        <MudTooltip Text="@(_history.UndoLabel is null ? "Nothing to undo" : $"Undo: {_history.UndoLabel}")">
            <MudButton StartIcon="@Icons.Material.Filled.Undo" Variant="Variant.Outlined" Size="Size.Small"
                       Disabled="@(AnyBusy || _history.UndoLabel is null)" OnClick="UndoAsync">Undo</MudButton>
        </MudTooltip>
        <MudTooltip Text="@(_history.RedoLabel is null ? "Nothing to redo" : $"Redo: {_history.RedoLabel}")">
            <MudButton StartIcon="@Icons.Material.Filled.Redo" Variant="Variant.Outlined" Size="Size.Small"
                       Disabled="@(AnyBusy || _history.RedoLabel is null)" OnClick="RedoAsync">Redo</MudButton>
        </MudTooltip>
        <MudButton StartIcon="@Icons.Material.Filled.Autorenew" Variant="Variant.Outlined" Size="Size.Small"
                   Disabled="@AnyBusy" OnClick="RegenerateAllAsync">Regenerate all</MudButton>
        <MudSpacer />
        <MudTooltip Text="The model every ✨ on this page runs on — change it in AI Studio">
            <MudChip T="string" Size="Size.Small" Icon="@Icons.Material.Filled.Memory"
                     Variant="Variant.Outlined">@_modelLabel</MudChip>
        </MudTooltip>
    </div>
```

- [ ] **Step 6: Verify it compiles and the suite still passes**

Run: `dotnet build ContentAutomatorX.slnx && dotnet test ContentAutomatorX.slnx`
Expected: Build succeeded, 0 warnings; 339 tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: undo, redo, regenerate-all and model badge in the issue toolbar (#composer)"
```

---

### Task 10: Live walkthrough

**Files:** none changed unless a defect is found.

This task is verification, not implementation. The composer has no bUnit harness, so the UI paths added in Tasks 7–9 are unproven until driven live. Run the app and work through the checklist; if a step fails, fix it and note the fix in the commit.

- [ ] **Step 1: Publish and run**

```bash
dotnet publish src/ContentAutomatorX.Web -c Release -o ./.local-publish
./.local-publish/ContentAutomatorX.Web.exe
```

Open `http://localhost:5090`. (Per the ledger, `dotnet run` has served 500s on framework assets with SDK 10.0.302 — publish and run the exe.)

- [ ] **Step 2: Work the checklist**

Open an existing newsletter issue with a header and at least two topics.

- [ ] a. The toolbar shows Undo (disabled), Redo (disabled), Regenerate all, and a model chip. The chip reads the tenant's model, or `CLI default` when neither the tenant nor appsettings names one.
- [ ] b. Edit a topic's body via the card and Apply. Undo becomes enabled with tooltip "Undo: Edit section". Click Undo — the old text returns. Redo restores the edit.
- [ ] c. Delete a topic, then Undo. The section returns in its original position with its title and body.
- [ ] d. Open the Chat tab. Ask "make the intro punchier". A spinner runs; when it finishes the assistant replies and the Header card shows Current / Proposed with Accept and Reject.
- [ ] e. Click Reject. The block disappears, the header text is unchanged.
- [ ] f. Ask again, then click Accept. The header updates, the preview reflects it, and Undo's tooltip reads "Undo: Accept suggestion". Undo restores the previous intro.
- [ ] g. Ask for a change, then — before accepting — edit that same section by hand and Apply. Click Accept: the "This section changed" dialog appears. "Keep mine" leaves your text; repeating and choosing "Replace it" applies the suggestion.
- [ ] h. Click Regenerate all. The dialog lists the header and each topic by name. Confirm; suggestions appear on those cards and **not** on the sponsor or footer.
- [ ] i. Reload the page. The conversation is still there and the pending suggestions are still on their cards.
- [ ] j. Switch to another tenant and back. No cross-tenant bleed; the chat belongs to the issue.

- [ ] **Step 3: Record the result**

Report which checklist items passed, and for any that failed, what was fixed. Run the full suite once more:

Run: `dotnet test ContentAutomatorX.slnx`
Expected: PASS, 339 tests.

- [ ] **Step 4: Commit (only if fixes were needed)**

```bash
git add -A
git commit -m "fix: issue chat walkthrough findings (#composer)"
```

---

## Plan self-review

**Spec coverage.** §4 data model → Task 1. §5 AI contract → Tasks 2 and 5 (`BuildPrompt` covers §5.1's five parts; `ChatReplyParser` plus `StoreProposalsAsync` cover §5.2–5.3). §6.1 service → Task 5 (+ `PurgeAsync` in Task 6). §6.2 card → Task 7. §6.3 page → Tasks 8 and 9. §6.4 badge → Task 9. §6.5 job → Task 6. §7 undo → Tasks 3 and 4. §8 retention → Task 6. §9 error handling → the retry loop in Task 5, the stale dialog in Task 8, snackbars throughout. §10 testing → Tasks 1–6, with the UI covered by Task 10 because no bUnit harness exists.

**Two spec items are intentionally not implemented as written.** §9's "Snapshot write fails → the mutation fails with it" is satisfied structurally rather than by a catch block: `SnapshotAsync` runs before the mutation and its exception propagates, so the mutation never starts. And §7.4's note that undo does not resurrect proposals is behaviour that falls out of the design — `RestoreAsync` touches sections and post fields only — so it needs no code.

**Known gaps a reviewer should not re-flag.** No test drives `MaxHistoryMessages` truncation at the boundary; the constant is exercised only indirectly. No test covers two concurrent circuits sharing one undo stack — spec §7.4 accepts that as a known limitation of a single-user tool. Both are deliberate.

**Type consistency check.** `IssueChatService` ctor is `(IAppDbContext, ILlmBackend, ILlmSettingsProvider, IssueHistoryService)` in Task 5 and constructed that way in Tasks 5 and 6's tests. `IssueComposerService` gains its fifth parameter in Task 4 and every construction site in that task passes it. `ChatReply` carries `(Reply, Edits, DroppedEdits)` in Task 2 and is consumed with those names in Task 5. `HistoryState(UndoLabel, RedoLabel)` is produced in Task 3 and read in Task 9.
