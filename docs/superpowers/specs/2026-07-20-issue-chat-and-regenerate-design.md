# Issue Chat, Whole-Issue Regenerate & Composer Undo — Design

**Date:** 2026-07-20
**Status:** Approved, not yet implemented
**Supersedes nothing.** Extends `2026-07-19-newsletter-composer-design.md`.

## 1. Goal

Three additions to the issue composer:

1. **Chat** — a back-and-forth conversation with the model about the issue you are editing.
2. **Regenerate all** — one button that offers a fresh Header and a fresh blurb for every topic.
3. **Undo / redo** — reversible history over every composer edit.

The existing per-section ✨ rewrite stays exactly as it is.

Chat and regenerate-all produce **proposals**, never direct writes. This is the organising idea
of both: the model suggests, the section card shows current-versus-proposed, and nothing reaches
the newsletter until you accept that specific proposal.

Undo is the safety net underneath everything else — including the writes that proposals, ✨, and
hand edits do make.

### 1.1 Why this does not break the composer's overwrite rule

`2026-07-19-newsletter-composer-design.md` records a deliberate rule:

> per-topic ✨ is the sole overwrite path, and it acts on one explicitly chosen section

A whole-issue regenerate looks like a direct contradiction, and a direct-write implementation
would be one. The proposal model preserves the rule instead: every overwrite remains an
explicit, per-section, deliberate act. Regenerate-all does not rewrite six sections — it offers
six rewrites, and you take the ones you want.

The rule is therefore restated, not revoked:

> Every write to a section's text is explicitly chosen, one section at a time. ✨ and Accept are
> the only two paths that write, and both are reversible.

## 2. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | The model may edit existing sections only — never add, remove, or reorder | User requirement. Structurally locks the riskiest class of change |
| 2 | The lock is enforced in the service, not the prompt | A prompt instruction can be talked around; a section-ID whitelist cannot |
| 3 | Chat and regenerate-all share one proposal mechanism and one review surface | One code path, one thing to learn; satisfies "list what it will overwrite" for free |
| 4 | Before/after renders inline in each `SectionCard` | Keeps the change next to the section it affects; no new screen |
| 5 | Chat persists per issue; pending proposals persist with it | A saved transcript saying "I've proposed a shorter intro" with nothing to accept would be incoherent |
| 6 | At most one pending proposal per section | A later turn touching the same section replaces the earlier proposal. Avoids a proposal queue nobody asked for |
| 7 | Transcript is flattened into the single `GenerateAsync` prompt | No `ILlmBackend` change; stays provider-neutral, which is the stated goal of the model-selector work |
| 8 | History caps at the last 20 messages | Bounds prompt growth against the 300 s CLI timeout. Safe because the current issue is always re-sent in full |
| 9 | Chat may edit any section type; regenerate-all touches Header + Topic only | Fixing a footer typo by chat is useful; regenerating tenant boilerplate from scratch is not |
| 10 | Proposals store the body they were generated against (`BaselineBodyMd`) | Persisted proposals can outlive the text they were based on. Without a baseline, a stale Accept silently discards hand edits |
| 11 | Retention: 30 days after publish; 90 days after last chat activity if never published | `PublishedAt` alone leaks every unpublished issue's thread forever (see §8) |
| 12 | New `IssueChatService`, not more methods on `IssueComposerService` | `IssueComposerService` is already 294 lines with 13 public methods |
| 13 | **Undo is snapshot-based, not a command journal** | One restore routine is correct for all eight mutations and for any added later; eight hand-written inverses are eight chances to be subtly wrong (see §7.1) |
| 14 | A snapshot covers the whole issue: post header fields plus all sections | Makes "everything in the composer" honest, and costs three extra strings |
| 15 | The toolbar shows the resolved model name, not the effort | User requirement. Effort is a tuning detail; which model wrote your text is editorial context |

## 3. Out of scope (deliberately)

- **Streaming replies.** Filed as **issue #18**. `ClaudeCliBackend` runs the CLI to completion and
  parses the final JSON; streaming needs `IProcessRunner` changes and benefits all six LLM call
  sites, not just chat, so it is designed on its own. Chat shows a spinner until then.
- **App-wide undo.** Undo here covers the composer only. Reversing a source deletion or a recipe
  edit would need a command journal across every service — a separate project.
