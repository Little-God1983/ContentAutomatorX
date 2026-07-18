using System.Text.Json;
using ContentAutomatorX.Application.Newsletter;
using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.IntegrationTests;

// Note: FakeLlm(string reply = "...") already exists in GenerationPipelineTests.cs (same namespace,
// reused as-is by McpToolsTests.cs too) — not redeclared here to avoid a duplicate-type compile error.

public class FakeDelivery : IDraftDelivery
{
    public Task<string> DeliverAsync(Tenant tenant, RecipeOutput output, Draft draft, CancellationToken ct = default) =>
        Task.FromResult(Path.Combine(Path.GetTempPath(), $"{draft.Id}.md"));
}

public class FakeMailerLite : IMailerLiteClient
{
    public List<(MailerLiteDraft Draft, string? ExistingId)> Pushes { get; } = [];
    public bool FailNextPush { get; set; }
    public MailerLiteCampaignStatus NextStatus { get; set; } = new("draft", null, null, null);

    public Task<bool> TestAsync(string apiKey, CancellationToken ct = default) => Task.FromResult(true);
    public Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(string apiKey, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MailerLiteGroup>>([new("g1", "Main")]);
    public Task<string> PushDraftAsync(string apiKey, MailerLiteDraft draft, string? existingCampaignId, CancellationToken ct = default)
    {
        if (FailNextPush) { FailNextPush = false; throw new InvalidOperationException("MailerLite POST /campaigns failed: 422"); }
        Pushes.Add((draft, existingCampaignId));
        return Task.FromResult(existingCampaignId ?? "c-100");
    }
    public Task<MailerLiteCampaignStatus> GetStatusAsync(string apiKey, string campaignId, CancellationToken ct = default) =>
        Task.FromResult(NextStatus);
}

public class InMemoryCredentials : ICredentialStore
{
    private readonly Dictionary<string, string> _map = [];
    public Task SetAsync(string name, string secret, CancellationToken ct = default) { _map[name] = secret; return Task.CompletedTask; }
    public Task<string?> GetAsync(string name, CancellationToken ct = default) => Task.FromResult(_map.GetValueOrDefault(name));
    public Task DeleteAsync(string name, CancellationToken ct = default) { _map.Remove(name); return Task.CompletedTask; }
}

public class PostServiceTests
{
    private sealed record World(TestDb Test, PostService Posts, PlatformService Platforms,
        FakeMailerLite MailerLite, Tenant Tenant, Recipe Recipe, Source SourceA, Source SourceB);

