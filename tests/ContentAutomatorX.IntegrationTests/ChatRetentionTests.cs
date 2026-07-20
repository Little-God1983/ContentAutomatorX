using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class ChatRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static IssueChatService Chat(TestDb test) =>
        new(test.Db, new SequenceLlm("unused"), new StubLlmSettings(), new IssueHistoryService(test.Db));

    private static async Task<Post> AddIssueAsync(TestDb test, PostStatus status,
        DateTimeOffset? publishedAt, DateTimeOffset lastMessageAt)
    {
        var tenant = new Tenant { Name = "T", Slug = $"t-ret-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Issue", Status = status, PublishedAt = publishedAt
        };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueChatMessages.Add(new IssueChatMessage
        {
            PostId = post.Id, Role = ChatRoles.User, Text = "hi", CreatedAt = lastMessageAt
        });
        test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = Guid.NewGuid(), ProposedBodyMd = "x", BaselineBodyMd = "",
            CreatedAt = lastMessageAt
        });
        test.Db.IssueRevisions.Add(new IssueRevision
        {
            PostId = post.Id, Stack = RevisionStacks.Undo, Ordinal = 1, Label = "E",
            SnapshotJson = "{}", CreatedAt = lastMessageAt
        });
        await test.Db.SaveChangesAsync();
        return post;
    }

    [Theory]
    [InlineData(30, true)]    // published long enough ago
    [InlineData(29, false)]   // still inside the 30-day window
    public async Task Published_issues_purge_thirty_days_after_publication(int daysAgo, bool purged)
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Published, Now.AddDays(-daysAgo), Now.AddDays(-daysAgo));

        var count = await Chat(test).PurgeAsync(Now);

        Assert.Equal(purged ? 1 : 0, count);
        Assert.Equal(purged ? 0 : 1, await test.Db.IssueChatMessages.CountAsync(m => m.PostId == post.Id));
        Assert.Equal(purged ? 0 : 1, await test.Db.IssueSectionProposals.CountAsync(p => p.PostId == post.Id));
        Assert.Equal(purged ? 0 : 1, await test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id));
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(89, false)]
    public async Task Unpublished_issues_purge_ninety_days_after_the_last_activity(int daysAgo, bool purged)
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Draft, null, Now.AddDays(-daysAgo));

        Assert.Equal(purged ? 1 : 0, await Chat(test).PurgeAsync(Now));
        Assert.Equal(purged ? 0 : 1, await test.Db.IssueChatMessages.CountAsync(m => m.PostId == post.Id));
    }

    [Fact]
    public async Task A_pushed_but_never_sent_issue_uses_the_activity_rule_not_the_publish_rule()
    {
        using var test = TestDb.Create();
        // Pushed to MailerLite but never sent, so PublishedAt was never set. Without the activity
        // rule this thread would never be collected at all.
        var post = await AddIssueAsync(test, PostStatus.Pushed, null, Now.AddDays(-100));

        Assert.Equal(1, await Chat(test).PurgeAsync(Now));
        Assert.Empty(await test.Db.IssueChatMessages.Where(m => m.PostId == post.Id).ToListAsync());
    }

    [Fact]
    public async Task Recent_chat_keeps_an_old_issue_alive()
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Draft, null, Now.AddDays(-1));
        // Created long ago, but still being worked on — the clock runs from activity, not creation.
        (await test.Db.Posts.SingleAsync(p => p.Id == post.Id)).CreatedAt = Now.AddDays(-400);
        await test.Db.SaveChangesAsync();

        Assert.Equal(0, await Chat(test).PurgeAsync(Now));
        Assert.Single(await test.Db.IssueChatMessages.Where(m => m.PostId == post.Id).ToListAsync());
    }

    [Fact]
    public async Task An_issue_with_revisions_but_no_chat_uses_the_revision_timestamp()
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Draft, null, Now.AddDays(-2));
        test.Db.IssueChatMessages.RemoveRange(
            await test.Db.IssueChatMessages.Where(m => m.PostId == post.Id).ToListAsync());
        await test.Db.SaveChangesAsync();

        Assert.Equal(0, await Chat(test).PurgeAsync(Now));
        Assert.Single(await test.Db.IssueRevisions.Where(r => r.PostId == post.Id).ToListAsync());
    }

    private static async Task<Post> AddProposalOnlyIssueAsync(TestDb test, DateTimeOffset proposalAt)
    {
        var tenant = new Tenant { Name = "T", Slug = $"t-prop-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Issue", Status = PostStatus.Draft
        };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = Guid.NewGuid(), ProposedBodyMd = "x",
            BaselineBodyMd = "", CreatedAt = proposalAt
        });
        await test.Db.SaveChangesAsync();
        return post;
    }

    [Fact]
    public async Task An_issue_whose_only_activity_is_proposals_is_still_collected()
    {
        using var test = TestDb.Create();
        // Regenerate-all writes proposals without a chat message or a revision. A sweep that
        // looked only at those two tables would never find this issue at all.
        var post = await AddProposalOnlyIssueAsync(test, Now.AddDays(-91));

        Assert.Equal(1, await Chat(test).PurgeAsync(Now));
        Assert.Empty(await test.Db.IssueSectionProposals.Where(p => p.PostId == post.Id).ToListAsync());
    }

    [Fact]
    public async Task A_fresh_proposal_keeps_a_long_quiet_issue_alive()
    {
        using var test = TestDb.Create();
        var post = await AddIssueAsync(test, PostStatus.Draft, null, Now.AddDays(-100));
        // Regenerate-all on an issue nobody has chatted about in months. The suggestion is seconds
        // old and must survive the next tick.
        test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = Guid.NewGuid(), ProposedBodyMd = "fresh",
            BaselineBodyMd = "", CreatedAt = Now
        });
        await test.Db.SaveChangesAsync();

        Assert.Equal(0, await Chat(test).PurgeAsync(Now));
        Assert.Equal(2, await test.Db.IssueSectionProposals.CountAsync(p => p.PostId == post.Id));
    }
}
