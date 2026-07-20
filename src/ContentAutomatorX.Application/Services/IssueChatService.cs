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

        // ask is null: the transcript already ends with the message just persisted above, so
        // re-appending it would show the model the same question twice.
        var reply = await RunTurnAsync(postId, ask: null, includeTranscript: true, restrictTo: null, ct);
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

    private async Task<ChatReply> RunTurnAsync(Guid postId, string? ask, bool includeTranscript,
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
        HashSet<Guid>? restrictTo, List<IssueChatMessage> transcript, string? ask)
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
        sb.AppendLine(ask ?? "Reply to the author's last message above.");
        return sb.ToString();
    }

    /// <summary>Writes proposals for the edits that survive validation. This is where the structural
    /// lock is enforced: an id that is not a section of this issue (or is outside restrictTo) is
    /// dropped, so the model cannot invent a section by inventing an id.</summary>
    private async Task<(int Stored, int Dropped)> StoreProposalsAsync(Guid postId,
        IReadOnlyList<ChatEdit> edits, HashSet<Guid>? restrictTo, CancellationToken ct)
    {
        if (edits.Count == 0) return (0, 0);
        // One reply can name the same section twice — a title change and a body change emitted as
        // two objects instead of one. Merge them per field, latest set wins. Without this both
        // stage an INSERT, the unique index on SectionId rejects the second, and the
        // DbUpdateException takes down the whole turn including every other valid proposal in it.
        var merged = edits.GroupBy(e => e.SectionId)
            .Select(g => new ChatEdit(g.Key,
                g.Select(e => e.Title).LastOrDefault(t => t is not null),
                g.Select(e => e.BodyMd).LastOrDefault(b => b is not null)))
            .ToList();
        var sections = (await db.IssueSections.Where(s => s.PostId == postId).ToListAsync(ct))
            .ToDictionary(s => s.Id);
        var existing = await db.IssueSectionProposals.Where(p => p.PostId == postId).ToListAsync(ct);

        var stored = 0;
        var dropped = 0;
        foreach (var edit in merged)
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
