namespace ContentAutomatorX.Domain.Entities;

public class IssueSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PostId { get; set; }
    public int Position { get; set; }          // 0-based, contiguous per post
    public required string Type { get; set; }  // SectionTypes.*
    public string? Title { get; set; }         // topic heading / sponsor name
    public string? BodyMd { get; set; }        // markdown copy
    public string? ImageUrl { get; set; }      // legacy/auto-metadata hotlink fallback (absolute URL)
    public string? ImageKey { get; set; }      // staging file name under data/newsletter-images; null = none
    public string? LinkUrl { get; set; }       // read-more / sponsor target / CTA target
    public string? LinkText { get; set; }      // CTA label
    public string? Category { get; set; }      // topic label — "Tutorial", "News"
    public Guid? SourceItemId { get; set; }    // ContentItem a topic came from (null = manual)
}
