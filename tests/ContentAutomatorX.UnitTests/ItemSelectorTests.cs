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
}
