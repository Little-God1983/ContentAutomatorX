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
