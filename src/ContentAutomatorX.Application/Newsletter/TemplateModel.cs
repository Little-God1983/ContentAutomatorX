using ContentAutomatorX.Domain;

namespace ContentAutomatorX.Application.Newsletter;

public enum TemplateIssueLevel { Error, Warning }

/// <summary>One validation finding. Line is 1-based and points at the construct that caused it,
/// so the editor can tell the user where to look.</summary>
public record TemplateIssue(TemplateIssueLevel Level, int Line, string Message);

public record TemplateBlock(string Name, string Content, int Line);

public record ParsedTemplate(IReadOnlyDictionary<string, TemplateBlock> Blocks,
    IReadOnlyList<TemplateIssue> Issues);

public static class TemplateBlocks
{
    public const string Shell = "shell";
    public const string Header = "header";
    public const string Topic = "topic";
    public const string Video = "video";
    public const string Sponsor = "sponsor";
    public const string Button = "button";
    public const string Divider = "divider";
    public const string Footer = "footer";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { Shell, Header, Topic, Video, Sponsor, Button, Divider, Footer };

    /// <summary>The optional blocks — every block except the shell. A missing one is a warning and
    /// falls back to the built-in renderer for that section type.</summary>
    public static readonly IReadOnlyList<string> Optional =
        [Header, Topic, Video, Sponsor, Button, Divider, Footer];

    /// <summary>LegacyBody deliberately maps to null: legacy free-markdown issues have no sections
    /// at all and never reach the template renderer.</summary>
    public static string? ForSectionType(string sectionType) => sectionType switch
    {
        SectionTypes.Header => Header,
        SectionTypes.Topic => Topic,
        SectionTypes.Video => Video,
        SectionTypes.Sponsor => Sponsor,
        SectionTypes.Button => Button,
        SectionTypes.Divider => Divider,
        SectionTypes.Footer => Footer,
        _ => null
    };
}

public static class TemplatePlaceholders
{
    /// <summary>Available in every block.</summary>
    public static readonly IReadOnlySet<string> Global = new HashSet<string>
        { "tenant_name", "accent", "issue_title", "issue_date", "unsubscribe_url" };

    private static readonly Dictionary<string, HashSet<string>> BlockSpecific = new()
    {
        [TemplateBlocks.Shell]   = ["preheader", "sections"],
        [TemplateBlocks.Header]  = ["title", "body_html"],
        [TemplateBlocks.Topic]   = ["title", "body_html", "image_url", "link_url", "link_text",
                                    "category", "reading_time"],
        [TemplateBlocks.Video]   = ["title", "body_html", "thumbnail_url", "video_url", "link_text"],
        [TemplateBlocks.Sponsor] = ["title", "body_html", "image_url", "link_url", "link_text"],
        [TemplateBlocks.Button]  = ["link_url", "link_text"],
        [TemplateBlocks.Divider] = [],
        [TemplateBlocks.Footer]  = ["body_html", "sender_identity"]
    };

    /// <summary>Condition name → the placeholder it tests. A condition is true when that
    /// placeholder resolves to a non-empty string.</summary>
    private static readonly Dictionary<string, string> ConditionTargets = new()
    {
        ["title"] = "title", ["body"] = "body_html", ["image"] = "image_url",
        ["link"] = "link_url", ["category"] = "category",
        ["thumbnail"] = "thumbnail_url", ["video"] = "video_url"
    };

    public static IReadOnlySet<string> For(string blockName) =>
        BlockSpecific.TryGetValue(blockName, out var own)
            ? new HashSet<string>(own.Concat(Global))
            : Global;

    /// <summary>Conditions valid in this block: those whose target placeholder exists here.</summary>
    public static IReadOnlySet<string> Conditions(string blockName)
    {
        var available = For(blockName);
        return ConditionTargets.Where(p => available.Contains(p.Value)).Select(p => p.Key).ToHashSet();
    }

    public static string? TargetOf(string condition) =>
        ConditionTargets.TryGetValue(condition, out var target) ? target : null;
}
