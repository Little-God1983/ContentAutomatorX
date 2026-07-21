namespace ContentAutomatorX.Domain;

public static class SourceTypes
{
    public const string Reddit = "Reddit";
    public const string Rss = "Rss";
    public const string Website = "Website";
    public const string LlmResearch = "LlmResearch";
}

public static class DraftKinds
{
    public const string Newsletter = "Newsletter";
    public const string SocialPost = "SocialPost";
    public const string VideoScript = "VideoScript";
    public static readonly string[] All = [Newsletter, SocialPost, VideoScript];
}

public static class RunKinds
{
    public const string Ingestion = "Ingestion";
    public const string Generation = "Generation";
}

public static class RunTriggers
{
    public const string Scheduled = "Scheduled";
    public const string Manual = "Manual";
    public const string Mcp = "Mcp";
}

public static class PlatformTypes
{
    public const string MailerLite = "MailerLite";
}

public static class SectionTypes
{
    public const string Header = "Header";
    public const string Topic = "Topic";
    public const string Sponsor = "Sponsor";
    public const string Button = "Button";
    public const string Divider = "Divider";
    public const string Video = "Video";
    public const string Footer = "Footer";
    public const string LegacyBody = "LegacyBody";

    /// <summary>Only a Topic section has a category — it is the one block with a {{category}}
    /// placeholder (TemplatePlaceholders.BlockSpecific) and the one type SectionCard's editor form
    /// shows a category field for. A proposed category on any other type would be stored but never
    /// render, and would be silently wiped the next time that section's card is expanded and applied
    /// (a full-replace write) — so anything that accepts a proposed category, not just the section
    /// editor form, must gate on this.</summary>
    public static bool HasCategory(string sectionType) => sectionType == Topic;
}

public static class ChatRoles
{
    public const string User = "User";
    public const string Assistant = "Assistant";
}

public static class RevisionStacks
{
    public const string Undo = "Undo";
    public const string Redo = "Redo";
}
