using ContentAutomatorX.Application.Services;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContentAutomatorX.IntegrationTests;

public class IssueChatServiceTests
{
    private static async Task<(TestDb Test, Post Post, List<IssueSection> Sections)> SeedAsync()
    {
        var test = TestDb.Create();
        var tenant = new Tenant { Name = "T", Slug = $"t-chat-svc-{Guid.NewGuid():N}", VoiceProfile = "dry wit" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "Issue" };
        var sections = new List<IssueSection>
        {
            new() { PostId = post.Id, Position = 0, Type = SectionTypes.Header, BodyMd = "intro" },
            new() { PostId = post.Id, Position = 1, Type = SectionTypes.Topic, Title = "A", BodyMd = "a" },
            new() { PostId = post.Id, Position = 2, Type = SectionTypes.Sponsor, Title = "Acme", BodyMd = "ad" },
            new() { PostId = post.Id, Position = 3, Type = SectionTypes.Footer, BodyMd = "bye" }
        };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.AddRange(sections);
        await test.Db.SaveChangesAsync();
        return (test, post, sections);
    }

    private static IssueChatService Chat(TestDb test, ILlmBackend llm) =>
        new(test.Db, llm, new StubLlmSettings(), new IssueHistoryService(test.Db));

    /// <summary>Header/topic/footer issue plus a FakeLlm that returns the given reply verbatim,
    /// with the literal "SECTION" placeholder swapped for the topic's real id — so callers can
    /// write the JSON reply inline without knowing the id up front.</summary>
    private static async Task<(IssueChatService Chat, Guid PostId, Guid SectionId)> ChatWithOneTopicAsync(
        TestDb test, string replyTemplate)
    {
        var tenant = new Tenant { Name = "T", Slug = $"t-chat-cat-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "Issue" };
        var topic = new IssueSection { PostId = post.Id, Position = 1, Type = SectionTypes.Topic, Title = "A", BodyMd = "a" };
        var sections = new List<IssueSection>
        {
            new() { PostId = post.Id, Position = 0, Type = SectionTypes.Header, BodyMd = "intro" },
            topic,
            new() { PostId = post.Id, Position = 2, Type = SectionTypes.Footer, BodyMd = "bye" }
        };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.AddRange(sections);
        await test.Db.SaveChangesAsync();

        var reply = replyTemplate.Replace("SECTION", topic.Id.ToString());
        var chat = Chat(test, new SequenceLlm(reply));
        return (chat, post.Id, topic.Id);
    }

    private static string ReplyJson(string prose, params (Guid Id, string Body)[] edits) =>
        $$"""{"reply":"{{prose}}","edits":[{{string.Join(",",
            edits.Select(e => $$"""{"sectionId":"{{e.Id}}","bodyMd":"{{e.Body}}"}"""))}}]}""";

    [Fact]
    public async Task A_turn_records_both_messages_and_creates_a_proposal()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("Shortened it.", (sections[1].Id, "shorter")));

        var result = await Chat(test, llm).SendAsync(post.Id, "make topic A shorter");

        Assert.Equal("Shortened it.", result.Reply);
        Assert.Equal(1, result.ProposalCount);
        Assert.Equal(0, result.DroppedEdits);

        // Ordered client-side: SQLite stores DateTimeOffset in a form its ORDER BY sorts wrongly.
        var messages = (await test.Db.IssueChatMessages.ToListAsync()).OrderBy(m => m.CreatedAt).ToList();
        Assert.Equal([ChatRoles.User, ChatRoles.Assistant], messages.Select(m => m.Role));

        var proposal = await test.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal(sections[1].Id, proposal.SectionId);
        Assert.Equal("shorter", proposal.ProposedBodyMd);
        Assert.Equal("a", proposal.BaselineBodyMd);       // captured from the live section
        Assert.Equal("a", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task The_prompt_carries_the_issue_the_voice_and_the_transcript()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("One."), ReplyJson("Two."));
        var chat = Chat(test, llm);

