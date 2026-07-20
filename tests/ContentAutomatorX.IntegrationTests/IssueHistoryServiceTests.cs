using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class IssueHistoryServiceTests
{
    private static async Task<(TestDb Test, Post Post, List<IssueSection> Sections)> SeedAsync()
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = $"t-hist-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post
        {
            TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Issue", Subject = "Subj"
        };
        var sections = new List<IssueSection>
        {
            new() { PostId = post.Id, Position = 0, Type = SectionTypes.Header, BodyMd = "intro" },
            new() { PostId = post.Id, Position = 1, Type = SectionTypes.Topic, Title = "A", BodyMd = "a" },
            new() { PostId = post.Id, Position = 2, Type = SectionTypes.Footer, BodyMd = "bye" }
        };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.AddRange(sections);
        await test.Db.SaveChangesAsync();
        return (test, post, sections);
    }

    [Fact]
    public async Task Undo_restores_an_edited_body_and_redo_reapplies_it()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var history = new IssueHistoryService(test.Db);

        await history.SnapshotAsync(post.Id, "Edit topic");
        sections[1].BodyMd = "edited";
        await test.Db.SaveChangesAsync();

        Assert.Equal("Edit topic", await history.UndoAsync(post.Id));
        Assert.Equal("a", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);

        Assert.Equal("Edit topic", await history.RedoAsync(post.Id));
        Assert.Equal("edited", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task Undo_resurrects_a_deleted_section_with_its_original_id()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var history = new IssueHistoryService(test.Db);
        var doomed = sections[1].Id;

        await history.SnapshotAsync(post.Id, "Delete section");
        test.Db.IssueSections.Remove(sections[1]);
        await test.Db.SaveChangesAsync();
        Assert.Equal(2, await test.Db.IssueSections.CountAsync(s => s.PostId == post.Id));

        await history.UndoAsync(post.Id);

        var restored = await test.Db.IssueSections.SingleAsync(s => s.Id == doomed);
        Assert.Equal("A", restored.Title);
        Assert.Equal(1, restored.Position);
    }

    [Fact]
    public async Task Undo_restores_post_header_fields()
    {
        var (test, post, _) = await SeedAsync();
        using var _t = test;
        var history = new IssueHistoryService(test.Db);

        await history.SnapshotAsync(post.Id, "Rename");
        post.Title = "Changed";
        post.Subject = "Changed too";
        await test.Db.SaveChangesAsync();

        await history.UndoAsync(post.Id);

        var after = await test.Db.Posts.SingleAsync(p => p.Id == post.Id);
        Assert.Equal("Issue", after.Title);
        Assert.Equal("Subj", after.Subject);
    }

    [Fact]
    public async Task A_new_snapshot_clears_the_redo_stack()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var history = new IssueHistoryService(test.Db);

        await history.SnapshotAsync(post.Id, "First");
        sections[1].BodyMd = "one";
        await test.Db.SaveChangesAsync();
        await history.UndoAsync(post.Id);
        Assert.Equal("First", (await history.GetStateAsync(post.Id)).RedoLabel);

        await history.SnapshotAsync(post.Id, "Second");

        var state = await history.GetStateAsync(post.Id);
        Assert.Equal("Second", state.UndoLabel);
        Assert.Null(state.RedoLabel);
    }

    [Fact]
    public async Task Undo_on_an_empty_stack_returns_null()
    {
        var (test, post, _) = await SeedAsync();
        using var _t = test;
        var history = new IssueHistoryService(test.Db);

        Assert.Null(await history.UndoAsync(post.Id));
        Assert.Null(await history.RedoAsync(post.Id));
        var state = await history.GetStateAsync(post.Id);
        Assert.Null(state.UndoLabel);
        Assert.Null(state.RedoLabel);
    }

    [Fact]
    public async Task The_undo_stack_trims_to_max_depth()
    {
        var (test, post, _) = await SeedAsync();
        using var _t = test;
        var history = new IssueHistoryService(test.Db);

        for (var n = 0; n < IssueHistoryService.MaxDepth + 5; n++)
            await history.SnapshotAsync(post.Id, $"Edit {n}");

        var rows = await test.Db.IssueRevisions
            .Where(r => r.PostId == post.Id && r.Stack == RevisionStacks.Undo).ToListAsync();
        Assert.Equal(IssueHistoryService.MaxDepth, rows.Count);
        // The oldest went, not the newest.
        Assert.Equal($"Edit {IssueHistoryService.MaxDepth + 4}", (await history.GetStateAsync(post.Id)).UndoLabel);
        Assert.DoesNotContain(rows, r => r.Label == "Edit 0");
    }
}
