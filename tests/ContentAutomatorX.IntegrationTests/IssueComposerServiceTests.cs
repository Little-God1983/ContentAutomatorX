using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class IssueComposerServiceTests
{
    [Fact]
    public async Task IssueSection_round_trips_and_cascades_on_post_delete()
    {
        using var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-sections" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "Issue" };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.Add(new IssueSection
        {
            PostId = post.Id, Position = 0, Type = SectionTypes.Topic,
            Title = "Topic A", BodyMd = "blurb", LinkUrl = "https://ex.com", SourceItemId = Guid.NewGuid()
        });
        await test.Db.SaveChangesAsync();

        using (var fresh = test.NewContext())
        {
            var s = await fresh.IssueSections.SingleAsync(x => x.PostId == post.Id);
            Assert.Equal(SectionTypes.Topic, s.Type);
            Assert.Equal("Topic A", s.Title);
        }

        using (var fresh = test.NewContext())
        {
            fresh.Posts.Remove(await fresh.Posts.SingleAsync(p => p.Id == post.Id));
            await fresh.SaveChangesAsync();
            Assert.Equal(0, await fresh.IssueSections.CountAsync());
        }
    }

    [Fact]
    public void TenantBranding_parses_malformed_json_to_empty_and_round_trips()
    {
        Assert.Equal(new TenantBranding(null, null, null), TenantBranding.Parse("not json"));
        Assert.Equal(new TenantBranding(null, null, null), TenantBranding.Parse(""));
        var b = new TenantBranding("#7C3AED", "https://ex.com/logo.png", "georgia");
        Assert.Equal(b, TenantBranding.Parse(b.ToJson()));
    }
}
