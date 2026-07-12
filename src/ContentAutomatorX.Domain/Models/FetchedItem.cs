namespace ContentAutomatorX.Domain.Models;

public record FetchedItem(
    string ExternalId,
    string Title,
    string? Url,
    string? Author,
    string Body,
    string MetadataJson,
    DateTimeOffset? PublishedAt);
