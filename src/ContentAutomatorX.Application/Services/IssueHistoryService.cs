using System.Text.Json;
using ContentAutomatorX.Application.Persistence;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.Application.Services;

public record HistoryState(string? UndoLabel, string? RedoLabel);

internal record SectionSnapshot(Guid Id, int Position, string Type, string? Title, string? BodyMd,
    string? ImageUrl, string? LinkUrl, string? LinkText, Guid? SourceItemId, string? Category);

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
                s.ImageUrl, s.LinkUrl, s.LinkText, s.SourceItemId, s.Category)).ToList());
    }

    private async Task RestoreAsync(Guid postId, IssueSnapshot snapshot, CancellationToken ct)
    {
        var post = await db.Posts.SingleAsync(p => p.Id == postId, ct);
        post.Title = snapshot.Title;
        post.Subject = snapshot.Subject;
        post.PreviewText = snapshot.PreviewText;

        var live = await db.IssueSections.Where(s => s.PostId == postId).ToListAsync(ct);
        var wanted = snapshot.Sections.ToDictionary(s => s.Id);
        var doomed = live.Where(s => !wanted.ContainsKey(s.Id)).ToList();
        foreach (var section in doomed) db.IssueSections.Remove(section);
        if (doomed.Count > 0)
        {
            // Same reasoning as RemoveSectionAsync: no FK, so an undo that removes a section would
            // otherwise strand its proposal.
            var doomedIds = doomed.Select(s => s.Id).ToList();
            db.IssueSectionProposals.RemoveRange(
                await db.IssueSectionProposals.Where(p => doomedIds.Contains(p.SectionId)).ToListAsync(ct));
        }

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
            section.Category = want.Category;
        }
    }
}