- **Cross-issue chat.** A thread belongs to exactly one `Post`.
- **Model choice per chat turn.** Chat resolves tenant `LlmSettings` like every other call site.
  The toolbar reports which model that resolved to (§6.4); it does not let you override it here.

## 4. Data model

Three new entities. All cascade-delete from `Post`, following the `IssueSection` → `Post`
cascade already configured in `AppDbContext.OnModelCreating` (the only cascade in the codebase).

```csharp
public class IssueChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public required string Role { get; set; }        // ChatRoles.User | ChatRoles.Assistant
    public required string Text { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class IssueSectionProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public Guid SectionId { get; set; }
    public string? ProposedTitle { get; set; }       // null = title unchanged
    public string? ProposedBodyMd { get; set; }      // null = body unchanged
    public required string BaselineBodyMd { get; set; }  // section body when proposed; "" if it had none
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class IssueRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public required string Stack { get; set; }       // RevisionStacks.Undo | RevisionStacks.Redo
    public int Ordinal { get; set; }                 // monotonic per post; highest = top of stack
    public required string Label { get; set; }       // "Regenerate all", "Delete section", …
    public required string SnapshotJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

`ChatRoles` and `RevisionStacks` join `SectionTypes` in `Constants.cs` as plain string consts,
matching the existing convention.

**Existence is status.** There is no `Accepted`/`Rejected` enum on proposals: Accept applies the
text and deletes the row, Reject deletes the row. A pending proposal is a row that exists.

**Indexes.** `IssueChatMessage` on `PostId`; `IssueSectionProposal` unique on `SectionId`
(enforcing decision 6) plus an index on `PostId`; `IssueRevision` on `(PostId, Stack, Ordinal)`.

All three must be added to **`IAppDbContext`** (Application) and **`AppDbContext`**
(Infrastructure) — twin lists that must stay in sync, and three test fakes implement
`IAppDbContext`.

Migration: `dotnet ef migrations add IssueChatProposalsAndRevisions --project src/ContentAutomatorX.Infrastructure --startup-project src/ContentAutomatorX.Web`.
Startup calls `db.Database.Migrate()`, and the integration test harness does the same, so a new
migration is exercised by the whole integration suite automatically.

## 5. The AI contract

### 5.1 Prompt

Assembled per turn, in this order:

1. **Role framing** — the model edits an existing newsletter; it may rewrite the title and body
   of sections it is shown, and may not add, remove, or reorder anything.
2. **Voice and tone** — `tenant.VoiceProfile`, `recipe.ToneModifiers`, `recipe.Language` when
   present, exactly as `BuildTopicsPrompt` does today.
3. **Current issue state** — every section, always in full: id, type, title, body.
4. **Transcript** — up to the last 20 messages, oldest first, labelled by role.
5. **The new user message.**

Because step 3 is re-sent in full every turn, truncating step 4 degrades gracefully: the model
may lose conversational nuance, but it never loses the document.

### 5.2 Response

```json
{
  "reply": "prose shown in the chat",
  "edits": [
    { "sectionId": "5c2f…", "title": "Local LLMs got faster", "bodyMd": "Ollama's new runtime…" }
  ]
}
```

`edits` may be empty — a question ("is this intro too salesy?") gets a reply and no proposals.
Within an edit, an omitted or null `title` or `bodyMd` means that field is unchanged.

### 5.3 Parsing and validation

A static `TryParseChatReply(string text, out ChatReply? reply)` mirroring the existing
`IssueComposerService.TryParseTopics`: strip ``` fences, deserialize, validate.

Validation is where the structural lock lives:

- An edit whose `sectionId` is not a section of **this post** is dropped.
- An edit with neither `title` nor `bodyMd` is dropped.
- Dropped edits are **surfaced, not silent** — the UI reports that the model proposed a change
  to an unknown section and it was ignored.

There is no add/remove/reorder verb in the schema, so those operations cannot be expressed. An
invented ID is the only way to attempt one, and the whitelist refuses it.

## 6. Components

### 6.1 `IssueChatService` (new, Application layer)

