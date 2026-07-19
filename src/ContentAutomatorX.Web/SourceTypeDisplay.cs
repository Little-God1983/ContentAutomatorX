using ContentAutomatorX.Domain;
using MudBlazor;

namespace ContentAutomatorX.Web;

/// <summary>
/// Single home for how a source type is presented across the UI: icon, friendly
/// name, create-flow hint, and the canonical ordering the type dropdowns render from.
/// </summary>
public static class SourceTypeDisplay
{
    public static string Icon(string type) => type switch
    {
        SourceTypes.Reddit => Icons.Custom.Brands.Reddit,
        SourceTypes.Rss => Icons.Material.Filled.RssFeed,
        SourceTypes.Website => Icons.Material.Filled.Language,
        SourceTypes.LlmResearch => Icons.Material.Filled.AutoAwesome,
        _ => Icons.Material.Filled.Source
    };

    public static string Label(string type) => type switch
    {
        SourceTypes.Reddit => "Reddit",
        SourceTypes.Rss => "RSS/Atom feed",
        SourceTypes.Website => "Website",
        SourceTypes.LlmResearch => "LLM research",
        _ => string.IsNullOrEmpty(type) ? "Unknown source" : type
    };

    public static readonly IReadOnlyList<string> All =
        [SourceTypes.Reddit, SourceTypes.Rss, SourceTypes.Website, SourceTypes.LlmResearch];

    /// <summary>Extra wording for create flows, e.g. "Website (page watch)". Null when the label stands alone.</summary>
    public static string? Hint(string type) => type switch
    {
        SourceTypes.Website => "page watch",
        SourceTypes.LlmResearch => "AI web sweep",
        _ => null
    };
}
