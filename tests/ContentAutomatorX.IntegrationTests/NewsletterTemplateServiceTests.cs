using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class NewsletterTemplateServiceTests
{
    [Fact]
    public async Task Migration_creates_the_table_and_columns()
    {
        using var t = TestDb.Create();
        var tenantId = Guid.NewGuid();

        t.Db.NewsletterTemplates.Add(new NewsletterTemplate
        {
            TenantId = tenantId, Name = "Into the Latent", Html = "<!-- BLOCK: shell -->{{sections}}<!-- /BLOCK -->",
            IsDefault = true
        });
        await t.Db.SaveChangesAsync();

        var stored = await t.Db.NewsletterTemplates.SingleAsync(x => x.TenantId == tenantId);
        Assert.Equal("Into the Latent", stored.Name);
        Assert.True(stored.IsDefault);
        Assert.NotEqual(default, stored.UpdatedAt);
    }

    [Fact]
    public async Task New_columns_round_trip_on_existing_entities()
    {
        using var t = TestDb.Create();
        var templateId = Guid.NewGuid();
        var postId = Guid.NewGuid();

        // IssueSection and IssueSectionProposal both FK to Post with cascade delete; SQLite does
        // enforce that here, so a real Post row is required (see brief note).
        t.Db.Posts.Add(new Post
        {
            Id = postId, TenantId = Guid.NewGuid(), PlatformId = Guid.NewGuid(),
            Kind = "Newsletter", Title = "Issue"
        });

        t.Db.Recipes.Add(new Recipe
        {
            TenantId = Guid.NewGuid(), Name = "Monthly", Kind = "Newsletter",
            PromptTemplateId = Guid.NewGuid(), NewsletterTemplateId = templateId
        });
        t.Db.IssueSections.Add(new IssueSection
        {
            PostId = postId, Position = 0, Type = "Topic", Category = "Tutorial"
        });
        t.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = postId, SectionId = Guid.NewGuid(), BaselineBodyMd = "",
            ProposedCategory = "News", BaselineCategory = "Tutorial"
        });
        await t.Db.SaveChangesAsync();

        Assert.Equal(templateId, (await t.Db.Recipes.SingleAsync()).NewsletterTemplateId);
        Assert.Equal("Tutorial", (await t.Db.IssueSections.SingleAsync()).Category);
        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal("News", proposal.ProposedCategory);
        Assert.Equal("Tutorial", proposal.BaselineCategory);
    }
}