```csharp
public class IssueChatService(
    IAppDbContext db, ILlmBackend llm, ILlmSettingsProvider llmSettings, IssueHistoryService history)
{
    Task<IssueChat> GetThreadAsync(Guid postId, CancellationToken ct = default);
    Task<ChatTurnResult> SendAsync(Guid postId, string message, CancellationToken ct = default);
    Task<int> RegenerateAllAsync(Guid postId, string? instruction, CancellationToken ct = default);
    Task AcceptAsync(Guid proposalId, bool force, CancellationToken ct = default);
    Task RejectAsync(Guid proposalId, CancellationToken ct = default);
    Task<int> PurgeAsync(DateTimeOffset now, CancellationToken ct = default);
}

public record IssueChat(
    IReadOnlyList<IssueChatMessage> Messages,
    IReadOnlyList<IssueSectionProposal> Proposals);

public record ChatTurnResult(string Reply, int ProposalCount, int DroppedEdits);
```

`IssueChat` carries messages plus pending proposals — one load for the whole tab.
`ChatTurnResult`'s `DroppedEdits` is what lets the UI report ignored edits per §5.3.

`SendAsync` persists the user message **before** calling the model, so a failed turn leaves your
message in the thread to retry from.

`RegenerateAllAsync` is the same pipeline with a canned instruction, scoped to Header and Topic
sections only (decision 9). It writes no user-visible chat message — regenerate-all is a button,
not a conversation turn — but it produces proposals identically. It returns 0 when the issue has
no Header and no Topic sections; the UI then reports "nothing to regenerate" rather than opening
an empty confirm dialog.

`AcceptAsync(force: false)` throws when the section's current `BodyMd` differs from the
proposal's `BaselineBodyMd`; the UI then asks and retries with `force: true`. Accept snapshots
first (§7), so it is undoable either way.

`PurgeAsync` returns the number of threads collected and lives here rather than in a separate
retention service: it is a query over chat data, and a service that owns the entity should own
its lifecycle.

Like every LLM call site, it resolves `await llmSettings.GetAsync(tenantId, ct)` and passes the
result to `GenerateAsync`.

### 6.2 `SectionCard.razor` (modified)

Gains `[Parameter] public IssueSectionProposal? Proposal { get; set; }` and two callbacks,
`OnAcceptProposal` / `OnRejectProposal`. When `Proposal` is null the card renders exactly as it
does today.

```
┌─ 2. Local LLMs got faster ────── ✨ ↑ ↓ ✕ ┐
│ CURRENT                                    │
│ Ollama shipped a new runtime that people   │
│ say is quicker.                            │
│ ── proposed ─────────────────────────────  │
│ Ollama's new runtime cuts cold starts to   │
│ under a second.                            │
│                                            │
│           [ Accept ]   [ Reject ]          │
└────────────────────────────────────────────┘
```

A proposed **title** change renders in the header row as `old → new`.

### 6.3 `IssueEditor.razor` (modified)

The right pane becomes tabbed. Structure stays fully visible, so proposals appear in the cards
while you type.

```
┌─ The Weekly #12 ──────────── [Save] [Export] [Push] ─┐
│ Subject: …                                ✨ Subjects│
│ ↶ Undo   ↷ Redo   ↻ Regenerate all      ◆ opus      │
├───────────────┬──────────────────────────────────────┤
│ STRUCTURE     │ [ Preview │ Chat ]                   │
│               │                                      │
│ ┌─ 1. Header ┐│ you  make the intro punchier         │
│ │  ── proposed│                                      │
│ │  Morning — …│ ai   Done — I've proposed a          │
│ │  ✓ accept  ││      shorter intro. See the          │
│ └────────────┘│      Header card. →                  │
│ ┌─ 2. Local ─┐│                                      │
│ │  Ollama…   ││ ┌──────────────────────────────────┐ │
│ └────────────┘│ │ ask for a change…                │ │
│               │ └──────────────────────────────────┘ │
└───────────────┴──────────────────────────────────────┘
```

`↻ Regenerate all` opens a confirm dialog naming the sections it will propose against, then
fills the cards. It never writes.

`↶ Undo` / `↷ Redo` are disabled when their stack is empty, and their tooltips name what they
will do — "Undo: Regenerate all" — read from the revision's `Label`.

`AnyBusy` gains `_chatting`, gating the whole page as the existing flags do.

Chat calls need the same scope-per-call treatment as composer calls, but `WithComposerAsync` is
typed to `IssueComposerService`, so the page gains a sibling `WithChatAsync` (both overloads)
resolving `IssueChatService` from a fresh scope. The comment on the existing helper explains why
LLM calls must not reuse the circuit-lived `DbContext`; that reasoning applies unchanged, and
chat turns are the longest-running calls in the page.

`ReloadSectionsAsync` additionally loads pending proposals and undo/redo availability, so
accepting a proposal refreshes sections, preview, proposals, and button state together.

