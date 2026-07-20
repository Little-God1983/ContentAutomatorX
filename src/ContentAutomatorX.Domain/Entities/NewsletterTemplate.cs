namespace ContentAutomatorX.Domain.Entities;

/// <summary>A tenant's own email design, stored as one HTML document carved into named blocks.
/// Text outside any block is ignored, so the file's explanatory header comment survives editing.</summary>
public class NewsletterTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Html { get; set; }
    /// <summary>At most one per tenant. Enforced by NewsletterTemplateService, not by the database:
    /// the EF SQLite provider has no filtered unique index, and the service is the only writer.</summary>
    public bool IsDefault { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
