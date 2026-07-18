using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.UnitTests;

public class ItemSelectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    private static ContentItem Item(string title, int score = 0, int ageDays = 1,
        ContentItemStatus status = ContentItemStatus.New, string body = "")
        => new()
        {
            Title = title, ExternalId = title, Body = body, Status = status,
            MetadataJson = $"{{\"score\":{score}}}",
            PublishedAt = Now.AddDays(-ageDays)
        };

    [Fact]
    public void Applies_window_score_keywords_order_and_max()
    {
        var items = new[]
        {
            Item("old high", score: 900, ageDays: 30),
            Item("fresh low", score: 5),
            Item("fresh high", score: 500),
            Item("fresh mid", score: 100),
            Item("ignored", score: 999, status: ContentItemStatus.Ignored),
            Item("excluded word crypto", score: 800)
        };
        var rules = new SelectionRules
        {
            TimeWindowDays = 7, MinScore = 10, MaxItems = 2,
            ExcludeKeywords = ["crypto"]
        };

        var result = ItemSelector.Select(items, rules, new HashSet<Guid>(), Now);

        Assert.Equal(["fresh high", "fresh mid"], result.Select(i => i.Title).ToArray());
    }

    [Fact]
    public void Scoreless_items_follow_source_listing_rank_not_date()
    {
        // the Reddit RSS fallback delivers no scores — rank must carry the hot order
        ContentItem Ranked(string title, int rank, int ageDays) => new()
        {
            Title = title, ExternalId = title, Body = "",
            MetadataJson = $"{{\"via\":\"rss\",\"rank\":{rank}}}",
            PublishedAt = Now.AddDays(-ageDays)
        };
        var items = new[]
        {
            Ranked("newest but rank 3", rank: 3, ageDays: 0),
            Ranked("oldest but rank 1", rank: 1, ageDays: 6),
            Ranked("rank 2", rank: 2, ageDays: 2)
        };

        var result = ItemSelector.Select(items, new SelectionRules { MaxItems = 3 }, new HashSet<Guid>(), Now);

        Assert.Equal(["oldest but rank 1", "rank 2", "newest but rank 3"],
            result.Select(i => i.Title).ToArray());
    }

    [Fact]
    public void Scored_items_still_order_by_score_first_rank_only_breaks_ties()
    {
        ContentItem Scored(string title, int score, int rank) => new()
        {
            Title = title, ExternalId = title, Body = "",
            MetadataJson = $"{{\"score\":{score},\"rank\":{rank}}}",
            PublishedAt = Now.AddDays(-1)
        };
        var items = new[]
        {
            Scored("low score rank 1", score: 10, rank: 1),
            Scored("high score rank 5", score: 500, rank: 5),
            Scored("tied A rank 4", score: 100, rank: 4),
            Scored("tied B rank 2", score: 100, rank: 2)
        };

        var result = ItemSelector.Select(items, new SelectionRules { MaxItems = 4 }, new HashSet<Guid>(), Now);

        Assert.Equal(["high score rank 5", "tied B rank 2", "tied A rank 4", "low score rank 1"],
            result.Select(i => i.Title).ToArray());
    }

    [Fact]
    public void Excludes_items_already_used_by_this_recipe_but_not_globally_used()
    {
        var usedByRecipe = Item("used by recipe", score: 50);
        var usedGlobally = Item("used by other recipe", score: 40, status: ContentItemStatus.Used);
        var items = new[] { usedByRecipe, usedGlobally };

        var result = ItemSelector.Select(items, new SelectionRules(), new HashSet<Guid> { usedByRecipe.Id }, Now);

        Assert.Equal(["used by other recipe"], result.Select(i => i.Title).ToArray());
    }

    [Fact]
    public void Include_keywords_require_a_match()
    {
        var items = new[] { Item("about comfyui nodes"), Item("about something else") };
        var rules = new SelectionRules { IncludeKeywords = ["ComfyUI"] };

        var result = ItemSelector.Select(items, rules, new HashSet<Guid>(), Now);

        Assert.Single(result);
        Assert.Equal("about comfyui nodes", result[0].Title);
    }

    [Fact]
    public void Missing_or_invalid_score_metadata_is_treated_as_score_zero()
    {
        var emptyJson = new ContentItem
        {
            Title = "empty", ExternalId = "empty", Body = "", MetadataJson = "{}",
            PublishedAt = Now.AddDays(-1)
        };
        var invalidJson = new ContentItem
        {
            Title = "invalid", ExternalId = "invalid", Body = "", MetadataJson = "not json",
            PublishedAt = Now.AddDays(-1)
        };
        var items = new[] { emptyJson, invalidJson };

        var excluded = ItemSelector.Select(items, new SelectionRules { MinScore = 1 }, new HashSet<Guid>(), Now);
        Assert.Empty(excluded);

        var included = ItemSelector.Select(items, new SelectionRules(), new HashSet<Guid>(), Now);
        Assert.Equal(2, included.Count);
    }

    [Fact]
    public void Equal_scores_break_ties_by_newer_PublishedAt_first()
    {
        var older = Item("older", score: 50, ageDays: 5);
        var newer = Item("newer", score: 50, ageDays: 1);
        var items = new[] { older, newer };

        var result = ItemSelector.Select(items, new SelectionRules(), new HashSet<Guid>(), Now);

        Assert.Equal(["newer", "older"], result.Select(i => i.Title).ToArray());
    }
}
