using ContentAutomatorX.Application.Pipelines;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Delivery;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class FakeLlm(string reply = "# Generated Title\nGenerated body.") : ILlmBackend
{
    public string Name => "fake";
    public string? LastPrompt { get; private set; }
    public Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        LastPrompt = prompt;
        return Task.FromResult(new LlmResult(reply, "fake-model"));
    }
}

public class FailingLlm : ILlmBackend
{
    public string Name => "failing";
    public Task<LlmResult> GenerateAsync(string prompt, CancellationToken ct = default) =>
        throw new InvalidOperationException("llm down");
}

public class GenerationPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"contentx-gen-{Guid.NewGuid():N}");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private (Tenant tenant, Source source, Recipe recipe) Seed(TestDb test)
    {
        var tenant = new Tenant { Name = "T", Slug = "t", VoiceProfile = "Casual.", OutputFolderPath = _dir };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        var template = new PromptTemplate { TenantId = null, Kind = DraftKinds.Newsletter,
            Template = "V:{voice_profile} T:{tone_modifiers} I:{items} E:{extra_instructions}" };
        var recipe = new Recipe
        {
            TenantId = tenant.Id, Name = "Weekly", Kind = DraftKinds.Newsletter,
            PromptTemplateId = template.Id, SelectionJson = """{"maxItems":5}"""
        };
        test.Db.Tenants.Add(tenant);
        test.Db.Sources.Add(source);
        test.Db.PromptTemplates.Add(template);
        test.Db.Recipes.Add(recipe);
        for (int n = 1; n <= 3; n++)
            test.Db.ContentItems.Add(new ContentItem
            {
                TenantId = tenant.Id, SourceId = source.Id, ExternalId = $"e{n}",
                Title = $"Item {n}", Body = "body", MetadataJson = $"{{\"score\":{n * 10}}}"
            });
        test.Db.SaveChanges();
        return (tenant, source, recipe);
    }

    [Fact]
    public async Task Happy_path_delivers_file_and_marks_items_used()
    {
        using var test = TestDb.Create();
        var (tenant, _, recipe) = Seed(test);
        var llm = new FakeLlm();
        var pipeline = new GenerationPipeline(test.Db, llm, new FileShareDraftDelivery());

        var (run, draft) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.NotNull(draft);
        Assert.Equal(DraftStatus.Delivered, draft.Status);
        Assert.True(File.Exists(draft.FilePath));
        Assert.Contains("Item 3", llm.LastPrompt);            // items reached the prompt
        Assert.Contains("Casual.", llm.LastPrompt);            // voice profile reached the prompt
        Assert.Equal("fake-model", draft.ModelUsed);
        Assert.Equal(3, await test.Db.ContentItems.CountAsync(i => i.Status == ContentItemStatus.Used));
        Assert.NotNull((await test.Db.Recipes.SingleAsync()).LastRunAt);
    }

    [Fact]
    public async Task Second_run_finds_no_new_items_and_fails_cleanly()
    {
        using var test = TestDb.Create();
        var (_, _, recipe) = Seed(test);
        var pipeline = new GenerationPipeline(test.Db, new FakeLlm(), new FileShareDraftDelivery());

        await pipeline.RunAsync(recipe.Id);
        var (run2, draft2) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Failed, run2.Status);
        Assert.Null(draft2);
        Assert.Contains("no items", run2.LogJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Llm_failure_yields_failed_run_and_no_draft()
    {
        using var test = TestDb.Create();
        var (_, _, recipe) = Seed(test);
        var pipeline = new GenerationPipeline(test.Db, new FailingLlm(), new FileShareDraftDelivery());

        var (run, draft) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Null(draft);
        Assert.Equal(0, await test.Db.Drafts.CountAsync());
        Assert.Equal(0, await test.Db.ContentItems.CountAsync(i => i.Status == ContentItemStatus.Used));
    }

    [Fact]
    public async Task Delivery_failure_keeps_draft_generated_and_run_partial()
    {
        using var test = TestDb.Create();
        var (tenant, _, recipe) = Seed(test);
        var fresh = test.NewContext();
        var t = fresh.Tenants.Single();
        t.OutputFolderPath = "";     // unconfigured folder → delivery throws
        fresh.SaveChanges();
        var pipeline = new GenerationPipeline(test.NewContext(), new FakeLlm(), new FileShareDraftDelivery());

        var (run, draft) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Partial, run.Status);
        Assert.NotNull(draft);
        Assert.Equal(DraftStatus.Generated, draft.Status);
        Assert.Null(draft.FilePath);
    }

    [Fact]
    public async Task Recipe_with_target_platform_creates_a_needs_review_post()
    {
        using var test = TestDb.Create();
        var (tenant, _, recipe) = Seed(test);
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        recipe.TargetPlatformId = platform.Id;
        test.Db.Platforms.Add(platform);
        test.Db.SaveChanges();
        var pipeline = new GenerationPipeline(test.Db, new FakeLlm("# Weekly\n\nbody"), new FileShareDraftDelivery());

        var (run, draft) = await pipeline.RunAsync(recipe.Id); // default createReviewPost: true

        Assert.Equal(RunStatus.Succeeded, run.Status);
        var post = await test.Db.Posts.SingleAsync();
        Assert.True(post.NeedsReview);
        Assert.Equal(PostStatus.Draft, post.Status);
        Assert.Equal(draft!.Id, post.DraftId);
        Assert.Equal(recipe.Id, post.RecipeId);
        Assert.Equal("Weekly", post.Title);
        Assert.Equal(platform.Id, post.PlatformId);
    }

    [Fact]
    public async Task Delivery_failure_with_target_platform_still_creates_the_review_post()
    {
        using var test = TestDb.Create();
        var (tenant, _, recipe) = Seed(test);
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        recipe.TargetPlatformId = platform.Id;
        test.Db.Platforms.Add(platform);
        test.Db.SaveChanges();
        var fresh = test.NewContext();
        var t = fresh.Tenants.Single();
        t.OutputFolderPath = "";     // unconfigured folder → delivery throws
        fresh.SaveChanges();
        var pipeline = new GenerationPipeline(test.NewContext(), new FakeLlm("# Weekly\n\nbody"), new FileShareDraftDelivery());

        var (run, _) = await pipeline.RunAsync(recipe.Id); // default createReviewPost: true

        Assert.Equal(RunStatus.Partial, run.Status);
        var post = await test.Db.Posts.SingleAsync();
        Assert.True(post.NeedsReview);
        Assert.Equal(platform.Id, post.PlatformId);
        Assert.Equal(recipe.Id, post.RecipeId);
    }

    [Fact]
    public async Task Recipe_with_target_platform_creates_no_post_when_caller_opts_out()
    {
        // PostService.ComposeAsync passes createReviewPost: false — the issue being composed
        // already IS the Post, so letting RunAsync also park a review-queue post here would
        // spawn a duplicate.
        using var test = TestDb.Create();
        var (tenant, _, recipe) = Seed(test);
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        recipe.TargetPlatformId = platform.Id;
        test.Db.Platforms.Add(platform);
        test.Db.SaveChanges();
        var pipeline = new GenerationPipeline(test.Db, new FakeLlm("# Weekly\n\nbody"), new FileShareDraftDelivery());

        var (run, _) = await pipeline.RunAsync(recipe.Id, createReviewPost: false);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Empty(test.Db.Posts.ToList());
    }

    [Fact]
    public async Task Recipe_without_target_platform_creates_no_post()
    {
        using var test = TestDb.Create();
        var (_, _, recipe) = Seed(test);
        var pipeline = new GenerationPipeline(test.Db, new FakeLlm(), new FileShareDraftDelivery());

        var (run, _) = await pipeline.RunAsync(recipe.Id);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Empty(test.Db.Posts.ToList());
    }
}
