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
