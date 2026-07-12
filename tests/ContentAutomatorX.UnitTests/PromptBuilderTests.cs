using ContentAutomatorX.Application.Generation;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.UnitTests;

public class PromptBuilderTests
{
    [Fact]
    public void Replaces_all_placeholders()
    {
        var tenant = new Tenant { Name = "Chan", Slug = "chan", VoiceProfile = "Friendly expert voice." };
        var recipe = new Recipe
        {
            TenantId = tenant.Id, Name = "Weekly", Kind = DraftKinds.Newsletter,
            ToneModifiers = "Slightly humorous.", LengthTarget = "800 words", Language = "English"
        };
        var items = new List<ContentItem>
        {
            new() { ExternalId = "1", Title = "Big News", Url = "https://x/1", Body = "Something happened.", MetadataJson = "{\"score\":42}" }
        };

        var prompt = PromptBuilder.Build(
            "VOICE:{voice_profile}|TONE:{tone_modifiers}|ITEMS:{items}|EXTRA:{extra_instructions}",
            tenant, recipe, items, "Mention the discord.");

        Assert.Contains("Friendly expert voice.", prompt);
        Assert.Contains("Slightly humorous.", prompt);
        Assert.Contains("800 words", prompt);
        Assert.Contains("English", prompt);
        Assert.Contains("Big News", prompt);
        Assert.Contains("https://x/1", prompt);
        Assert.Contains("Mention the discord.", prompt);
        Assert.DoesNotContain("{voice_profile}", prompt);
        Assert.DoesNotContain("{items}", prompt);
    }

    [Fact]
    public void Long_item_bodies_are_truncated()
    {
        var tenant = new Tenant { Name = "T", Slug = "t" };
        var recipe = new Recipe { TenantId = tenant.Id, Name = "R", Kind = DraftKinds.SocialPost };
        var items = new List<ContentItem>
        {
            new() { ExternalId = "1", Title = "Long", Body = new string('x', 5000) }
        };

        var prompt = PromptBuilder.Build("{items}", tenant, recipe, items, null);

        Assert.True(prompt.Length < 3000, $"prompt was {prompt.Length} chars");
        Assert.Contains("[truncated]", prompt);
    }

    [Fact]
    public void Default_templates_exist_for_all_kinds()
    {
        foreach (var kind in DraftKinds.All)
        {
            var template = DefaultTemplates.GetFor(kind);
            Assert.Contains("{voice_profile}", template);
            Assert.Contains("{items}", template);
            Assert.Contains("{extra_instructions}", template);
        }
        Assert.Throws<ArgumentException>(() => DefaultTemplates.GetFor("Nope"));
    }
}