### 6.4 Model badge

The toolbar shows the model that will actually run, resolved through the existing chain:
tenant setting → appsettings → CLI default.

- Tenant or appsettings names a model → show it (`◆ opus`).
- Neither does → show `◆ CLI default`. The CLI chooses and the app genuinely does not know
  which; naming a guess would be worse than admitting this.

Effort is deliberately not shown (decision 15). The badge reads from
`ILlmSettingsProvider.GetAsync` — the *resolved* value, not `GetStoredAsync` — because the
question it answers is "what wrote this text", not "what did I configure".

It sits in the toolbar rather than the chat tab because the tenant model applies to every ✨
action on the page. Putting it only in chat would imply chat is special.

### 6.5 `ChatRetentionJob` (new, Web/Jobs)

A `BackgroundService` on a daily `PeriodicTimer`, registered beside `SchedulerService` and
`PlatformSyncJob`, delegating to `IssueChatService.PurgeAsync(DateTimeOffset now, ct)`.

It is a separate job rather than a branch inside `PlatformSyncJob`: retention is not platform
sync, and folding unrelated work into an existing tick is how jobs become junk drawers.

## 7. Undo and redo

### 7.1 Why snapshots rather than a command journal

Eight composer operations mutate state: add, remove, move, update, ✨ regenerate, generate
topics, add topics from items, and accept proposal. A command journal needs a correct inverse
for each — and delete's inverse must restore the row's original `Id`, because `@key` and
`IssueSectionProposal.SectionId` both reference it. Eight inverses are eight chances to be
subtly wrong, and every operation added later silently needs a ninth.

A snapshot has **one** restore routine that is correct for all of them, and stays correct for
operations not yet written. Newsletters are small — fifteen sections of a few hundred characters
is roughly 8 KB of JSON — so the storage cost is trivial next to the correctness gain.

### 7.2 Model

A snapshot is the issue's editable state: `Post.Title`, `Subject`, `PreviewText`, and every
`IssueSection` in full, including `Id` and `Position`.

Two stacks, both rows in `IssueRevision` distinguished by `Stack`:

- Every mutation pushes the state **before** it onto Undo, then clears Redo.
- Undo pushes current state onto Redo, pops the top of Undo, restores it.
- Redo pushes current state onto Undo, pops the top of Redo, restores it.

Restore is a reconcile against the snapshot: delete sections absent from it, re-create sections
missing from the database preserving their original `Id`, update the rest, then write the post
header fields.

Each stack caps at **25 entries**; pushing past the cap trims the oldest. Revisions cascade with
the post, and are dropped by the same purge that collects chat threads (§8).

### 7.3 `IssueHistoryService` (new, Application layer)

```csharp
public class IssueHistoryService(IAppDbContext db)
{
    Task SnapshotAsync(Guid postId, string label, CancellationToken ct = default);
    Task<string?> UndoAsync(Guid postId, CancellationToken ct = default);   // returns label undone
    Task<string?> RedoAsync(Guid postId, CancellationToken ct = default);
    Task<HistoryState> GetStateAsync(Guid postId, CancellationToken ct = default);
}

public record HistoryState(string? UndoLabel, string? RedoLabel);
```

`SnapshotAsync` is the first line of every mutating method in `IssueComposerService` and of
`IssueChatService.AcceptAsync`. There is no single chokepoint that could enforce this, so a test
enumerates the public mutating methods and asserts each one produces a revision — that test is
the guard against a future method forgetting.

### 7.4 What undo does not cover

Stated explicitly so the gaps are chosen, not discovered:

- **`ContentItemStatus.Used`.** `GenerateTopicsAsync` marks source items consumed, and undo does
  not un-consume them. Harmless in practice: that method selects sections by empty body, not by
  item status, so re-running it after an undo still works. The only visible effect is the inbox.
- **Proposals.** Accepting a proposal deletes it; undoing restores your text but does not
  resurrect the proposal. Ask the model again if you want it back.
- **Chat messages.** The transcript is a record of what was said and is never rewritten by undo.
- **Concurrent editors.** History is per post, not per circuit. Two tabs open on one issue share
  one undo stack. Acceptable in a single-user tool; noted so it is not a surprise.

## 8. Retention

| Issue state | Purged |
|---|---|
| `Published` | 30 days after `PublishedAt` |
| Anything else | 90 days after the thread's most recent message |
| Post deleted | Immediately, by cascade |

