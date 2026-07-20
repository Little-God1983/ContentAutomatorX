namespace ContentAutomatorX.Domain.Entities;

public class IssueSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public int Position { get; set; }          // 0-based, contiguous per post
    public required string Type { get; set; }  // SectionTypes.*
    public string? Title { get; set; }         // topic heading / sponsor name
    public string? BodyMd { get; set; }        // markdown copy
    public string? ImageUrl { get; set; }      // topic image / sponsor logo (absolute URL)
    public string? LinkUrl { get; set; }       // read-more / sponsor target / CTA target
    public string? LinkText { get; set; }      // CTA label
    public Guid? SourceItemId { get; set; }    // ContentItem a topic came from (null = manual)
}
