using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;
using ContentAutomatorX.Infrastructure.Sources;

namespace ContentAutomatorX.UnitTests;

public class QueueLlm(params string[] replies) : ILlmBackend
{
    private readonly Queue<string> _replies = new(replies);
    public List<string> Prompts { get; } = [];
    public LlmSettings? LastSettings { get; private set; }
    public int Calls { get; private set; }
    public string Name => "fake";
    public Task<LlmResult> GenerateAsync(string prompt, LlmSettings settings, CancellationToken ct = default)
    {
        Calls++;
        Prompts.Add(prompt);
        LastSettings = settings;
        return Task.FromResult(new LlmResult(_replies.Dequeue(), "fake-model"));
    }
}

/// <summary>Returns the same settings for every tenant.</summary>
public class StubLlmSettings(LlmSettings? settings = null) : ILlmSettingsProvider
{
    private readonly LlmSettings _settings = settings ?? LlmSettings.Inherit;
    public Guid? LastTenantId { get; private set; }
    public string? LastJob { get; private set; }
    public Task<LlmSettings> GetAsync(Guid tenantId, string? job = null, CancellationToken ct = default)
    {
        LastTenantId = tenantId;
        LastJob = job;
        return Task.FromResult(_settings);
    }
    public Task<LlmSettings> GetStoredAsync(Guid tenantId, string? job = null, CancellationToken ct = default) =>
        Task.FromResult(_settings);
    public Task SaveAsync(Guid tenantId, LlmSettings settings, string? job = null, CancellationToken ct = default) =>
        Task.CompletedTask;
}

public class LlmResearchConnectorTests
{
    private static Source Research(int maxItems = 10) => new()
    {
        Type = SourceTypes.LlmResearch, DisplayName = "sweep",
        ConfigJson = $$"""{"prompt":"top AI image-gen news this week","maxItems":{{maxItems}}}"""
    };

    private const string GoodJson =
        """[{"title":"Flux 2 rumors","url":"https://ex.com/1","summary":"what we know","source":"ex.com"}]""";

    [Fact]
    public async Task Parses_items_from_strict_json_reply()
    {
        var connector = new LlmResearchConnector(new QueueLlm(GoodJson), new StubLlmSettings());
        var items = await connector.FetchAsync(Research());

        var item = Assert.Single(items);
        Assert.Equal("https://ex.com/1", item.ExternalId);
        Assert.Equal("Flux 2 rumors", item.Title);
        Assert.Equal("what we know", item.Body);
        Assert.Contains("llm-research", item.MetadataJson);
    }

    [Fact]
    public async Task Strips_markdown_fences_before_parsing()
    {
        var fenced = "```json\n" + GoodJson + "\n```";
        var connector = new LlmResearchConnector(new QueueLlm(fenced), new StubLlmSettings());
        Assert.Single(await connector.FetchAsync(Research()));
    }

    [Fact]
    public async Task Retries_once_on_malformed_json_then_succeeds()
    {
        var llm = new QueueLlm("Sure! Here are the news items I found:", GoodJson);
        var connector = new LlmResearchConnector(llm, new StubLlmSettings());

        var items = await connector.FetchAsync(Research());

        Assert.Single(items);
        Assert.Equal(2, llm.Prompts.Count);
        Assert.Contains("ONLY the JSON array", llm.Prompts[1]);
    }

    [Fact]
    public async Task Caps_at_max_items_and_skips_entries_without_url_or_title()
    {
        var many = """
        [{"title":"a","url":"https://ex.com/a","summary":"s"},
         {"title":"","url":"https://ex.com/empty","summary":"s"},
         {"title":"no-url","url":"","summary":"s"},
         {"title":"b","url":"https://ex.com/b","summary":"s"},
         {"title":"c","url":"https://ex.com/c","summary":"s"}]
        """;
        var connector = new LlmResearchConnector(new QueueLlm(many), new StubLlmSettings());
        var items = await connector.FetchAsync(Research(maxItems: 2));
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.StartsWith("https://ex.com/", i.ExternalId));
    }

    [Fact]
    public async Task Empty_array_reply_yields_no_items_without_retry()
    {
        var llm = new QueueLlm("[]");
        var connector = new LlmResearchConnector(llm, new StubLlmSettings());

        var items = await connector.FetchAsync(Research());

        Assert.Empty(items);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task Two_malformed_replies_throw_an_actionable_error()
    {
        var llm = new QueueLlm("not json", "still not json");
        var connector = new LlmResearchConnector(llm, new StubLlmSettings());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.FetchAsync(Research()));

        Assert.Contains("did not return valid JSON after retry", ex.Message);
    }

    [Fact]
    public async Task Resolves_settings_for_the_sources_own_tenant()
    {
        var tenantId = Guid.NewGuid();
        var source = Research();
        source.TenantId = tenantId;
        var llm = new QueueLlm(GoodJson);
        var settings = new StubLlmSettings(new LlmSettings("haiku", LlmEffort.Low));

        await new LlmResearchConnector(llm, settings).FetchAsync(source);

        Assert.Equal(tenantId, settings.LastTenantId);
        Assert.Equal(LlmJobs.Research, settings.LastJob);   // resolves under the research job override
        Assert.Equal("haiku", llm.LastSettings!.Model);
        Assert.Equal(LlmEffort.Low, llm.LastSettings!.Effort);
    }
}
