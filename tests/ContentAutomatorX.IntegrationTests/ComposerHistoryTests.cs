using System.Reflection;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class ComposerHistoryTests
{
    /// <summary>Pins the public surface so a new method cannot be added without someone deciding
    /// whether it mutates and therefore needs a SnapshotAsync call. There is no chokepoint that
    /// could enforce this automatically; this test is the guard instead.</summary>
    [Fact]
    public void Composer_public_surface_is_pinned_so_new_mutations_must_opt_into_history()
    {
        var names = typeof(IssueComposerService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[]
        {
            "AddSectionAsync", "AddTopicsFromItemsAsync", "ClearSectionImageAsync", "CreateFromItemsAsync",
            "EnsureSectionsAsync", "ExportMarkdownAsync", "GenerateTopicsAsync", "GetSectionsAsync",
            "MoveSectionAsync", "RegenerateSectionAsync", "RemoveSectionAsync", "RenderPreviewAsync",
            "SetSectionImageKeyAsync", "TryParseTopics", "UpdateSectionAsync"
        }, names);
    }

    [Fact]
    public async Task Every_mutation_pushes_exactly_one_revision()
    {
        var w = await IssueComposerServiceTests.BuildWorldAsync();
        using var _ = w.Test;
        var history = new IssueHistoryService(w.Test.Db);
        var composer = IssueComposerServiceTests.ComposerWith(w, new SequenceLlm(IssueComposerServiceTests.TopicsJsonFor(w.Items)), history);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, w.Items.Select(i => i.Id).ToList(), "t");

        async Task<int> RevisionsAsync() =>
            await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id && r.Stack == RevisionStacks.Undo);

        var sections = await composer.GetSectionsAsync(post.Id);
        var before = await RevisionsAsync();

        await composer.AddSectionAsync(post.Id, SectionTypes.Divider);
        Assert.Equal(before + 1, await RevisionsAsync());

        await composer.UpdateSectionAsync(sections[1].Id, "T", "B", null, null, null, null);
        Assert.Equal(before + 2, await RevisionsAsync());

        await composer.MoveSectionAsync(sections[2].Id, -1);
        Assert.Equal(before + 3, await RevisionsAsync());

        await composer.GenerateTopicsAsync(post.Id, null);
        Assert.Equal(before + 4, await RevisionsAsync());

        await composer.RegenerateSectionAsync(sections[1].Id, null);
        Assert.Equal(before + 5, await RevisionsAsync());

        await composer.RemoveSectionAsync(sections[2].Id);
        Assert.Equal(before + 6, await RevisionsAsync());

        await composer.AddTopicsFromItemsAsync(post.Id, [w.Items[0].Id]);
        Assert.Equal(before + 7, await RevisionsAsync());
    }

    [Fact]
    public async Task Rejected_and_no_op_mutations_leave_no_revision()
    {
        var w = await IssueComposerServiceTests.BuildWorldAsync();
        using var _ = w.Test;
        var history = new IssueHistoryService(w.Test.Db);
        var composer = IssueComposerServiceTests.ComposerWith(
            w, new SequenceLlm(IssueComposerServiceTests.TopicsJsonFor(w.Items)), history);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        await composer.GenerateTopicsAsync(post.Id, null);   // fill the skeleton so the later call is a no-op
        var sections = await composer.GetSectionsAsync(post.Id);
        var before = await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.AddSectionAsync(post.Id, SectionTypes.Header));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.RemoveSectionAsync(sections[0].Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.RegenerateSectionAsync(sections[2].Id, null));
        await composer.MoveSectionAsync(sections[0].Id, 1);    // the header cannot move
        await composer.MoveSectionAsync(sections[1].Id, -1);   // already directly under the header
        Assert.Equal(0, await composer.GenerateTopicsAsync(post.Id, null));  // nothing left to fill

        Assert.Equal(before, await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id));
    }

    [Fact]
    public async Task A_failed_generation_leaves_no_revision()
    {
        var w = await IssueComposerServiceTests.BuildWorldAsync();
        using var _ = w.Test;
        var history = new IssueHistoryService(w.Test.Db);
        var composer = IssueComposerServiceTests.ComposerWith(w, new SequenceLlm("garbage", "still garbage"), history);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var before = await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.GenerateTopicsAsync(post.Id, null));

        Assert.Equal(before, await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id));
    }

    [Fact]
    public async Task A_failed_rewrite_leaves_no_revision()
    {
        var w = await IssueComposerServiceTests.BuildWorldAsync();
        using var _ = w.Test;
        var history = new IssueHistoryService(w.Test.Db);
        var composer = IssueComposerServiceTests.ComposerWith(w, new FailingLlm(), history);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var sections = await composer.GetSectionsAsync(post.Id);
        var before = await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.RegenerateSectionAsync(sections[1].Id, null));

        Assert.Equal(before, await w.Test.Db.IssueRevisions.CountAsync(r => r.PostId == post.Id));
    }
}