        await chat.SendAsync(post.Id, "first question");
        await chat.SendAsync(post.Id, "second question");

        var second = llm.Prompts[^1];
        Assert.Contains(sections[1].Id.ToString(), second);   // section ids so the model can target them
        Assert.Contains("dry wit", second);                   // tenant voice
        Assert.Contains("first question", second);            // prior turn
        Assert.Contains("One.", second);                      // prior assistant turn
        Assert.Contains("second question", second);
    }

    [Fact]
    public async Task An_edit_naming_an_unknown_section_is_dropped_and_reported()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("Tried.", (sections[1].Id, "ok"), (Guid.NewGuid(), "nope")));

        var result = await Chat(test, llm).SendAsync(post.Id, "change things");

        Assert.Equal(1, result.ProposalCount);
        Assert.Equal(1, result.DroppedEdits);
        Assert.Equal(sections[1].Id, (await test.Db.IssueSectionProposals.SingleAsync()).SectionId);
    }

    [Fact]
    public async Task Two_edits_for_one_section_merge_into_one_proposal()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        // The model split one change across two edit objects instead of merging them itself.
        var reply = $$"""
            {"reply":"Both.","edits":[
              {"sectionId":"{{sections[1].Id}}","title":"New title"},
              {"sectionId":"{{sections[1].Id}}","bodyMd":"New body"}
            ]}
            """;

        var result = await Chat(test, new SequenceLlm(reply)).SendAsync(post.Id, "change both");

        Assert.Equal(1, result.ProposalCount);
        Assert.Equal(0, result.DroppedEdits);
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal("New title", proposal.ProposedTitle);
        Assert.Equal("New body", proposal.ProposedBodyMd);
    }

    [Fact]
    public async Task An_edit_naming_a_section_of_another_post_is_dropped()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var other = new Post
        {
            TenantId = post.TenantId, PlatformId = post.PlatformId,
            Kind = DraftKinds.Newsletter, Title = "Other issue"
        };
        var foreign = new IssueSection
        {
            PostId = other.Id, Position = 0, Type = SectionTypes.Topic, Title = "Theirs", BodyMd = "theirs"
        };
        test.Db.Add(other);
        test.Db.IssueSections.Add(foreign);
        await test.Db.SaveChangesAsync();

        var result = await Chat(test, new SequenceLlm(ReplyJson("Reaching.", (foreign.Id, "hijacked"))))
            .SendAsync(post.Id, "edit the other issue");

        Assert.Equal(0, result.ProposalCount);
        Assert.Equal(1, result.DroppedEdits);
        Assert.Empty(await test.Db.IssueSectionProposals.ToListAsync());
        Assert.Equal("theirs", (await test.Db.IssueSections.SingleAsync(s => s.Id == foreign.Id)).BodyMd);
    }

    [Fact]
    public async Task A_second_turn_replaces_the_proposal_for_the_same_section()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("v1", (sections[1].Id, "first")),
                                  ReplyJson("v2", (sections[1].Id, "second")));
        var chat = Chat(test, llm);

        await chat.SendAsync(post.Id, "try");
        await chat.SendAsync(post.Id, "try again");

        var proposal = await test.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal("second", proposal.ProposedBodyMd);
    }

    [Fact]
    public async Task A_failed_turn_keeps_the_user_message_and_creates_nothing()
    {
        var (test, post, _) = await SeedAsync();
        using var _t = test;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Chat(test, new SequenceLlm("garbage", "still garbage")).SendAsync(post.Id, "hello"));

        var message = await test.Db.IssueChatMessages.SingleAsync();
        Assert.Equal(ChatRoles.User, message.Role);
        Assert.Equal("hello", message.Text);
        Assert.Empty(await test.Db.IssueSectionProposals.ToListAsync());
    }

    [Fact]
    public async Task Chat_may_propose_against_any_section_type()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("Fixed the sponsor.", (sections[2].Id, "better ad")));

        var result = await Chat(test, llm).SendAsync(post.Id, "fix the sponsor blurb");

        Assert.Equal(1, result.ProposalCount);
        Assert.Equal(sections[2].Id, (await test.Db.IssueSectionProposals.SingleAsync()).SectionId);
    }

    [Fact] // F5 — free chat applies no type filter, so the model can propose a category on a
    // section type that has no category concept (only Topic renders {{category}} —
    // TemplatePlaceholders.BlockSpecific). It must be dropped, not stored: stored-but-unrenderable
    // would also be silently wiped the next time the section card is expanded and Apply is clicked
    // (SectionCard.HasCategory() is Topic-only, and Apply is a full-field replace).
    public async Task A_proposed_category_on_a_section_type_with_no_category_is_dropped()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var reply = $$"""
            {"reply":"Tagged it.","edits":[{"sectionId":"{{sections[2].Id}}","bodyMd":"better ad","category":"Ads"}]}
            """;

        var result = await Chat(test, new SequenceLlm(reply)).SendAsync(post.Id, "fix the sponsor and tag it");

        Assert.Equal(1, result.ProposalCount);
        Assert.Equal(1, result.DroppedEdits);   // the category component, not the whole edit
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal(sections[2].Id, proposal.SectionId);   // Sponsor — the rest of the edit still lands
        Assert.Equal("better ad", proposal.ProposedBodyMd);
        Assert.Null(proposal.ProposedCategory);
    }

    [Fact] // Fix — a category-only edit on a section type with no category concept must not become
    // a no-op proposal. StoreProposalsAsync nulls the category (as the test above confirms) but
    // previously still called db.IssueSectionProposals.Add unconditionally afterward — with Title,
    // BodyMd and Category all null, that stores a proposal card that changes nothing when accepted,
    // and counts the one edit as BOTH stored (ProposalCount) AND dropped (DroppedEdits). Once
    // nothing survives the category nulling, the edit must be dropped entirely and counted once.
    public async Task A_category_only_edit_on_a_section_type_with_no_category_is_dropped_not_stored()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var reply = $$"""
            {"reply":"Tagged it.","edits":[{"sectionId":"{{sections[2].Id}}","category":"Ads"}]}
            """;

        var result = await Chat(test, new SequenceLlm(reply)).SendAsync(post.Id, "tag the sponsor");

        Assert.Equal(0, result.ProposalCount);
        Assert.Equal(1, result.DroppedEdits);
        Assert.Empty(await test.Db.IssueSectionProposals.ToListAsync());
    }

    [Fact]
    public async Task RegenerateAll_targets_header_and_topics_only_and_writes_no_chat_messages()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var llm = new SequenceLlm(ReplyJson("Here you go.",
            (sections[0].Id, "new intro"), (sections[1].Id, "new blurb"), (sections[2].Id, "new ad")));

        var count = await Chat(test, llm).RegenerateAllAsync(post.Id, "punchier");

        Assert.Equal(2, count);                                  // the sponsor edit is refused
        var ids = await test.Db.IssueSectionProposals.Select(p => p.SectionId).ToListAsync();
        Assert.Contains(sections[0].Id, ids);
        Assert.Contains(sections[1].Id, ids);
        Assert.DoesNotContain(sections[2].Id, ids);
        Assert.Empty(await test.Db.IssueChatMessages.ToListAsync());
        Assert.Contains("punchier", llm.Prompts.Single());
    }

    [Fact]
    public async Task RegenerateAll_returns_zero_when_there_is_nothing_to_regenerate()
    {
        var test = TestDb.Create();
        using var _ = test;
        var tenant = new Tenant { Name = "T", Slug = $"t-empty-{Guid.NewGuid():N}" };
        var platform = new Platform { TenantId = tenant.Id, Type = PlatformTypes.MailerLite, DisplayName = "ML" };
        var post = new Post { TenantId = tenant.Id, PlatformId = platform.Id, Kind = DraftKinds.Newsletter, Title = "I" };
        test.Db.AddRange(tenant, platform, post);
        test.Db.IssueSections.Add(new IssueSection { PostId = post.Id, Position = 0, Type = SectionTypes.Footer });
        await test.Db.SaveChangesAsync();
        var llm = new SequenceLlm("unused");

        Assert.Equal(0, await Chat(test, llm).RegenerateAllAsync(post.Id, null));
        Assert.Empty(llm.Prompts);                               // no model call at all
    }

    [Fact]
    public async Task Accept_applies_the_text_deletes_the_proposal_and_is_undoable()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var chat = Chat(test, new SequenceLlm(ReplyJson("ok", (sections[1].Id, "accepted body"))));
        await chat.SendAsync(post.Id, "rewrite");
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();

        await chat.AcceptAsync(proposal.Id, force: false);

        Assert.Equal("accepted body", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
        Assert.Empty(await test.Db.IssueSectionProposals.ToListAsync());

        await new IssueHistoryService(test.Db).UndoAsync(post.Id);
        Assert.Equal("a", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task Accept_refuses_a_stale_proposal_unless_forced()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var chat = Chat(test, new SequenceLlm(ReplyJson("ok", (sections[1].Id, "proposed"))));
        await chat.SendAsync(post.Id, "rewrite");
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();

        sections[1].BodyMd = "hand edited since";               // the user typed over it meanwhile
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<StaleProposalException>(() => chat.AcceptAsync(proposal.Id, force: false));
        Assert.Equal("hand edited since", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);

        await chat.AcceptAsync(proposal.Id, force: true);
        Assert.Equal("proposed", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task Reject_deletes_the_proposal_without_writing()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var chat = Chat(test, new SequenceLlm(ReplyJson("ok", (sections[1].Id, "not wanted"))));
        await chat.SendAsync(post.Id, "rewrite");
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();

        await chat.RejectAsync(proposal.Id);

        Assert.Empty(await test.Db.IssueSectionProposals.ToListAsync());
        Assert.Equal("a", (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).BodyMd);
    }

    [Fact]
    public async Task GetThread_returns_messages_in_order_with_pending_proposals()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        var chat = Chat(test, new SequenceLlm(ReplyJson("ok", (sections[1].Id, "p"))));
        await chat.SendAsync(post.Id, "hello");

        var thread = await chat.GetThreadAsync(post.Id);

        Assert.Equal([ChatRoles.User, ChatRoles.Assistant], thread.Messages.Select(m => m.Role));
        Assert.Single(thread.Proposals);
    }

    [Fact]
    public async Task Removing_a_section_deletes_its_pending_proposal_too()
    {
        var w = await IssueComposerServiceTests.BuildWorldAsync();
        using var _ = w.Test;
        var history = new IssueHistoryService(w.Test.Db);
        var composer = IssueComposerServiceTests.ComposerWith(w, new SequenceLlm("unused"), history);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");
        var topic = (await composer.GetSectionsAsync(post.Id))[1];
        // No FK ties this row to the section — only RemoveSectionAsync's own cleanup does.
        w.Test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = topic.Id, ProposedBodyMd = "x", BaselineBodyMd = ""
        });
        await w.Test.Db.SaveChangesAsync();

        await composer.RemoveSectionAsync(topic.Id);

        Assert.Empty(await w.Test.Db.IssueSectionProposals.Where(p => p.SectionId == topic.Id).ToListAsync());
    }

    [Fact]
    public async Task Undoing_an_added_section_deletes_its_pending_proposal_too()
    {
        var w = await IssueComposerServiceTests.BuildWorldAsync();
        using var _ = w.Test;
        var history = new IssueHistoryService(w.Test.Db);
        var composer = IssueComposerServiceTests.ComposerWith(w, new SequenceLlm("unused"), history);
        var post = await composer.CreateFromItemsAsync(w.Tenant.Id, w.Recipe.Id, [w.Items[0].Id], "t");

        var added = await composer.AddSectionAsync(post.Id, SectionTypes.Sponsor);
        w.Test.Db.IssueSectionProposals.Add(new IssueSectionProposal
        {
            PostId = post.Id, SectionId = added.Id, ProposedBodyMd = "x", BaselineBodyMd = ""
        });
        await w.Test.Db.SaveChangesAsync();

        // Undoing the add restores the pre-add snapshot, which doesn't contain this section — so
        // RestoreAsync removes it exactly like RemoveSectionAsync would, proposal included.
        await history.UndoAsync(post.Id);

        Assert.Empty(await w.Test.Db.IssueSections.Where(s => s.Id == added.Id).ToListAsync());
        Assert.Empty(await w.Test.Db.IssueSectionProposals.Where(p => p.SectionId == added.Id).ToListAsync());
    }

    [Fact]
    public async Task Accept_refuses_a_title_only_proposal_when_the_title_was_hand_edited()
    {
        var (test, post, sections) = await SeedAsync();
        using var _ = test;
        // Title-only edit: ReplyJson only ever emits bodyMd, so build this reply by hand.
        var reply = $$"""{"reply":"ok","edits":[{"sectionId":"{{sections[1].Id}}","title":"Proposed title"}]}""";
        var chat = Chat(test, new SequenceLlm(reply));
        await chat.SendAsync(post.Id, "rename it");
        var proposal = await test.Db.IssueSectionProposals.SingleAsync();

        sections[1].Title = "Hand typed title";              // the user renamed it meanwhile
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<StaleProposalException>(() => chat.AcceptAsync(proposal.Id, force: false));
        Assert.Equal("Hand typed title",
            (await test.Db.IssueSections.SingleAsync(s => s.Id == sections[1].Id)).Title);
    }

    [Fact]
    public async Task Accepting_a_category_proposal_writes_it_to_the_section()
    {
        using var t = TestDb.Create();
        var (chat, postId, sectionId) = await ChatWithOneTopicAsync(t, """
            {"reply":"Relabelled.","edits":[{"sectionId":"SECTION","category":"Tutorial"}]}
            """);

        await chat.SendAsync(postId, "Give this a category.");
        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        Assert.Equal("Tutorial", proposal.ProposedCategory);

        await chat.AcceptAsync(proposal.Id, force: false);
        Assert.Equal("Tutorial", (await t.Db.IssueSections.SingleAsync(s => s.Id == sectionId)).Category);
    }

    [Fact]
    public async Task A_category_proposal_goes_stale_when_the_category_changes_underneath_it()
    {
        using var t = TestDb.Create();
        var (chat, postId, sectionId) = await ChatWithOneTopicAsync(t, """
            {"reply":"Relabelled.","edits":[{"sectionId":"SECTION","category":"Tutorial"}]}
            """);

        await chat.SendAsync(postId, "Give this a category.");
        var section = await t.Db.IssueSections.SingleAsync(s => s.Id == sectionId);
        section.Category = "Changed by hand";
        await t.Db.SaveChangesAsync();

        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        await Assert.ThrowsAsync<StaleProposalException>(() => chat.AcceptAsync(proposal.Id, force: false));
    }

    [Fact]
    public async Task A_body_only_proposal_is_not_stale_because_the_category_changed()
    {
        using var t = TestDb.Create();
        var (chat, postId, sectionId) = await ChatWithOneTopicAsync(t, """
            {"reply":"Rewrote it.","edits":[{"sectionId":"SECTION","bodyMd":"New body."}]}
            """);

        await chat.SendAsync(postId, "Rewrite this.");
        var section = await t.Db.IssueSections.SingleAsync(s => s.Id == sectionId);
        section.Category = "Tutorial";
        await t.Db.SaveChangesAsync();

        var proposal = await t.Db.IssueSectionProposals.SingleAsync();
        await chat.AcceptAsync(proposal.Id, force: false);   // must not throw
        Assert.Equal("New body.", (await t.Db.IssueSections.SingleAsync(s => s.Id == sectionId)).BodyMd);
    }
}
