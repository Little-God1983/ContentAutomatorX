using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class IssueChatPersistenceTests
{
    private static async Task<(TestDb Test, Post Post, IssueSection Section)> SeedAsync()
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = $"t-chat-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "Issue" };
        var section = new IssueSection { PostId = post.Id, Position = 0, Type = SectionTypes.Topic, Title = "T" };
        test.Db.AddRange(tenant, platform, post, section);
        await test.Db.SaveChangesAsync();
        return (test, post, section);
    }

    [Fact]
    public async Task Chat_rows_round_trip_and_cascade_on_post_delete()
    {
        var (test, post, section) = await SeedAsync();
        using var _ = test;
        test.Db.IssueChatMessages.Add(new IssueChatMessage { PostId = post.Id, Role = ChatRoles.User, Text = "hi" });
        test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = section.Id, ProposedBodyMd = "new", BaselineBodyMd = ""
        });
        test.Db.IssueRevisions.Add(new IssueRevision
        {
            PostId = post.Id, Stack = RevisionStacks.Undo, Ordinal = 1, Label = "Edit", SnapshotJson = "{}"
        });
        await test.Db.SaveChangesAsync();

        using (var fresh = test.NewContext())
        {
            Assert.Equal(ChatRoles.User, (await fresh.IssueChatMessages.SingleAsync()).Role);
            Assert.Equal("new", (await fresh.IssueSectionProposals.SingleAsync()).ProposedBodyMd);
            Assert.Equal("Edit", (await fresh.IssueRevisions.SingleAsync()).Label);
        }

        using (var deleter = test.NewContext())
        {
            deleter.Posts.Remove(await deleter.Posts.SingleAsync(p => p.Id == post.Id));
            await deleter.SaveChangesAsync();
        }

        using (var after = test.NewContext())
        {
            Assert.Empty(await after.IssueChatMessages.ToListAsync());
            Assert.Empty(await after.IssueSectionProposals.ToListAsync());
            Assert.Empty(await after.IssueRevisions.ToListAsync());
        }
    }

    [Fact]
    public async Task A_section_can_have_only_one_pending_proposal()
    {
        var (test, post, section) = await SeedAsync();
        using var _ = test;
        test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = section.Id, ProposedBodyMd = "first", BaselineBodyMd = ""
        });
        await test.Db.SaveChangesAsync();

        using var second = test.NewContext();
        second.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = section.Id, ProposedBodyMd = "second", BaselineBodyMd = ""
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Two_revisions_cannot_share_the_same_post_stack_and_ordinal()
    {
        var (test, post, _) = await SeedAsync();
        using var _ = test;
        // Simulates two circuits racing to push the same undo step: each computes "top + 1" from
        // its own read and neither sees the other's write until the unique index says no.
        test.Db.IssueRevisions.Add(new IssueRevision
        {
            PostId = post.Id, Stack = RevisionStacks.Undo, Ordinal = 1, Label = "First", SnapshotJson = "{}"
        });
        await test.Db.SaveChangesAsync();

        using var second = test.NewContext();
        second.IssueRevisions.Add(new IssueRevision
        {
            PostId = post.Id, Stack = RevisionStacks.Undo, Ordinal = 1, Label = "Second", SnapshotJson = "{}"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }
}
