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
    public List<string> StatusCalls { get; } = [];
    public string? ThrowForCampaignId { get; set; }
    public int TestCalls { get; private set; }

    public Task<bool> TestAsync(string apiKey, CancellationToken ct = default) { TestCalls++; return Task.FromResult(true); }
    public Task<IReadOnlyList<MailerLiteGroup>> ListGroupsAsync(string apiKey, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MailerLiteGroup>>([new("g1", "Main")]);
    public Task<string> PushDraftAsync(string apiKey, MailerLiteDraft draft, string? existingCampaignId, CancellationToken ct = default)
    {
        if (FailNextPush) { FailNextPush = false; throw new InvalidOperationException("MailerLite POST /campaigns failed: 422"); }
        Pushes.Add((draft, existingCampaignId));
        return Task.FromResult(existingCampaignId ?? "c-100");
    }
    public Task<MailerLiteCampaignStatus> GetStatusAsync(string apiKey, string campaignId, CancellationToken ct = default)
    {
        StatusCalls.Add(campaignId);
        if (campaignId == ThrowForCampaignId) throw new InvalidOperationException("MailerLite GET /campaigns/{id} failed: 500");
        return Task.FromResult(NextStatus);
    }
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
        var generation = new GenerationPipeline(test.Db, new FakeLlm(llmReply), new FakeDelivery(), new StubLlmSettings());
        var posts = new PostService(test.Db, generation, new FakeLlm("[\"s1\",\"s2\",\"s3\",\"s4\",\"s5\"]"), platforms, ml, new StubLlmSettings(),
            new NewsletterTemplateService(test.Db));
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
    public async Task Set_issue_sources_persists_the_id_list()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        await w.Posts.SetIssueSourcesAsync(post, [idA, idB]);

        using var fresh = w.Test.NewContext();
        var reloaded = await fresh.Posts.SingleAsync(p => p.Id == post.Id);
        var ids = JsonSerializer.Deserialize<Guid[]>(reloaded.SourceIdsJson!);
        Assert.Equal(new[] { idA, idB }, ids);
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
    public async Task Subject_ideas_throws_after_two_unparseable_replies()
    {
        // BuildAsync's own PostService always wires SubjectIdeasAsync to a hardcoded-valid FakeLlm
        // (its `llmReply` builder param only drives the GenerationPipeline's compose path), so a
        // dedicated PostService with a consistently-unparseable FakeLlm is built here instead —
        // BuildAsync itself, and the World's own w.Posts, are left untouched.
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        await w.Posts.SaveIssueAsync(post.Id, "t", "body", null, null);
        var badLlm = new FakeLlm("this is not a JSON array");
        var postsWithBadSubjectLlm = new PostService(w.Test.Db,
            new GenerationPipeline(w.Test.Db, badLlm, new FakeDelivery(), new StubLlmSettings()), badLlm, w.Platforms, w.MailerLite,
            new StubLlmSettings(), new NewsletterTemplateService(w.Test.Db));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => postsWithBadSubjectLlm.SubjectIdeasAsync(post.Id));

        Assert.Contains("did not return subject lines", ex.Message);
    }

    [Fact]
    public async Task Subject_ideas_resolves_llm_settings_for_the_posts_tenant()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        await w.Posts.SaveIssueAsync(post.Id, "t", "body", null, null);
        var llm = new FakeLlm("[\"s1\",\"s2\",\"s3\",\"s4\",\"s5\"]");
        var settings = new StubLlmSettings(new LlmSettings("haiku", LlmEffort.Low));
        var postsWithStub = new PostService(w.Test.Db,
            new GenerationPipeline(w.Test.Db, llm, new FakeDelivery(), new StubLlmSettings()), llm, w.Platforms, w.MailerLite,
            settings, new NewsletterTemplateService(w.Test.Db));

        await postsWithStub.SubjectIdeasAsync(post.Id);

        Assert.Equal(post.TenantId, settings.LastTenantId);
        Assert.Equal("haiku", llm.LastSettings!.Model);
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

    [Fact]
    public async Task Review_queue_includes_failed_posts()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        var failed = new Post
        {
            TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Broken push", Status = PostStatus.Failed, NeedsReview = false
        };
        w.Test.Db.Posts.Add(failed);
        await w.Test.Db.SaveChangesAsync();

        var queue = await w.Posts.ReviewQueueAsync(w.Tenant.Id);

        Assert.Contains(queue, p => p.Id == failed.Id);
    }

    [Fact]
    public async Task Mark_reviewed_clears_the_flag()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        post.NeedsReview = true;
        await w.Test.Db.SaveChangesAsync();

        await w.Posts.MarkReviewedAsync(post.Id);

        using var fresh = w.Test.NewContext();
        var reloaded = await fresh.Posts.SingleAsync(p => p.Id == post.Id);
        Assert.False(reloaded.NeedsReview);
    }

    [Fact]
    public async Task List_returns_newest_first()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        var now = DateTimeOffset.UtcNow;
        var older = new Post
        {
            TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Older", CreatedAt = now.AddHours(-1)
        };
        var newer = new Post
        {
            TenantId = w.Tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter,
            Title = "Newer", CreatedAt = now
        };
        w.Test.Db.Posts.AddRange(older, newer);
        await w.Test.Db.SaveChangesAsync();

        var list = await w.Posts.ListAsync(w.Tenant.Id);

        Assert.Equal(new[] { newer.Id, older.Id }, list.Select(p => p.Id));
    }

    [Fact]
    public async Task Compose_of_an_existing_issue_does_not_spawn_a_duplicate_review_post_when_the_recipe_targets_a_platform()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        w.Recipe.TargetPlatformId = platform.Id;
        await w.Test.Db.SaveChangesAsync();
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var items = await w.Posts.GetCandidatesAsync(post);

        await w.Posts.ComposeAsync(post.Id, items.Select(i => i.Id).ToList(), null);

        // Manual compose only ever touches the issue's own Post row — the pipeline's own
        // review-queue post creation is gated to scheduled runs, so no second row appears.
        Assert.Equal(1, await w.Test.Db.Posts.CountAsync());
    }

    [Fact]
    public async Task Push_of_a_published_post_throws_and_leaves_status_and_mailerlite_untouched()
    {
        var w = await BuildAsync();
        using var _ = w.Test;
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        await w.Platforms.SetApiKeyAsync(platform, "KEY");
        await w.Platforms.SaveConfigAsync(platform, new MailerLiteConfig("g1", "Main", "A", "n@x.com"));
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var items = await w.Posts.GetCandidatesAsync(post);
        await w.Posts.ComposeAsync(post.Id, items.Select(i => i.Id).ToList(), null);
        await w.Posts.PushAsync(post.Id);
        var tracked = await w.Test.Db.Posts.SingleAsync(p => p.Id == post.Id);
        tracked.Status = PostStatus.Published; // simulates MailerLite reporting the campaign sent
        await w.Test.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => w.Posts.PushAsync(post.Id));

        Assert.Contains("already sent", ex.Message);
        Assert.Equal(PostStatus.Published, (await w.Posts.GetAsync(post.Id))!.Status);
        Assert.Single(w.MailerLite.Pushes); // only the original push — the rejected re-push never called out
    }

    [Fact]
    public async Task GetFresh_resyncs_a_stale_tracked_post_after_a_cross_context_compose()
    {
        // Mirrors IssueEditor's flow: the "circuit" context (A) loads the post, then a fresh
        // scope (B) composes — creating a NEW draft and repointing the DB row's DraftId. Without
        // GetFreshAsync, context A's tracked Post instance would keep pointing at the orphaned
        // old draft, and a later save through context A would silently write into it.
        var w = await BuildAsync();
        using var _ = w.Test;
        var post = await w.Posts.CreateIssueAsync(w.Tenant.Id, w.Recipe.Id, 7, null, "t");
        var itemIds = (await w.Posts.GetCandidatesAsync(post)).Select(i => i.Id).ToList();
        await w.Posts.ComposeAsync(post.Id, itemIds, null);
        var firstDraftId = (await w.Posts.GetAsync(post.Id))!.DraftId;
        Assert.NotNull(firstDraftId);

        using var contextA = w.Test.NewContext();
        var postSvcA = new PostService(contextA,
            new GenerationPipeline(contextA, new FakeLlm("# First\nbody"), new FakeDelivery(), new StubLlmSettings()),
            new FakeLlm("[]"), new PlatformService(contextA, new InMemoryCredentials(), w.MailerLite), w.MailerLite,
            new StubLlmSettings(), new NewsletterTemplateService(contextA));
        var trackedByA = await postSvcA.GetAsync(post.Id);
        Assert.Equal(firstDraftId, trackedByA!.DraftId);

        using var contextB = w.Test.NewContext();
        var postSvcB = new PostService(contextB,
            new GenerationPipeline(contextB, new FakeLlm("# Second\nnew body"), new FakeDelivery(), new StubLlmSettings()),
            new FakeLlm("[]"), new PlatformService(contextB, new InMemoryCredentials(), w.MailerLite), w.MailerLite,
            new StubLlmSettings(), new NewsletterTemplateService(contextB));
        await postSvcB.ComposeAsync(post.Id, itemIds, null);

        var fresh = await postSvcA.GetFreshAsync(post.Id);
        Assert.NotNull(fresh);
        Assert.NotEqual(firstDraftId, fresh!.DraftId);

        await postSvcA.SaveIssueAsync(post.Id, "Edited title", "edited body", null, null);

        using var contextC = w.Test.NewContext();
        var dbPost = await contextC.Posts.SingleAsync(p => p.Id == post.Id);
        var dbDraft = await contextC.Drafts.SingleAsync(d => d.Id == dbPost.DraftId);
        Assert.Equal("edited body", dbDraft.Body);
    }
}
