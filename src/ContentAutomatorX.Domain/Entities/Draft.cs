namespace ContentAutomatorX.Domain.Entities;

public enum DraftStatus { Generated, Delivered }

public class Draft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RecipeId { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public string Body { get; set; } = "";
    public string? TargetPlatform { get; set; }
    public string SourceItemIdsJson { get; set; } = "[]";
    public string? FilePath { get; set; }
    public DraftStatus Status { get; set; } = DraftStatus.Generated;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ModelUsed { get; set; }
}
