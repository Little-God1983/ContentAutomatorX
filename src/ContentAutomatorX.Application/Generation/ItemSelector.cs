using System.Text.Json;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Application.Generation;

public static class ItemSelector
{
    public static List<ContentItem> Select(IEnumerable<ContentItem> candidates, SelectionRules rules,
        IReadOnlySet<Guid> usedByRecipe, DateTimeOffset now)
    {
        var query = candidates
            .Where(i => i.Status != ContentItemStatus.Ignored)
            .Where(i => !usedByRecipe.Contains(i.Id));

        if (rules.TimeWindowDays is int days)
        {
            var cutoff = now.AddDays(-days);
            query = query.Where(i => (i.PublishedAt ?? i.FetchedAt) >= cutoff);
        }
        if (rules.MinScore is int min)
            query = query.Where(i => Score(i) >= min);
        if (rules.IncludeKeywords.Length > 0)
            query = query.Where(i => rules.IncludeKeywords.Any(k => Matches(i, k)));
        if (rules.ExcludeKeywords.Length > 0)
            query = query.Where(i => !rules.ExcludeKeywords.Any(k => Matches(i, k)));

        return query
            .OrderByDescending(Score)
            .ThenByDescending(i => i.PublishedAt ?? i.FetchedAt)
            .Take(rules.MaxItems)
            .ToList();
    }

    private static bool Matches(ContentItem i, string keyword) =>
        i.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
        i.Body.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static int Score(ContentItem i)
    {
        try
        {
            using var doc = JsonDocument.Parse(i.MetadataJson);
            return doc.RootElement.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
        }
        catch { return 0; }
    }
}
