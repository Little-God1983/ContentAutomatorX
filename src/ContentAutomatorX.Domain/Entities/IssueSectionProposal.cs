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
