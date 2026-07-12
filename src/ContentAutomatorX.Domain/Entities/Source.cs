namespace ContentAutomatorX.Domain.Entities;

public class Source
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public required string Type { get; set; }          // SourceTypes.*
    public required string DisplayName { get; set; }
    public string ConfigJson { get; set; } = "{}";     // Reddit: {subreddit,sort,timeframe}; Rss: {feedUrl}
    public string? ScheduleCron { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastFetchedAt { get; set; }
}
