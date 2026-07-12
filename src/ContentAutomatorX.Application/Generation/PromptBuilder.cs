using System.Text;
using System.Text.Json;
using ContentAutomatorX.Domain.Entities;

namespace ContentAutomatorX.Application.Generation;

public static class PromptBuilder
{
    private const int MaxBodyChars = 2000;

    public static string Build(string template, Tenant tenant, Recipe recipe,
        IReadOnlyList<ContentItem> items, string? extraInstructions)
    {
        var tone = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(recipe.ToneModifiers)) tone.AppendLine(recipe.ToneModifiers);
        if (!string.IsNullOrWhiteSpace(recipe.LengthTarget)) tone.AppendLine($"Target length: {recipe.LengthTarget}");
        if (!string.IsNullOrWhiteSpace(recipe.Language)) tone.AppendLine($"Write in: {recipe.Language}");

        return template
            .Replace("{voice_profile}", tenant.VoiceProfile)
            .Replace("{tone_modifiers}", tone.ToString().TrimEnd())
            .Replace("{items}", FormatItems(items))
            .Replace("{extra_instructions}", extraInstructions ?? "(none)");
    }

    private static string FormatItems(IReadOnlyList<ContentItem> items)
    {
        var sb = new StringBuilder();
        for (int n = 0; n < items.Count; n++)
        {
            var i = items[n];
            sb.AppendLine($"--- Item {n + 1} ---");
            sb.AppendLine($"Title: {i.Title}");
            if (i.Url is not null) sb.AppendLine($"URL: {i.Url}");
            if (Score(i) is int s and > 0) sb.AppendLine($"Score: {s}");
            var body = i.Body.Length > MaxBodyChars ? i.Body[..MaxBodyChars] + " [truncated]" : i.Body;
            if (body.Length > 0) sb.AppendLine(body);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static int? Score(ContentItem i)
    {
        try
        {
            using var doc = JsonDocument.Parse(i.MetadataJson);
            return doc.RootElement.TryGetProperty("score", out var s) ? s.GetInt32() : null;
        }
        catch { return null; }
    }
}
