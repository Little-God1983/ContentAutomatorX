namespace ContentAutomatorX.Domain.Entities;

public class Recipe
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }          // DraftKinds.*
    public bool IsEnabled { get; set; } = true;
    public string SourceIdsJson { get; set; } = "[]";  // empty array = all tenant sources
    public string SelectionJson { get; set; } = "{}";  // SelectionRules
    public Guid PromptTemplateId { get; set; }
    public string? ToneModifiers { get; set; }
    public string? LengthTarget { get; set; }
    public string? Language { get; set; }
    public string OutputJson { get; set; } = "{}";     // RecipeOutput
    public string? ScheduleCron { get; set; }          // null = manual only
    public DateTimeOffset? LastRunAt { get; set; }
    public Guid? TargetPlatformId { get; set; } // set → each run also creates a review-queue Post
}
