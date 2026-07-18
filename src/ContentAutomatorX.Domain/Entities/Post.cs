namespace ContentAutomatorX.Domain.Entities;

public enum PostStatus { Draft, Pushed, Published, Failed }

public class Post
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PlatformId { get; set; }
    public Guid? RecipeId { get; set; }        // the automation this issue is based on
    public Guid? DraftId { get; set; }         // content payload; null until composed/typed
    public required string Kind { get; set; }  // DraftKinds.*
    public required string Title { get; set; }
    public string? Subject { get; set; }
    public string? PreviewText { get; set; }
    public PostStatus Status { get; set; } = PostStatus.Draft;
    public bool NeedsReview { get; set; }
    public string? SourceIdsJson { get; set; } // this issue's source set; null = automation's set
    public int WindowDays { get; set; } = 7;   // material window for candidate selection
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ExternalId { get; set; }    // MailerLite campaign id
    public string? ExternalUrl { get; set; }
    public string StatsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
