namespace ContentAutomatorX.UnitTests;

/// <summary>Regression coverage for the source-type display unification shipped in PR #12
/// (issues #13 and #14). SourceTypeDisplay is the single home for how a source type is
/// presented, and the three type dropdowns must render from it rather than hardcoding their
/// own labels — the whole point being that a new source type is added in exactly two places
/// (SourceTypes + SourceTypeDisplay) and every dropdown picks it up automatically.
///
/// SourceTypeDisplay lives in ContentAutomatorX.Web, which the unit-test project does not
/// reference (and there is no bUnit harness to render the .razor dropdowns), so — following
/// the same technique as RecipesTemplateDropdownTests and ReferenceTemplateTests — these read
/// the shipped source directly and assert on it. If the harness ever gains a way to reference
/// the helper or render components, these should graduate to behavioural assertions.</summary>
public class SourceTypeDisplayTests
{
    private static string WebFile(params string[] parts) => System.IO.Path.Combine(
        new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ContentAutomatorX.Web" }
            .Concat(parts).ToArray());

    private static string Read(string path)
    {
        Assert.True(File.Exists(path), $"Expected file not found at {System.IO.Path.GetFullPath(path)}");
        return File.ReadAllText(path);
    }

    private static string Helper() => Read(WebFile("SourceTypeDisplay.cs"));

    // ---- #13: deleted/unknown source shows a meaningful tooltip, not an empty one ----

    /// <summary>#13: the missing-source case (TypeOf returns "") must resolve to "Unknown source"
    /// rather than falling through to the raw (empty) type string, which rendered an empty tooltip.</summary>
    [Fact]
    public void Label_maps_an_empty_type_to_Unknown_source()
    {
        Assert.Contains("string.IsNullOrEmpty(type) ? \"Unknown source\" : type", Helper());
    }

    /// <summary>#13: an unmapped-but-present type string must still surface the raw string (it is
    /// informative for a future, not-yet-mapped type) — the empty-only special case must not have
    /// been widened into "return Unknown source for everything unmapped".</summary>
    [Fact]
    public void Label_keeps_the_raw_string_for_an_unmapped_but_present_type()
    {
        var helper = Helper();
        // The fallback arm returns the raw `type` for the non-empty case rather than a constant.
        Assert.Contains("? \"Unknown source\" : type", helper);
        Assert.DoesNotContain("_ => \"Unknown source\"", helper);
    }

    // ---- #14: SourceTypeDisplay owns the canonical list the dropdowns iterate ----

    /// <summary>#14: the ordered canonical list every dropdown renders from must exist and contain
    /// all four current source types.</summary>
    [Fact]
    public void All_exposes_every_known_source_type_in_order()
    {
        var helper = Helper();
        Assert.Contains("public static readonly IReadOnlyList<string> All", helper);
        Assert.Contains("[SourceTypes.Reddit, SourceTypes.Rss, SourceTypes.Website, SourceTypes.LlmResearch]", helper);
    }

    /// <summary>#14: the longer create-flow wording is preserved via Hint (the recorded product
    /// decision was to keep it), not dropped and not duplicated back into the dropdowns.</summary>
    [Fact]
    public void Hint_supplies_the_longer_create_flow_wording()
    {
        var helper = Helper();
        Assert.Contains("static string? Hint(string type)", helper);
        Assert.Contains("\"page watch\"", helper);
        Assert.Contains("\"AI web sweep\"", helper);
    }

    // ---- #14: the three dropdowns render from SourceTypeDisplay.All, not hardcoded labels ----

    public static IEnumerable<object[]> Dropdowns =>
    [
        [WebFile("Components", "Pages", "Content.razor")],
        [WebFile("Components", "Pages", "Sources.razor")],
        [WebFile("Components", "Shared", "QuickSourceDialog.razor")],
    ];

    [Theory]
    [MemberData(nameof(Dropdowns))]
    public void Each_type_dropdown_iterates_the_shared_list(string dropdownPath)
    {
        var source = Read(dropdownPath);
        Assert.Contains("foreach (var t in SourceTypeDisplay.All)", source);
        Assert.Contains("SourceTypeDisplay.Label(t)", source);
    }

    /// <summary>#14: the friendly labels must not be hardcoded as literal option text anymore —
    /// that duplication (five places to touch per new type) is exactly what the refactor removed.</summary>
    [Theory]
    [MemberData(nameof(Dropdowns))]
    public void No_type_dropdown_hardcodes_the_friendly_labels(string dropdownPath)
    {
        var source = Read(dropdownPath);
        Assert.DoesNotContain(">RSS/Atom feed<", source);
        Assert.DoesNotContain(">LLM research<", source);
    }
}
