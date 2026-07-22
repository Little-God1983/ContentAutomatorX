using System.Text.Json;
using ContentAutomatorX.Domain;
using ContentAutomatorX.Domain.Abstractions;
using ContentAutomatorX.Domain.Entities;
using ContentAutomatorX.Domain.Models;

namespace ContentAutomatorX.Infrastructure.Sources;

/// <summary>"AI as a source": one LLM call (ideally with web search enabled via
/// Claude:ExtraArgs) returns found articles as strict JSON; each becomes a ContentItem.
/// It finds material — it never writes the newsletter.</summary>
public class LlmResearchConnector(ILlmBackend llm, ILlmSettingsProvider llmSettings) : ISourceConnector
{
    public string Type => SourceTypes.LlmResearch;

    private record ResearchConfig(string Prompt, int MaxItems = 10);
    private record ResearchItem(string? Title, string? Url, string? Summary, string? Source);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<FetchedItem>> FetchAsync(Source source, CancellationToken ct = default)
    {
        var config = JsonSerializer.Deserialize<ResearchConfig>(source.ConfigJson, JsonOpts)
            ?? throw new InvalidOperationException($"Source {source.Id}: invalid LlmResearch config");

        var prompt = BuildPrompt(config, retry: false);
        var settings = await llmSettings.GetAsync(source.TenantId, LlmJobs.Research, ct);
        var reply = await llm.GenerateAsync(prompt, settings, ct);
        if (!TryParse(reply.Text, out var parsed))
        {
            reply = await llm.GenerateAsync(BuildPrompt(config, retry: true), settings, ct);
            if (!TryParse(reply.Text, out parsed))
                throw new InvalidOperationException(
                    $"LlmResearch source {source.DisplayName}: model did not return valid JSON after retry");
        }

        return parsed!
            .Where(i => !string.IsNullOrWhiteSpace(i.Url) && !string.IsNullOrWhiteSpace(i.Title))
            .Take(config.MaxItems)
            .Select(i => new FetchedItem(
                ExternalId: i.Url!.Trim(), Title: i.Title!.Trim(), Url: i.Url!.Trim(),
                Author: i.Source, Body: i.Summary?.Trim() ?? "",
                MetadataJson: """{"via":"llm-research"}""", PublishedAt: null))
            .ToList();
    }

    private static string BuildPrompt(ResearchConfig config, bool retry) =>
        $$"""
        You are a research assistant with web access. Task: {{config.Prompt}}

        Find up to {{config.MaxItems}} relevant, recent items. Respond with ONLY a JSON array,
        no prose, no markdown fences: [{"title": "...", "url": "...", "summary": "1-2 sentences", "source": "site name"}]
        {{(retry ? "\nYour previous reply was not valid JSON. Return ONLY the JSON array this time." : "")}}
        """;

    private static bool TryParse(string text, out List<ResearchItem>? items)
    {
        items = null;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
        }
        try
        {
            items = JsonSerializer.Deserialize<List<ResearchItem>>(trimmed, JsonOpts);
            return items is { Count: >= 0 };
        }
        catch (JsonException) { return false; }
    }
}
