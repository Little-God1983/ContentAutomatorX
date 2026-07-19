namespace ContentAutomatorX.Domain.Entities;

public enum ContentItemStatus { New, Selected, Ignored, Used }

public class ContentItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SourceId { get; set; }
    public required string ExternalId { get; set; }
    public required string Title { get; set; }
    public string? Url { get; set; }
    public string? NormalizedUrl { get; set; }
    public string? Author { get; set; }
    public string Body { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";   // e.g. {"score":123}
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; } = DateTimeOffset.UtcNow;
    public ContentItemStatus Status { get; set; } = ContentItemStatus.New;
}
