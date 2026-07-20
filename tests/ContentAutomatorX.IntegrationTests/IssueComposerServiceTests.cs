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

public class SequenceLlm(params string[] replies) : ILlmBackend
{
    private int _n;
    public string Name => "seq";
    public List<string> Prompts { get; } = [];
    public LlmSettings? LastSettings { get; private set; }
    public Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default)
    {
        Prompts.Add(prompt);
        LastSettings = settings;
        var reply = replies[Math.Min(_n++, replies.Length - 1)];
        return Task.FromResult(new LlmResult(reply, "seq-model"));
    }
}

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

    public sealed record World(TestDb Test, PlatformService Platforms, FakeMailerLite MailerLite,
        Tenant Tenant, Recipe Recipe, Source Source, List<ContentItem> Items);

    public static async Task<World> BuildWorldAsync()
    {
        var test = TestDb.Create();
        var tenant = new Tenant
        {
            Name = "T", Slug = "t-composer",
            DefaultHeaderMd = "Hi friends!", DefaultFooterMd = "Bye! — Chris",
            SenderIdentity = "Acme Media, Musterstr. 1, Berlin, DE"
        };
        var source = new Source { TenantId = tenant.Id, Type = SourceTypes.Rss, DisplayName = "feed" };
        var template = new PromptTemplate { TenantId = null, Kind = DraftKinds.Newsletter, Template = "{items}" };
        var recipe = new Recipe
        {
            TenantId = tenant.Id, Name = "AI Weekly", Kind = DraftKinds.Newsletter,
            PromptTemplateId = template.Id, ToneModifiers = "punchy"
        };
        test.Db.AddRange(tenant, source, template, recipe);
        var items = new List<ContentItem>();
        for (var n = 1; n <= 3; n++)
        {
            var item = new ContentItem
            {
                TenantId = tenant.Id, SourceId = source.Id, ExternalId = $"e{n}",
                Title = $"Item {n}", Url = $"https://ex.com/{n}", Body = $"body {n}",
                PublishedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            items.Add(item);
            test.Db.ContentItems.Add(item);
        }
        await test.Db.SaveChangesAsync();
        var ml = new FakeMailerLite();
        var platforms = new PlatformService(test.Db, new InMemoryCredentials(), ml);
        return new World(test, platforms, ml, tenant, recipe, source, items);
    }

    public static IssueComposerService ComposerWith(World w, ILlmBackend llm, IssueHistoryService history) =>
        new(w.Test.Db, llm,
            new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery(), new StubLlmSettings()),
                llm, w.Platforms, w.MailerLite, new StubLlmSettings()),
            new StubLlmSettings(), history);

    private static IssueComposerService Composer(World w, ILlmBackend llm, StubLlmSettings? settings = null) =>
        new(w.Test.Db, llm,
            new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery(), new StubLlmSettings()),
                llm, w.Platforms, w.MailerLite, new StubLlmSettings()),
            settings ?? new StubLlmSettings(), new IssueHistoryService(w.Test.Db));

    [Fact]
    public async Task CreateFromItems_builds_header_topics_footer_with_contiguous_positions()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());

        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "AI Weekly #1");
        var sections = await composer.GetSectionsAsync(post.Id);

        Assert.Equal(5, sections.Count);
        Assert.Equal(SectionTypes.Header, sections[0].Type);
        Assert.Equal("Hi friends!", sections[0].BodyMd);
        Assert.Equal(SectionTypes.Footer, sections[^1].Type);
        Assert.Equal(Enumerable.Range(0, 5), sections.Select(s => s.Position));
        var topic = sections[1];
        Assert.Equal(SectionTypes.Topic, topic.Type);
        Assert.Equal("Item 1", topic.Title);
        Assert.Equal("https://ex.com/1", topic.LinkUrl);
        Assert.Equal(w.Items[0].Id, topic.SourceItemId);
        Assert.True(string.IsNullOrEmpty(topic.BodyMd)); // skeleton until generation
    }

    [Fact]
    public async Task EnsureSections_wraps_a_legacy_draft_body_and_is_idempotent()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        var draft = new Draft { TenantId = w.Tenant.Id, RecipeId = w.Recipe.Id, Kind = DraftKinds.Newsletter, Title = "Old", Body = "old markdown body" };
        var post = new Post { TenantId = w.Tenant.Id, PlatformId = platform.Id, DraftId = draft.Id, Kind = DraftKinds.Newsletter, Title = "Old issue" };
        w.Test.Db.AddRange(draft, post);
        await w.Test.Db.SaveChangesAsync();

        await composer.EnsureSectionsAsync(post.Id);
        await composer.EnsureSectionsAsync(post.Id); // idempotent

        var sections = await composer.GetSectionsAsync(post.Id);
        Assert.Equal(3, sections.Count);
        Assert.Equal(new[] { SectionTypes.Header, SectionTypes.LegacyBody, SectionTypes.Footer },
            sections.Select(s => s.Type));
        Assert.Equal("old markdown body", sections[1].BodyMd);
    }

    [Fact]
    public async Task AddSection_inserts_above_footer_and_rejects_header_footer()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");

        var sponsor = await composer.AddSectionAsync(post.Id, SectionTypes.Sponsor);

        var sections = await composer.GetSectionsAsync(post.Id);
        Assert.Equal(sponsor.Id, sections[^2].Id);                 // directly above the footer
        Assert.Equal(SectionTypes.Footer, sections[^1].Type);
        Assert.Equal(Enumerable.Range(0, sections.Count), sections.Select(s => s.Position));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.AddSectionAsync(post.Id, SectionTypes.Header));
    }

    [Fact]
    public async Task Move_swaps_within_bounds_and_never_crosses_header_or_footer()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "t");
        var sections = await composer.GetSectionsAsync(post.Id);
        var first = sections[1];   // topic "Item 1"
        var second = sections[2];  // topic "Item 2"

        await composer.MoveSectionAsync(second.Id, -1);            // swap 1 and 2
        var after = await composer.GetSectionsAsync(post.Id);
        Assert.Equal(new[] { second.Id, first.Id }, after.Skip(1).Take(2).Select(s => s.Id));

        await composer.MoveSectionAsync(second.Id, -1);            // would cross the header — no-op
        Assert.Equal(second.Id, (await composer.GetSectionsAsync(post.Id))[1].Id);
        await composer.MoveSectionAsync(sections[3].Id, 1);        // would cross the footer — no-op
        Assert.Equal(SectionTypes.Footer, (await composer.GetSectionsAsync(post.Id))[^1].Type);
    }

    [Fact]
    public async Task Update_and_remove_persist_and_renumber_but_protect_header_footer()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "t");
        var sections = await composer.GetSectionsAsync(post.Id);

        await composer.UpdateSectionAsync(sections[1].Id, "New title", "new body", null, "https://ex.com/new", null);
        await composer.RemoveSectionAsync(sections[2].Id);

        var after = await composer.GetSectionsAsync(post.Id);
        Assert.Equal(4, after.Count);
        Assert.Equal("New title", after[1].Title);
        Assert.Equal("new body", after[1].BodyMd);
        Assert.Equal(Enumerable.Range(0, 4), after.Select(s => s.Position));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.RemoveSectionAsync(after[0].Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.RemoveSectionAsync(after[^1].Id));
    }

    [Fact]
    public async Task Export_and_preview_render_the_sections()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "My issue");

        var md = await composer.ExportMarkdownAsync(post.Id);
        Assert.Contains("## Item 1", md);
        Assert.Contains("Hi friends!", md);

        var html = await composer.RenderPreviewAsync(post.Id, "My issue");
        Assert.Contains("My issue", html);
        Assert.Contains("href=\"#\"", html);                                    // token replaced for preview
        Assert.DoesNotContain(SectionHtmlRenderer.UnsubscribeToken, html);
    }

    public static string TopicsJsonFor(IEnumerable<ContentItem> items) =>
        "[" + string.Join(",", items.Select(i =>
            $$"""{"itemId":"{{i.Id}}","title":"{{i.Title}} improved","blurb":"Blurb for {{i.Title}}."}""")) + "]";

    [Fact]
    public async Task GenerateTopics_fills_only_empty_topics_and_marks_items_used()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm(TopicsJsonFor(w.Items));
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "t");
        var sections = await composer.GetSectionsAsync(post.Id);
        await composer.UpdateSectionAsync(sections[1].Id, sections[1].Title, "HAND EDITED", null, sections[1].LinkUrl, null);

        var filled = await composer.GenerateTopicsAsync(post.Id, "keep it short");

        Assert.Equal(2, filled);                                            // topic 1 was hand-edited
        var after = await composer.GetSectionsAsync(post.Id);
        Assert.Equal("HAND EDITED", after[1].BodyMd);                       // bulk never overwrites edits
        Assert.Equal("Blurb for Item 2.", after[2].BodyMd);
        Assert.Equal("Item 2 improved", after[2].Title);
        Assert.Contains("keep it short", llm.Prompts.Single());
        Assert.Contains("punchy", llm.Prompts.Single());                    // recipe tone reached the prompt
        Assert.Equal(2, await w.Test.Db.ContentItems.CountAsync(i => i.Status == ContentItemStatus.Used));
    }

    [Fact]
    public async Task GenerateTopics_retries_once_then_succeeds()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("garbage", TopicsJsonFor(w.Items.Take(1)));
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");

        var filled = await composer.GenerateTopicsAsync(post.Id, null);

        Assert.Equal(1, filled);
        Assert.Equal(2, llm.Prompts.Count);
        Assert.Contains("was not valid JSON", llm.Prompts[1]);
    }

    [Fact]
    public async Task GenerateTopics_throws_after_two_bad_replies_and_keeps_skeletons()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var composer = Composer(w, new SequenceLlm("garbage", "more garbage"));
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");

        await Assert.ThrowsAsync<InvalidOperationException>(() => composer.GenerateTopicsAsync(post.Id, null));

        var sections = await composer.GetSectionsAsync(post.Id);
        Assert.True(string.IsNullOrEmpty(sections[1].BodyMd));              // skeleton intact for retry
        Assert.Equal(0, await w.Test.Db.ContentItems.CountAsync(i => i.Status == ContentItemStatus.Used));
    }

    [Fact]
    public async Task GenerateTopics_with_no_empty_topics_is_a_noop_without_llm_calls()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("should never be used");
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var sections = await composer.GetSectionsAsync(post.Id);
        await composer.UpdateSectionAsync(sections[1].Id, "T", "done", null, null, null);

        Assert.Equal(0, await composer.GenerateTopicsAsync(post.Id, null));
        Assert.Empty(llm.Prompts);
    }

    [Fact]
    public async Task GenerateTopics_resolves_llm_settings_for_the_posts_tenant()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm(TopicsJsonFor(w.Items.Take(1)));
        var settings = new StubLlmSettings(new LlmSettings("haiku", LlmEffort.Low));
        var composer = Composer(w, llm, settings);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");

        await composer.GenerateTopicsAsync(post.Id, null);

        Assert.Equal(w.Tenant.Id, settings.LastTenantId);
        Assert.Equal("haiku", llm.LastSettings!.Model);
    }

    [Fact]
    public async Task RegenerateSection_rewrites_a_topic_blurb_from_its_source_item()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("A fresh new blurb.");
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var topic = (await composer.GetSectionsAsync(post.Id))[1];

        await composer.RegenerateSectionAsync(topic.Id, "shorter");

        Assert.Equal("A fresh new blurb.", (await composer.GetSectionsAsync(post.Id))[1].BodyMd);
        Assert.Contains("body 1", llm.Prompts.Single());                    // source item material in prompt
        Assert.Contains("shorter", llm.Prompts.Single());
    }

    [Fact]
    public async Task RegenerateSection_writes_a_header_intro_referencing_topics_and_rejects_other_types()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("Welcome! This week: things.");
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id,
            w.Items.Select(i => i.Id).ToList(), "t");
        var sections = await composer.GetSectionsAsync(post.Id);

        await composer.RegenerateSectionAsync(sections[0].Id, null);

        Assert.Equal("Welcome! This week: things.", (await composer.GetSectionsAsync(post.Id))[0].BodyMd);
        Assert.Contains("Item 1", llm.Prompts.Single());                    // topic titles in prompt
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => composer.RegenerateSectionAsync(sections[^1].Id, null));  // footer is not regenerable
    }

    [Fact]
    public async Task RegenerateSection_resolves_llm_settings_for_the_posts_tenant()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm("A fresh new blurb.");
        var settings = new StubLlmSettings(new LlmSettings("haiku", LlmEffort.Low));
        var composer = Composer(w, llm, settings);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var topic = (await composer.GetSectionsAsync(post.Id))[1];

        await composer.RegenerateSectionAsync(topic.Id, null);

        Assert.Equal(w.Tenant.Id, settings.LastTenantId);
        Assert.Equal("haiku", llm.LastSettings!.Model);
    }

    private static async Task ConfigureMailerLiteAsync(World w)
    {
        var platform = await w.Platforms.GetOrCreateMailerLiteAsync(w.Tenant.Id);
        await w.Platforms.SetApiKeyAsync(platform, "KEY");
        await w.Platforms.SaveConfigAsync(platform, new MailerLiteConfig("g1", "Main", "Acme", "n@x.com"));
    }

    [Fact]
    public async Task Push_renders_sections_with_the_mailerlite_unsubscribe_variable_and_needs_no_draft()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        await ConfigureMailerLiteAsync(w);
        var llm = new SequenceLlm(TopicsJsonFor(w.Items.Take(1)));
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "Sectioned issue");
        await composer.GenerateTopicsAsync(post.Id, null);
        var posts = new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery(), new StubLlmSettings()),
            llm, w.Platforms, w.MailerLite, new StubLlmSettings());

        var pushed = await posts.PushAsync(post.Id);

        Assert.Equal(PostStatus.Pushed, pushed.Status);
        Assert.Null(pushed.DraftId);                                        // no Draft row was ever needed
        var html = w.MailerLite.Pushes.Single().Draft.Html;
        Assert.Contains("Blurb for Item 1.", html);
        Assert.Contains("Hi friends!", html);                               // tenant default header
        Assert.Contains("{$unsubscribe}", html);
        Assert.DoesNotContain(SectionHtmlRenderer.UnsubscribeToken, html);
    }

    [Fact]
    public async Task Push_rejects_an_overlong_subject_before_calling_mailerlite()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        await ConfigureMailerLiteAsync(w);
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var posts = new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, new FakeLlm(), new FakeDelivery(), new StubLlmSettings()),
            new FakeLlm(), w.Platforms, w.MailerLite, new StubLlmSettings());
        await posts.SaveIssueMetaAsync(post.Id, "t", new string('x', 256), null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => posts.PushAsync(post.Id));

        Assert.Contains("255", ex.Message);
        Assert.Empty(w.MailerLite.Pushes);
    }

    [Fact]
    public async Task SaveIssueMeta_persists_title_subject_preview_without_touching_sections()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var composer = Composer(w, new FakeLlm());
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "Old");
        var posts = new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, new FakeLlm(), new FakeDelivery(), new StubLlmSettings()),
            new FakeLlm(), w.Platforms, w.MailerLite, new StubLlmSettings());

        await posts.SaveIssueMetaAsync(post.Id, "New title", "Subj", "Pv");

        using var fresh = w.Test.NewContext();
        var reloaded = await fresh.Posts.SingleAsync(p => p.Id == post.Id);
        Assert.Equal(("New title", "Subj", "Pv"), (reloaded.Title, reloaded.Subject, reloaded.PreviewText));
        Assert.Null(reloaded.DraftId);                                      // meta save never creates a Draft
        Assert.Equal(3, await fresh.IssueSections.CountAsync(s => s.PostId == post.Id));
    }

    [Fact]
    public async Task SubjectIdeas_reads_section_markdown_for_sectioned_issues()
    {
        var w = await BuildWorldAsync();
        using var _ = w.Test;
        var llm = new SequenceLlm(TopicsJsonFor(w.Items.Take(1)), "[\"s1\",\"s2\",\"s3\",\"s4\",\"s5\"]");
        var composer = Composer(w, llm);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        await composer.GenerateTopicsAsync(post.Id, null);
        var posts = new PostService(w.Test.Db, new GenerationPipeline(w.Test.Db, llm, new FakeDelivery(), new StubLlmSettings()),
            llm, w.Platforms, w.MailerLite, new StubLlmSettings());

        var ideas = await posts.SubjectIdeasAsync(post.Id);

        Assert.Equal(5, ideas.Count);
        Assert.Contains("Blurb for Item 1.", llm.Prompts[^1]);              // section markdown reached the prompt
    }
}