    private static async Task<World> BuildAsync(string llmReply = "# Composed Issue\n\nbody")
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = "t-posts", OutputFolderPath = Path.GetTempPath() };
        var sourceA = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "A" };
        var sourceB = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "B" };
        var template = new PromptTemplate { TenantId = null, Kind = DraftKinds.Newsletter, Template = "{voice_profile}{tone_modifiers}{items}{extra_instructions}" };
        var recipe = new Recipe
        {
            TenantId = tenant.Id, Name = "AI Weekly", Kind = DraftKinds.Newsletter,
            PromptTemplateId = template.Id,
            SourceIdsJson = JsonSerializer.Serialize(new[] { sourceA.Id, sourceB.Id })
        };
        test.Db.AddRange(tenant, sourceA, sourceB, template, recipe);
        foreach (var (source, n) in new[] { (sourceA, 1), (sourceB, 2) })
            test.Db.ContentItems.Add(new ContentItem
            {
                TenantId = tenant.Id, SourceId = source.Id, ExternalId = $"e{n}",
                Title = $"Item {n}", Body = "b", PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        await test.Db.SaveChangesAsync();

        var ml = new FakeMailerLite();
        var creds = new InMemoryCredentials();
        var platforms = new PlatformService(test.Db, creds, ml);
        var generation = new GenerationPipeline(test.Db, new FakeLlm(llmReply), new FakeDelivery());
        var posts = new PostService(test.Db, generation, new FakeLlm("[\"s1\",\"s2\",\"s3\",\"s4\",\"s5\"]"), platforms, ml);
        return new World(test, posts, platforms, ml, tenant, recipe, sourceA, sourceB);
    }

    [Fact]
    public async Task Create_issue_numbers_titles_and_binds_platform()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        Assert.Equal("AI Weekly #1", await w.Posts.SuggestTitleAsync(w.Recipe.Id));

        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "AI Weekly #1");

        Assert.Equal(PostStatus.Draft, post.Status);
        Assert.Equal(w.Recipe.Id, post.RecipeId);
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        Assert.Equal(platform.Id, post.PlatformId);
        Assert.Equal("AI Weekly #2", await w.Posts.SuggestTitleAsync(w.Recipe.Id));
    }

    [Fact]
    public async Task Candidates_respect_the_per_issue_source_subset()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, [w.SourceA.Id], "t");

        var candidates = await w.Posts.GetCandidatesAsync(post);

        var item = Assert.Single(candidates);           // sourceB's item excluded
        Assert.Equal(w.SourceA.Id, item.SourceId);
    }

    [Fact]
    public async Task Compose_links_draft_and_prefills_subject()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var items = await w.Posts.GetCandidatesAsync(post);

        var (run, updated) = await w.Posts.ComposeAsync(post.Id, items.Select(i => i.Id).ToList(), null);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.NotNull(updated.DraftId);
        Assert.Equal("Composed Issue", updated.Title);
        Assert.Equal("Composed Issue", updated.Subject);
    }

    [Fact]
    public async Task Push_renders_html_creates_campaign_and_repush_reuses_id()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        await w.Platforms.SetApiKeyAsync(platform, "KEY");
        await w.Platforms.SaveConfigAsync(platform, new MailerLiteConfig("g1", "Main", "AIVisions", "n@x.com"));
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var items = await w.Posts.GetCandidatesAsync(post);
        await w.Posts.ComposeAsync(post.Id, items.Select(i => i.Id).ToList(), null);

        var pushed = await w.Posts.PushAsync(post.Id);

        Assert.Equal(PostStatus.Pushed, pushed.Status);
        Assert.Equal("c-100", pushed.ExternalId);
        Assert.Contains("Composed Issue", w.MailerLite.Pushes.Single().Draft.Html);

        await w.Posts.PushAsync(post.Id); // re-push
        Assert.Equal("c-100", w.MailerLite.Pushes[1].ExistingId);
    }

    [Fact]
    public async Task Push_failure_marks_failed_and_rethrows()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        await w.Platforms.SetApiKeyAsync(platform, "KEY");
        await w.Platforms.SaveConfigAsync(platform, new MailerLiteConfig("g1", "Main", "A", "n@x.com"));
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var items = await w.Posts.GetCandidatesAsync(post);
        await w.Posts.ComposeAsync(post.Id, items.Select(i => i.Id).ToList(), null);
        w.MailerLite.FailNextPush = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Posts.PushAsync(post.Id));
        Assert.Equal(PostStatus.Failed, (await w.Posts.GetAsync(post.Id))!.Status);
    }

    [Fact]
    public async Task Push_without_config_throws_actionable_message()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        await w.Posts.SaveIssueAsync(post.Id, "t", "hand-written body", "s", null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => w.Posts.PushAsync(post.Id));
        Assert.Contains("Platforms", ex.Message);
    }

    [Fact]
    public async Task SaveIssue_creates_a_draft_for_a_hand_written_issue()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");

        await w.Posts.SaveIssueAsync(post.Id, "My title", "typed by hand", "subj", "pv");

        var reloaded = await w.Posts.GetAsync(post.Id);
        Assert.NotNull(reloaded!.DraftId);
        Assert.Equal("My title", reloaded.Title);
        var draft = await w.Test.Db.Drafts.FindAsync(reloaded.DraftId);
        Assert.Equal("typed by hand", draft!.Body);
    }

    [Fact]
    public async Task Subject_ideas_parses_five_strings()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        await w.Posts.SaveIssueAsync(post.Id, "t", "body", null, null);

        var ideas = await w.Posts.SubjectIdeasAsync(post.Id);

        Assert.Equal(5, ideas.Count);
        Assert.Equal("s1", ideas[0]);
    }

    [Fact]
    public async Task Review_queue_lists_needs_review_and_pushed()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        w.Test.Db.Posts.AddRange(
            new Post { TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "r", NeedsReview = true },
            new Post { TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "p", Status = PostStatus.Pushed },
            new Post { TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "done", Status = PostStatus.Published });
        await w.Test.Db.SaveChangesAsync();

        var queue = await w.Posts.ReviewQueueAsync(w.Tenant.Id);

        Assert.Equal(2, queue.Count);
        Assert.DoesNotContain(queue, p => p.Title == "done");
    }
}