Chat messages, proposals, and revisions are collected together.

The second rule exists because of a real leak. `PublishedAt` is set in exactly one place — the
hourly MailerLite poll, on the `Pushed → Published` transition in `PostSyncService.TickAsync`.
Pushing does **not** set it; the user still has to hit Send in MailerLite. An issue that is
drafted, chatted about at length, and then abandoned has `PublishedAt == null` forever, so a
publish-anchored rule would never collect its thread.

Anchoring the unpublished rule to **last message** rather than creation means an issue you are
still working on is never collected: chatting resets its clock. An issue with revisions but no
chat messages falls back to the newest revision's timestamp, so a heavily edited but never
discussed issue is not collected while in use either.

`PublishedAt` is a detection timestamp, skewed later than the true send by up to the poll
interval. Irrelevant at 30-day granularity.

The sweep must split its filters — SQLite cannot translate an enum-status comparison and a date
comparison in the same server-side query. `PostSyncService` documents this and is the pattern to
follow.

## 9. Error handling

| Failure | Behaviour |
|---|---|
| Model returns non-JSON | One retry appending the "respond with ONLY the JSON object" nudge, mirroring `GenerateTopicsAsync`'s 2-attempt loop. Then a snackbar; the user message stays in the thread |
| Model proposes an unknown `sectionId` | Edit dropped; the turn still applies its valid edits; the UI reports how many were ignored |
| Accept on a stale proposal | Blocked, with a dialog naming the conflict; accepting anyway overwrites, rejecting keeps your text |
| Accept on a section deleted meanwhile | Proposal is deleted, snackbar explains |
| CLI timeout (300 s default) | Surfaces as the turn's exception; user message remains |
| Chat load fails | Chat tab shows an error with a retry; the rest of the editor stays usable |
| Undo with an empty stack | Button is disabled; the service also returns null rather than throwing |
| Snapshot write fails | The mutation fails with it — a mutation that cannot be undone must not happen silently |

## 10. Testing

**Unit — parsing and validation.** `TryParseChatReply` against: valid JSON; fenced JSON; prose
with no JSON; empty `edits`; an edit with an unknown `sectionId`; an edit with neither field.
These mirror the existing `TryParseTopics` tests.

**Unit — chat service.** Using the existing `SequenceLlm` fake: a turn persists user then
assistant message in order; a turn with edits creates proposals with the correct baseline; a
second turn touching the same section replaces rather than duplicates; a failed turn leaves the
user message and creates no proposals; `RegenerateAllAsync` proposes against Header and Topics
and skips Sponsor/Button/Divider/Footer/LegacyBody.

**Unit — accept/reject.** Accept writes title and body and deletes the row; accept with a
changed baseline throws without writing; `force: true` writes anyway; reject deletes without
writing.

**Unit — history.** Undo after each of the eight mutations restores the prior state, delete
included, with the original section `Id` intact; redo re-applies it; a new mutation clears the
redo stack; the stack trims at 25; undo on an empty stack returns null. Plus the enumeration
test from §7.3 asserting every public mutating method snapshots.

**Unit — retention.** Published 31 days ago purges; published 29 days ago does not; unpublished
with last message 91 days ago purges; unpublished with a recent message survives even when
created long ago; an issue with revisions but no messages uses the revision timestamp.

**Integration.** Migration applies; the cascade removes messages, proposals, and revisions when
a post is deleted; the unique index rejects a second pending proposal for one section.

## 11. Open questions

1. **Should Accept-all exist?** A regenerate-all producing six proposals means six clicks. An
   "accept all proposed" button is an obvious convenience and an obvious way to defeat the
   review this design is built around. Deferred until the click count actually annoys — and
   cheaper to allow now that undo can reverse it.
2. **Should the chat see previously rejected proposals?** Today it does not — a rejection is
   silent, so the model may re-propose something you already refused. Feeding rejections back
   ("the user declined this") would help it learn within a thread, at the cost of prompt size.
3. **Does `_extraInstructions` survive?** The shared instruction box feeding Generate and every
   ✨ overlaps conceptually with chat. Left alone for now; revisit once chat has been used.
4. **Should undo cover hand edits keystroke by keystroke?** Currently a hand edit snapshots once
   when applied, so undo reverts the whole edit rather than the last word. The browser's native
   text undo covers the finer grain while the field has focus. Revisit if that seam is felt.
